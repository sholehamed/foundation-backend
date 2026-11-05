using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
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
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true ||
            !user.HasClaim("account_type", "tenant") ||
            !Guid.TryParse(user.FindFirstValue(OpenIddictConstants.Claims.Subject), out var userId) ||
            !Guid.TryParse(user.FindFirstValue("tenant_id"), out var tenantId))
            return;

        var ct = context.Resource is HttpContext http ? http.RequestAborted : CancellationToken.None;

        var accountActive = await db.Users.AsNoTracking().AnyAsync(x =>
            x.Id == userId && x.UserType == UserType.Tenant &&
            x.TenantId == tenantId && x.IsActive &&
            (!x.IsLockedOut || x.LockoutEndUtc <= DateTime.UtcNow), ct);
        if (!accountActive)
            return;

        if (!await db.Tenants.AsNoTracking().AnyAsync(x => x.Id == tenantId && x.IsActive, ct))
            return;

        // Under the current Identity schema User.TenantId is the home-tenant
        // membership. Cross-tenant access needs an explicit verified membership
        // and is intentionally NOT granted by this Tenant-only requirement.
        // Preserve the established Identity model: a user-level explicit
        // revocation blocks inherited grants from roles and groups.
        var explicitlyDenied = await db.UserPermissions.IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.TenantId == tenantId &&
                !x.IsGranted && x.Permission.IsActive && x.Permission.Code == requirement.Key, ct);
        if (explicitlyDenied)
            return;

        var direct = await db.UserPermissions.IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.TenantId == tenantId &&
                x.IsGranted && x.Permission.IsActive && x.Permission.Code == requirement.Key, ct);
        if (direct)
        {
            context.Succeed(requirement);
            return;
        }

        var viaRole = await db.UserRoles.IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.TenantId == tenantId &&
                x.Role.TenantId == tenantId && x.Role.IsActive &&
                x.Role.RolePermissions.Any(rp => rp.TenantId == tenantId &&
                    rp.Permission.IsActive && rp.Permission.Code == requirement.Key), ct);
        if (viaRole)
        {
            context.Succeed(requirement);
            return;
        }

        var viaGroup = await db.UserGroups.IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.TenantId == tenantId &&
                x.Group.TenantId == tenantId && x.Group.IsActive &&
                x.Group.GroupRoles.Any(gr => gr.TenantId == tenantId &&
                    gr.Role.TenantId == tenantId && gr.Role.IsActive &&
                    gr.Role.RolePermissions.Any(rp => rp.TenantId == tenantId &&
                        rp.Permission.IsActive && rp.Permission.Code == requirement.Key)), ct);

        if (viaGroup)
            context.Succeed(requirement);
    }
}
