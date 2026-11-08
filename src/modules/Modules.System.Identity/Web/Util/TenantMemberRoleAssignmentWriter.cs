using System.Data;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;

namespace Modules.System.Identity.Web.Util;

public sealed class TenantMemberRoleValidationException(string message) : Exception(message);
public sealed class TenantMemberRoleConflictException(string message) : Exception(message);

/// <summary>
/// Actor-scoped replacement of direct ordinary local member roles. Never
/// creates OWNER links, mutates roles, system links, or grants permissions.
/// </summary>
public static class TenantMemberRoleAssignmentWriter
{
    public sealed record ReplaceRequest(string? ExpectedVersion, IReadOnlyList<Guid>? RoleIds);
    public sealed record AssignmentItem(Guid Id, string Name, bool IsActive, bool Assigned);
    public sealed record AssignmentMatrix(Guid UserId, string UserName,
        string Version, bool CanEdit, IReadOnlyList<AssignmentItem> Roles);

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

        // FR-12D is a NEW elevation-capable write path. Unlike legacy Owner
        // read compatibility, it must not activate before the explicitly
        // reviewed code-owned ACTION has been applied to the SQL catalog.
        var registered = await db.Permissions.AsNoTracking()
            .Where(p => p.Code == TenantWorkspacePermissionCatalog.MembersAssignRoles &&
                p.IsActive && p.ResourceId != null && p.Resource != null &&
                p.Resource.Code == TenantPermissionCatalogSync.ResourceCode &&
                p.Resource.IsActive && p.Resource.Module != null &&
                p.Resource.Module.Code == TenantPermissionCatalogSync.ModuleCode &&
                p.Resource.Module.IsActive &&
                db.PermissionUiResources.Any(l =>
                    l.PermissionId == p.Id && l.UiResource != null &&
                    l.UiResource.IsActive && l.UiResource.ResourceId == p.ResourceId &&
                    l.UiResource.Key == "tenant.workspace.members.assign-roles" &&
                    l.UiResource.Type == UiResourceType.ACTION &&
                    l.UiResource.Description == null))
            .Select(p => p.Id).ToListAsync(ct);
        if (registered.Count != 1) return false;

