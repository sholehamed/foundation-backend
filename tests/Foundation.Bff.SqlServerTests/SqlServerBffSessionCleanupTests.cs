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
/// Real SQL Server cleanup tests; no in-memory DB, clock mocks or production DB.
/// Each test gets its own disposable test database and actual deployment DDL.
/// </summary>
public sealed class SqlServerBffSessionCleanupTests
{
    [Fact]
    public async Task Cleanup_removes_only_aged_rows_in_bounded_batches_and_is_idempotent()
        => await SqlServerBffAtomicStoreTests.WithDatabaseAsync(async connectionString =>
        {
            using var provider = CreateProvider();
            var store = new SqlServerBffAtomicTicketStore(connectionString,
                provider.GetRequiredService<IDataProtectionProvider>());
            var agedOriginal = NewTicket("aged-active");
            var agedActive = await store.StoreAsync(agedOriginal);
            var agedRevoked = await store.StoreAsync(NewTicket("aged-revoked"));
            var youngActive = await store.StoreAsync(NewTicket("young-active"));
            var youngRevoked = await store.StoreAsync(NewTicket("young-revoked"));
            await store.RemoveAsync(agedRevoked);
            await store.RemoveAsync(youngRevoked);

            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                // Fix one SQL clock instant to satisfy the original 24h CHECK,
                // even at high DATETIME2 precision.
                await using var command = new SqlCommand("""
                    DECLARE @clock DATETIME2(7) = SYSUTCDATETIME();
                    UPDATE dbo.FoundationBffSessions
                    SET CreatedUtc = DATEADD(DAY, -6, @clock),
                        AbsoluteExpiresUtc = DATEADD(DAY, -5, @clock),
                        ExpiresUtc = DATEADD(DAY, -5, @clock)
                    WHERE SessionKey IN (@agedActive, @agedRevoked);
                    """, connection);
                command.Parameters.Add("@agedActive", SqlDbType.VarChar, 80).Value = agedActive;
                command.Parameters.Add("@agedRevoked", SqlDbType.VarChar, 80).Value = agedRevoked;
                Assert.Equal(2, await command.ExecuteNonQueryAsync());
            }

            var cleanup = new SqlServerBffSessionCleanup(connectionString);
            Assert.Equal(1, await cleanup.DeleteExpiredBatchAsync(1));
            Assert.Equal(1, await cleanup.DeleteExpiredBatchAsync(1));
            Assert.Equal(0, await cleanup.DeleteExpiredBatchAsync(100));

            await using var verification = new SqlConnection(connectionString);
            await verification.OpenAsync();
            await using var check = new SqlCommand("""
                SELECT SessionKey FROM dbo.FoundationBffSessions;
                """, verification);
            var remaining = new HashSet<string>(StringComparer.Ordinal);
            await using (var reader = await check.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    remaining.Add(reader.GetString(0));
            }

            Assert.DoesNotContain(agedActive, remaining);
            Assert.DoesNotContain(agedRevoked, remaining);
            Assert.Contains(youngActive, remaining);
            Assert.Contains(youngRevoked, remaining);
            Assert.NotNull(await store.RetrieveAsync(youngActive));
            Assert.Null(await store.RetrieveAsync(youngRevoked));

            // Stale renewal never INSERTs deleted keys.
            await store.RenewAsync(agedActive, RenewedTicket("old", agedOriginal));
            Assert.Null(await store.RetrieveAsync(agedActive));
        });

    [Fact]
    public async Task Cleanup_rejects_invalid_batch_sizes_without_touching_database()
        => await SqlServerBffAtomicStoreTests.WithDatabaseAsync(async connectionString =>
        {
            var cleanup = new SqlServerBffSessionCleanup(connectionString);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => cleanup.DeleteExpiredBatchAsync(0));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => cleanup.DeleteExpiredBatchAsync(1001));
            Assert.Equal(0, await cleanup.DeleteExpiredBatchAsync(25));
        });

    [Fact]
    public async Task Cleanup_never_removes_rows_inside_retention_even_when_already_expired()
        => await SqlServerBffAtomicStoreTests.WithDatabaseAsync(async connectionString =>
        {
            using var provider = CreateProvider();
            var store = new SqlServerBffAtomicTicketStore(connectionString,
                provider.GetRequiredService<IDataProtectionProvider>());
            var key = await store.StoreAsync(NewTicket("retention"));
            await store.RemoveAsync(key);

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using (var command = new SqlCommand("""
                DECLARE @clock DATETIME2(7) = SYSUTCDATETIME();
                UPDATE dbo.FoundationBffSessions
                SET CreatedUtc = DATEADD(DAY, -2, @clock),
                    AbsoluteExpiresUtc = DATEADD(DAY, -1, @clock),
                    ExpiresUtc = DATEADD(DAY, -1, @clock)
                WHERE SessionKey = @key;
                """, connection))
            {
                command.Parameters.Add("@key", SqlDbType.VarChar, 80).Value = key;
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
            }

            var cleanup = new SqlServerBffSessionCleanup(connectionString);
            Assert.Equal(0, await cleanup.DeleteExpiredBatchAsync(100));
            await using var check = new SqlCommand("""
                SELECT COUNT(*) FROM dbo.FoundationBffSessions WHERE SessionKey = @key;
                """, connection);
            check.Parameters.Add("@key", SqlDbType.VarChar, 80).Value = key;
            Assert.Equal(1, Convert.ToInt32(await check.ExecuteScalarAsync()));
        });

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        return services.BuildServiceProvider();
    }

    private static AuthenticationTicket NewTicket(string userId)
        => new(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)], BffDefaults.CookieScheme)),
            new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            }, BffDefaults.CookieScheme);

    private static AuthenticationTicket RenewedTicket(
        string userId, AuthenticationTicket source)
    {
        var ticket = NewTicket(userId);
        foreach (var property in source.Properties.Items)
            ticket.Properties.Items[property.Key] = property.Value;
        return ticket;
    }
}
