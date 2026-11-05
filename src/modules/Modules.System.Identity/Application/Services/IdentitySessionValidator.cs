using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Application.Services;

/// <summary>
/// Revalidates an Identity Server cookie before it can authorize an OpenIddict code.
/// Claims are never treated as the source of truth for account, tenant or permissions.
/// </summary>
public sealed class IdentitySessionValidator(
    IIdentityDbContext db,
    IIdentityPrincipalFactory principalFactory) : IIdentitySessionValidator
{
    public async Task<ClaimsPrincipal?> ValidateAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(OpenIddictConstants.Claims.Subject), out var userId))
        {
            return null;
        }

        var accountType = principal.FindFirstValue("account_type");
        var securityStamp = principal.FindFirstValue("security_stamp");
        var authTimeValue = principal.FindFirstValue("auth_time");

        if (string.IsNullOrWhiteSpace(securityStamp) ||
            !long.TryParse(authTimeValue, out var authTime))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        User? user;
        Tenant? tenant = null;
        IdentityLoginContext context;

        if (accountType == "system")
        {
            if (principal.HasClaim(c => c.Type is "tenant_id" or "tenant"))
                return null;

            context = IdentityLoginContext.System;
            user = await db.Users.IgnoreQueryFilters(["tenant"])
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == userId &&
                    x.UserType == UserType.System &&
                    x.TenantId == null &&
                    x.IsActive,
                    cancellationToken);
        }
        else if (accountType == "tenant")
        {
            if (!Guid.TryParse(principal.FindFirstValue("tenant_id"), out var tenantId))
                return null;

            var tenantSlug = principal.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenantSlug))
                return null;

            tenant = await db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == tenantId &&
                    x.Slug == tenantSlug &&
                    x.IsActive,
                    cancellationToken);

            if (tenant is null)
                return null;

            context = IdentityLoginContext.Tenant;
            user = await db.Users.IgnoreQueryFilters(["tenant"])
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == userId &&
                    x.TenantId == tenantId &&
                    x.UserType == UserType.Tenant &&
                    x.IsActive,
                    cancellationToken);
        }
        else
        {
            return null;
        }

        if (user is null ||
            user.IsLockedOut ||
            user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > now ||
            string.IsNullOrWhiteSpace(user.SecurityStamp) ||
            !string.Equals(user.SecurityStamp, securityStamp, StringComparison.Ordinal))
        {
            return null;
        }

        return await principalFactory.CreateAsync(
            user,
            context,
            tenant,
            authTime,
            cancellationToken);
    }
}
