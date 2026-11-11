using System.Data;
using System.Diagnostics;
using Dapper;
using Infrastructure.SharedKernel.Observability;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Shinera.Identity.SqlServerTests;

public sealed class SourceOwnedSqlClientAndDapperTracingTests
{
    private sealed class TraceRow
    {
        public Guid Id { get; set; }
        public string Text { get; set; } = "";
    }

    private sealed class TraceDb(DbContextOptions<TraceDb> options) : DbContext(options)
    {
        public DbSet<TraceRow> Rows => Set<TraceRow>();

        protected override void OnModelCreating(ModelBuilder builder)
            => builder.Entity<TraceRow>().ToTable("FR18D2_SourceOwnedRows");
    }

    [Fact]
    public async Task Sensitive_AspNet_path_does_not_export_SQL_children_just_because_traceparent_is_sampled()
    {
        var recorded = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is
                "Microsoft.AspNetCore" or SourceOwnedSqlTrace.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.Source.Name == SourceOwnedSqlTrace.ActivitySourceName)
                    recorded.Add(activity);
            }
        };
        ActivitySource.AddActivityListener(listener);
        using var httpSource = new ActivitySource("Microsoft.AspNetCore");
        using var inbound = httpSource.StartActivity("POST /api/system/Auth/login", ActivityKind.Server);
        Assert.NotNull(inbound);
        Assert.False(SourceOwnedSqlTrace.CanCapture());

        var executed = false;
        var result = await SourceOwnedSqlTrace.RunDapperAsync(
            "SELECT @token", () =>
            {
                executed = true;
                return Task.FromResult(7);
            });
        Assert.True(executed);
        Assert.Equal(7, result);
        using (var ignored = SourceOwnedTransactionTrace.Start("auth_test"))
            ignored.CommitConfirmed();
        Assert.Empty(recorded);
    }

    [Fact]
    public async Task Real_raw_sqlclient_and_dapper_have_safe_once_only_spans_and_true_transaction_outcomes()
    {
        var raw = Environment.GetEnvironmentVariable("SHINERA_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Disposable SQL Server connection required");
        var admin = new SqlConnectionStringBuilder(raw);
        if (!admin.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase)
            || !(admin.DataSource.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase)
              || admin.DataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Only localhost disposable SQL Server master");

        admin.InitialCatalog = "FoundationRawSqlTrace_" + Guid.NewGuid().ToString("N");
        admin.ConnectTimeout = 30;
        var options = new DbContextOptionsBuilder<TraceDb>()
            .UseSqlServer(admin.ConnectionString).Options;
        await using var fixture = new TraceDb(options);

        try
        {
            await fixture.Database.EnsureCreatedAsync();
            await using var connection = new SqlConnection(admin.ConnectionString);
            await connection.OpenAsync();

            var spans = new List<Activity>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name is
                    SourceOwnedSqlTrace.ActivitySourceName or "fr18d2.test.root",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.Source.Name == SourceOwnedSqlTrace.ActivitySourceName)
                        spans.Add(activity);
                }
            };
            ActivitySource.AddActivityListener(listener);
            using var parentSource = new ActivitySource("fr18d2.test.root");
            using var parent = parentSource.StartActivity("test.http.request", ActivityKind.Server);
            Assert.NotNull(parent);

            const string privateValue = "SECRET-RAW-SQL-DO-NOT-EXPORT";
            var rowId = Guid.NewGuid();

            await using (var rollbackTransaction = (SqlTransaction)await connection.BeginTransactionAsync())
            {
                using var transactionSpan = SourceOwnedTransactionTrace.Start("raw_sql_test");
                await using var insert = new SqlCommand(
                    "INSERT dbo.FR18D2_SourceOwnedRows(Id, [Text]) VALUES (@id, @private)",
                    connection, rollbackTransaction);
                insert.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = rowId;
                insert.Parameters.Add("@private", SqlDbType.NVarChar, 256).Value = privateValue;
                Assert.Equal(1, await SourceOwnedSqlTrace.ExecuteNonQueryAsync(insert));

                const string readSql =
                    "SELECT COUNT_BIG(*) FROM dbo.FR18D2_SourceOwnedRows WHERE Id=@id";
                var found = await SourceOwnedSqlTrace.RunDapperAsync(readSql,
                    () => connection.ExecuteScalarAsync<long>(readSql, new { id = rowId },
                        rollbackTransaction));
                Assert.Equal(1, found);

                await SourceOwnedSqlTransactionCommands.RollbackAsync(rollbackTransaction);
                transactionSpan.RollbackConfirmed();
            }

            await using (var check = new SqlCommand(
                "SELECT COUNT_BIG(*) FROM dbo.FR18D2_SourceOwnedRows",
                connection))
            {
                Assert.Equal(0L, await SourceOwnedSqlTrace.ExecuteScalarAsync(check));
            }

            var committedId = Guid.NewGuid();
            await using (var committedTransaction = (SqlTransaction)await connection.BeginTransactionAsync())
            {
                using var transactionSpan = SourceOwnedTransactionTrace.Start("raw_sql_test");
                await using var insert = new SqlCommand(
                    "INSERT dbo.FR18D2_SourceOwnedRows(Id, [Text]) VALUES (@id, @private)",
                    connection, committedTransaction);
                insert.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = committedId;
                insert.Parameters.Add("@private", SqlDbType.NVarChar, 256).Value = privateValue;
                Assert.Equal(1, await SourceOwnedSqlTrace.ExecuteNonQueryAsync(insert));
                transactionSpan.CommitAttempted();
                await SourceOwnedSqlTransactionCommands.CommitAsync(committedTransaction);
                transactionSpan.CommitConfirmed();
            }

            await using (var checkCommitted = new SqlCommand(
                "SELECT COUNT_BIG(*) FROM dbo.FR18D2_SourceOwnedRows",
                connection))
            {
                Assert.Equal(1L, await SourceOwnedSqlTrace.ExecuteScalarAsync(checkCommitted));
            }

            await using (var invalid = new SqlCommand(
                "SELECT * FROM dbo.FR18D2_NonexistentTable WHERE Secret = @private",
                connection))
            {
                invalid.Parameters.Add("@private", SqlDbType.NVarChar, 256).Value = privateValue;
                await Assert.ThrowsAsync<SqlException>(() =>
                    SourceOwnedSqlTrace.ExecuteReaderAsync(invalid));
            }

            var sqlSpans = spans.Where(s => s.DisplayName == "db.sqlclient.command").ToArray();
            Assert.Equal(6, sqlSpans.Length); // insert,Dapper,rollback-check,insert,commit-check,fail
            Assert.Equal(1, sqlSpans.Count(s =>
                s.GetTagItem("foundation.db.client")?.ToString() == "dapper"));
            Assert.Equal(1, sqlSpans.Count(s => s.Status == ActivityStatusCode.Error));
            Assert.Equal(2, spans.Count(s =>
                s.DisplayName.StartsWith("db.sqlclient.transaction.", StringComparison.Ordinal)));

            var transactionSpans = spans.Where(s =>
                s.DisplayName == "db.sqlclient.transaction").ToArray();
            Assert.Equal(2, transactionSpans.Length);
            Assert.Contains(transactionSpans, s =>
                Equals(s.GetTagItem("foundation.db.transaction.outcome"), "rolled_back"));
            Assert.Contains(transactionSpans, s =>
                Equals(s.GetTagItem("foundation.db.transaction.outcome"), "committed"));

            Assert.All(spans, span =>
            {
                Assert.Equal(parent.TraceId, span.TraceId);
                // Transaction operation wrappers can be nested under the outer
                // transaction span; all carry the same trace ancestry.
                Assert.True(span.Duration >= TimeSpan.Zero);
                var tags = string.Join(";", span.TagObjects.Select(t => t.Key + "=" + t.Value));
                Assert.DoesNotContain(privateValue, tags);
                Assert.DoesNotContain("FR18D2_SourceOwnedRows", tags);
                Assert.DoesNotContain("Password=", tags, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Data Source=", tags, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(privateValue, span.DisplayName);
            });
        }
        finally
        {
            fixture.ChangeTracker.Clear();
            await fixture.Database.EnsureDeletedAsync();
        }
    }
}
