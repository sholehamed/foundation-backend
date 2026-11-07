using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;
using OpenIddict.Abstractions;
using Xunit;

public sealed class EffectivePermissionEvaluatorTests
{
    private static readonly Guid MasterScope = DefaultSeedData.MasterTenant.Id;

    [Fact]
    public async Task System_master_role_grant_is_live_and_direct_deny_takes_precedence()
    {
        await using var f = await Fixture.CreateAsync();
        f.Tenant(MasterScope, "master");
        var user = f.User(null, UserType.System);
        var permission = f.Permission("USERS.READ");
        var role = f.Role(MasterScope, "system-reader");
        f.AssignRole(user.Id, role.Id, MasterScope);
        f.AssignRolePermission(role.Id, permission.Id, MasterScope);
        f.Save();

        var evaluator = new EffectivePermissionEvaluator(f.Db);
        Assert.True(await evaluator.HasPermissionAsync(
            user.Id, MasterScope, UserType.System, "USERS.READ"));
        Assert.False(await evaluator.HasPermissionAsync(
            user.Id, MasterScope, UserType.Tenant, "USERS.READ"));

        var denied = f.Direct(user.Id, MasterScope, permission.Id, false);
        f.Save();
        Assert.False(await evaluator.HasPermissionAsync(
            user.Id, MasterScope, UserType.System, "USERS.READ"));

        f.Db.UserPermissions.Remove(denied);
        f.Save();
        Assert.True(await evaluator.HasPermissionAsync(
            user.Id, MasterScope, UserType.System, "USERS.READ"));

        role.IsActive = false;
        f.Save();
        Assert.False(await evaluator.HasPermissionAsync(
            user.Id, MasterScope, UserType.System, "USERS.READ"));

        f.Direct(user.Id, MasterScope, permission.Id, true);
        f.Save();
        Assert.True(await evaluator.HasPermissionAsync(
            user.Id, MasterScope, UserType.System, "USERS.READ"));

        permission.IsActive = false;
        f.Save();
        Assert.False(await evaluator.HasPermissionAsync(
            user.Id, MasterScope, UserType.System, "USERS.READ"));
    }

    [Fact]
    public async Task Tenant_cannot_inherit_other_tenant_grants_or_use_system_scope()
    {
        await using var f = await Fixture.CreateAsync();
        f.Tenant(MasterScope, "master");
        var own = f.Tenant(Guid.NewGuid(), "own");
        var other = f.Tenant(Guid.NewGuid(), "other");
        var user = f.User(own.Id, UserType.Tenant);
        var permission = f.Permission("ORDERS.READ");
        var otherRole = f.Role(other.Id, "foreign");
        f.AssignRole(user.Id, otherRole.Id, other.Id);
        f.AssignRolePermission(otherRole.Id, permission.Id, other.Id);
        f.Save();

        var evaluator = new EffectivePermissionEvaluator(f.Db);
        Assert.False(await evaluator.HasPermissionAsync(
            user.Id, own.Id, UserType.Tenant, "ORDERS.READ"));
        Assert.False(await evaluator.HasPermissionAsync(
            user.Id, other.Id, UserType.Tenant, "ORDERS.READ"));
        Assert.False(await evaluator.HasPermissionAsync(
            user.Id, MasterScope, UserType.System, "ORDERS.READ"));

        var direct = f.Direct(user.Id, own.Id, permission.Id, true);
        f.Save();
        Assert.True(await evaluator.HasPermissionAsync(
            user.Id, own.Id, UserType.Tenant, "ORDERS.READ"));

        own.IsActive = false;
        f.Save();
        Assert.False(await evaluator.HasPermissionAsync(
            user.Id, own.Id, UserType.Tenant, "ORDERS.READ"));

        own.IsActive = true;
        user.IsActive = false;
        f.Save();
        Assert.False(await evaluator.HasPermissionAsync(
            user.Id, own.Id, UserType.Tenant, "ORDERS.READ"));
    }

