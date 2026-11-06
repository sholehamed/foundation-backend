using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Modules.System.Bff;
using Modules.System.Identity.Application.Services;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Server.Bff;
using Xunit;

namespace Shinera.Identity.Tests;

public sealed class BffLiveIdentityValidationTests
{
    [Fact]
    public async Task System_stamp_and_disabled_or_locked_account_invalidate_bff_snapshot()
    {
        await using var h = await Fixture.CreateAsync();
        var user = h.AddUser(null, UserType.System);
        await h.SaveAsync();
        var principal = await h.Factory.CreateAsync(user, IdentityLoginContext.System, null, 12345);
        var issued = await h.Validator.CaptureAsync(principal);
        Assert.NotNull(issued);
        Assert.True(await h.Validator.IsCurrentAsync(principal, issued!));

        user.SecurityStamp = "rotated";
        await h.SaveAsync();
        Assert.False(await h.Validator.IsCurrentAsync(principal, issued!));
        user.SecurityStamp = "initial";
        user.IsLockedOut = true;
        await h.SaveAsync();
        Assert.False(await h.Validator.IsCurrentAsync(principal, issued!));
        user.IsLockedOut = false;
        user.IsActive = false;
        await h.SaveAsync();
        Assert.Null(await h.Validator.CaptureAsync(principal));
    }

    [Fact]
    public async Task Disabled_or_renamed_tenant_invalidates_existing_bff_snapshot()
    {
        await using var h = await Fixture.CreateAsync();
        var tenant = h.AddTenant("tenant-a");
        var user = h.AddUser(tenant.Id, UserType.Tenant);
        await h.SaveAsync();
        var principal = await h.Factory.CreateAsync(user, IdentityLoginContext.Tenant, tenant, 12345);
        var issued = await h.Validator.CaptureAsync(principal);
        Assert.NotNull(issued);
        Assert.True(await h.Validator.IsCurrentAsync(principal, issued!));

        tenant.IsActive = false;
        await h.SaveAsync();
        Assert.False(await h.Validator.IsCurrentAsync(principal, issued!));
        tenant.IsActive = true;
        tenant.Slug = "tenant-renamed";
        await h.SaveAsync();
        Assert.False(await h.Validator.IsCurrentAsync(principal, issued!));
    }

    [Fact]
    public async Task Role_removal_or_addition_invalidates_persisted_ticket_authority()
    {
        await using var h = await Fixture.CreateAsync();
        var user = h.AddUser(null, UserType.System);
        var role = new Role(DefaultSeedData.MasterTenant.Id, "operator", null) { RowVersion = [1] };
        var assignment = new UserRole(DefaultSeedData.MasterTenant.Id, user.Id, role.Id)
            { RowVersion = [1] };
        h.Db.Roles.Add(role);
        h.Db.UserRoles.Add(assignment);
        await h.SaveAsync();

        var principal = await h.Factory.CreateAsync(user, IdentityLoginContext.System, null, 12345);
        var issued = await h.Validator.CaptureAsync(principal);
        Assert.NotNull(issued);
        h.Db.UserRoles.Remove(assignment);
        await h.SaveAsync();
        Assert.False(await h.Validator.IsCurrentAsync(principal, issued!));

        var now = await h.Factory.CreateAsync(user, IdentityLoginContext.System, null, 12345);
        var snapshot = await h.Validator.CaptureAsync(now);
        Assert.NotNull(snapshot);
        h.Db.UserRoles.Add(new UserRole(DefaultSeedData.MasterTenant.Id, user.Id, role.Id)
            { RowVersion = [1] });
        await h.SaveAsync();
        Assert.False(await h.Validator.IsCurrentAsync(now, snapshot!));
    }

    [Fact]
    public async Task Forged_account_tenant_or_role_claims_and_missing_snapshot_are_denied()
    {
        await using var h = await Fixture.CreateAsync();
        var tenant = h.AddTenant("tenant-a");
        var user = h.AddUser(tenant.Id, UserType.Tenant);
        await h.SaveAsync();
        var principal = await h.Factory.CreateAsync(user, IdentityLoginContext.Tenant, tenant, 12345);
        var issued = await h.Validator.CaptureAsync(principal);
        Assert.NotNull(issued);
        Assert.False(await h.Validator.IsCurrentAsync(principal,
            new BffSessionAccessSnapshot("", issued!.GrantsFingerprint)));

        ClaimsPrincipal Alter(string type, string value) =>
            new(new ClaimsIdentity(principal.Claims.Where(x => x.Type != type)
                .Append(new Claim(type, value)), "test"));
        Assert.Null(await h.Validator.CaptureAsync(Alter("tenant", "another")));
        Assert.Null(await h.Validator.CaptureAsync(Alter("account_type", "system")));
        var roleForged = new ClaimsPrincipal(new ClaimsIdentity(
            principal.Claims.Append(new Claim("role", "super-admin")), "test"));
        Assert.Null(await h.Validator.CaptureAsync(roleForged));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public IdentityDbContext Db { get; }
        public IdentityPrincipalFactory Factory { get; }
        public IdentityBffSessionAccessValidator Validator { get; }

        private Fixture(IdentityDbContext db)
        {
            Db = db;
            Factory = new IdentityPrincipalFactory(db, new DefaultSystemTenantScope());
            Validator = new IdentityBffSessionAccessValidator(db, Factory);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
            var db = new IdentityDbContext(options, new TenantContext());
            await db.Database.EnsureCreatedAsync();
            return new Fixture(db);
        }

        public Tenant AddTenant(string slug)
        {
            var tenant = new Tenant(Guid.NewGuid(), null, slug, slug, slug + ".test")
                { RowVersion = [1] };
            Db.Tenants.Add(tenant);
            return tenant;
        }

        public User AddUser(Guid? tenantId, UserType type)
        {
            var id = Guid.NewGuid();
            var user = new User(id, tenantId, "operator-" + id.ToString("N"),
                "test@example.test", "Test", "User", type)
            {
                SecurityStamp = "initial",
                PasswordHash = "test-only",
                RowVersion = [1],
                NormalizedUserName = "OPERATOR",
                NormalizedEmail = "TEST@EXAMPLE.TEST"
            };
            Db.Users.Add(user);
            return user;
        }

        public Task SaveAsync() => Db.SaveChangesAsync();
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
