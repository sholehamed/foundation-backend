using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using OpenIddict.Abstractions;
using System.Security.Claims;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Refuses to renew a refresh token when its snapshot of the account's
/// authorizations no longer matches the database. The user must log in again.
/// Does not override OpenIddict's own token expiration/revocation checks.
/// </summary>
public sealed class RefreshGrantValidator(IIdentityDbContext database)
{
    public async Task<bool> IsStillAuthorizedAsync(ClaimsPrincipal original, CancellationToken ct = default)
    {
        if (!Guid.TryParse(original.GetClaim(OpenIddictConstants.Claims.Subject), out var userId))
            return false;

        var accountType = original.GetClaim("account_type");
        if (accountType is not ("system" or "tenant"))
            return false;

        var user = await database.Users.IgnoreQueryFilters(["tenant"])
            .AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);

        if (user is null || !user.IsActive ||
            (user.IsLockedOut && user.LockoutEndUtc > DateTime.UtcNow))
            return false;

        // Tokens created before the security-stamp rollout require a new login.
        if (string.IsNullOrEmpty(user.SecurityStamp) ||
            !string.Equals(user.SecurityStamp, original.GetClaim("security_stamp"), StringComparison.Ordinal))
            return false;

        Guid? tenantId = null;
        if (accountType == "system")
        {
            if (user.UserType != UserType.System || user.TenantId is not null ||
                original.HasClaim(c => c.Type == "tenant_id"))
                return false;
        }
        else
        {
            if (!Guid.TryParse(original.GetClaim("tenant_id"), out var homeTenant) ||
                user.UserType != UserType.Tenant || user.TenantId != homeTenant)
                return false;

            if (!await database.Tenants.AsNoTracking()
                .AnyAsync(x => x.Id == homeTenant && x.IsActive &&
                               x.Slug == original.GetClaim("tenant"), ct))
                return false;

            tenantId = homeTenant;
        }

        var rolesQuery = database.UserRoles.IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .Where(x => x.UserId == userId && x.Role.IsActive);
        var groupRolesQuery = database.UserGroups.IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .Where(x => x.UserId == userId && x.Group.IsActive)
            .SelectMany(x => x.Group.GroupRoles.Where(gr => gr.Role.IsActive));
        if (tenantId is Guid tenantScope)
        {
            rolesQuery = rolesQuery.Where(x => x.TenantId == tenantScope);
            groupRolesQuery = groupRolesQuery.Where(x => x.TenantId == tenantScope);
        }

        var directRoles = await rolesQuery.Select(x => x.Role.Name).ToListAsync(ct);
        var groupRoles = await groupRolesQuery.Select(x => x.Role.Name).ToListAsync(ct);
        var currentRoles = directRoles.Concat(groupRoles).Distinct(StringComparer.OrdinalIgnoreCase);

        if (!EqualClaims(original, OpenIddictConstants.Claims.Role, currentRoles))
            return false;

        var directPermissions = database.UserPermissions.IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .Where(x => x.UserId == userId && x.IsGranted && x.Permission.IsActive);
        if (tenantId is Guid scope)
            directPermissions = directPermissions.Where(x => x.TenantId == scope);

        var rolePermissions = rolesQuery
            .SelectMany(x => x.Role.RolePermissions.Where(rp => rp.Permission.IsActive))
            .Select(x => x.Permission.Name);
        var groupPermissions = groupRolesQuery
            .SelectMany(x => x.Role.RolePermissions.Where(rp => rp.Permission.IsActive))
            .Select(x => x.Permission.Name);
        var currentPermissions = (await directPermissions.Select(x => x.Permission.Name)
                .ToListAsync(ct))
            .Concat(await rolePermissions.ToListAsync(ct))
            .Concat(await groupPermissions.ToListAsync(ct))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return EqualClaims(original, "permission", currentPermissions);
    }

    private static bool EqualClaims(ClaimsPrincipal principal, string claimType,
        IEnumerable<string> actual) =>
        principal.FindAll(claimType).Select(x => x.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(actual);
}