    [Fact]
    public async Task Group_inherited_permission_requires_active_group_and_scoped_role()
    {
        await using var f = await Fixture.CreateAsync();
        var tenant = f.Tenant(Guid.NewGuid(), "group-test");
        var user = f.User(tenant.Id, UserType.Tenant);
        var permission = f.Permission("APPOINTMENTS.READ");
        var role = f.Role(tenant.Id, "reader");
        f.AssignRolePermission(role.Id, permission.Id, tenant.Id);
        var group = new Group(Guid.NewGuid())
        {
            RowVersion = [1], TenantId = tenant.Id, Name = "Team",
            NormalizedName = "TEAM", IsActive = true
        };
        f.Db.Groups.Add(group);
        f.Db.UserGroups.Add(new UserGroup(Guid.NewGuid())
        {
            RowVersion = [1], TenantId = tenant.Id,
            UserId = user.Id, GroupId = group.Id
        });
        f.Db.GroupRoles.Add(new GroupRole(Guid.NewGuid())
        {
            RowVersion = [1], TenantId = tenant.Id,
            GroupId = group.Id, RoleId = role.Id
        });
        f.Save();

        var evaluator = new EffectivePermissionEvaluator(f.Db);
        Assert.True(await evaluator.HasPermissionAsync(
            user.Id, tenant.Id, UserType.Tenant, "APPOINTMENTS.READ"));

        group.IsActive = false;
        f.Save();
        Assert.False(await evaluator.HasPermissionAsync(
            user.Id, tenant.Id, UserType.Tenant, "APPOINTMENTS.READ"));

        group.IsActive = true;
        role.IsActive = false;
        f.Save();
        Assert.False(await evaluator.HasPermissionAsync(
            user.Id, tenant.Id, UserType.Tenant, "APPOINTMENTS.READ"));
    }

    [Fact]
    public async Task System_requirement_rejects_tenant_principal_even_with_master_grant()
    {
        await using var f = await Fixture.CreateAsync();
        f.Tenant(MasterScope, "master");
        var user = f.User(null, UserType.System);
        var permission = f.Permission("USERS.READ");
        f.Direct(user.Id, MasterScope, permission.Id, true);
        f.Save();

        var requirement = new SystemPermissionRequirement("users", "read");
        var good = new ClaimsPrincipal(new ClaimsIdentity([
            new(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
            new("account_type", "system")
        ], "test"));
        var goodContext = new AuthorizationHandlerContext([requirement], good, null);
        await new SystemPermissionHandler(f.Db).HandleAsync(goodContext);
        Assert.True(goodContext.HasSucceeded);

        var forged = new ClaimsPrincipal(new ClaimsIdentity([
            new(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
            new("account_type", "system"),
            new("tenant_id", MasterScope.ToString())
        ], "test"));
        var deniedContext = new AuthorizationHandlerContext([requirement], forged, null);
        await new SystemPermissionHandler(f.Db).HandleAsync(deniedContext);
        Assert.False(deniedContext.HasSucceeded);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public IdentityDbContext Db { get; }

        private Fixture(IdentityDbContext db) => Db = db;

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
            var db = new IdentityDbContext(options, new TenantContext());
            await db.Database.EnsureCreatedAsync();
            return new Fixture(db);
        }

        public Tenant Tenant(Guid id, string slug)
        {
            var tenant = new Tenant(id, null, slug, slug, slug + ".example.test")
            {
                RowVersion = [1], IsActive = true
            };
            Db.Tenants.Add(tenant);
            return tenant;
        }

        public User User(Guid? tenantId, UserType type)
        {
            var id = Guid.NewGuid();
            var user = new User(id, tenantId, "test-" + id.ToString("N"),
                id.ToString("N") + "@example.test", "Test", "User", type)
            {
                RowVersion = [1], IsActive = true,
                SecurityStamp = "test-stamp", PasswordHash = "test-only",
                NormalizedUserName = id.ToString("N").ToUpperInvariant(),
                NormalizedEmail = id.ToString("N").ToUpperInvariant() + "@EXAMPLE.TEST"
            };
            Db.Users.Add(user);
            return user;
        }

        public Permission Permission(string code)
        {
            var permission = new Permission(Guid.NewGuid(), code, null, code + " display", null)
            { RowVersion = [1], IsActive = true };
            Db.Permissions.Add(permission);
            return permission;
        }

        public Role Role(Guid scope, string name)
        {
            var role = new Role(scope, name, null) { RowVersion = [1], IsActive = true };
            Db.Roles.Add(role);
            return role;
        }

        public void AssignRole(Guid userId, Guid roleId, Guid scope) =>
            Db.UserRoles.Add(new UserRole(scope, userId, roleId) { RowVersion = [1] });

        public void AssignRolePermission(Guid roleId, Guid permissionId, Guid scope) =>
            Db.RolePermissions.Add(new RolePermission(scope, roleId, permissionId) { RowVersion = [1] });

        public UserPermission Direct(Guid userId, Guid scope, Guid permissionId, bool granted)
        {
            var assignment = new UserPermission(Guid.NewGuid())
            {
                RowVersion = [1], UserId = userId, TenantId = scope,
                PermissionId = permissionId, IsGranted = granted
            };
            Db.UserPermissions.Add(assignment);
            return assignment;
        }

        public void Save() => Db.SaveChanges();
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
