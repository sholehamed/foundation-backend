using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Web.Endpoints;

/// <summary>
/// Server-authoritative, database-backed tenant PAGE/ACTION permission evaluation.
/// Only the actor's own, active home Tenant can contribute role/group/user grants.
/// A single explicit direct-user deny takes precedence over all grants and Owner.
/// Legacy Owner fallback keeps previously shipped routes working without
/// silently seeding permissions or role assignments.
/// </summary>
public static class TenantWorkspacePermissionEvaluator
{
    public static async Task<bool> AllowsAsync(
        ITenantContext current, IdentityDbContext db, string key, CancellationToken ct)
    {
        if (!TenantWorkspacePermissionCatalog.IsKnownKey(key) ||
            current.IsSystemContext || current.ActiveTenantId is not Guid tenantId ||
            current.HomeTenantId != tenantId || current.UserId is not Guid userId ||
            !current.ReadableTenantIds.Contains(tenantId))
            return false;

        if (!await db.Tenants.AsNoTracking().AnyAsync(
                t => t.Id == tenantId && t.IsActive, ct) ||
            !await db.Users.AsNoTracking().AnyAsync(
                u => u.Id == userId && u.TenantId == tenantId &&
                     u.UserType == UserType.Tenant && u.IsActive, ct))
            return false;

        // No silent grant when the shared catalog is missing. Legacy local Owner
        // is the only fallback. Duplicate or deactivated definitions fail closed.
        var definitions = await db.Permissions.AsNoTracking()
            .Where(p => p.Code == key)
            .Select(p => new { p.Id, p.IsActive })
            .ToListAsync(ct);
        if (definitions.Count > 1 || (definitions.Count == 1 && !definitions[0].IsActive))
            return false;

        if (definitions.Count == 1)
        {
            var permissionId = definitions[0].Id;

            // Explicit direct-user override is authoritative. An explicit deny
            // defeats inherited role, group, and even the legacy Owner fallback.
            var overrides = await db.UserPermissions.AsNoTracking()
                .Where(p => p.TenantId == tenantId && p.UserId == userId &&
                            p.PermissionId == permissionId)
                .Select(p => p.IsGranted).ToListAsync(ct);
            if (overrides.Count != 0)
                return overrides.Count == 1 && overrides[0];

            // Match role assignments, role ownership/scope and permission
            // assignments to the *same* active Tenant. System roles cannot cross.
            var fromRole = await db.UserRoles.AsNoTracking().AnyAsync(ur =>
                ur.TenantId == tenantId && ur.UserId == userId &&
                ur.Role.TenantId == tenantId && ur.Role.IsActive &&
                ur.Role.Scope == RoleScope.Tenant &&
                db.RolePermissions.Any(rp =>
                    rp.TenantId == tenantId && rp.RoleId == ur.RoleId &&
                    rp.PermissionId == permissionId), ct);
            if (fromRole) return true;

            // Membership must be in a live local Group, and the GroupRole and
            // RolePermission links must remain in the same tenant boundary.
            var fromGroup = await db.UserGroups.AsNoTracking().AnyAsync(ug =>
                ug.TenantId == tenantId && ug.UserId == userId &&
                ug.Group.TenantId == tenantId && ug.Group.IsActive &&
                db.GroupRoles.Any(gr =>
                    gr.TenantId == tenantId && gr.GroupId == ug.GroupId &&
                    gr.Role.TenantId == tenantId && gr.Role.IsActive &&
                    gr.Role.Scope == RoleScope.Tenant &&
                    db.RolePermissions.Any(rp =>
                        rp.TenantId == tenantId && rp.RoleId == gr.RoleId &&
                        rp.PermissionId == permissionId)), ct);
            if (fromGroup) return true;
        }

        return await IsLocalOwnerAsync(current, db, ct);
    }

    public static Task<bool> IsLocalOwnerAsync(
        ITenantContext current, IdentityDbContext db, CancellationToken ct)
    {
        // Called only after validating the live, active tenant & user in AllowsAsync.
        var tenantId = current.ActiveTenantId!.Value;
        var userId = current.UserId!.Value;
        return db.UserRoles.AsNoTracking().AnyAsync(ur =>
            ur.TenantId == tenantId && ur.UserId == userId &&
            ur.Role.TenantId == tenantId &&
            ur.Role.NormalizedName == "OWNER" && ur.Role.IsActive &&
            // Legacy public signups created an Owner role with Scope=0.
            ur.Role.Scope != RoleScope.System, ct);
    }
}
