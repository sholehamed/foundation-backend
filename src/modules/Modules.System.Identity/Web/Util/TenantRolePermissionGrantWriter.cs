using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Infrastructure.Persistence.Interceptors;
using Modules.System.Identity.Web.Endpoints;

namespace Modules.System.Identity.Web.Util;

public sealed class TenantRoleGrantConflictException(string message) : Exception(message);
public sealed class TenantRoleGrantValidationException(string message) : Exception(message);
public sealed class TenantRoleGrantMissingException : Exception;

/// <summary>
/// Owner-only, audited, compare-and-swap Tenant role grants.
/// No System user, foreign tenant, OWNER role or actor-assigned target can be edited.
/// </summary>
public static class TenantRolePermissionGrantWriter
{
    public sealed record ReplaceRequest(string? ExpectedVersion, IReadOnlyList<string>? Codes);

    public static async Task<bool> CanEditAsync(
        ITenantContext current, IdentityDbContext db, Guid roleId, CancellationToken ct)
    {
        if (current.IsSystemContext ||
            current.UserId is not Guid actor || actor == Guid.Empty ||
            current.ActiveTenantId is not Guid tenantId || tenantId == Guid.Empty ||
            current.HomeTenantId != tenantId ||
            !current.WritableTenantIds.Contains(tenantId) ||
            !current.ReadableTenantIds.Contains(tenantId) || roleId == Guid.Empty)
            return false;

        // Both PAGE and ACTION must be independently effective. An explicit
        // UserPermission deny overrides Owner. No non-owner can administer grants.
        if (!await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesRead, ct) ||
            !await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesAssignPermissions, ct) ||
            !await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(current, db, ct))
            return false;

        if (!await db.Roles.AsNoTracking().AnyAsync(r =>
                r.Id == roleId && r.TenantId == tenantId &&
                r.Scope == RoleScope.Tenant && r.IsActive &&
                r.NormalizedName != "OWNER", ct))
            return false;

        // Prevent writing a role the actor already holds, whether directly or
        // through a group. No self-privilege escalation through RolePermission.
        if (await db.UserRoles.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenantId && x.UserId == actor &&
                x.RoleId == roleId, ct) ||
            await db.UserGroups.AsNoTracking().AnyAsync(ug =>
                ug.TenantId == tenantId && ug.UserId == actor &&
                ug.Group.TenantId == tenantId && ug.Group.IsActive &&
                db.GroupRoles.Any(gr => gr.TenantId == tenantId &&
                    gr.GroupId == ug.GroupId && gr.RoleId == roleId), ct))
            return false;
        return true;
    }

    public static async Task<string> ReplaceAsync(
        ITenantContext current, IdentityDbContext db, Guid roleId,
        ReplaceRequest? request, CancellationToken ct = default,
        TrustedTenantWriteScope? intentScope = null)
    {
        // No InMemory/provider fallback: audit+CAS must be atomic real SQL.
        if (!db.Database.IsSqlServer())
            throw new InvalidOperationException("Tenant grant writes require SQL Server.");

        if (!await CanEditAsync(current, db, roleId, ct))
            throw new UnauthorizedAccessException("Tenant grant write not authorized.");

        if (request?.Codes is null ||
            request.Codes.Count > TenantWorkspacePermissionCatalog.Definitions.Count ||
            request.Codes.Any(x => x is null || !TenantWorkspacePermissionCatalog.IsKnownKey(x)) ||
            request.Codes.Distinct(StringComparer.Ordinal).Count() != request.Codes.Count)
            throw new TenantRoleGrantValidationException("Unknown or duplicate permission code.");

        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(request.ExpectedVersion ?? "");
            if (expected.Length != 8)
                throw new FormatException();
        }
        catch (FormatException)
        {
            throw new TenantRoleGrantValidationException("Expected SQL RowVersion is invalid.");
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            // Re-check authorization within the transaction. Live owner role,
            // tenant, target, assigned roles and denials may have changed.
            if (!await CanEditAsync(current, db, roleId, ct))
                throw new UnauthorizedAccessException("Tenant grant permission revoked.");

            var tenantId = current.ActiveTenantId!.Value;
            var actor = current.UserId!.Value;

            // The catalog must be complete and operator-owned. Do not allow a
            // role write against inactive/missing/orphaned code definitions.
            var keys = TenantWorkspacePermissionCatalog.Definitions
                .Select(x => x.Key).ToArray();
            var definitions = await db.Permissions.AsNoTracking()
                .Where(p => keys.Contains(p.Code) && p.IsActive &&
                    p.Resource != null &&
                    p.Resource.Code == TenantPermissionCatalogSync.ResourceCode &&
                    p.Resource.IsActive && p.Resource.Module != null &&
                    p.Resource.Module.Code == TenantPermissionCatalogSync.ModuleCode &&
                    p.Resource.Module.IsActive)
                .Select(p => new { p.Id, p.Code, p.ResourceId })
                .ToListAsync(ct);
            if (definitions.Count != keys.Length ||
                definitions.Select(x => x.Code).Distinct().Count() != keys.Length)
                throw new TenantRoleGrantConflictException(
                    "Tenant permission registry is not fully registered.");

            var uis = await db.UiResources.AsNoTracking()
                .Where(x => x.IsActive && x.ResourceId == definitions[0].ResourceId)
                .Select(x => new { x.Id, x.Key, x.Type, x.Description }).ToListAsync(ct);
            var definitionIds = definitions.Select(p => p.Id).ToArray();
            var uiLinks = await db.PermissionUiResources.AsNoTracking()
                .Where(x => definitionIds.Contains(x.PermissionId))
                .Select(x => new { x.PermissionId, x.UiResourceId }).ToListAsync(ct);

            foreach (var d in TenantWorkspacePermissionCatalog.Definitions)
            {
                var permission = definitions.Single(x => x.Code == d.Key);
                var ui = uis.SingleOrDefault(x => x.Key == d.UiKey &&
                    x.Type == (d.Kind == "PAGE" ? UiResourceType.PAGE : UiResourceType.ACTION) &&
                    x.Description == (d.Kind == "PAGE" ? d.Route : null));
                if (ui is null || uiLinks.Count(x => x.PermissionId == permission.Id &&
                        x.UiResourceId == ui.Id) != 1)
                    throw new TenantRoleGrantConflictException(
                        "Tenant UI permission registry needs reviewed apply.");
            }

            var currentRole = await db.Roles.AsNoTracking()
                .Where(x => x.Id == roleId && x.TenantId == tenantId &&
                    x.Scope == RoleScope.Tenant && x.IsActive &&
                    x.NormalizedName != "OWNER")
                .Select(x => new { x.Id, x.RowVersion }).SingleOrDefaultAsync(ct);
            if (currentRole is null)
                throw new TenantRoleGrantMissingException();
            if (currentRole.RowVersion is null ||
                !currentRole.RowVersion.SequenceEqual(expected))
                throw new TenantRoleGrantConflictException("Role permissions changed. Refresh.");

            var permissionIds = definitions.Select(x => x.Id).ToArray();
            var existing = await db.RolePermissions
                .Where(x => x.TenantId == tenantId && x.RoleId == roleId &&
                    permissionIds.Contains(x.PermissionId)).ToListAsync(ct);
            var desired = definitions.Where(x => request.Codes.Contains(x.Code))
                .ToDictionary(x => x.Id, x => x.Code);
            var old = existing.Select(x => x.PermissionId).ToHashSet();
            var additions = desired.Keys.Where(x => !old.Contains(x)).ToArray();
            var removals = existing.Where(x => !desired.ContainsKey(x.PermissionId)).ToArray();

            if (additions.Length == 0 && removals.Length == 0)
            {
                await tx.CommitAsync(ct);
                return Convert.ToBase64String(expected);
            }

            // Role.RowVersion is the CAS token for the *entire* grant set.
            // This update serializes concurrent writers to the same role.
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[Roles] SET [Description] = [Description]
                WHERE [Id] = {roleId} AND [TenantId] = {tenantId}
                  AND [RowVersion] = {expected}
                """, ct);
            if (changed != 1)
                throw new TenantRoleGrantConflictException("Role version changed.");

            using (var intent = intentScope?.BeginTenantGrantMutation(
                current, roleId, "RolePermission",
                additions.Concat(removals.Select(x => x.PermissionId)).ToArray()))
            {
                foreach (var id in additions)
                    db.RolePermissions.Add(new RolePermission(tenantId, roleId, id));
                db.RolePermissions.RemoveRange(removals);
                await db.SaveChangesAsync(ct);
            }

            foreach (var id in additions)
                await WriteAuditAsync(db, tenantId, actor, roleId, id, desired[id], "grant", ct);
            foreach (var row in removals)
                await WriteAuditAsync(db, tenantId, actor, roleId,
                    row.PermissionId, definitions.Single(x => x.Id == row.PermissionId).Code,
                    "revoke", ct);

            var next = await db.Roles.AsNoTracking()
                .Where(x => x.Id == roleId && x.TenantId == tenantId)
                .Select(x => x.RowVersion).SingleAsync(ct);
            await tx.CommitAsync(ct);
            return Convert.ToBase64String(next);
        });
    }

    private static Task WriteAuditAsync(
        IdentityDbContext db, Guid tenantId, Guid actor, Guid roleId,
        Guid permissionId, string code, string action, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Tenant grant audit requires atomic SQL transaction.");
        var auditId = Guid.NewGuid();
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [dbo].[TenantRoleGrantAuditEntries]
                ([Id],[TenantId],[ActorUserId],[RoleId],[PermissionId],[PermissionCode],[Action])
            VALUES
                ({auditId},{tenantId},{actor},{roleId},{permissionId},{code},{action})
            """, ct);
    }
}
