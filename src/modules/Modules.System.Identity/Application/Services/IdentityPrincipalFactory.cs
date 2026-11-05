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

        var userPermissions = db.UserPermissions.IgnoreQueryFilters(["tenant"])
            .Where(x => x.UserId == user.Id &&
                        x.TenantId == scopeTenantId &&
                        x.IsGranted &&
                        x.Permission.IsActive);

        var directPermissions = await userPermissions
            .Select(x => x.Permission.Name)
            .ToListAsync(cancellationToken);

        var rolePermissions = await userRoles
            .SelectMany(x => x.Role.RolePermissions
                .Where(rp => rp.TenantId == scopeTenantId && rp.Permission.IsActive)
                .Select(rp => rp.Permission.Name))
            .ToListAsync(cancellationToken);

        var groupPermissions = await userGroups
            .SelectMany(x => x.Group.GroupRoles
                .Where(gr => gr.TenantId == scopeTenantId &&
                             gr.Role.IsActive &&
                             gr.Role.TenantId == scopeTenantId)
                .SelectMany(gr => gr.Role.RolePermissions
                    .Where(rp => rp.TenantId == scopeTenantId && rp.Permission.IsActive)
                    .Select(rp => rp.Permission.Name)))
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

        var permissions = directPermissions
            .Concat(rolePermissions)
            .Concat(groupPermissions)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var roles = directRoles
            .Concat(groupRoles)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var claims = new List<Claim>
        {
            new(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
            new(OpenIddictConstants.Claims.Username, user.UserName),
            new(OpenIddictConstants.Claims.Email, user.Email),
            new(OpenIddictConstants.Claims.Picture, user.ImageId?.ToString() ?? string.Empty),
            new(OpenIddictConstants.Claims.GivenName, $"{user.FirstName} {user.LastName}".Trim()),
            new("account_type", context == IdentityLoginContext.System ? "system" : "tenant"),
            new("security_stamp", user.SecurityStamp ?? string.Empty),
            new("auth_time", (authenticationTimeUnixSeconds ??
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        };

        if (tenant is not null)
        {
            claims.Add(new Claim("tenant_id", tenant.Id.ToString()));
            claims.Add(new Claim("tenant", tenant.Slug));
        }

        claims.AddRange(roles.Select(role =>
            new Claim(OpenIddictConstants.Claims.Role, role)));
        claims.AddRange(permissions.Select(permission =>
            new Claim("permission", permission)));

        var identity = new ClaimsIdentity(
            claims,
            TokenValidationParameters.DefaultAuthenticationType,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);

        return new ClaimsPrincipal(identity);
    }
}
