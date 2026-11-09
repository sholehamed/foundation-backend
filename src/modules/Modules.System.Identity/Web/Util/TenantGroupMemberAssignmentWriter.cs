using System.Data;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Infrastructure.Persistence.Interceptors;
using Modules.System.Identity.Web.Endpoints;

namespace Modules.System.Identity.Web.Util;

public sealed class TenantGroupMemberValidationException(string message) : Exception(message);
public sealed class TenantGroupMemberConflictException(string message) : Exception(message);

/// <summary>
/// Single-user, group-bound UserGroup grant/revoke. Each change requires
/// explicit registered ACTION + live Owner, SQL Group RowVersion CAS,
/// checked user target, matching storage intent, and transaction-local audit.
/// Never writes GroupRole or Owner membership.
/// </summary>
public static class TenantGroupMemberAssignmentWriter
{
    public sealed record ChangeRequest(bool Assigned, string? ExpectedVersion);
    public sealed record MemberItem(Guid Id, string UserName, bool Assigned);
    public sealed record MembersPage(
        Guid GroupId, string GroupName, string Version, bool CanEdit,
        IReadOnlyList<MemberItem> Items, int Page, bool HasNext);

    public static async Task<bool> CanManageAsync(
        ITenantContext current, IdentityDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer() || current.IsSystemContext ||
            current.UserId is not Guid actor || actor == Guid.Empty ||
            current.ActiveTenantId is not Guid tenantId || tenantId == Guid.Empty ||
            current.HomeTenantId != tenantId ||
            !current.WritableTenantIds.Contains(tenantId) ||
            !current.ReadableTenantIds.Contains(tenantId))
            return false;

        // A legacy Owner fallback must not silently register this
        // privilege-changing ACTION. Validate the code/UI/catalog ownership.
        var registered = await db.Permissions.AsNoTracking()
            .Where(p => p.Code == TenantWorkspacePermissionCatalog.GroupsAssignMembers &&
                p.IsActive && p.Resource != null &&
                p.Resource.Code == TenantPermissionCatalogSync.ResourceCode &&
                p.Resource.IsActive && p.Resource.Module != null &&
                p.Resource.Module.Code == TenantPermissionCatalogSync.ModuleCode &&
                p.Resource.Module.IsActive &&
                db.PermissionUiResources.Any(link =>
                    link.PermissionId == p.Id && link.UiResource != null &&
                    link.UiResource.IsActive &&
                    link.UiResource.ResourceId == p.ResourceId &&
                    link.UiResource.Key == "tenant.workspace.groups.assign-members" &&
                    link.UiResource.Type == UiResourceType.ACTION &&
                    link.UiResource.Description == null))
            .Select(p => p.Id).ToListAsync(ct);
        if (registered.Count != 1) return false;

