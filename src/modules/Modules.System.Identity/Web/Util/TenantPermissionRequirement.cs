using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// First explicit ADR-003 permission building block. Supports Tenant scope ONLY.
/// Platform, Branch, Own and Child authorization must use dedicated handlers.
/// No implicit mapping from URL or HTTP method is performed.
/// </summary>
public sealed class TenantPermissionRequirement(string resource, string action)
    : IAuthorizationRequirement
{
    public string Key { get; } = Normalize(resource, action);

    private static string Normalize(string resource, string action)
    {
        if (string.IsNullOrWhiteSpace(resource) || string.IsNullOrWhiteSpace(action) ||
            !resource.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-') ||
            !action.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
            throw new ArgumentException("Permission must be a stable lowercase resource and action key.");

        return (resource + "." + action).ToUpperInvariant();
    }
}

public static class TenantPermissionEndpointExtensions
{
    public static TBuilder RequireTenantPermission<TBuilder>(
        this TBuilder endpoint, string resource, string action)
        where TBuilder : IEndpointConventionBuilder
    {
        // This is additive: an endpoint may impose other authorization requirements.
        endpoint.RequireAuthorization(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new TenantPermissionRequirement(resource, action))
            .Build());
        return endpoint;
    }
}

/// <summary>
/// Fails closed on absent/forged tenant claims or inactive membership. Does not
/// grant platform-level permission and never infers access from a displayed menu.
/// </summary>
public sealed class TenantPermissionHandler(IIdentityDbContext db)
    : AuthorizationHandler<TenantPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, TenantPermissionRequirement requirement)
    {
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated != true ||
            !principal.HasClaim("account_type", "tenant") ||
            !Guid.TryParse(principal.FindFirstValue(OpenIddictConstants.Claims.Subject), out var userId) ||
            !Guid.TryParse(principal.FindFirstValue("tenant_id"), out var tenantId))
            return;

        var ct = context.Resource is HttpContext http
            ? http.RequestAborted : CancellationToken.None;
        if (await new EffectivePermissionEvaluator(db).HasPermissionAsync(
                userId, tenantId, UserType.Tenant, requirement.Key, ct))
            context.Succeed(requirement);
    }
}
