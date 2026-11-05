using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using OpenIddict.Abstractions;
using Modules.System.Identity.Infrastructure.Persistence;
using System.Security.Claims;

namespace Modules.System.Identity.Web.Util;

public sealed class ActiveSystemAdministratorRequirement : IAuthorizationRequirement;

public sealed class ActiveSystemAdministratorHandler(IIdentityDbContext database)
    : AuthorizationHandler<ActiveSystemAdministratorRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        ActiveSystemAdministratorRequirement requirement)
    {
        if (!SystemAuthorizationPolicies.IsSystemAdministrator(context.User) ||
            !Guid.TryParse(context.User.FindFirstValue(OpenIddictConstants.Claims.Subject), out var userId))
            return;

        // Database check on every sensitive request ensures a revoked role or disabled
        // administrator cannot keep using a still-unexpired JWT with stale role claims.
        var active = await database.Users.AsNoTracking().AnyAsync(x =>
            x.Id == userId && x.UserType == UserType.System && x.TenantId == null &&
            x.IsActive && (!x.IsLockedOut || x.LockoutEndUtc <= DateTime.UtcNow));

        if (!active)
            return;

        var hasRole = await database.UserRoles.IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .AnyAsync(x => x.UserId == userId &&
                           x.TenantId == DefaultSeedData.MasterTenant.Id &&
                           x.Role.TenantId == DefaultSeedData.MasterTenant.Id &&
                           x.Role.IsActive && x.Role.NormalizedName == "SUPERADMIN");

        if (hasRole)
            context.Succeed(requirement);
    }
}
