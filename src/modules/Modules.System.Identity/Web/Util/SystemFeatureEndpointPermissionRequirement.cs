using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using OpenIddict.Abstractions;
using System.Security.Claims;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// FR-14A: Every registered System endpoint descriptor is an executable
/// authorization requirement when the reviewed granular rollout is enabled.
/// It is never enough merely to attach descriptive FeatureEndpointMetadata.
/// </summary>
public sealed class SystemFeatureEndpointPermissionRequirement(
    string resourceCode, string permissionCode) : IAuthorizationRequirement
{
    public string ResourceCode { get; } = resourceCode;
    public string PermissionCode { get; } = permissionCode;

    // Reject invented, moved or cross-resource permission codes, including
    // ones which happen to exist in an unrelated persisted database row.
    public bool IsRegistered =>
        FeatureRegistryDefinitions.Features.Any(f =>
            f.ResourceCode == ResourceCode &&
            f.PermissionCodes.Contains(PermissionCode, StringComparer.Ordinal));
}

public sealed class SystemFeatureEndpointPermissionHandler(
    IIdentityDbContext db, IConfiguration configuration)
    : AuthorizationHandler<SystemFeatureEndpointPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SystemFeatureEndpointPermissionRequirement requirement)
    {
        // Still bound by the separate live SystemAdministrator endpoint-group
        // policy; no Tenant/system-principal crossover, including disabled mode.
        if (!SystemAuthorizationPolicies.IsSystemAdministrator(context.User) ||
            !requirement.IsRegistered)
            return;

        // All System routes have metadata & explicit platform permission
        // definitions. Rollout remains OFF in normal builds until approved
        // Master SQL inventory, Registry Apply and grants are independently
        // reviewed; a code merge cannot silently activate the stricter policy.
        if (!configuration.GetValue<bool>(
            "Identity:PlatformPermissions:EnforcePermissionEndpoints"))
        {
            context.Succeed(requirement);
            return;
        }

        if (!Guid.TryParse(context.User.FindFirstValue(
                OpenIddictConstants.Claims.Subject), out var userId) ||
            context.User.HasClaim(c => c.Type == "tenant_id"))
            return;

        var cancellationToken = context.Resource is HttpContext http
            ? http.RequestAborted : CancellationToken.None;

        // FR-14C: a code match alone is NOT sufficient. A historical/global
        // Permission with ResourceId=NULL, or one moved to a foreign Resource,
        // would otherwise pass a role grant despite not being owned by the
        // registered System feature. Verify the live Module/Resource/Permission
        // graph before evaluating any user grant. Duplicate codes fail closed.
        var feature = FeatureRegistryDefinitions.Features.SingleOrDefault(f =>
            f.ResourceCode == requirement.ResourceCode &&
            f.PermissionCodes.Contains(requirement.PermissionCode, StringComparer.Ordinal));
        if (feature is null)
            return;
        var owners = await db.Permissions.AsNoTracking()
            .Where(p => p.Code == requirement.PermissionCode)
            .Select(p => new
            {
                p.IsActive,
                p.ResourceId,
                ResourceActive = p.Resource != null && p.Resource.IsActive,
                ResourceCode = p.Resource == null ? null : p.Resource.Code,
                ModuleActive = p.Resource != null && p.Resource.Module != null &&
                    p.Resource.Module.IsActive,
                ModuleCode = p.Resource == null || p.Resource.Module == null ?
                    null : p.Resource.Module.Code
            }).ToListAsync(cancellationToken);
        // Exactly one row, owned by exactly the reviewed System feature.
        // No null/foreign owner fallback and no arbitrary duplicate winner.
        if (owners.Count != 1 || !owners[0].IsActive ||
            owners[0].ResourceId is null || !owners[0].ResourceActive ||
            !owners[0].ModuleActive ||
            owners[0].ResourceCode != feature.ResourceCode ||
            owners[0].ModuleCode != feature.ModuleCode)
            return;

        if (await new EffectivePermissionEvaluator(db).HasPermissionAsync(
                userId, DefaultSeedData.MasterTenant.Id, UserType.System,
                requirement.PermissionCode, cancellationToken))
            context.Succeed(requirement);
    }
}
