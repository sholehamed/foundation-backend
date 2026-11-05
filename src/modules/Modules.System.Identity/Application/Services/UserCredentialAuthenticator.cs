using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using OpenIddict.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Modules.System.Identity.Application.Services;

/// <summary>
/// Validates a user credential pair and builds the canonical Identity principal.
/// OAuth grant handlers and interactive Identity sign-in share this service so
/// password/lockout/tenant/permission rules cannot drift between login paths.
/// </summary>
public sealed class UserCredentialAuthenticator(
    IIdentityDbContext db,
    IPasswordHasher<User> passwordHasher) : IUserCredentialAuthenticator
{
    public async Task<ClaimsPrincipal?> AuthenticateAsync(
        string username,
        string password,
        IdentityLoginContext context,
        string? tenantSlug,
        CancellationToken cancellationToken = default)
    {
        var normalizedUserName = username?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedUserName) ||
            string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        Tenant? tenant = null;
        User? user;

        if (context == IdentityLoginContext.Tenant)
        {
            if (string.IsNullOrWhiteSpace(tenantSlug))
                return null;

            var normalizedSlug = tenantSlug.Trim().ToLowerInvariant();
            tenant = await db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Slug == normalizedSlug && x.IsActive,
                    cancellationToken);

            if (tenant is null)
                return null;

            user = await db.Users.IgnoreQueryFilters(["tenant"])
                .FirstOrDefaultAsync(x =>
                    x.TenantId == tenant.Id &&
                    x.UserType == UserType.Tenant &&
                    x.NormalizedUserName == normalizedUserName &&
                    x.IsActive,
                    cancellationToken);
        }
        else
        {
            user = await db.Users.IgnoreQueryFilters(["tenant"])
                .FirstOrDefaultAsync(x =>
                    x.NormalizedUserName == normalizedUserName &&
                    x.UserType == UserType.System &&
                    x.TenantId == null &&
                    x.IsActive,
                    cancellationToken);
        }

        if (user is null)
            return null;

        var now = DateTime.UtcNow;
        if (user.IsLockedOut && user.LockoutEndUtc.HasValue &&
            user.LockoutEndUtc.Value > now)
        {
            return null;
        }

        var verification = passwordHasher.VerifyHashedPassword(
            user, user.PasswordHash, password);

        if (verification == PasswordVerificationResult.Failed)
        {
            user.AccessFailedCount++;
            if (user.AccessFailedCount >= 5)
            {
                user.IsLockedOut = true;
                user.LockoutEndUtc = now.AddMinutes(15);
            }

            user.LastModifiedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        var accountChanged = false;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            accountChanged = true;
        }

        if (user.AccessFailedCount > 0 || user.IsLockedOut ||
            user.LockoutEndUtc.HasValue)
        {
            user.AccessFailedCount = 0;
            user.IsLockedOut = false;
            user.LockoutEndUtc = null;
            accountChanged = true;
        }

        if (accountChanged)
        {
            user.LastModifiedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        var userRoles = db.UserRoles.IgnoreQueryFilters(["tenant"])
            .Where(x => x.UserId == user.Id);
        var userGroups = db.UserGroups.IgnoreQueryFilters(["tenant"])
            .Where(x => x.UserId == user.Id);
        var userPermissions = db.UserPermissions.IgnoreQueryFilters(["tenant"])
            .Where(x => x.UserId == user.Id && x.IsGranted);

        if (tenant is not null)
        {
            userRoles = userRoles.Where(x => x.TenantId == tenant.Id);
            userGroups = userGroups.Where(x => x.TenantId == tenant.Id);
            userPermissions = userPermissions.Where(x => x.TenantId == tenant.Id);
        }

        var permissions = await userPermissions
            .Select(x => x.Permission.Name)
            .Union(userRoles.SelectMany(x =>
                x.Role.RolePermissions.Select(rp => rp.Permission.Name)))
            .Union(userGroups.SelectMany(x =>
                x.Group.GroupRoles.SelectMany(gr =>
                    gr.Role.RolePermissions.Select(rp => rp.Permission.Name))))
            .Distinct()
            .ToListAsync(cancellationToken);

        var roles = await userRoles.Select(x => x.Role.Name)
            .Union(userGroups.SelectMany(x =>
                x.Group.GroupRoles.Select(gr => gr.Role.Name)))
            .Distinct()
            .ToListAsync(cancellationToken);

        var claims = new List<Claim>
        {
            new(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
            new(OpenIddictConstants.Claims.Username, user.UserName),
            new(OpenIddictConstants.Claims.Email, user.Email),
            new(OpenIddictConstants.Claims.Picture, user.ImageId?.ToString() ?? string.Empty),
            new(OpenIddictConstants.Claims.GivenName,
                $"{user.FirstName} {user.LastName}".Trim()),
            new("account_type",
                context == IdentityLoginContext.System ? "system" : "tenant"),
            new("security_stamp", user.SecurityStamp ?? string.Empty)
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
