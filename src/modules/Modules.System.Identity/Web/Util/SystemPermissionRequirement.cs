using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
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
        var scope = DefaultSeedData.MasterTenant.Id;

        // System accounts are platform subjects, not tenants. This checks the
        // pre-existing platform role-storage scope, not User.TenantId.
        if (!await db.Users.AsNoTracking().AnyAsync(x =>
            x.Id == userId && x.UserType == UserType.System &&
            x.TenantId == null && x.IsActive &&
            (!x.IsLockedOut || x.LockoutEndUtc <= DateTime.UtcNow), ct))
            return;

        if (!await db.Tenants.AsNoTracking().AnyAsync(x =>
            x.Id == scope && x.IsActive, ct))
            return;

        // The existing identity schema supports IsGranted=false. Respect an
        // explicit user-level denial before checking role/group inheritance.
        var denied = await db.UserPermissions.IgnoreQueryFilters(["tenant"])
            .AsNoTracking().AnyAsync(x =>
                x.UserId == userId && x.TenantId == scope && !x.IsGranted &&
                x.Permission.IsActive && x.Permission.Code == requirement.Key, ct);
        if (denied)
            return;

        var direct = await db.UserPermissions.IgnoreQueryFilters(["tenant"])
            .AsNoTracking().AnyAsync(x =>
                x.UserId == userId && x.TenantId == scope && x.IsGranted &&
                x.Permission.IsActive && x.Permission.Code == requirement.Key, ct);
        if (direct)
        {
            context.Succeed(requirement);
            return;
        }

        var viaRole = await db.UserRoles.IgnoreQueryFilters(["tenant"])
            .AsNoTracking().AnyAsync(x =>
                x.UserId == userId && x.TenantId == scope &&
                x.Role.TenantId == scope && x.Role.IsActive &&
                x.Role.RolePermissions.Any(rp =>
                    rp.TenantId == scope && rp.Permission.IsActive &&
                    rp.Permission.Code == requirement.Key), ct);
        if (viaRole)
        {
            context.Succeed(requirement);
            return;
        }

        var viaGroup = await db.UserGroups.IgnoreQueryFilters(["tenant"])
            .AsNoTracking().AnyAsync(x =>
                x.UserId == userId && x.TenantId == scope &&
                x.Group.TenantId == scope && x.Group.IsActive &&
                x.Group.GroupRoles.Any(gr =>
                    gr.TenantId == scope &&
                    gr.Role.TenantId == scope && gr.Role.IsActive &&
                    gr.Role.RolePermissions.Any(rp =>
                        rp.TenantId == scope && rp.Permission.IsActive &&
                        rp.Permission.Code == requirement.Key)), ct);

        if (viaGroup)
            context.Succeed(requirement);
    }
}
