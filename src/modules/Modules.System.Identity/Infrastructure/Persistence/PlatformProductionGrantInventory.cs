using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Infrastructure.Persistence;

/// <summary>
/// Read-only Production cutover inventory. It NEVER grants privileges, creates
/// users/roles or opens a trusted Master write scope. This snapshot is a
/// prerequisite for an operator-approved, separate deployment transaction.
/// All Master relationships are explicitly scoped, not inferred from User.TenantId.
/// </summary>
public static class PlatformProductionGrantInventory
{
    public sealed record Snapshot(
        bool MasterTenantActive,
        int SuperAdminRoleCount,
        int ActiveSuperAdminRoleCount,
        int ActiveMasterSuperAdminAssignments,
        int SystemUsersWithTenantId,
        int InvalidMasterUserRoleAssignments,
        int InvalidMasterRolePermissionAssignments,
        int InvalidMasterGroupRoleAssignments,
        int InvalidMasterUserGroupAssignments,
        int InvalidMasterUserPermissionAssignments,
        int MasterCatalogUserDenials,
        IReadOnlyList<string> MissingCatalogCodes,
        IReadOnlyList<string> InactiveCatalogCodes,
        IReadOnlyList<string> DuplicateCatalogCodes,
        IReadOnlyList<string> MissingSuperAdminGrantCodes)
    {
        // "No obvious blockers" is NOT an authorization to enable enforcement.
        // Operations, backup, cutover order and named-person approval remain mandatory.
        public bool HasBlockingFindings =>
            !MasterTenantActive ||
            SuperAdminRoleCount != 1 || ActiveSuperAdminRoleCount != 1 ||
            ActiveMasterSuperAdminAssignments < 1 ||
            SystemUsersWithTenantId != 0 || InvalidMasterUserRoleAssignments != 0 ||
            InvalidMasterRolePermissionAssignments != 0 ||
            InvalidMasterGroupRoleAssignments != 0 ||
            InvalidMasterUserGroupAssignments != 0 ||
            InvalidMasterUserPermissionAssignments != 0 ||
            MasterCatalogUserDenials != 0 ||
            MissingCatalogCodes.Count != 0 || InactiveCatalogCodes.Count != 0 ||
            DuplicateCatalogCodes.Count != 0 ||
            MissingSuperAdminGrantCodes.Count != 0;
    }

    public static async Task<Snapshot> ReadAsync(
        IdentityDbContext db, CancellationToken ct = default)
    {
        var master = DefaultSeedData.MasterTenant.Id;
        var required = PlatformPermissionCatalog.Codes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var masterActive = await db.Tenants.AsNoTracking()
            .AnyAsync(x => x.Id == master && x.IsActive, ct);
        var roles = await db.Roles.IgnoreQueryFilters(["tenant"])
            .AsNoTracking().Where(x => x.TenantId == master &&
                                       x.NormalizedName == "SUPERADMIN")
            .Select(x => new { x.Id, x.IsActive }).ToListAsync(ct);
        var activeRoles = roles.Where(x => x.IsActive).ToArray();
        var activeRoleId = roles.Count == 1 && activeRoles.Length == 1
            ? activeRoles[0].Id : (Guid?)null;

        var assignments = activeRoleId.HasValue
            ? await db.UserRoles.IgnoreQueryFilters(["tenant"]).AsNoTracking()
                .CountAsync(x => x.TenantId == master && x.RoleId == activeRoleId &&
                    x.Role.TenantId == master && x.User.UserType == UserType.System &&
                    x.User.TenantId == null && x.User.IsActive &&
                    (!x.User.IsLockedOut || x.User.LockoutEndUtc <= DateTime.UtcNow), ct)
            : 0;

        var invalidAssignments = await db.UserRoles.IgnoreQueryFilters(["tenant"])
            .AsNoTracking()
            .CountAsync(x => x.TenantId == master &&
                (x.Role.TenantId != master || x.User.UserType != UserType.System ||
                 x.User.TenantId != null), ct);

        // All four Master-scoped relationship tables must connect to a
        // compatible Master Role/Group or a TenantId-null System user. SQL
        // foreign keys do NOT validate cross-Tenant scope compatibility.
        var invalidRolePermissions = await db.RolePermissions
            .IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .CountAsync(x => x.TenantId == master && x.Role.TenantId != master, ct);
        var invalidGroupRoles = await db.GroupRoles
            .IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .CountAsync(x => x.TenantId == master &&
                (x.Role!.TenantId != master || x.Group!.TenantId != master), ct);
        var invalidUserGroups = await db.UserGroups
            .IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .CountAsync(x => x.TenantId == master &&
                (x.Group.TenantId != master || x.User.UserType != UserType.System ||
                 x.User.TenantId != null), ct);
        var invalidUserPermissions = await db.UserPermissions
            .IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .CountAsync(x => x.TenantId == master &&
                (x.User.UserType != UserType.System || x.User.TenantId != null), ct);

        var tenantBoundSystemUsers = await db.Users.AsNoTracking()
            .CountAsync(x => x.UserType == UserType.System &&
                             x.TenantId != null, ct);

        // Read the whole reserved catalog; distinguish missing, deactivated
        // and duplicates even on old databases without the new unique index.
        var catalog = await db.Permissions.AsNoTracking()
            .Where(x => x.Code.StartsWith("ROLES.") ||
                        x.Code.StartsWith("PERMISSIONS."))
            .Select(x => new { x.Id, x.Code, x.IsActive })
            .ToListAsync(ct);
        var normalized = catalog.GroupBy(x => x.Code.Trim().ToUpperInvariant())
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);

        var missing = required.Where(x => !normalized.ContainsKey(x)).ToArray();
        var inactive = required.Where(x => normalized.TryGetValue(x, out var found) &&
            found.All(p => !p.IsActive)).ToArray();
        var duplicates = required.Where(x => normalized.TryGetValue(x, out var found) &&
            found.Length != 1).ToArray();

        var granted = activeRoleId.HasValue
            ? await db.RolePermissions.IgnoreQueryFilters(["tenant"]).AsNoTracking()
                .Where(x => x.TenantId == master && x.RoleId == activeRoleId &&
                            x.Role.TenantId == master)
                .Select(x => x.PermissionId).ToListAsync(ct)
            : [];
        var grantedIds = granted.ToHashSet();
        var ungranted = required.Where(x =>
            !normalized.TryGetValue(x, out var matched) ||
            matched.Length != 1 || !matched[0].IsActive ||
            !grantedIds.Contains(matched[0].Id)).ToArray();

        var denials = await db.UserPermissions.IgnoreQueryFilters(["tenant"])
            .AsNoTracking().CountAsync(x =>
                x.TenantId == master && !x.IsGranted &&
                x.User.UserType == UserType.System && x.User.TenantId == null &&
                (x.Permission.Code.StartsWith("ROLES.") ||
                 x.Permission.Code.StartsWith("PERMISSIONS.")), ct);

        return new Snapshot(masterActive, roles.Count, activeRoles.Length,
            assignments, tenantBoundSystemUsers, invalidAssignments,
            invalidRolePermissions, invalidGroupRoles, invalidUserGroups,
            invalidUserPermissions, denials,
            missing, inactive, duplicates, ungranted);
    }
}