        return await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.GroupsRead, ct) &&
            await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.MembersRead, ct) &&
            await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.GroupsAssignMembers, ct) &&
            await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(current, db, ct);
    }

    private static async Task<bool> EligibleTargetAsync(
        ITenantContext current, IdentityDbContext db, Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty || userId == current.UserId ||
            current.ActiveTenantId is not Guid tenantId)
            return false;
        var eligible = await db.Users.AsNoTracking().AnyAsync(u =>
            u.Id == userId && u.TenantId == tenantId &&
            u.UserType == UserType.Tenant && u.IsActive, ct);
        if (!eligible) return false;
        // No Owner may be added/removed (including historical legacy Owner
        // roles, whether currently active or not).
        return !await db.UserRoles.AsNoTracking().AnyAsync(ur =>
            ur.TenantId == tenantId && ur.UserId == userId &&
            ur.Role.TenantId == tenantId &&
            ur.Role.NormalizedName == "OWNER" &&
            ur.Role.Scope != RoleScope.System, ct);
    }

    public static async Task<MembersPage?> ReadAsync(
        ITenantContext current, IdentityDbContext db, Guid groupId,
        int page = 1, CancellationToken ct = default)
    {
        if (groupId == Guid.Empty || page is < 1 or > 1000 ||
            current.IsSystemContext || current.ActiveTenantId is not Guid tenantId ||
            current.HomeTenantId != tenantId || !current.ReadableTenantIds.Contains(tenantId) ||
            !await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.GroupsRead, ct) ||
            !await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.MembersRead, ct))
            return null;

        var group = await db.Groups.AsNoTracking()
            .Where(g => g.Id == groupId && g.TenantId == tenantId)
            .Select(g => new { g.Id, g.Name, g.RowVersion }).SingleOrDefaultAsync(ct);
        if (group is null) return null;

        // Page through eligible members, rather than a whole-tenant
        // replacement: no truncation can silently revoke unrelated links.
        var actor = current.UserId;
        var all = db.Users.AsNoTracking().Where(u =>
            u.TenantId == tenantId && u.UserType == UserType.Tenant &&
            u.IsActive && u.Id != actor &&
            !db.UserRoles.Any(ur => ur.TenantId == tenantId &&
                ur.UserId == u.Id && ur.Role.TenantId == tenantId &&
                ur.Role.NormalizedName == "OWNER" &&
                ur.Role.Scope != RoleScope.System));
        var rows = await all.OrderBy(u => u.UserName).ThenBy(u => u.Id)
            .Skip((page - 1) * 20).Take(21)
            .Select(u => new MemberItem(u.Id, u.UserName,
                db.UserGroups.Any(ug =>
                    ug.TenantId == tenantId && ug.GroupId == groupId &&
                    ug.UserId == u.Id)))
            .ToListAsync(ct);
        return new MembersPage(group.Id, group.Name,
            Convert.ToBase64String(group.RowVersion ?? []),
            await CanManageAsync(current, db, ct),
            rows.Take(20).ToArray(), page, rows.Count > 20);
    }

    public static async Task<string> ChangeAsync(
        ITenantContext current, IdentityDbContext db, Guid groupId, Guid userId,
        ChangeRequest? request, TrustedTenantWriteScope scope,
        CancellationToken ct = default)
    {
        if (groupId == Guid.Empty || request is null ||
            !await CanManageAsync(current, db, ct) ||
            !await EligibleTargetAsync(current, db, userId, ct))
            throw new UnauthorizedAccessException("Tenant Group membership not authorized.");
        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(request.ExpectedVersion ?? "");
            if (expected.Length != 8) throw new FormatException();
        }
        catch (FormatException)
        {
            throw new TenantGroupMemberValidationException("Group version is invalid.");
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            if (!await CanManageAsync(current, db, ct) ||
                !await EligibleTargetAsync(current, db, userId, ct))
                throw new UnauthorizedAccessException("Tenant Group member privilege revoked.");

            var tenantId = current.ActiveTenantId!.Value;
            var group = await db.Groups.AsNoTracking().Where(g =>
                g.Id == groupId && g.TenantId == tenantId)
                .Select(g => new { g.RowVersion, g.IsActive }).SingleOrDefaultAsync(ct);
            if (group is null)
                throw new UnauthorizedAccessException("Group is outside active Tenant.");
            if (group.RowVersion is null || !group.RowVersion.SequenceEqual(expected))
                throw new TenantGroupMemberConflictException("Group version changed; reload.");

            var existing = await db.UserGroups.SingleOrDefaultAsync(ug =>
                ug.TenantId == tenantId && ug.GroupId == groupId && ug.UserId == userId, ct);
            if ((existing is not null) == request.Assigned)
            {
                await tx.CommitAsync(ct);
                return Convert.ToBase64String(expected);
            }
            if (request.Assigned && !group.IsActive)
                throw new TenantGroupMemberConflictException(
                    "Cannot newly add a member to an inactive Group.");

            // Serialize against all other Group membership and lifecycle
            // changes, even writes affecting another member in this Group.
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[Groups] SET [Description] = [Description]
                WHERE [Id] = {groupId} AND [TenantId] = {tenantId}
                    AND [RowVersion] = {expected}
                """, ct);
            if (affected != 1)
                throw new TenantGroupMemberConflictException("Group changed concurrently.");

            using (scope.BeginTenantGrantMutation(
                current, groupId, "UserGroup", [userId]))
            {
                if (request.Assigned)
                    db.UserGroups.Add(new UserGroup(Guid.NewGuid())
                    {
                        TenantId = tenantId, GroupId = groupId, UserId = userId
                    });
                else
                    db.UserGroups.Remove(existing!);
                await db.SaveChangesAsync(ct);
            }

            var auditId = Guid.NewGuid();
            var action = request.Assigned ? "grant" : "revoke";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantGroupMembershipAuditEntries]
                    ([Id],[TenantId],[ActorUserId],[GroupId],[TargetUserId],[Action])
                VALUES ({auditId},{tenantId},{current.UserId!.Value},
                    {groupId},{userId},{action})
                """, ct);
            var next = await db.Groups.AsNoTracking()
                .Where(g => g.Id == groupId && g.TenantId == tenantId)
                .Select(g => g.RowVersion).SingleAsync(ct);
            await tx.CommitAsync(ct);
            return Convert.ToBase64String(next);
        });
    }
}
