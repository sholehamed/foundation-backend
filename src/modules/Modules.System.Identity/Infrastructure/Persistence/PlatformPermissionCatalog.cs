using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Infrastructure.Persistence;

/// <summary>
/// Stable ADR-003 Resource.Action keys for platform endpoints. Database Code is
/// upper-case while the requirement API uses validated lower-case components.
/// This catalog only describes privileges; it never grants them implicitly.
/// </summary>
public static class PlatformPermissionCatalog
{
    public sealed record Definition(string Resource, string Action, string Name)
    {
        public string Code => new global::Modules.System.Identity.Web.Util.SystemPermissionRequirement(Resource, Action).Key;
    }

    public static readonly Definition[] Definitions =
    [
        new("roles", "create", "Create platform roles"),
        new("roles", "update", "Update platform roles"),
        new("roles", "delete", "Delete platform roles"),
        new("roles", "change-state", "Change platform role state"),
        new("roles", "assign-permissions", "Replace platform role permissions"),
        new("permissions", "create", "Create platform permissions"),
        new("permissions", "update", "Update platform permissions"),
        new("permissions", "delete", "Delete platform permissions"),
        new("permissions", "change-state", "Change platform permission state")
    ];

    public static IReadOnlyList<string> Codes => Definitions.Select(d => d.Code).ToArray();

    // Definitions backing System authorization must be immutable through the
    // administrative CRUD surface. This also reserves future Role/Permission keys.
    public static bool IsReservedCode(string? code)
    {
        var key = code?.Trim().ToUpperInvariant();
        return key is not null &&
            (key.StartsWith("ROLES.", StringComparison.Ordinal) ||
             key.StartsWith("PERMISSIONS.", StringComparison.Ordinal));
    }
}

/// <summary>
/// Safe additive catalog sync. Never reactivates disabled permissions, overwrites
/// manually configured grants, or implicitly assigns platform capabilities.
/// A separate one-time reviewed production grant migration is required.
/// </summary>
public static class PlatformPermissionCatalogSeeder
{
    public static async Task SeedDefinitionsAsync(IdentityDbContext db,
        CancellationToken cancellationToken = default)
    {
        var codes = PlatformPermissionCatalog.Codes;
        var existing = await db.Permissions.AsNoTracking()
            .Where(x => codes.Contains(x.Code))
            .Select(x => x.Code).ToListAsync(cancellationToken);
        var present = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in PlatformPermissionCatalog.Definitions)
        {
            if (!present.Add(definition.Code))
                continue;

            db.Permissions.Add(new Permission
            {
                Code = definition.Code,
                Name = definition.Name,
                IsActive = true
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Development/CI-only opt-in bootstrap. Never call for Production.
    /// Admin assignments must already exist and belong to the master scope.
    /// The flag should not be left enabled when testing grant revocation.
    /// </summary>
    public static async Task GrantDevelopmentSuperAdminAsync(IdentityDbContext db,
        CancellationToken cancellationToken = default)
    {
        var master = DefaultSeedData.MasterTenant.Id;
        var roles = await db.Roles.IgnoreQueryFilters(["tenant"])
            .Where(x => x.TenantId == master && x.NormalizedName == "SUPERADMIN" && x.IsActive)
            .Select(x => x.Id).ToListAsync(cancellationToken);
        if (roles.Count != 1)
            throw new InvalidOperationException("Development platform grants require exactly one active master-scoped SuperAdmin role.");

        var codes = PlatformPermissionCatalog.Codes;
        var permissions = await db.Permissions.AsNoTracking()
            .Where(x => codes.Contains(x.Code) && x.IsActive)
            .Select(x => new { x.Id, x.Code }).ToListAsync(cancellationToken);
        if (permissions.Count != codes.Count)
            throw new InvalidOperationException("Platform catalog must be fully active before development grants.");

        var roleId = roles[0];
        var assignedIds = (await db.RolePermissions.IgnoreQueryFilters(["tenant"])
            .Where(x => x.TenantId == master && x.RoleId == roleId)
            .Select(x => x.PermissionId).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var permission in permissions)
        {
            if (assignedIds.Add(permission.Id))
                db.RolePermissions.Add(new RolePermission(master, roleId, permission.Id));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
