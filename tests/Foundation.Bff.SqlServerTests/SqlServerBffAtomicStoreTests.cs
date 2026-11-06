using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Modules.System.Bff;
using Server.Bff;
using Xunit;

namespace Foundation.Bff.SqlServerTests;

/// <summary>
/// Real SQL Server tests only: a fresh database per test, never EF InMemory.
/// CI supplies a disposable SQL Server; absence of a connection fails tests.
/// </summary>
public sealed class SqlServerBffAtomicStoreTests
{
    [Fact]
    public async Task Roundtrip_renewal_and_durable_revocation_survive_new_store_instances()
        => await WithDatabaseAsync(async connectionString =>
        {
            using var provider = CreateProvider();
            var protection = provider.GetRequiredService<IDataProtectionProvider>();
            var writer = new SqlServerBffAtomicTicketStore(connectionString, protection);
            var reader = new SqlServerBffAtomicTicketStore(connectionString, protection);

            var original = NewTicket("alice");
            var key = await writer.StoreAsync(original);
            Assert.Equal("alice", (await reader.RetrieveAsync(key))?.Principal
                .FindFirst(ClaimTypes.NameIdentifier)?.Value);

            var renewal = Renewal(original, "bob");
            await reader.RenewAsync(key, renewal);
            Assert.Equal("bob", (await writer.RetrieveAsync(key))?.Principal
                .FindFirst(ClaimTypes.NameIdentifier)?.Value);

            await writer.RemoveAsync(key);
            Assert.Null(await reader.RetrieveAsync(key));
            await reader.RenewAsync(key, renewal);
            Assert.Null(await writer.RetrieveAsync(key));

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                SELECT COUNT(*) FROM dbo.FoundationBffSessions
                WHERE SessionKey = @key AND RevokedUtc IS NOT NULL
                    AND TicketCiphertext IS NULL AND Revision = 3
                """, connection);
            command.Parameters.Add("@key", SqlDbType.VarChar, 80).Value = key;
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
        });

    [Fact]
    public async Task Concurrent_cross_connection_renewals_and_logout_cannot_resurrect_ticket()
        => await WithDatabaseAsync(async connectionString =>
        {
            using var provider = CreateProvider();
            var protection = provider.GetRequiredService<IDataProtectionProvider>();
            var store1 = new SqlServerBffAtomicTicketStore(connectionString, protection);
            var store2 = new SqlServerBffAtomicTicketStore(connectionString, protection);
            var original = NewTicket("alice");
            var key = await store1.StoreAsync(original);
            var oldRenewal = Renewal(original, "alice");

            // Force both UPDATE operations to queue behind an external
            // exclusive SQL row lock; their eventual order must never cause
            // a removed session to be reinserted.
            await using var locker = new SqlConnection(connectionString);
            await locker.OpenAsync();
            await using var transaction = (SqlTransaction)await locker.BeginTransactionAsync(
                IsolationLevel.Serializable);
            await using (var lockCommand = new SqlCommand("""
                UPDATE dbo.FoundationBffSessions
                SET Revision = Revision
                WHERE SessionKey = @key;
                """, locker, transaction))
            {
                lockCommand.Parameters.Add("@key", SqlDbType.VarChar, 80).Value = key;
                await lockCommand.ExecuteNonQueryAsync();
            }

            var renewalTask = Task.Run(() => store1.RenewAsync(key, oldRenewal));
            var logoutTask = Task.Run(() => store2.RemoveAsync(key));
            await Task.Delay(75);
            await transaction.CommitAsync();
            await Task.WhenAll(renewalTask, logoutTask);

            Assert.Null(await store1.RetrieveAsync(key));
            Assert.Null(await store2.RetrieveAsync(key));
            await store2.RenewAsync(key, oldRenewal);
            Assert.Null(await store1.RetrieveAsync(key));
        });

    [Fact]
    public async Task Expired_missing_and_revoked_rows_are_never_recreated_by_renewal()
        => await WithDatabaseAsync(async connectionString =>
        {
            using var provider = CreateProvider();
            var protection = provider.GetRequiredService<IDataProtectionProvider>();
            var store = new SqlServerBffAtomicTicketStore(connectionString, protection);
            var original = NewTicket("alice");
            var key = await store.StoreAsync(original);
            var oldRenewal = Renewal(original, "bob");
            var absent = BffDefaults.TicketCachePrefix +
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
            await store.RenewAsync(absent, oldRenewal);
            Assert.Null(await store.RetrieveAsync(absent));

            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = new SqlCommand("""
                    UPDATE dbo.FoundationBffSessions
                    SET ExpiresUtc = DATEADD(MINUTE, -1, SYSUTCDATETIME())
                    WHERE SessionKey = @key;
                    """, connection);
                command.Parameters.Add("@key", SqlDbType.VarChar, 80).Value = key;
                await command.ExecuteNonQueryAsync();
            }
            await store.RenewAsync(key, oldRenewal);
            Assert.Null(await store.RetrieveAsync(key));

            await store.RemoveAsync(key);
            await store.RenewAsync(key, oldRenewal);
            Assert.Null(await store.RetrieveAsync(key));
        });

    [Fact]
    public async Task Absolute_lifetime_is_clamped_and_original_baseline_cannot_be_replaced()
        => await WithDatabaseAsync(async connectionString =>
        {
            using var provider = CreateProvider();
            var store = new SqlServerBffAtomicTicketStore(connectionString,
                provider.GetRequiredService<IDataProtectionProvider>());
            var issued = DateTimeOffset.UtcNow.AddMinutes(-30);
            var initial = NewTicket("alice");
            initial.Properties.IssuedUtc = issued;
            initial.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(40);
            var key = await store.StoreAsync(initial);

            var first = await store.RetrieveAsync(key);
            Assert.NotNull(first);
            // AuthenticationProperties persists expiry as Unix seconds, not ticks.
            Assert.Equal(issued.AddHours(24).ToUnixTimeSeconds(),
                first.Properties.ExpiresUtc!.Value.ToUnixTimeSeconds());

            var renewal = Renewal(initial, "bob");
            renewal.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(40);
            await store.RenewAsync(key, renewal);
            var after = await store.RetrieveAsync(key);
            Assert.NotNull(after);
            Assert.Equal(first.Properties.ExpiresUtc, after.Properties.ExpiresUtc);

            var forged = NewTicket("mallory");
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.RenewAsync(key, forged));
            Assert.Equal("bob", (await store.RetrieveAsync(key))?.Principal
                .FindFirst(ClaimTypes.NameIdentifier)?.Value);
        });

    [Fact]
    public async Task Tampered_ticket_and_unavailable_sql_fail_closed()
        => await WithDatabaseAsync(async connectionString =>
        {
            using var provider = CreateProvider();
            var store = new SqlServerBffAtomicTicketStore(connectionString,
                provider.GetRequiredService<IDataProtectionProvider>());
            var key = await store.StoreAsync(NewTicket("alice"));

            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = new SqlCommand("""
                    UPDATE dbo.FoundationBffSessions
                    SET TicketCiphertext = 0x01020304 WHERE SessionKey = @key;
                    """, connection);
                command.Parameters.Add("@key", SqlDbType.VarChar, 80).Value = key;
                await command.ExecuteNonQueryAsync();
            }
            Assert.Null(await store.RetrieveAsync(key));

            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = new SqlCommand(
                    "DROP TABLE dbo.FoundationBffSessions;", connection);
                await command.ExecuteNonQueryAsync();
            }
            await Assert.ThrowsAnyAsync<Exception>(() => store.RetrieveAsync(key));
        });

    [Fact]
    public async Task Revoked_rows_remain_visible_until_explicit_cleanup()
        => await WithDatabaseAsync(async connectionString =>
        {
            using var provider = CreateProvider();
            var store = new SqlServerBffAtomicTicketStore(connectionString,
                provider.GetRequiredService<IDataProtectionProvider>());
            var key = await store.StoreAsync(NewTicket("alice"));
            await store.RemoveAsync(key);
            await store.RemoveAsync(key); // idempotent
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                SELECT COUNT(*) FROM dbo.FoundationBffSessions
                WHERE SessionKey = @key AND RevokedUtc IS NOT NULL
                """, connection);
            command.Parameters.Add("@key", SqlDbType.VarChar, 80).Value = key;
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
        });

    private static AuthenticationTicket NewTicket(string name)
        => new(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, name)], BffDefaults.CookieScheme)),
            new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            }, BffDefaults.CookieScheme);

    private static AuthenticationTicket Renewal(AuthenticationTicket original, string name)
    {
        var replacement = NewTicket(name);
        foreach (var property in original.Properties.Items)
            replacement.Properties.Items[property.Key] = property.Value;
        return replacement;
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        return services.BuildServiceProvider();
    }

    private static async Task WithDatabaseAsync(Func<string, Task> action)
    {
        var raw = Environment.GetEnvironmentVariable("SHINERA_SQLSERVER_TEST_CONNECTION")
            ?? throw new InvalidOperationException(
                "SQL Server integration requires SHINERA_SQLSERVER_TEST_CONNECTION.");
        var dbName = "FoundationBff_" + Guid.NewGuid().ToString("N");
        var admin = new SqlConnectionStringBuilder(raw) { InitialCatalog = "master" };
        var database = new SqlConnectionStringBuilder(raw) { InitialCatalog = dbName };

        // SQL service may take a few seconds to start. Fail loudly if it
        // never becomes reachable; never silently skip integration tests.
        await using (var server = new SqlConnection(admin.ConnectionString))
        {
            Exception? last = null;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    await server.OpenAsync();
                    last = null;
                    break;
                }
                catch (SqlException error)
                {
                    last = error;
                    await Task.Delay(1000);
                }
            }
            if (last is not null)
                throw new InvalidOperationException("Disposable SQL Server unavailable.", last);

            await using var create = new SqlCommand(
                "CREATE DATABASE [" + dbName + "];", server);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using (var connection = new SqlConnection(database.ConnectionString))
            {
                await connection.OpenAsync();
                using var resource = typeof(SqlServerBffAtomicTicketStore).Assembly
                    .GetManifestResourceStream("Foundation.Bff.SqlServerSchema.sql")
                    ?? throw new InvalidOperationException("Embedded BFF SQL migration missing.");
                using var reader = new StreamReader(resource);
                var sql = await reader.ReadToEndAsync();
                await using var migration = new SqlCommand(sql, connection);
                await migration.ExecuteNonQueryAsync();
                // Safe rerun must not delete or change the newly created schema.
                await migration.ExecuteNonQueryAsync();
            }

            await action(database.ConnectionString);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using var server = new SqlConnection(admin.ConnectionString);
            await server.OpenAsync();
            await using var cleanup = new SqlCommand($"""
                ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{dbName}];
                """, server);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
