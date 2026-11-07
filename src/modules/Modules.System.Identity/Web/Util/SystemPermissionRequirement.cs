using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Platform-specific Resource.Action authorization. The current schema stores
/// platform role/permission *definitions* under the fixed Master Tenant scope,
/// while platform users themselves have NO TenantId. This must not confer
/// platform permissions on tenant users or tenant-scoped role assignments.
///
/// Does not replace the existing SystemAdministrator boundary and does not
/// implement Branch/Own/Child assignment scopes from ADR-003.
/// </summary>
public sealed class SystemPermissionRequirement(string resource, string action)
    : IAuthorizationRequirement
{
    // Reuse the existing stable key validator; permission keys never derive from URLs.
    public string Key { get; } = new TenantPermissionRequirement(resource, action).Key;
}

public static class SystemPermissionEndpointExtensions
{
    public static TBuilder RequireSystemPermission<TBuilder>(
        this TBuilder endpoint, string resource, string action)
        where TBuilder : IEndpointConventionBuilder
    {
        endpoint.RequireAuthorization(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new SystemPermissionRequirement(resource, action))
            .Build());
        return endpoint;
    }
}

public sealed class SystemPermissionHandler(IIdentityDbContext db)
    : AuthorizationHandler<SystemPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, SystemPermissionRequirement requirement)
    {
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated != true ||
            !principal.HasClaim("account_type", "system") ||
            principal.HasClaim(c => c.Type == "tenant_id") ||
            !Guid.TryParse(principal.FindFirstValue(OpenIddictConstants.Claims.Subject), out var userId))
            return;

        var ct = context.Resource is HttpContext http
            ? http.RequestAborted : CancellationToken.None;
        if (await new EffectivePermissionEvaluator(db).HasPermissionAsync(
                userId, DefaultSeedData.MasterTenant.Id, UserType.System, requirement.Key, ct))
            context.Succeed(requirement);
    }
}
