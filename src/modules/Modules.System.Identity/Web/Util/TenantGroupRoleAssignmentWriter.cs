using System.Data;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Infrastructure.Persistence.Interceptors;
using Modules.System.Identity.Web.Endpoints;

namespace Modules.System.Identity.Web.Util;

public sealed class TenantGroupRoleValidationException(string message) : Exception(message);
public sealed class TenantGroupRoleConflictException(string message) : Exception(message);

/// <summary>
/// Exact-pair Owner-only GroupRole mutations. Each change holds a SQL Server
/// serializable transaction, CASes the Group RowVersion, writes only its
/// (GroupId, RoleId) pair in a temporary reviewed storage intent, and audits
/// the grant/revoke atomically. No bulk replacement or OWNER role assignment.
/// </summary>
public static class TenantGroupRoleAssignmentWriter
{
    public sealed record ChangeRequest(bool Assigned, string? ExpectedVersion);
    public sealed record RoleItem(Guid Id, string Name, bool IsActive, bool Assigned);
    public sealed record RolesPage(
        Guid GroupId, string GroupName, string Version, bool GroupIsActive,
        bool CanEdit, IReadOnlyList<RoleItem> Items, int Page, bool HasNext);

    public static async Task<bool> CanManageAsync(
        ITenantContext current, IdentityDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer() || current.IsSystemContext ||
            current.UserId is not Guid actor || actor == Guid.Empty ||
            current.ActiveTenantId is not Guid tenantId || tenantId == Guid.Empty ||
            current.HomeTenantId != tenantId ||
            !current.ReadableTenantIds.Contains(tenantId) ||
            !current.WritableTenantIds.Contains(tenantId))
            return false;

        // Owner compatibility alone MUST NEVER enable this new grant path.
        // Require an operator-reviewed active ACTION+UiResource registration.
        var registration = await db.Permissions.AsNoTracking()
            .Where(p => p.Code == TenantWorkspacePermissionCatalog.GroupsAssignRoles &&
                p.IsActive && p.Resource != null &&
                p.Resource.Code == TenantPermissionCatalogSync.ResourceCode &&
                p.Resource.IsActive && p.Resource.Module != null &&
                p.Resource.Module.Code == TenantPermissionCatalogSync.ModuleCode &&
                p.Resource.Module.IsActive &&
                db.PermissionUiResources.Any(link =>
                    link.PermissionId == p.Id && link.UiResource != null &&
                    link.UiResource.IsActive && link.UiResource.ResourceId == p.ResourceId &&
                    link.UiResource.Key == "tenant.workspace.groups.assign-roles" &&
                    link.UiResource.Type == UiResourceType.ACTION &&
                    link.UiResource.Description == null))
            .Select(p => p.Id).ToListAsync(ct);
        if (registration.Count != 1) return false;

