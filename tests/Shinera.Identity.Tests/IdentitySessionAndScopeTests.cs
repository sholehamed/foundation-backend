using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Application.Services;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using OpenIddict.Abstractions;
using Xunit;

public sealed class IdentitySessionAndScopeTests
{
    private static readonly Guid MasterId = DefaultSeedData.MasterTenant.Id;

    [Fact]
    public async Task System_account_receives_only_master_scoped_roles_and_permissions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var user = fixture.AddUser(null, UserType.System);
        var otherTenant = fixture.AddTenant("another-tenant");
        var systemRole = fixture.AddRole(MasterId, "platform-operator");
        var tenantRole = fixture.AddRole(otherTenant.Id, "tenant-admin");
        fixture.AssignRole(user, systemRole, MasterId);
        fixture.AssignRole(user, tenantRole, otherTenant.Id);
        fixture.AssignPermission(systemRole, MasterId, "platform.manage");
        fixture.AssignPermission(tenantRole, otherTenant.Id, "tenant.secrets");
        fixture.Save();

        var principal = await fixture.Factory.CreateAsync(user, IdentityLoginContext.System, null, 12345);
        var roles = principal.FindAll(OpenIddictConstants.Claims.Role).Select(x => x.Value).ToArray();
        var permissions = principal.FindAll("permission").Select(x => x.Value).ToArray();

