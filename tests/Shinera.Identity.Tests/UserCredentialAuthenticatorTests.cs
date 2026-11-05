using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Application.Services;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using OpenIddict.Abstractions;
using Xunit;

public sealed class UserCredentialAuthenticatorTests
{
    private const string Password = "Correct-Horse-Battery-9!";

    [Fact]
    public async Task System_user_authentication_returns_system_principal_without_tenant_claims()
    {
        await using var fixture = await Fixture.CreateAsync();
        var user = fixture.AddUser(null, UserType.System, "root");
        var service = new UserCredentialAuthenticator(fixture.Db, fixture.PasswordHasher);

        var principal = await service.AuthenticateAsync(
            "root", Password, IdentityLoginContext.System, null);

        Assert.NotNull(principal);
        Assert.Equal(user.Id.ToString(),
            principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value);
        Assert.Equal("system", principal.FindFirst("account_type")?.Value);
        Assert.Null(principal.FindFirst("tenant_id"));
        Assert.Null(principal.FindFirst("tenant"));
    }

    [Fact]
    public async Task Tenant_user_authentication_is_bound_to_the_requested_tenant()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = fixture.AddTenant("tenant-a");
        var user = fixture.AddUser(tenant.Id, UserType.Tenant, "operator");
        var service = new UserCredentialAuthenticator(fixture.Db, fixture.PasswordHasher);

        var principal = await service.AuthenticateAsync(
            "operator", Password, IdentityLoginContext.Tenant, tenant.Slug);

        Assert.NotNull(principal);
        Assert.Equal(user.Id.ToString(),
            principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value);
        Assert.Equal("tenant", principal.FindFirst("account_type")?.Value);
        Assert.Equal(tenant.Id.ToString(), principal.FindFirst("tenant_id")?.Value);
        Assert.Equal(tenant.Slug, principal.FindFirst("tenant")?.Value);

        Assert.Null(await service.AuthenticateAsync(
            "operator", Password, IdentityLoginContext.Tenant, "another-tenant"));
    }

    [Fact]
    public async Task Invalid_password_increments_failed_attempt_count()
    {
        await using var fixture = await Fixture.CreateAsync();
        var user = fixture.AddUser(null, UserType.System, "root");
        var service = new UserCredentialAuthenticator(fixture.Db, fixture.PasswordHasher);

        var principal = await service.AuthenticateAsync(
            "root", "wrong-password", IdentityLoginContext.System, null);

        Assert.Null(principal);
        Assert.Equal(1, user.AccessFailedCount);
        Assert.False(user.IsLockedOut);
    }

    [Fact]
    public async Task Fifth_failed_password_attempt_locks_account_and_blocks_correct_password()
    {
        await using var fixture = await Fixture.CreateAsync();
        var user = fixture.AddUser(null, UserType.System, "root");
        var service = new UserCredentialAuthenticator(fixture.Db, fixture.PasswordHasher);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Null(await service.AuthenticateAsync(
                "root", "wrong-password", IdentityLoginContext.System, null));
        }

        Assert.Equal(5, user.AccessFailedCount);
        Assert.True(user.IsLockedOut);
        Assert.True(user.LockoutEndUtc > DateTime.UtcNow);

        Assert.Null(await service.AuthenticateAsync(
            "root", Password, IdentityLoginContext.System, null));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public IdentityDbContext Db { get; }
        public PasswordHasher<User> PasswordHasher { get; } = new();

        private Fixture(SqliteConnection connection, IdentityDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new IdentityDbContext(options, new TenantContext());
            await db.Database.EnsureCreatedAsync();
            return new Fixture(connection, db);
        }

        public Tenant AddTenant(string slug)
        {
            var tenant = new Tenant(
                Guid.NewGuid(), null, $"Tenant {slug}", slug, $"{slug}.example.test");
            Db.Tenants.Add(tenant);
            Db.SaveChanges();
            return tenant;
        }

        public User AddUser(Guid? tenantId, UserType type, string username)
        {
            var user = new User(
                Guid.NewGuid(), tenantId, username, $"{username}@example.test",
                "Test", "User", type)
            {
                NormalizedUserName = username.Trim().ToUpperInvariant(),
                NormalizedEmail = $"{username}@example.test".ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N")
            };
            user.PasswordHash = PasswordHasher.HashPassword(user, Password);
            Db.Users.Add(user);
            Db.SaveChanges();
            return user;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