        return await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.GroupsRead, ct) &&
            await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesRead, ct) &&
            await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.GroupsAssignRoles, ct) &&
            await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(current, db, ct);
    }

    private static async Task<bool> EligibleGroupAsync(
        ITenantContext current, IdentityDbContext db, Guid groupId,
        CancellationToken ct)
    {
        if (groupId == Guid.Empty || current.ActiveTenantId is not Guid tenantId ||
            current.UserId is not Guid actor)
            return false;

        if (!await db.Groups.AsNoTracking().AnyAsync(g =>
                g.Id == groupId && g.TenantId == tenantId, ct))
            return false;

        // Prevent self-escalation through role grants to a Group that the
        // current Owner also belongs to. Do not depend on active state:
        // re-activating a Group later must not bypass this safety check.
        if (await db.UserGroups.AsNoTracking().AnyAsync(ug =>
                ug.TenantId == tenantId && ug.GroupId == groupId &&
                ug.UserId == actor, ct))
            return false;

        // Legacy Owner membership in a group must not be a privilege bridge.
        return !await db.UserGroups.AsNoTracking().AnyAsync(ug =>
            ug.TenantId == tenantId && ug.GroupId == groupId &&
            db.UserRoles.Any(ur =>
                ur.TenantId == tenantId && ur.UserId == ug.UserId &&
                ur.Role.TenantId == tenantId &&
                ur.Role.NormalizedName == "OWNER" &&
                ur.Role.Scope != RoleScope.System), ct);
    }

    public static async Task<RolesPage?> ReadAsync(
        ITenantContext current, IdentityDbContext db, Guid groupId,
        int page = 1, CancellationToken ct = default)
    {
        if (groupId == Guid.Empty || page is < 1 or > 1000 ||
            current.IsSystemContext || current.ActiveTenantId is not Guid tenantId ||
            current.HomeTenantId != tenantId ||
            !current.ReadableTenantIds.Contains(tenantId) ||
            !await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.GroupsRead, ct) ||
            !await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesRead, ct))
            return null;

        var group = await db.Groups.AsNoTracking()
            .Where(g => g.Id == groupId && g.TenantId == tenantId)
            .Select(g => new { g.Id, g.Name, g.IsActive, g.RowVersion })
            .SingleOrDefaultAsync(ct);
        if (group is null) return null;

        // Page over ordinary roles, not an incomplete whole-Group replacement.
        var roles = await db.Roles.AsNoTracking()
            .Where(r => r.TenantId == tenantId &&
                r.Scope == RoleScope.Tenant && r.NormalizedName != "OWNER")
            .OrderBy(r => r.Name).ThenBy(r => r.Id)
            .Skip((page - 1) * 20).Take(21)
            .Select(r => new RoleItem(r.Id, r.Name, r.IsActive,
                db.GroupRoles.Any(gr =>
                    gr.TenantId == tenantId && gr.GroupId == groupId &&
                    gr.RoleId == r.Id)))
            .ToListAsync(ct);

        return new RolesPage(group.Id, group.Name,
            Convert.ToBase64String(group.RowVersion ?? []), group.IsActive,
            await CanManageAsync(current, db, ct) &&
                await EligibleGroupAsync(current, db, groupId, ct),
            roles.Take(20).ToArray(), page, roles.Count > 20);
    }

    public static async Task<string> ChangeAsync(
        ITenantContext current, IdentityDbContext db, Guid groupId, Guid roleId,
        ChangeRequest? request, TrustedTenantWriteScope scope,
        CancellationToken ct = default)
    {
        if (groupId == Guid.Empty || roleId == Guid.Empty || request is null ||
            !await CanManageAsync(current, db, ct) ||
            !await EligibleGroupAsync(current, db, groupId, ct))
            throw new UnauthorizedAccessException("Group role assignment not authorized.");
        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(request.ExpectedVersion ?? "");
            if (expected.Length != 8) throw new FormatException();
        }
        catch (FormatException)
        {
            throw new TenantGroupRoleValidationException("Group version is invalid.");
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            if (!await CanManageAsync(current, db, ct) ||
                !await EligibleGroupAsync(current, db, groupId, ct))
                throw new UnauthorizedAccessException("Group role authority revoked.");

            var tenantId = current.ActiveTenantId!.Value;
            var group = await db.Groups.AsNoTracking()
                .Where(g => g.Id == groupId && g.TenantId == tenantId)
                .Select(g => new { g.RowVersion, g.IsActive }).SingleOrDefaultAsync(ct);
            if (group is null)
                throw new UnauthorizedAccessException("Foreign group.");
            if (group.RowVersion is null || !group.RowVersion.SequenceEqual(expected))
                throw new TenantGroupRoleConflictException("Group changed, reload.");

            // Explicit TenantId, RoleScope, normalized OWNER name and activity;
            // disallow foreign/System/Owner Role targets even if client guesses ID.
            var role = await db.Roles.AsNoTracking()
                .Where(r => r.Id == roleId && r.TenantId == tenantId &&
                    r.Scope == RoleScope.Tenant && r.NormalizedName != "OWNER")
                .Select(r => new { r.Name, r.IsActive }).SingleOrDefaultAsync(ct);
            if (role is null)
                throw new UnauthorizedAccessException("Role is not ordinary local Tenant role.");

            var existing = await db.GroupRoles.SingleOrDefaultAsync(gr =>
                gr.TenantId == tenantId && gr.GroupId == groupId && gr.RoleId == roleId, ct);
            if ((existing is not null) == request.Assigned)
            {
                await tx.CommitAsync(ct);
                return Convert.ToBase64String(expected);
            }
            if (request.Assigned && (!group.IsActive || !role.IsActive))
                throw new TenantGroupRoleConflictException(
                    "Cannot grant inactive Role or add Role to inactive Group.");

            // Serialize with Group lifecycle and UserGroup mutations, including
            // writes for other roles/members, using a single Group RowVersion.
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[Groups] SET [Description] = [Description]
                WHERE [Id] = {groupId} AND [TenantId] = {tenantId}
                    AND [RowVersion] = {expected}
                """, ct);
            if (affected != 1)
                throw new TenantGroupRoleConflictException("Group edited concurrently.");

            using (scope.BeginTenantGrantMutation(current, groupId, "GroupRole", [roleId]))
            {
                if (request.Assigned)
                    db.GroupRoles.Add(new GroupRole(Guid.NewGuid())
                    {
                        TenantId = tenantId, GroupId = groupId, RoleId = roleId
                    });
                else
                    db.GroupRoles.Remove(existing!);
                await db.SaveChangesAsync(ct);
            }

            var auditId = Guid.NewGuid();
            var action = request.Assigned ? "grant" : "revoke";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantGroupRoleAuditEntries]
                    ([Id],[TenantId],[ActorUserId],[GroupId],[RoleId],[RoleName],[Action])
                VALUES ({auditId},{tenantId},{current.UserId!.Value},
                    {groupId},{roleId},{role.Name},{action})
                """, ct);
            var next = await db.Groups.AsNoTracking()
                .Where(g => g.Id == groupId && g.TenantId == tenantId)
                .Select(g => g.RowVersion).SingleAsync(ct);
            await tx.CommitAsync(ct);
            return Convert.ToBase64String(next);
        });
    }
}
