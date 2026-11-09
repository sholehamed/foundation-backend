using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Application.Services;

/// <summary>
/// Builds a current principal from assignments in exactly one authorization scope.
/// System identities use the master platform scope; tenant identities use only their
/// own tenant scope. Nested RolePermission and GroupRole assignments are filtered too.
/// </summary>
public sealed class IdentityPrincipalFactory(
    IIdentityDbContext db,
    ISystemTenantScope systemTenantScope) : IIdentityPrincipalFactory
{
    public async Task<ClaimsPrincipal> CreateAsync(
        User user,
        IdentityLoginContext context,
        Tenant? tenant,
        long? authenticationTimeUnixSeconds,
        CancellationToken cancellationToken = default)
    {
        if (context == IdentityLoginContext.System &&
            (user.UserType != UserType.System || user.TenantId is not null || tenant is not null))
        {
            throw new InvalidOperationException("A System principal must be built from a global System account.");
        }

        if (context == IdentityLoginContext.Tenant &&
            (user.UserType != UserType.Tenant || tenant is null ||
             user.TenantId != tenant.Id || !tenant.IsActive))
        {
            throw new InvalidOperationException("A Tenant principal must be bound to its active owning tenant.");
        }

        var scopeTenantId = context == IdentityLoginContext.System
            ? systemTenantScope.MasterTenantId
            : tenant!.Id;

        var userRoles = db.UserRoles.IgnoreQueryFilters(["tenant"])
            .Where(x => x.UserId == user.Id &&
                        x.TenantId == scopeTenantId &&
                        x.Role.TenantId == scopeTenantId &&
                        x.Role.IsActive);

        var userGroups = db.UserGroups.IgnoreQueryFilters(["tenant"])
            .Where(x => x.UserId == user.Id &&
                        x.TenantId == scopeTenantId &&
                        x.Group.TenantId == scopeTenantId &&
                        x.Group.IsActive);

        // Claims are snapshots for presentation only; API policy gates always
        // re-read live permissions. Include both Code and legacy Name while
        // filtering explicit denials out of both projections.
        var userPermissions = db.UserPermissions.IgnoreQueryFilters(["tenant"])
            .Where(x => x.UserId == user.Id &&
                        x.TenantId == scopeTenantId &&
                        x.Permission.IsActive);

        var deniedCodes = (await userPermissions
            .Where(x => !x.IsGranted)
            .Select(x => x.Permission.Code)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var directPermissions = await userPermissions
            .Where(x => x.IsGranted)
            .Select(x => new { x.Permission.Code, x.Permission.Name })
            .ToListAsync(cancellationToken);

        var rolePermissions = await userRoles
            .SelectMany(x => x.Role.RolePermissions
                .Where(rp => rp.TenantId == scopeTenantId && rp.Permission.IsActive)
                .Select(rp => new { rp.Permission.Code, rp.Permission.Name }))
            .ToListAsync(cancellationToken);

        var groupPermissions = await userGroups
            .SelectMany(x => x.Group.GroupRoles
                .Where(gr => gr.TenantId == scopeTenantId &&
                             gr.Role.IsActive &&
                             gr.Role.TenantId == scopeTenantId)
                .SelectMany(gr => gr.Role.RolePermissions
                    .Where(rp => rp.TenantId == scopeTenantId && rp.Permission.IsActive)
                    .Select(rp => new { rp.Permission.Code, rp.Permission.Name })))
            .ToListAsync(cancellationToken);

        var directRoles = await userRoles
            .Select(x => x.Role.Name)
            .ToListAsync(cancellationToken);

        var groupRoles = await userGroups
            .SelectMany(x => x.Group.GroupRoles
                .Where(gr => gr.TenantId == scopeTenantId &&
                             gr.Role.IsActive &&
                             gr.Role.TenantId == scopeTenantId)
                .Select(gr => gr.Role.Name))
            .ToListAsync(cancellationToken);

        var permitted = directPermissions
            .Concat(rolePermissions)
            .Concat(groupPermissions)
            .Where(x => !deniedCodes.Contains(x.Code))
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        var legacyPermissionNames = permitted.Select(x => x.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var stablePermissionCodes = permitted.Select(x => x.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var roles = directRoles
            .Concat(groupRoles)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var claims = new List<Claim>
        {
            new(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
            new(OpenIddictConstants.Claims.Username, user.UserName),
            new(OpenIddictConstants.Claims.Picture, user.ImageId?.ToString() ?? string.Empty),
            new(OpenIddictConstants.Claims.GivenName, $"{user.FirstName} {user.LastName}".Trim()),
            new("account_type", context == IdentityLoginContext.System ? "system" : "tenant"),
            new("security_stamp", user.SecurityStamp ?? string.Empty),
            new("auth_time", (authenticationTimeUnixSeconds ??
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        };

        // Standards-compliant: the email claim is OPTIONAL. Never fabricate
        // a fake address or construct a Claim with a null value for
        // physically onboarded Tenant staff without an email.
        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new Claim(OpenIddictConstants.Claims.Email, user.Email));

        if (tenant is not null)
        {
            claims.Add(new Claim("tenant_id", tenant.Id.ToString()));
            claims.Add(new Claim("tenant", tenant.Slug));
        }

        claims.AddRange(roles.Select(role =>
            new Claim(OpenIddictConstants.Claims.Role, role)));
        claims.AddRange(legacyPermissionNames.Select(name =>
            new Claim("permission", name)));
        claims.AddRange(stablePermissionCodes.Select(permissionCode =>
            new Claim("permission_code", permissionCode)));

        var identity = new ClaimsIdentity(
            claims,
            TokenValidationParameters.DefaultAuthenticationType,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);

        return new ClaimsPrincipal(identity);
    }
}
