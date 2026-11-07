using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Web.Util;

public sealed record CurrentNavigationCategory(string Title, List<CurrentNavigationItem> Items);

public sealed record CurrentNavigationItem(
    string Title,
    string? Icon,
    string? Route,
    string? ExternalUrl,
    List<CurrentNavigationItem> Childs);

/// <summary>
/// Builds a principal-scoped navigation projection from live grants. This is
/// navigation, NOT an authorization policy: every API must still enforce its
/// own live authorization requirements. No RoleId or TenantId is accepted
/// from the caller. Invalid principals return null (403); no catalog rows
/// return an empty list (200), never an administrative fallback.
/// </summary>
public sealed class CurrentUserNavigationService(IIdentityDbContext db)
{
    public async Task<List<CurrentNavigationCategory>?> GetAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(OpenIddictConstants.Claims.Subject), out var userId))
            return null;

        var accountType = principal.FindFirstValue("account_type");
        Guid scopeId;
        UserType kind;
        if (accountType == "system" && !principal.HasClaim(c => c.Type is "tenant_id" or "tenant"))
        {
            kind = UserType.System;
            scopeId = DefaultSeedData.MasterTenant.Id;
        }
        else if (accountType == "tenant" &&
            Guid.TryParse(principal.FindFirstValue("tenant_id"), out var tenantId) &&
            !string.IsNullOrWhiteSpace(principal.FindFirstValue("tenant")))
        {
            kind = UserType.Tenant;
            scopeId = tenantId;
        }
        else
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var userActive = await db.Users.AsNoTracking().AnyAsync(x =>
            x.Id == userId && x.IsActive &&
            (!x.IsLockedOut || x.LockoutEndUtc <= now) &&
            (kind == UserType.System
                ? x.UserType == UserType.System && x.TenantId == null
                : x.UserType == UserType.Tenant && x.TenantId == scopeId), cancellationToken);
        if (!userActive ||
            !await db.Tenants.AsNoTracking().AnyAsync(x =>
                x.Id == scopeId && x.IsActive &&
                (kind == UserType.System || x.Slug == principal.FindFirstValue("tenant")),
                cancellationToken))
            return null;

        // Distinct live Permission IDs, not token claims. Four bounded queries
        // regardless of number of menus; direct denials override all grants.
        var direct = db.UserPermissions.IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .Where(x => x.UserId == userId && x.TenantId == scopeId && x.Permission.IsActive);
        var denied = (await direct.Where(x => !x.IsGranted)
            .Select(x => x.PermissionId).ToListAsync(cancellationToken)).ToHashSet();
        var allowed = (await direct.Where(x => x.IsGranted)
            .Select(x => x.PermissionId).ToListAsync(cancellationToken)).ToHashSet();

        allowed.UnionWith(await db.UserRoles.IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .Where(x => x.UserId == userId && x.TenantId == scopeId &&
                x.Role.TenantId == scopeId && x.Role.IsActive)
            .SelectMany(x => x.Role.RolePermissions
                .Where(rp => rp.TenantId == scopeId && rp.Permission.IsActive)
                .Select(rp => rp.PermissionId))
            .Distinct().ToListAsync(cancellationToken));

        allowed.UnionWith(await db.UserGroups.IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .Where(x => x.UserId == userId && x.TenantId == scopeId &&
                x.Group.TenantId == scopeId && x.Group.IsActive)
            .SelectMany(x => x.Group.GroupRoles
                .Where(gr => gr.TenantId == scopeId &&
                    gr.Role.TenantId == scopeId && gr.Role.IsActive)
                .SelectMany(gr => gr.Role.RolePermissions
                    .Where(rp => rp.TenantId == scopeId && rp.Permission.IsActive)
                    .Select(rp => rp.PermissionId)))
            .Distinct().ToListAsync(cancellationToken));

        allowed.ExceptWith(denied);

        // Keep category data and immutable navigation facts local; this endpoint
        // does not expose PermissionId, grants, user identities or role IDs.
        var categories = await db.MenuCategories.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Order).ThenBy(x => x.Title)
            .Select(x => new { x.Id, x.Title })
            .ToListAsync(cancellationToken);

        var entries = await db.Menus.AsNoTracking()
            .Where(x => x.IsActive && !x.IsHidden)
            .OrderBy(x => x.Order).ThenBy(x => x.Title)
            .Select(x => new
            {
                x.Id, x.CategoryId, x.ParentId, x.Title, x.Icon,
                x.Route, x.ExternalUrl, x.Order, x.PermissionId
            })
            .ToListAsync(cancellationToken);

        var result = new List<CurrentNavigationCategory>();
        foreach (var category in categories)
        {
            var rows = entries.Where(x => x.CategoryId == category.Id).ToList();
            var byParent = rows.ToLookup(x => x.ParentId);
            var visited = new HashSet<Guid>();

            List<CurrentNavigationItem> Build(Guid? parentId, int depth)
            {
                // Bound depth and visited IDs to fail closed on malformed cycles.
                if (depth > 32)
                    return [];

                var items = new List<CurrentNavigationItem>();
                foreach (var row in byParent[parentId])
                {
                    if (!visited.Add(row.Id))
                        continue;

                    // A hidden, disabled, invalid or denied parent blocks descendants.
                    // Never leak unauthorized ancestor labels as empty containers.
                    if (row.PermissionId is Guid permission && !allowed.Contains(permission))
                        continue;
                    if (!ValidRoute(row.Route, kind) || !ValidExternalUrl(row.ExternalUrl))
                        continue;

                    var childs = Build(row.Id, depth + 1);
                    // Empty containers are not navigable; do not render them.
                    if (string.IsNullOrWhiteSpace(row.Route) &&
                        string.IsNullOrWhiteSpace(row.ExternalUrl) &&
                        childs.Count == 0)
                        continue;

                    items.Add(new CurrentNavigationItem(
                        row.Title, row.Icon, row.Route, row.ExternalUrl, childs));
                }
                return items;
            }

            var items = Build(null, 0);
            if (items.Count > 0)
                result.Add(new CurrentNavigationCategory(category.Title, items));
        }
        return result;
    }

    private static bool ValidRoute(string? route, UserType kind)
    {
        if (string.IsNullOrWhiteSpace(route))
            return true;

        if (!route.StartsWith('/') || route.StartsWith("//") ||
            route.Contains('\\') || route.Contains('?') || route.Contains('#') ||
            route.Contains("..", StringComparison.Ordinal))
            return false;

        // Until Menu has a real Scope/feature key, never put /system pages in
        // the Tenant sidebar and never put Tenant routes in the System sidebar.
        var isSystem = route.StartsWith("/system/", StringComparison.OrdinalIgnoreCase);
        if (kind == UserType.System)
            return isSystem &&
                !route.StartsWith("/system/auth/", StringComparison.OrdinalIgnoreCase);

        return !isSystem &&
            !route.Equals("/system", StringComparison.OrdinalIgnoreCase) &&
            !route.StartsWith("/auth", StringComparison.OrdinalIgnoreCase) &&
            !route.StartsWith("/api", StringComparison.OrdinalIgnoreCase) &&
            !route.StartsWith("/connect", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ValidExternalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
            parsed.Scheme == Uri.UriSchemeHttps &&
            string.IsNullOrEmpty(parsed.UserInfo) &&
            !string.IsNullOrWhiteSpace(parsed.Host);
    }
}
