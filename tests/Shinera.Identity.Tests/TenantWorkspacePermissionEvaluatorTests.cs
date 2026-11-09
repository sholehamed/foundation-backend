using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;
using Xunit;

public sealed class TenantWorkspacePermissionEvaluatorTests
{
    private const string Members = TenantWorkspacePermissionCatalog.MembersRead;
    private const string Branches = TenantWorkspacePermissionCatalog.BranchesManage;

    private static void AssertForbidden(IResult result)
    {
        var response = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Direct_tenant_role_grant_allows_page_and_branch_write_but_foreign_role_cannot()
    {
        var tenantId = Guid.NewGuid();
        var foreignId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var current = Current(tenantId, userId, writable: true);
        await using var db = await OpenAsync(current);
        await SeedTenantUserAsync(db, tenantId, userId);
        db.Tenants.Add(new Tenant(foreignId, null, "Other", "other", "other.test")
            { IsActive = true, RowVersion = [1] });
        var pagePermission = AddPermission(db, Members);
        var branchPermission = AddPermission(db, Branches);
        var localRole = new Role(tenantId, "Manager", null)
            { Scope = RoleScope.Tenant, RowVersion = [1] };
        var foreignRole = new Role(foreignId, "Foreign Manager", null)
            { Scope = RoleScope.Tenant, RowVersion = [1] };
        db.Roles.AddRange(localRole, foreignRole);
        db.UserRoles.Add(new UserRole(tenantId, userId, localRole.Id) { RowVersion = [1] });
        // A malformed cross-tenant role assignment must not leak a grant.
        db.UserRoles.Add(new UserRole(tenantId, userId, foreignRole.Id) { RowVersion = [1] });
        db.RolePermissions.AddRange(
            new RolePermission(foreignId, foreignRole.Id, pagePermission.Id) { RowVersion = [1] },
            new RolePermission(tenantId, localRole.Id, branchPermission.Id) { RowVersion = [1] });
        await db.SaveChangesAsync();

        Assert.False(await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db, Members, default));
        Assert.True(await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db, Branches, default));
        AssertForbidden(await TenantWorkspace.GetMembers(
            current, db, new DefaultHttpContext()));
        Assert.IsType<Ok<TenantWorkspace.WorkspaceBranchDto>>(await TenantWorkspace.CreateBranch(
            current, db, new DefaultHttpContext(),
            new TenantWorkspace.BranchWriteRequest("North", "Tehran", "North street", null), default));

        db.RolePermissions.Add(new RolePermission(tenantId, localRole.Id, pagePermission.Id)
            { RowVersion = [1] });
        await db.SaveChangesAsync();

