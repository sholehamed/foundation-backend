using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Observability;
using Xunit;

namespace Shinera.Identity.SqlServerTests;

/// <summary>
/// Real disposable SQL Server command execution. This checks the trace
/// interceptor's observed timing, SQL identity suppression, unique spans,
/// fail state, and absence of any customer text in telemetry tags.
/// </summary>
public sealed class FoundationEfSqlTracingTests
{
    private sealed class ProbeRow
    {
        public Guid Id { get; set; }
        public string Payload { get; set; } = "";
    }

    private sealed class ProbeDb(DbContextOptions<ProbeDb> options) : DbContext(options)
    {
        public DbSet<ProbeRow> Rows => Set<ProbeRow>();

        protected override void OnModelCreating(ModelBuilder builder)
            => builder.Entity<ProbeRow>().ToTable("FR18D_ProbeRow");
    }

    [Fact]
    public async Task Ef_sql_commands_are_actual_children_with_duration_and_one_span_per_execution()
    {
        var raw = Environment.GetEnvironmentVariable("SHINERA_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrEmpty(raw))
            throw new InvalidOperationException("Disposable SQL Server is required");

        var admin = new SqlConnectionStringBuilder(raw);
        if (!admin.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase) ||
            !(admin.DataSource.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
              admin.DataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Only localhost disposable master is allowed");

        admin.InitialCatalog = "FoundationSqlTrace_" + Guid.NewGuid().ToString("N");
        admin.ConnectTimeout = 30;
        var options = new DbContextOptionsBuilder<ProbeDb>()
            .UseSqlServer(admin.ConnectionString)
            .AddInterceptors(new FoundationEfCommandInterceptor()).Options;

        await using var db = new ProbeDb(options);
        try
        {
            await db.Database.EnsureCreatedAsync();

            var spans = new List<Activity>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name is "Foundation.Observability"
                    or "fr18d.sql.test",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.Source.Name == "Foundation.Observability")
                        spans.Add(activity);
                }
            };
            ActivitySource.AddActivityListener(listener);

            using var parentSource = new ActivitySource("fr18d.sql.test");
            using var parent = parentSource.StartActivity("test.real.database", ActivityKind.Server);
            Assert.NotNull(parent);

            const string secret = "customer-PII-sentinel-DO-NOT-EXPORT-2026";
            var id = Guid.NewGuid();
            db.Rows.Add(new ProbeRow { Id = id, Payload = secret });
            await db.SaveChangesAsync();

            var readBack = await db.Rows.AsNoTracking()
                .SingleAsync(x => x.Id == id);
            Assert.Equal(secret, readBack.Payload);

            await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync(
                "SELECT * FROM dbo.TableThatDoesNotExist_FR18D WHERE Value = {0}",
                secret));

            var commands = spans.Where(s => s.DisplayName == "db.ef.command").ToArray();
            Assert.True(commands.Length >= 3,
                $"Expected insert, select, and failed SQL commands, got {commands.Length}");
            Assert.All(commands, span =>
            {
                Assert.Equal(parent.TraceId, span.TraceId);
                Assert.Equal(parent.SpanId, span.ParentSpanId);
                Assert.True(span.Duration >= TimeSpan.Zero);
                Assert.NotNull(span.GetTagItem("foundation.db.command_type"));
                var fingerprint = span.GetTagItem("foundation.db.fingerprint")?.ToString();
                Assert.Equal(64, fingerprint?.Length);
                Assert.Equal("process", span.GetTagItem("foundation.db.fingerprint_scope"));
                var tags = string.Join(";", span.TagObjects.Select(t => t.Key + "=" + t.Value));
                Assert.DoesNotContain(secret, tags);
                Assert.DoesNotContain("TableThatDoesNotExist_FR18D", tags);
                Assert.DoesNotContain("Password=", tags, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Data Source=", tags, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("db.statement", tags);
            });
            Assert.Contains(commands, span => span.Status == ActivityStatusCode.Error &&
                span.GetTagItem("foundation.error.type") is not null);

            // A query that executes only once creates exactly one completion
            // span; retries would instead appear as separate actual attempts.
            Assert.Equal(1, commands.Count(span =>
                span.GetTagItem("foundation.db.execution")?.ToString() == "failure" &&
                span.Status == ActivityStatusCode.Error));
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