        Assert.Contains("platform-operator", roles);
        Assert.DoesNotContain("tenant-admin", roles);
        Assert.Contains("Platform Manage", permissions);
        Assert.DoesNotContain("Tenant Secrets", permissions);
        Assert.Null(principal.FindFirst("tenant_id"));
    }

    [Fact]
    public async Task Tenant_account_receives_only_owning_tenant_roles()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.AddTenant("tenant-one");
        var other = fixture.AddTenant("tenant-two");
        var user = fixture.AddUser(first.Id, UserType.Tenant);
        var allowedRole = fixture.AddRole(first.Id, "cashier");
        var forbiddenRole = fixture.AddRole(other.Id, "other-owner");
        fixture.AssignRole(user, allowedRole, first.Id);
        fixture.AssignRole(user, forbiddenRole, other.Id);
        fixture.Save();

        var principal = await fixture.Factory.CreateAsync(user, IdentityLoginContext.Tenant, first, 12345);
        var roles = principal.FindAll(OpenIddictConstants.Claims.Role).Select(x => x.Value).ToArray();

        Assert.Contains("cashier", roles);
        Assert.DoesNotContain("other-owner", roles);
        Assert.Equal(first.Id.ToString(), principal.FindFirst("tenant_id")?.Value);
        Assert.Equal(first.Slug, principal.FindFirst("tenant")?.Value);
    }

    [Fact]
    public async Task Invalidated_security_stamp_rejects_identity_session()
    {
        await using var fixture = await Fixture.CreateAsync();
        var user = fixture.AddUser(null, UserType.System);
        fixture.Save();
        var cookie = await fixture.Factory.CreateAsync(user, IdentityLoginContext.System, null, 12345);

        user.SecurityStamp = "new-stamp";
        fixture.Save();

        Assert.Null(await fixture.Validator.ValidateAsync(cookie));
    }

    [Fact]
    public async Task Disabled_user_rejects_existing_identity_session()
    {
        await using var fixture = await Fixture.CreateAsync();
        var user = fixture.AddUser(null, UserType.System);
        fixture.Save();
        var cookie = await fixture.Factory.CreateAsync(user, IdentityLoginContext.System, null, 12345);

        user.IsActive = false;
        fixture.Save();

        Assert.Null(await fixture.Validator.ValidateAsync(cookie));
    }

    [Fact]
    public async Task Disabled_tenant_rejects_existing_identity_session()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = fixture.AddTenant("tenant-one");
        var user = fixture.AddUser(tenant.Id, UserType.Tenant);
        fixture.Save();
        var cookie = await fixture.Factory.CreateAsync(user, IdentityLoginContext.Tenant, tenant, 12345);

        tenant.IsActive = false;
        fixture.Save();

        Assert.Null(await fixture.Validator.ValidateAsync(cookie));
    }

    [Fact]
    public async Task Removed_role_is_not_restored_from_stale_cookie_claims()
    {
        await using var fixture = await Fixture.CreateAsync();
        var user = fixture.AddUser(null, UserType.System);
        var role = fixture.AddRole(MasterId, "platform-operator");
        var assignment = fixture.AssignRole(user, role, MasterId);
        fixture.Save();
        var cookie = await fixture.Factory.CreateAsync(user, IdentityLoginContext.System, null, 12345);
        Assert.Contains(cookie.FindAll(OpenIddictConstants.Claims.Role), x => x.Value == "platform-operator");

        fixture.Db.UserRoles.Remove(assignment);
        fixture.Save();

        var fresh = await fixture.Validator.ValidateAsync(cookie);
        Assert.NotNull(fresh);
        Assert.DoesNotContain(fresh.FindAll(OpenIddictConstants.Claims.Role), x => x.Value == "platform-operator");
    }

    [Fact]
    public async Task Tenant_cookie_cannot_be_reinterpreted_as_system_session()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = fixture.AddTenant("tenant-one");
        var user = fixture.AddUser(tenant.Id, UserType.Tenant);
        fixture.Save();

        var cookie = await fixture.Factory.CreateAsync(user, IdentityLoginContext.Tenant, tenant, 12345);
        var forgedClaims = cookie.Claims.Where(x => x.Type is not ("tenant_id" or "tenant" or "account_type"))
            .Concat([new Claim("account_type", "system")]);
        var forged = new ClaimsPrincipal(new ClaimsIdentity(forgedClaims, "test"));

        Assert.Null(await fixture.Validator.ValidateAsync(forged));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public IdentityDbContext Db { get; }
        public IIdentityPrincipalFactory Factory { get; }
        public IIdentitySessionValidator Validator { get; }

        private Fixture(IdentityDbContext db)
        {
            Db = db;
            Factory = new IdentityPrincipalFactory(db, new DefaultSystemTenantScope());
            Validator = new IdentitySessionValidator(db, Factory);
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
            var entity = new Tenant(Guid.NewGuid(), null, slug, slug, slug + ".example.test")
                { RowVersion = [1] };
            Db.Tenants.Add(entity);
            return entity;
        }

        public User AddUser(Guid? tenantId, UserType type)
        {
            var id = Guid.NewGuid();
            var user = new User(id, tenantId, "operator-" + id.ToString("N"),
                id.ToString("N") + "@example.test", "Example", "User", type)
            {
                SecurityStamp = "current-stamp",
                PasswordHash = "test-only",
                RowVersion = [1],
                NormalizedUserName = "OPERATOR",
                NormalizedEmail = id.ToString("N").ToUpperInvariant() + "@EXAMPLE.TEST"
            };
            Db.Users.Add(user);
            return user;
        }

        public Role AddRole(Guid tenantId, string name)
        {
            var role = new Role(tenantId, name, null) { RowVersion = [1] };
            Db.Roles.Add(role);
            return role;
        }

        public UserRole AssignRole(User user, Role role, Guid tenantId)
        {
            var assignment = new UserRole(tenantId, user.Id, role.Id)
                { RowVersion = [1] };
            Db.UserRoles.Add(assignment);
            return assignment;
        }

        public void AssignPermission(Role role, Guid tenantId, string code)
        {
            var label = string.Join(" ", code.Split('.').Select(x =>
                char.ToUpperInvariant(x[0]) + x[1..]));
            var permission = new Permission(Guid.NewGuid(), code, null, label, null)
                { RowVersion = [1] };
            Db.Permissions.Add(permission);
            Db.RolePermissions.Add(new RolePermission(tenantId, role.Id, permission.Id)
                { RowVersion = [1] });
        }

        public void Save() => Db.SaveChanges();

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
