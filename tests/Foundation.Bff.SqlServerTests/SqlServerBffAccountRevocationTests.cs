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

public sealed class SqlServerBffAccountRevocationTests
{
    [Fact]
    public async Task Logout_all_revokes_only_existing_tickets_for_authenticated_subject()
        => await SqlServerBffAtomicStoreTests.WithDatabaseAsync(async cs =>
        {
            using var provider = CreateProvider();
            var storeA = NewStore(cs, provider);
            var storeB = NewStore(cs, provider);
            var alice = Guid.NewGuid();
            var bob = Guid.NewGuid();
            var first = Ticket(alice);
            var second = Ticket(alice);
            var outsider = Ticket(bob);
            var key1 = await storeA.StoreAsync(first);
            var key2 = await storeB.StoreAsync(second);
            var otherKey = await storeB.StoreAsync(outsider);

            var revokedCount = await storeA.RevokeAllAsync(alice);
            Assert.Equal(2, revokedCount);
            Assert.Null(await storeB.RetrieveAsync(key1));
            Assert.Null(await storeA.RetrieveAsync(key2));
            Assert.NotNull(await storeA.RetrieveAsync(otherKey));

            await storeB.RenewAsync(key1, Renewal(first, alice));
            Assert.Null(await storeA.RetrieveAsync(key1));
            Assert.Equal(0, await storeB.RevokeAllAsync(alice));
            Assert.Equal(1, await storeB.RevokeAllAsync(bob));
            Assert.Null(await storeA.RetrieveAsync(otherKey));

            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync();
            await using var cmd = new SqlCommand("""
                SELECT COUNT(*) FROM dbo.FoundationBffSessions
                WHERE SubjectId = @subject AND RevokedUtc IS NOT NULL
                    AND TicketCiphertext IS NULL;
                """, connection);
            cmd.Parameters.Add("@subject", SqlDbType.UniqueIdentifier).Value = alice;
            Assert.Equal(2, Convert.ToInt32(await cmd.ExecuteScalarAsync()));

            // A fresh successful OIDC authorization can issue a new BFF
            // session later; upstream token/session revocation is NOT done.
            var fresh = await storeA.StoreAsync(Ticket(alice));
            Assert.NotNull(await storeB.RetrieveAsync(fresh));
        });

    [Fact]
    public async Task SQL_subject_binding_rejects_missing_or_changed_user_on_renewal()
        => await SqlServerBffAtomicStoreTests.WithDatabaseAsync(async cs =>
        {
            using var provider = CreateProvider();
            var store = NewStore(cs, provider);
            var subject = Guid.NewGuid();
            var ticket = Ticket(subject);
            var key = await store.StoreAsync(ticket);

            var changed = Renewal(ticket, Guid.NewGuid());
            await store.RenewAsync(key, changed); // UPDATE 0, never changes owner
            Assert.Equal(subject.ToString("D"),
                (await store.RetrieveAsync(key))?.Principal.FindFirst("sub")?.Value);

            var missing = Ticket(subject);
            if (missing.Principal.Identity is ClaimsIdentity identity)
                identity.RemoveClaim(identity.FindFirst("sub")!);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.StoreAsync(missing));
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.RevokeAllAsync(Guid.Empty));
        });

    [Fact]
    public async Task Concurrent_renewal_and_account_revocation_never_resurrect_existing_key()
        => await SqlServerBffAtomicStoreTests.WithDatabaseAsync(async cs =>
        {
            using var provider = CreateProvider();
            var renewer = NewStore(cs, provider);
            var revoker = NewStore(cs, provider);
            var subject = Guid.NewGuid();
            var initial = Ticket(subject);
            var key = await renewer.StoreAsync(initial);

            await using var locker = new SqlConnection(cs);
            await locker.OpenAsync();
            await using var transaction = (SqlTransaction)await locker.BeginTransactionAsync(
                IsolationLevel.Serializable);
            await using (var command = new SqlCommand("""
                UPDATE dbo.FoundationBffSessions
                SET Revision = Revision WHERE SessionKey = @key;
                """, locker, transaction))
            {
                command.Parameters.Add("@key", SqlDbType.VarChar, 80).Value = key;
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
            }

            var renewal = Task.Run(() => renewer.RenewAsync(key, Renewal(initial, subject)));
            var revoke = Task.Run(() => revoker.RevokeAllAsync(subject));
            await Task.Delay(60);
            await transaction.CommitAsync();
            await Task.WhenAll(renewal, revoke);

            Assert.Null(await renewer.RetrieveAsync(key));
            await renewer.RenewAsync(key, Renewal(initial, subject));
            Assert.Null(await revoker.RetrieveAsync(key));
        });

    private static SqlServerBffAtomicTicketStore NewStore(
        string connectionString, ServiceProvider provider)
        => new(connectionString, provider.GetRequiredService<IDataProtectionProvider>());

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        return services.BuildServiceProvider();
    }

    private static AuthenticationTicket Ticket(Guid subject)
        => new(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", subject.ToString("D")),
             new Claim("account_type", "system")], BffDefaults.CookieScheme)),
            new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            }, BffDefaults.CookieScheme);

    private static AuthenticationTicket Renewal(
        AuthenticationTicket original, Guid subject)
    {
        var next = Ticket(subject);
        foreach (var property in original.Properties.Items)
            next.Properties.Items[property.Key] = property.Value;
        return next;
    }
}
