using System.Security.Claims;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;
using OpenIddict.Abstractions;
using Xunit;

public sealed class CurrentSystemUiAccessTests
{
    private static readonly Guid Master = DefaultSeedData.MasterTenant.Id;

    [Fact]
    public async Task Live_role_grants_require_parent_read_and_child_action_with_direct_deny_priority()
    {
        await using var db = await DbAsync();
        await RegisterAsync(db);
        var user = NewUser();
        var role = new Role(Master, "role-admin", null)
            { IsActive = true, RowVersion = [1] };
        db.Users.Add(user);
        db.Roles.Add(role);
        db.UserRoles.Add(new UserRole(Master, user.Id, role.Id) { RowVersion = [1] });
        var read = await db.Permissions.SingleAsync(x => x.Code == "ROLES.READ");
        var edit = await db.Permissions.SingleAsync(x => x.Code == "ROLES.ASSIGN-PERMISSIONS");
        db.RolePermissions.Add(new RolePermission(Master, role.Id, edit.Id) { RowVersion = [1] });
        await db.SaveChangesAsync();

        var service = new CurrentSystemUiAccessService(db);
        var principal = Principal(user);
        Assert.False(await service.IsAllowedAsync(principal, "system.roles.permissions"));
        Assert.False(await service.IsAllowedAsync(principal, "system.roles.action.assign-permissions"));

        db.RolePermissions.Add(new RolePermission(Master, role.Id, read.Id) { RowVersion = [1] });
        await db.SaveChangesAsync();
        Assert.True(await service.IsAllowedAsync(principal, "system.roles.permissions"));
        Assert.True(await service.IsAllowedAsync(principal, "system.roles.action.assign-permissions"));
        Assert.True(await service.IsAllowedAsync(principal, "system.roles"));

        var denied = new UserPermission(Guid.NewGuid())
        {
            TenantId = Master, UserId = user.Id, PermissionId = edit.Id,
            IsGranted = false, RowVersion = [1]
        };
        db.UserPermissions.Add(denied);
        await db.SaveChangesAsync();
        Assert.True(await service.IsAllowedAsync(principal, "system.roles"));
        Assert.False(await service.IsAllowedAsync(principal, "system.roles.permissions"));
        Assert.False(await service.IsAllowedAsync(principal, "system.roles.action.assign-permissions"));

        db.UserPermissions.Remove(denied);
        await db.SaveChangesAsync();
        Assert.True(await service.IsAllowedAsync(principal, "system.roles.permissions"));

        role.IsActive = false;
        await db.SaveChangesAsync();
        Assert.False(await service.IsAllowedAsync(principal, "system.roles.permissions"));
    }

    [Fact]
    public async Task Inactive_ui_owner_permission_and_invalid_principal_fail_closed()
    {
        await using var db = await DbAsync();
        await RegisterAsync(db);
        var user = NewUser();
        db.Users.Add(user);
        var read = await db.Permissions.SingleAsync(x => x.Code == "ROLES.READ");
        var assign = await db.Permissions.SingleAsync(x => x.Code == "ROLES.ASSIGN-PERMISSIONS");
        db.UserPermissions.AddRange(
            new UserPermission(Guid.NewGuid()) {
                TenantId = Master, UserId = user.Id, PermissionId = read.Id,
                IsGranted = true, RowVersion = [1]
            },
            new UserPermission(Guid.NewGuid()) {
                TenantId = Master, UserId = user.Id, PermissionId = assign.Id,
                IsGranted = true, RowVersion = [1]
            });
        await db.SaveChangesAsync();

        var access = new CurrentSystemUiAccessService(db);
        var principal = Principal(user);
        Assert.True(await access.IsAllowedAsync(principal, "system.roles.permissions"));
        Assert.False(await access.IsAllowedAsync(principal, "system.users.create"));
        Assert.False(await access.IsAllowedAsync(principal, "system.roles.unlisted"));

        var page = await db.UiResources.SingleAsync(x => x.Key == "system.roles.permissions");
        page.IsActive = false;
        await db.SaveChangesAsync();
        Assert.False(await access.IsAllowedAsync(principal, "system.roles.permissions"));
        page.IsActive = true;

        var root = await db.UiResources.SingleAsync(x => x.Key == "system.roles");
        root.IsActive = false;
        await db.SaveChangesAsync();
        Assert.False(await access.IsAllowedAsync(principal, "system.roles.permissions"));
        root.IsActive = true;

        var wrong = await db.Permissions.SingleAsync(x => x.Code == "GROUPS.READ");
        db.PermissionUiResources.Add(new PermissionUiResource(wrong.Id, page.Id) { RowVersion = [1] });
        await db.SaveChangesAsync();
        Assert.False(await access.IsAllowedAsync(principal, "system.roles.permissions"));

        Assert.False(await access.IsAllowedAsync(new ClaimsPrincipal(new ClaimsIdentity()),
            "system.roles.permissions"));
        Assert.False(await access.IsAllowedAsync(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
            new Claim("account_type", "tenant"),
            new Claim("tenant_id", Master.ToString())
        ], "test")), "system.roles.permissions"));
        Assert.False(await access.IsAllowedAsync(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
            new Claim("account_type", "system"),
            new Claim("tenant_id", Master.ToString())
        ], "test")), "system.roles.permissions"));
    }

    [Fact]
    public async Task Principal_revocation_and_inactive_master_tenant_never_leave_snapshot_access()
    {
        await using var db = await DbAsync();
        await RegisterAsync(db);
        var user = NewUser();
        db.Users.Add(user);
        var read = await db.Permissions.SingleAsync(x => x.Code == "ROLES.READ");
        db.UserPermissions.Add(new UserPermission(Guid.NewGuid())
        {
            TenantId = Master, UserId = user.Id, PermissionId = read.Id,
            IsGranted = true, RowVersion = [1]
        });
        await db.SaveChangesAsync();
        var evaluator = new CurrentSystemUiAccessService(db);
        Assert.True(await evaluator.IsAllowedAsync(Principal(user), "system.roles"));

        user.IsActive = false;
        await db.SaveChangesAsync();
        Assert.False(await evaluator.IsAllowedAsync(Principal(user), "system.roles"));
        user.IsActive = true;
        var master = await db.Tenants.SingleAsync(x => x.Id == Master);
        master.IsActive = false;
        await db.SaveChangesAsync();
        Assert.False(await evaluator.IsAllowedAsync(Principal(user), "system.roles"));
    }

    private static ClaimsPrincipal Principal(User user) => new(
        new ClaimsIdentity([
            new(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
            new("account_type", "system")
        ], "test"));

    private static User NewUser()
    {
        var id = Guid.NewGuid();
        return new User(id, null, "ui-" + id.ToString("N"), id.ToString("N") + "@test.invalid",
            "UI", "User", UserType.System)
        {
            IsActive = true, RowVersion = [1], PasswordHash = "test-only",
            SecurityStamp = "test-stamp",
            NormalizedUserName = id.ToString("N").ToUpperInvariant(),
            NormalizedEmail = id.ToString("N").ToUpperInvariant() + "@TEST.INVALID"
        };
    }

    private static async Task<IdentityDbContext> DbAsync()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var db = new IdentityDbContext(options, new TenantContext());
        await db.Database.EnsureCreatedAsync();
        db.Tenants.Add(new Tenant(Master, null, "System", "master", "localhost")
            { IsActive = true, RowVersion = [1] });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task RegisterAsync(IdentityDbContext db)
    {
        var r = await new FeatureRegistrySyncService(db, new DefaultEndpointDataSource([]))
            .ApplyAsync();
        Assert.True(r.CanApply);
    }
}
