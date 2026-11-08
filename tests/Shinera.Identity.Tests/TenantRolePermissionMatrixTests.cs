using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;
using Xunit;

public sealed class TenantRolePermissionMatrixTests
{
    [Fact]
    public async Task Owner_reads_only_local_role_grants_and_registration_state()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var current = NewTenant(tenantId, actorId);
        await using var db = Open(current);
        db.Tenants.AddRange(new Tenant(tenantId, null, "Own", "own", "own.test")
            { IsActive = true, RowVersion = [1] },
            new Tenant(foreignTenant, null, "Other", "other", "other.test")
            { IsActive = true, RowVersion = [1] });
        db.Users.Add(new User(actorId, tenantId, "owner", "owner@local.test",
            "Local", "Owner", UserType.Tenant)
            { IsActive = true, NormalizedUserName = "OWNER",
              NormalizedEmail = "OWNER@LOCAL.TEST", PasswordHash = "fixture", RowVersion = [1] });
        var owner = new Role(tenantId, "Owner", "Local Owner")
            { Scope = RoleScope.Tenant, RowVersion = [1] };
        var operatorRole = new Role(tenantId, "Operator", null)
            { Scope = RoleScope.Tenant, RowVersion = [1] };
        var foreignRole = new Role(foreignTenant, "Foreign role", null)
            { Scope = RoleScope.Tenant, RowVersion = [1] };
        db.Roles.AddRange(owner, operatorRole, foreignRole);
        db.UserRoles.Add(new UserRole(tenantId, actorId, owner.Id) { RowVersion = [1] });
        var permission = new Permission
        {
            Code = TenantWorkspacePermissionCatalog.GroupsRead,
            Name = "Groups", IsActive = true, RowVersion = [1]
        };
        db.Permissions.Add(permission);
        db.RolePermissions.Add(new RolePermission(tenantId, operatorRole.Id, permission.Id)
            { RowVersion = [1] });
        await db.SaveChangesAsync();

        var http = new DefaultHttpContext();
        var response = Assert.IsType<Ok<TenantWorkspace.TenantRolePermissionMatrix>>(
            await TenantWorkspace.GetRolePermissions(current, db, http, operatorRole.Id, default));
        Assert.Equal(9, response.Value!.Permissions.Count);
        Assert.Equal("Operator", response.Value.RoleName);
        var group = Assert.Single(response.Value.Permissions.Where(x =>
            x.Code == TenantWorkspacePermissionCatalog.GroupsRead));
        Assert.True(group.Registered);
        Assert.True(group.Granted);
        var branch = Assert.Single(response.Value.Permissions.Where(x =>
            x.Code == TenantWorkspacePermissionCatalog.BranchesManage));
        Assert.False(branch.Registered);
        Assert.False(branch.Granted);
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());

        Assert.IsType<NotFound>(await TenantWorkspace.GetRolePermissions(
            current, db, new DefaultHttpContext(), foreignRole.Id, default));
    }

    [Fact]
    public async Task Nonowner_is_forbidden_even_when_role_ID_is_local()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var current = NewTenant(tenantId, actorId);
        await using var db = Open(current);
        db.Tenants.Add(new Tenant(tenantId, null, "Own", "own", "own.test")
            { IsActive = true, RowVersion = [1] });
        db.Users.Add(new User(actorId, tenantId, "staff", "staff@local.test", "Local",
            "Staff", UserType.Tenant) { IsActive = true, NormalizedUserName = "STAFF",
            NormalizedEmail = "STAFF@LOCAL.TEST", PasswordHash = "fixture", RowVersion = [1] });
        var role = new Role(tenantId, "Viewer", null)
            { Scope = RoleScope.Tenant, RowVersion = [1] };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        Assert.IsType<ForbidHttpResult>(await TenantWorkspace.GetRolePermissions(
            current, db, new DefaultHttpContext(), role.Id, default));
    }

    private static TenantContext NewTenant(Guid tenantId, Guid actor)
    {
        var current = new TenantContext();
        current.InitializeTenant(actor, tenantId, [tenantId], [tenantId], tenantId);
        return current;
    }
    private static IdentityDbContext Open(TenantContext tenant)
    {
        var db = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, tenant);
        return db;
    }
}
