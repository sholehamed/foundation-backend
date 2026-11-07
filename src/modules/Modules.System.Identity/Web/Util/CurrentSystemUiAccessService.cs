using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Live System-only UI affordance decision for one reviewed code-owned key.
/// This cannot authorize APIs. Never trust client-supplied permission, role,
/// resource, subject or tenant identifiers; only the stable UI key is accepted.
/// A missing/inactive/misconfigured catalog, disabled user or direct deny always
/// yields false; no superadmin implicit blanket grant.
/// </summary>
public sealed class CurrentSystemUiAccessService(IIdentityDbContext db)
{
    public async Task<bool> IsAllowedAsync(
        ClaimsPrincipal principal, string key, CancellationToken ct = default)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            !principal.HasClaim("account_type", "system") ||
            principal.HasClaim(c => c.Type is "tenant_id" or "tenant") ||
            !Guid.TryParse(principal.FindFirstValue(OpenIddictConstants.Claims.Subject), out var userId) ||
            string.IsNullOrWhiteSpace(key))
            return false;

        var feature = FeatureRegistryDefinitions.Features.SingleOrDefault(x =>
            string.Equals(x.PageKey, key, StringComparison.OrdinalIgnoreCase));
        var element = feature is null
            ? FeatureRegistryDefinitions.UiElements.SingleOrDefault(x =>
                string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
            : null;

        if (feature is null && element is null)
            return false;

        var owner = feature ?? FeatureRegistryDefinitions.Features.Single(x =>
            x.PageKey == element!.FeatureKey);

        // A child is never available when its owning PAGE is unavailable.
        if (!await MatchesCatalogAsync(owner, owner.PageKey, owner.ReadPermission,
                UiResourceType.PAGE, owner.Route, ct))
            return false;

        var evaluator = new EffectivePermissionEvaluator(db);
        if (!await evaluator.HasPermissionAsync(userId,
                DefaultSeedData.MasterTenant.Id, UserType.System, owner.ReadPermission, ct))
            return false;

        if (element is null)
            return true;

        if (!await MatchesCatalogAsync(owner, element.Key, element.PermissionCode,
                element.Type, element.Route, ct))
            return false;

        return await evaluator.HasPermissionAsync(userId,
            DefaultSeedData.MasterTenant.Id, UserType.System, element.PermissionCode, ct);
    }

    private async Task<bool> MatchesCatalogAsync(
        FeatureRegistryDefinitions.Feature feature,
        string key, string permissionCode,
        UiResourceType expectedType, string? expectedRoute, CancellationToken ct)
    {
        // Reject historical duplicate UI keys rather than trusting First().
        var rows = await db.UiResources.AsNoTracking()
            .Where(x => x.Key == key)
            .Select(x => new
            {
                x.Id, x.ResourceId, x.IsActive, x.Type, x.Description
            }).Take(2).ToListAsync(ct);
        if (rows.Count != 1)
            return false;

        var ui = rows[0];
        if (!ui.IsActive || ui.Type != expectedType ||
            !string.Equals(ui.Description, expectedRoute, StringComparison.Ordinal))
            return false;

        var resources = await db.Resources.AsNoTracking()
            .Where(x => x.Id == ui.ResourceId && x.IsActive &&
                x.Code == feature.ResourceCode &&
                x.ModuleId != Guid.Empty && x.Module!.IsActive &&
                x.Module.Code == feature.ModuleCode)
            .Select(x => x.Id).Take(2).ToListAsync(ct);
        if (resources.Count != 1)
            return false;

        // UI keys with multiple permissions could allow an unrelated grant.
        // The exact single active code-owned link is required. A legacy
        // ResourceId=null platform permission is accepted as already supported
        // by the Registry, but a foreign ResourceId is never accepted.
        var links = await db.PermissionUiResources.AsNoTracking()
            .Where(x => x.UiResourceId == ui.Id)
            .Select(x => new
            {
                x.PermissionId,
                x.Permission!.Code,
                x.Permission.IsActive,
                x.Permission.ResourceId
            }).Take(2).ToListAsync(ct);

        return links.Count == 1 && links[0].IsActive &&
            string.Equals(links[0].Code, permissionCode, StringComparison.Ordinal) &&
            (links[0].ResourceId is null || links[0].ResourceId == ui.ResourceId);
    }
}
