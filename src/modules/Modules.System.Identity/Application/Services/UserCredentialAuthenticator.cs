using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Services;

/// <summary>
/// Verifies credentials and delegates claim construction to the same scoped
/// principal factory used when an existing Identity cookie is revalidated.
/// </summary>
public sealed class UserCredentialAuthenticator(
    IIdentityDbContext db,
    IPasswordHasher<User> passwordHasher,
    IIdentityPrincipalFactory principalFactory) : IUserCredentialAuthenticator
{
    public async Task<ClaimsPrincipal?> AuthenticateAsync(
        string username,
        string password,
        IdentityLoginContext context,
        string? tenantSlug,
        CancellationToken cancellationToken = default)
    {
        if (context is not (IdentityLoginContext.System or IdentityLoginContext.Tenant))
            return null;

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

        if (string.IsNullOrWhiteSpace(user.SecurityStamp))
        {
            user.SecurityStamp = Guid.NewGuid().ToString("N");
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

        return await principalFactory.CreateAsync(
            user,
            context,
            tenant,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            cancellationToken);
    }
}