        Assert.True(await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db, Members, default));
        Assert.IsType<Ok<TenantWorkspace.DirectoryPage<TenantWorkspace.WorkspaceMemberDto>>>(
            await TenantWorkspace.GetMembers(current, db, new DefaultHttpContext()));
    }

    [Fact]
    public async Task Direct_deny_overrides_role_group_and_legacy_owner_then_revocation_applies_live()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var current = Current(tenantId, actor, writable: true);
        await using var db = await OpenAsync(current);
        await SeedTenantUserAsync(db, tenantId, actor);
        var permission = AddPermission(db, Branches);
        var owner = new Role(tenantId, "Owner", "Legacy owner")
            { Scope = 0, RowVersion = [1] };
        db.Roles.Add(owner);
        db.UserRoles.Add(new UserRole(tenantId, actor, owner.Id) { RowVersion = [1] });
        await db.SaveChangesAsync();

        Assert.True(await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db, Branches, default));
        var deny = new UserPermission { TenantId = tenantId, UserId = actor,
            PermissionId = permission.Id, IsGranted = false, RowVersion = [1] };
        db.UserPermissions.Add(deny);
        await db.SaveChangesAsync();

        Assert.False(await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db, Branches, default));
        AssertForbidden(await TenantWorkspace.CreateBranch(
            current, db, new DefaultHttpContext(),
            new TenantWorkspace.BranchWriteRequest("Denied", "City", "Street", null), default));
        db.UserPermissions.Remove(deny);
        await db.SaveChangesAsync();
        Assert.True(await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db, Branches, default));

        var assignment = await db.UserRoles.SingleAsync();
        db.UserRoles.Remove(assignment);
        await db.SaveChangesAsync();
        Assert.False(await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db, Branches, default));
    }

    [Fact]
    public async Task Active_local_group_grant_works_and_inactive_group_or_foreign_role_fails()
    {
        var tenantId = Guid.NewGuid();
        var foreignId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var current = Current(tenantId, actor, writable: false);
        await using var db = await OpenAsync(current);
        await SeedTenantUserAsync(db, tenantId, actor);
        db.Tenants.Add(new Tenant(foreignId, null, "Other", "other", "other.test")
            { IsActive = true, RowVersion = [1] });
        var permission = AddPermission(db, Members);
        var role = new Role(tenantId, "Reviewer", null)
            { Scope = RoleScope.Tenant, RowVersion = [1] };
        var group = new Group { TenantId = tenantId, Name = "Reviewers",
            NormalizedName = "REVIEWERS", RowVersion = [1], IsActive = true };
        db.Roles.Add(role);
        db.Groups.Add(group);
        db.UserGroups.Add(new UserGroup { UserId = actor, TenantId = tenantId,
            GroupId = group.Id, RowVersion = [1] });
        db.GroupRoles.Add(new GroupRole { GroupId = group.Id, RoleId = role.Id,
            TenantId = tenantId, RowVersion = [1] });
        db.RolePermissions.Add(new RolePermission(tenantId, role.Id, permission.Id)
            { RowVersion = [1] });
        await db.SaveChangesAsync();

        Assert.True(await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db, Members, default));
        group.IsActive = false;
        await db.SaveChangesAsync();
        Assert.False(await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db, Members, default));
        group.IsActive = true;
        role.Scope = RoleScope.System;
        await db.SaveChangesAsync();
        Assert.False(await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db, Members, default));
    }

    [Fact]
    public async Task Missing_inactive_or_duplicate_catalog_definitions_fail_closed()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var current = Current(tenantId, userId, true);
        await using var db = await OpenAsync(current);
        await SeedTenantUserAsync(db, tenantId, userId);
        var permission = AddPermission(db, Members);
        db.UserPermissions.Add(new UserPermission { TenantId = tenantId, UserId = userId,
            PermissionId = permission.Id, IsGranted = true, RowVersion = [1] });
        await db.SaveChangesAsync();

        Assert.True(await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db, Members, default));
        Assert.False(await TenantWorkspacePermissionEvaluator.AllowsAsync(
            current, db, "SYSTEM.USERS.READ", default));
        permission.IsActive = false;
        await db.SaveChangesAsync();
        Assert.False(await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db, Members, default));
        permission.IsActive = true;
        db.Permissions.Add(new Permission { Code = Members, Name = "Duplicate", RowVersion = [1] });
        await db.SaveChangesAsync();
        Assert.False(await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db, Members, default));

        var system = new TenantContext();
        system.InitializeSystem(Guid.NewGuid(), true);
        Assert.False(await TenantWorkspacePermissionEvaluator.AllowsAsync(system, db, Members, default));
        var readOnly = Current(tenantId, userId, writable: false);
        Assert.False(await TenantWorkspacePermissionEvaluator.AllowsAsync(readOnly, db, Branches, default));
    }

    [Fact]
    public async Task Ui_snapshot_is_scoped_no_store_and_write_cap_requires_writable_scope()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var current = Current(tenantId, actor, writable: false);
        await using var db = await OpenAsync(current);
        await SeedTenantUserAsync(db, tenantId, actor);
        var permission = AddPermission(db, Branches);
        db.UserPermissions.Add(new UserPermission { TenantId = tenantId, UserId = actor,
            PermissionId = permission.Id, IsGranted = true, RowVersion = [1] });
        await db.SaveChangesAsync();

        var http = new DefaultHttpContext();
        var result = await TenantWorkspace.GetCurrentUiAccess(current, db, http, default);
        var envelope = Assert.IsAssignableFrom<IValueHttpResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(envelope.Value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var items = json.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(19, items.Length);
        Assert.False(items.Single(x => x.GetProperty("key").GetString() == Branches)
            .GetProperty("allowed").GetBoolean());
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());

        var system = new TenantContext();
        system.InitializeSystem(Guid.NewGuid(), true);
        AssertForbidden(
            await TenantWorkspace.GetCurrentUiAccess(system, db, new DefaultHttpContext(), default));
    }

    private static Permission AddPermission(IdentityDbContext db, string key)
    {
        var permission = new Permission { Code = key, Name = key, RowVersion = [1], IsActive = true };
        db.Permissions.Add(permission);
        return permission;
    }

    private static async Task SeedTenantUserAsync(IdentityDbContext db, Guid tenantId, Guid actor)
    {
        db.Tenants.Add(new Tenant(tenantId, null, "Workspace", "workspace", "own.test")
            { IsActive = true, RowVersion = [1] });
        db.Users.Add(new User(actor, tenantId, "actor", "actor@local.test", "Tenant", "Actor",
            UserType.Tenant)
            {
                IsActive = true, NormalizedUserName = "ACTOR",
                NormalizedEmail = "ACTOR@LOCAL.TEST", PasswordHash = "fixture-hash", RowVersion = [1]
            });
        await db.SaveChangesAsync();
    }

    private static TenantContext Current(Guid tenantId, Guid userId, bool writable)
    {
        var current = new TenantContext();
        current.InitializeTenant(userId, tenantId, [tenantId],
            writable ? [tenantId] : [], tenantId);
        return current;
    }

    private static async Task<IdentityDbContext> OpenAsync(TenantContext current)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddInterceptors(new BranchRowVersionInterceptor()).Options;
        var db = new IdentityDbContext(options, current);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class BranchRowVersionInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            foreach (var entry in eventData.Context!.ChangeTracker.Entries<Branch>())
                if (entry.State == EntityState.Added && entry.Entity.RowVersion is null)
                    entry.Entity.RowVersion = [1];
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