        // Owner status is an additional mandatory gate; a direct explicit
        // user DENY for either PAGE or ACTION blocks writes even for Owner.
        return await TenantWorkspacePermissionEvaluator.AllowsAsync(
                   current, db, TenantWorkspacePermissionCatalog.MembersRead, ct) &&
               await TenantWorkspacePermissionEvaluator.AllowsAsync(
                   current, db, TenantWorkspacePermissionCatalog.RolesRead, ct) &&
               await TenantWorkspacePermissionEvaluator.AllowsAsync(
                   current, db, TenantWorkspacePermissionCatalog.MembersAssignRoles, ct) &&
               await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(current, db, ct);
    }

    public static async Task<bool> CanEditTargetAsync(
        ITenantContext current, IdentityDbContext db, Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty || current.UserId is not Guid actor ||
            actor == userId || current.ActiveTenantId is not Guid tenantId)
            return false;

        if (!await db.Users.AsNoTracking().AnyAsync(x =>
                x.Id == userId && x.TenantId == tenantId &&
                x.UserType == UserType.Tenant && x.IsActive, ct))
            return false;

        // Owner memberships are immutable via this API, irrespective of
        // active/inactive role state or old legacy RoleScope=0.
        return !await db.UserRoles.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenantId && x.UserId == userId &&
            x.Role.TenantId == tenantId && x.Role.NormalizedName == "OWNER" &&
            x.Role.Scope != RoleScope.System, ct);
    }

    public static async Task<AssignmentMatrix?> ReadAsync(
        ITenantContext current, IdentityDbContext db, Guid userId, CancellationToken ct)
    {
        if (current.IsSystemContext || userId == Guid.Empty ||
            current.ActiveTenantId is not Guid tenantId ||
            current.HomeTenantId != tenantId ||
            !current.ReadableTenantIds.Contains(tenantId) ||
            !await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.MembersRead, ct) ||
            !await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesRead, ct))
            return null;

        var user = await db.Users.AsNoTracking()
            .Where(x => x.Id == userId && x.TenantId == tenantId &&
                        x.UserType == UserType.Tenant)
            .Select(x => new { x.Id, x.UserName, x.RowVersion })
            .SingleOrDefaultAsync(ct);
        if (user is null) return null;

        var allRoles = await db.Roles.AsNoTracking()
            .Where(r => r.TenantId == tenantId &&
                r.Scope == RoleScope.Tenant && r.NormalizedName != "OWNER")
            .OrderBy(r => r.Name).ThenBy(r => r.Id)
            .Select(r => new { r.Id, r.Name, r.IsActive }).ToListAsync(ct);
        var linkedIds = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.TenantId == tenantId && ur.UserId == userId)
            .Select(ur => ur.RoleId).ToListAsync(ct);
        var assigned = linkedIds.ToHashSet();
        var roles = allRoles.Select(r =>
            new AssignmentItem(r.Id, r.Name, r.IsActive, assigned.Contains(r.Id))).ToArray();
        var editable = await CanManageAsync(current, db, ct) &&
                       await CanEditTargetAsync(current, db, userId, ct);
        return new AssignmentMatrix(user.Id, user.UserName,
            user.RowVersion is { Length: > 0 }
                ? Convert.ToBase64String(user.RowVersion) : "",
            editable, roles);
    }

    public static async Task<string> ReplaceAsync(
        ITenantContext current, IdentityDbContext db, Guid userId,
        ReplaceRequest? request, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlServer())
            throw new InvalidOperationException("Tenant member role writes require SQL Server.");

        if (!await CanManageAsync(current, db, ct) ||
            !await CanEditTargetAsync(current, db, userId, ct))
            throw new UnauthorizedAccessException("Member role assignment not authorized.");

        if (request?.RoleIds is null || request.RoleIds.Count > 256 ||
            request.RoleIds.Any(x => x == Guid.Empty) ||
            request.RoleIds.Distinct().Count() != request.RoleIds.Count)
            throw new TenantMemberRoleValidationException("Invalid or duplicate Tenant role IDs.");

        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(request.ExpectedVersion ?? "");
            if (expected.Length != 8) throw new FormatException();
        }
        catch (FormatException)
        {
            throw new TenantMemberRoleValidationException("Expected User RowVersion is invalid.");
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);

            // Re-evaluate authority and the target inside the write transaction;
            // revoked actor or Owner target must never proceed.
            if (!await CanManageAsync(current, db, ct) ||
                !await CanEditTargetAsync(current, db, userId, ct))
                throw new UnauthorizedAccessException("Member role assignment permission revoked.");

            var tenantId = current.ActiveTenantId!.Value;
            var actor = current.UserId!.Value;
            var version = await db.Users.AsNoTracking()
                .Where(x => x.Id == userId && x.TenantId == tenantId &&
                            x.UserType == UserType.Tenant && x.IsActive)
                .Select(x => x.RowVersion).SingleOrDefaultAsync(ct);
            if (version is null || !version.SequenceEqual(expected))
                throw new TenantMemberRoleConflictException(
                    "Member roles changed. Reload the latest member version.");

            // Explicit TenantId/Scope/Owner predicates must not depend only
            // on the ambient EF tenant filter.
            var allowedRoles = await db.Roles.AsNoTracking()
                .Where(r => r.TenantId == tenantId &&
                    r.Scope == RoleScope.Tenant && r.NormalizedName != "OWNER")
                .Select(r => new { r.Id, r.Name, r.IsActive }).ToListAsync(ct);
            var ordinary = allowedRoles.ToDictionary(r => r.Id);
            var requested = request.RoleIds.ToHashSet();
            if (requested.Any(id => !ordinary.ContainsKey(id)))
                throw new TenantMemberRoleValidationException(
                    "Role IDs must be ordinary roles in the active Tenant.");
            var ordinaryIds = ordinary.Keys.ToArray();
            var existing = await db.UserRoles
                .Where(x => x.TenantId == tenantId && x.UserId == userId &&
                            ordinaryIds.Contains(x.RoleId)).ToListAsync(ct);
            var old = existing.Select(x => x.RoleId).ToHashSet();
            var additions = requested.Except(old).ToArray();
            // Inactive direct memberships may be retained or revoked.
            // A new inactive membership is never granted, even if a client
            // submits an obsolete role list from a stale UI.
            if (additions.Any(id => !ordinary[id].IsActive))
                throw new TenantMemberRoleConflictException(
                    "Cannot newly assign inactive Tenant roles.");
            var removals = existing.Where(x => !requested.Contains(x.RoleId)).ToArray();
            if (additions.Length == 0 && removals.Length == 0)
            {
                await tx.CommitAsync(ct);
                return Convert.ToBase64String(expected);
            }

            // The User RowVersion serializes the ENTIRE member UserRole set.
            // Even competing requests with non-overlapping role lists conflict.
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[Users] SET [ConcurrencyStamp] = [ConcurrencyStamp]
                WHERE [Id] = {userId} AND [TenantId] = {tenantId}
                    AND [UserType] = {(byte)UserType.Tenant} AND [IsActive] = {true}
                    AND [RowVersion] = {expected}
                """, ct);
            if (changed != 1)
                throw new TenantMemberRoleConflictException("Member version changed.");

            foreach (var roleId in additions)
                db.UserRoles.Add(new UserRole(tenantId, userId, roleId));
            db.UserRoles.RemoveRange(removals);
            await db.SaveChangesAsync(ct);

            foreach (var roleId in additions)
                await WriteAuditAsync(db, tenantId, actor, userId, roleId,
                    ordinary[roleId].Name, "grant", ct);
            foreach (var link in removals)
                await WriteAuditAsync(db, tenantId, actor, userId, link.RoleId,
                    ordinary[link.RoleId].Name, "revoke", ct);

            var next = await db.Users.AsNoTracking()
                .Where(x => x.Id == userId && x.TenantId == tenantId)
                .Select(x => x.RowVersion).SingleAsync(ct);
            await tx.CommitAsync(ct);
            return Convert.ToBase64String(next);
        });
    }

    private static Task WriteAuditAsync(
        IdentityDbContext db, Guid tenantId, Guid actor, Guid userId,
        Guid roleId, string roleName, string action, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Tenant member role audit requires SQL transaction.");
        var id = Guid.NewGuid();
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [dbo].[TenantMemberRoleAuditEntries]
                ([Id],[TenantId],[ActorUserId],[TargetUserId],[RoleId],[RoleName],[Action])
            VALUES ({id},{tenantId},{actor},{userId},{roleId},{roleName},{action})
            """, ct);
    }
}
