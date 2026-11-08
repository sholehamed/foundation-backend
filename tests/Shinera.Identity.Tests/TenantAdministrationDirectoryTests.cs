using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;
using Xunit;

public sealed class TenantAdministrationDirectoryTests
{
    private static void AssertForbidden(IResult result)
    {
        var response = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Owner_sees_only_own_member_role_and_group_records()
    {
        var tenantId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var current = Current(tenantId, actorId);
        await using var db = await OpenAsync(current);
        db.Tenants.AddRange(
            new Tenant(tenantId, null, "Own", "own", "own.test") { RowVersion = [1], IsActive = true },
            new Tenant(otherId, null, "Other", "other", "other.test") { RowVersion = [1], IsActive = true });
        var owner = new User(actorId, tenantId, "owner", "owner@test.local", "Tenant", "Owner", UserType.Tenant)
            { RowVersion = [1], NormalizedEmail = "OWNER@TEST.LOCAL", PasswordHash = "fixture-hash", NormalizedUserName = "OWNER", IsActive = true };
        var staff = new User(tenantId, "staff", "staff@test.local", "Staff", "Person", UserType.Tenant)
            { RowVersion = [1], NormalizedEmail = "STAFF@TEST.LOCAL", PasswordHash = "fixture-hash", NormalizedUserName = "STAFF" };
        var foreign = new User(otherId, "secret", "secret@test.local", "Other", "Tenant", UserType.Tenant)
            { RowVersion = [1], NormalizedEmail = "SECRET@TEST.LOCAL", PasswordHash = "fixture-hash", NormalizedUserName = "SECRET" };
        db.Users.AddRange(owner, staff, foreign);
        var ownerRole = new Role(tenantId, "Owner", "Local owner") { RowVersion = [1], Scope = RoleScope.Tenant };
        db.Roles.AddRange(ownerRole, new Role(otherId, "Secret role", null) { RowVersion = [1], Scope = RoleScope.Tenant });
        db.UserRoles.Add(new UserRole(tenantId, actorId, ownerRole.Id) { RowVersion = [1] });
        db.Groups.AddRange(
            new Group { TenantId = tenantId, Name = "Operators", NormalizedName = "OPERATORS", RowVersion = [1] },
            new Group { TenantId = otherId, Name = "Secret group", NormalizedName = "SECRET GROUP", RowVersion = [1] });
        await db.SaveChangesAsync();

        var http = new DefaultHttpContext();
        var permissions = await TenantWorkspace.GetAdministrationCapabilities(current, db, http, new ConfigurationBuilder().Build(), default);
        Assert.IsAssignableFrom<IResult>(permissions);
        var members = Assert.IsType<Ok<TenantWorkspace.DirectoryPage<TenantWorkspace.WorkspaceMemberDto>>>(
            await TenantWorkspace.GetMembers(current, db, http));
        Assert.Equal(2, members.Value!.Items.Count);
        Assert.DoesNotContain(members.Value.Items, x => x.UserName == "secret");
        Assert.False(members.Value.HasNext);
        var roles = Assert.IsType<Ok<TenantWorkspace.DirectoryPage<TenantWorkspace.WorkspaceRoleDto>>>(
            await TenantWorkspace.GetRoles(current, db, http));
        Assert.Single(roles.Value!.Items);
        Assert.Equal("Owner", roles.Value.Items[0].Name);
        var groups = Assert.IsType<Ok<TenantWorkspace.DirectoryPage<TenantWorkspace.WorkspaceGroupDto>>>(
            await TenantWorkspace.GetGroups(current, db, http));
        Assert.Single(groups.Value!.Items);
        Assert.Equal("Operators", groups.Value.Items[0].Name);
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Staff_and_system_context_cannot_read_owner_directory()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var current = Current(tenantId, staffId);
        await using var db = await OpenAsync(current);
        db.Tenants.Add(new Tenant(tenantId, null, "Own", "own", "own.test")
            { RowVersion = [1], IsActive = true });
        db.Users.AddRange(
            new User(ownerId, tenantId, "owner", "owner@test.local", "Owner", "Person", UserType.Tenant)
                { RowVersion = [1], NormalizedEmail = "OWNER@TEST.LOCAL", PasswordHash = "fixture-hash", NormalizedUserName = "OWNER" },
            new User(staffId, tenantId, "staff", "staff@test.local", "Staff", "Person", UserType.Tenant)
                { RowVersion = [1], NormalizedEmail = "STAFF@TEST.LOCAL", PasswordHash = "fixture-hash", NormalizedUserName = "STAFF" });
        var role = new Role(tenantId, "Owner", "Local owner") { RowVersion = [1], Scope = RoleScope.Tenant };
        db.Roles.Add(role);
        db.UserRoles.Add(new UserRole(tenantId, ownerId, role.Id) { RowVersion = [1] });
        await db.SaveChangesAsync();

        AssertForbidden(await TenantWorkspace.GetMembers(current, db, new DefaultHttpContext()));
        AssertForbidden(await TenantWorkspace.GetRoles(current, db, new DefaultHttpContext()));
        AssertForbidden(await TenantWorkspace.GetGroups(current, db, new DefaultHttpContext()));

        var system = new TenantContext();
        system.InitializeSystem(Guid.NewGuid(), true);
        AssertForbidden(await TenantWorkspace.GetMembers(system, db, new DefaultHttpContext()));
        AssertForbidden(await TenantWorkspace.GetAdministrationCapabilities(
            system, db, new DefaultHttpContext(), new ConfigurationBuilder().Build(), default));
    }

    [Fact]
    public async Task Owner_directory_denies_if_owner_role_revoked_or_tenant_inactive()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var current = Current(tenantId, actorId);
        await using var db = await OpenAsync(current);
        var tenant = new Tenant(tenantId, null, "Own", "own", "own.test")
            { RowVersion = [1], IsActive = true };
        db.Tenants.Add(tenant);
        db.Users.Add(new User(actorId, tenantId, "owner", "owner@test.local", "Owner", "Person", UserType.Tenant)
            { RowVersion = [1], NormalizedEmail = "OWNER@TEST.LOCAL", PasswordHash = "fixture-hash", NormalizedUserName = "OWNER" });
        var role = new Role(tenantId, "Owner", null) { RowVersion = [1] }; // legacy RoleScope = 0
        db.Roles.Add(role);
        var assignment = new UserRole(tenantId, actorId, role.Id) { RowVersion = [1] };
        db.UserRoles.Add(assignment);
        await db.SaveChangesAsync();

        Assert.IsType<Ok<TenantWorkspace.DirectoryPage<TenantWorkspace.WorkspaceMemberDto>>>(
            await TenantWorkspace.GetMembers(current, db, new DefaultHttpContext()));
        db.UserRoles.Remove(assignment);
        await db.SaveChangesAsync();
        AssertForbidden(await TenantWorkspace.GetMembers(current, db, new DefaultHttpContext()));
        db.UserRoles.Add(new UserRole(tenantId, actorId, role.Id) { RowVersion = [1] });
        tenant.IsActive = false;
        await db.SaveChangesAsync();
        AssertForbidden(await TenantWorkspace.GetMembers(current, db, new DefaultHttpContext()));
    }

    private static TenantContext Current(Guid tenantId, Guid userId)
    {
        var current = new TenantContext();
        current.InitializeTenant(userId, tenantId, [tenantId], [tenantId], tenantId);
        return current;
    }

    private static async Task<IdentityDbContext> OpenAsync(TenantContext current)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var db = new IdentityDbContext(options, current);
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}
