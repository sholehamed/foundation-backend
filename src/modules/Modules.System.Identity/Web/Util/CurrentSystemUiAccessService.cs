using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Web.Util;

public sealed record CurrentUiAccessEntry(
    string Key, string? Route, string Type, bool Allowed);

public sealed record CurrentUiAccessSnapshot(
    int SchemaVersion, IReadOnlyList<CurrentUiAccessEntry> Entries);

/// <summary>
/// A bounded-query, live System-only UI projection. This is NEVER an API
/// authorization decision: protected endpoints must still enforce policies.
/// Reads code-owned keys, reconciled UI metadata, active Master-scope grants
/// and deny overrides in one database snapshot request for the whole page.
/// </summary>
public sealed class CurrentSystemUiAccessService(IIdentityDbContext db)
{
    public async Task<bool> IsAllowedAsync(
        ClaimsPrincipal principal, string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var result = await GetSnapshotAsync(principal, ct);
        return result?.Entries.SingleOrDefault(x =>
            string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))?.Allowed == true;
    }

    public async Task<CurrentUiAccessSnapshot?> GetSnapshotAsync(
        ClaimsPrincipal principal, CancellationToken ct = default)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            !principal.HasClaim("account_type", "system") ||
            principal.HasClaim(c => c.Type is "tenant_id" or "tenant") ||
            !Guid.TryParse(principal.FindFirstValue(OpenIddictConstants.Claims.Subject),
                out var userId))
            return null;

        var now = DateTime.UtcNow;
        var master = DefaultSeedData.MasterTenant.Id;
        var account = await db.Users.AsNoTracking().AnyAsync(x =>
            x.Id == userId && x.UserType == UserType.System && x.TenantId == null &&
            x.IsActive && (!x.IsLockedOut || x.LockoutEndUtc <= now), ct);
        var tenant = await db.Tenants.AsNoTracking()
            .AnyAsync(x => x.Id == master && x.IsActive, ct);
        if (!account || !tenant) return null;

        var features = FeatureRegistryDefinitions.Features;
        var children = FeatureRegistryDefinitions.UiElements;

        // Code-owned entries only; no untrusted catalog key can introduce a
        // new executable UI capability. Preflight duplicate definitions fail
        // closed rather than promoting one arbitrary row.
        var descriptors = features.Select(x =>
                new Descriptor(x.PageKey, x.PageKey, x.ResourceCode,
                    x.ModuleCode, x.Route, UiResourceType.PAGE, x.ReadPermission))
            .Concat(children.Select(x =>
            {
                var owner = features.Single(f => f.PageKey == x.FeatureKey);
                return new Descriptor(x.Key, x.FeatureKey, owner.ResourceCode,
                    owner.ModuleCode, x.Route, x.Type, x.PermissionCode);
            }))
            .ToArray();

        if (descriptors.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Count() != 1))
            return null;

        var keys = descriptors.Select(x => x.Key).ToArray();
        var codes = descriptors.Select(x => x.PermissionCode).Distinct().ToArray();

        var uiRows = await db.UiResources.AsNoTracking()
            .Where(x => keys.Contains(x.Key))
            .Select(x => new
            {
                x.Id, x.Key, x.ResourceId, x.IsActive, x.Type, x.Description
            }).ToListAsync(ct);
        var ids = uiRows.Select(x => x.Id).ToArray();

        var links = await db.PermissionUiResources.AsNoTracking()
            .Where(x => ids.Contains(x.UiResourceId))
            .Select(x => new { x.UiResourceId, x.PermissionId })
            .ToListAsync(ct);

        var permissions = await db.Permissions.AsNoTracking()
            .Where(x => codes.Contains(x.Code))
            .Select(x => new { x.Id, x.Code, x.IsActive, x.ResourceId })
            .ToListAsync(ct);

        var resourceIds = uiRows.Select(x => x.ResourceId).Distinct().ToArray();
        var resources = await db.Resources.AsNoTracking()
            .Where(x => resourceIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id, x.Code, x.IsActive,
                ModuleCode = x.Module!.Code, ModuleActive = x.Module.IsActive
            }).ToListAsync(ct);

        var direct = await db.UserPermissions.IgnoreQueryFilters(["tenant"])
            .AsNoTracking().Where(x => x.TenantId == master &&
                x.UserId == userId && x.Permission.IsActive)
            .Select(x => new { x.PermissionId, x.IsGranted }).ToListAsync(ct);
        var denied = direct.Where(x => !x.IsGranted)
            .Select(x => x.PermissionId).ToHashSet();
        var allowed = direct.Where(x => x.IsGranted)
            .Select(x => x.PermissionId).ToHashSet();

        var roles = (await db.UserRoles.IgnoreQueryFilters(["tenant"])
            .AsNoTracking().Where(x =>
                x.UserId == userId && x.TenantId == master &&
                x.Role.TenantId == master && x.Role.IsActive)
            .Select(x => x.RoleId).ToListAsync(ct)).ToHashSet();

        var groups = await db.UserGroups.IgnoreQueryFilters(["tenant"])
            .AsNoTracking().Where(x => x.UserId == userId &&
                x.TenantId == master && x.Group.TenantId == master &&
                x.Group.IsActive)
            .Select(x => x.GroupId).ToListAsync(ct);

        if (groups.Count > 0)
            roles.UnionWith(await db.GroupRoles.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().Where(x =>
                    x.TenantId == master && groups.Contains(x.GroupId) &&
                    x.Group!.TenantId == master && x.Group.IsActive &&
                    x.Role!.TenantId == master && x.Role.IsActive)
                .Select(x => x.RoleId).ToListAsync(ct));

        if (roles.Count > 0)
            allowed.UnionWith(await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().Where(x =>
                    x.TenantId == master && roles.Contains(x.RoleId) &&
                    x.Role!.TenantId == master && x.Role.IsActive &&
                    x.Permission!.IsActive)
                .Select(x => x.PermissionId).Distinct().ToListAsync(ct));

        allowed.ExceptWith(denied);

        // Duplicates, missing catalog rows, wrong Resource/Module, inactive UI
        // or multiple unexpected links must not become an allowed affordance.
        var structurallyValid = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in descriptors)
        {
            var matches = uiRows.Where(x =>
                string.Equals(x.Key, d.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
            {
                structurallyValid[d.Key] = false;
                continue;
            }

            var item = matches[0];
            var owners = resources.Where(x => x.Id == item.ResourceId &&
                x.IsActive && x.ModuleActive &&
                string.Equals(x.Code, d.ResourceCode, StringComparison.Ordinal) &&
                string.Equals(x.ModuleCode, d.ModuleCode, StringComparison.Ordinal))
                .ToArray();
            var matchedPermission = permissions.Where(x =>
                string.Equals(x.Code, d.PermissionCode, StringComparison.Ordinal) &&
                x.IsActive &&
                (x.ResourceId == null || x.ResourceId == item.ResourceId)).ToArray();
            var itemLinks = links.Where(x => x.UiResourceId == item.Id).ToArray();

            structurallyValid[d.Key] =
                item.IsActive &&
                item.Type == d.Type &&
                string.Equals(item.Description, d.Route, StringComparison.Ordinal) &&
                owners.Length == 1 &&
                matchedPermission.Length == 1 &&
                itemLinks.Length == 1 &&
                itemLinks[0].PermissionId == matchedPermission[0].Id &&
                allowed.Contains(matchedPermission[0].Id);
        }

        var entries = descriptors.Select(d =>
        {
            var rootAllowed = structurallyValid.TryGetValue(d.FeatureKey, out var root) && root;
            return new CurrentUiAccessEntry(d.Key, d.Route, d.Type.ToString(),
                rootAllowed && structurallyValid.GetValueOrDefault(d.Key));
        }).OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();

        return new CurrentUiAccessSnapshot(2, entries);
    }

    private sealed record Descriptor(
        string Key, string FeatureKey, string ResourceCode, string ModuleCode,
        string? Route, UiResourceType Type, string PermissionCode);
}
