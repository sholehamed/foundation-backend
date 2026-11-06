using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.System.Bff;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Application.Services;
using Modules.System.Identity.Domain.Entities;

namespace Server.Bff;

/// <summary>
/// The Web composition-root adapter bridges BFF's narrow contract to the
/// current Identity database. No Identity -> BFF project dependency.
/// </summary>
public sealed class IdentityBffSessionAccessValidator(
    IIdentityDbContext database,
    IIdentityPrincipalFactory principals) : IBffSessionAccessValidator
{
    public async Task<BffSessionAccessSnapshot?> CaptureAsync(
        ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirst("sub")?.Value, out var userId))
            return null;

        var accountType = principal.FindFirst("account_type")?.Value;
        User? user;
        Tenant? tenant = null;
        IdentityLoginContext context;

        if (accountType == "system")
        {
            if (principal.HasClaim(c => c.Type is "tenant_id" or "tenant"))
                return null;

            context = IdentityLoginContext.System;
            user = await database.Users.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().FirstOrDefaultAsync(x =>
                    x.Id == userId && x.UserType == UserType.System &&
                    x.TenantId == null && x.IsActive, cancellationToken);
        }
        else if (accountType == "tenant")
        {
            if (!Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out var tenantId) ||
                string.IsNullOrWhiteSpace(principal.FindFirst("tenant")?.Value))
                return null;

            var slug = principal.FindFirst("tenant")!.Value;
            tenant = await database.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == tenantId && x.Slug == slug && x.IsActive,
                    cancellationToken);
            if (tenant is null)
                return null;

            context = IdentityLoginContext.Tenant;
            user = await database.Users.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().FirstOrDefaultAsync(x =>
                    x.Id == userId && x.TenantId == tenantId &&
                    x.UserType == UserType.Tenant && x.IsActive,
                    cancellationToken);
        }
        else
        {
            return null;
        }

        if (user is null || user.IsLockedOut ||
            (user.LockoutEndUtc is { } lockedUntil &&
             lockedUntil > DateTime.UtcNow) ||
            string.IsNullOrWhiteSpace(user.SecurityStamp))
            return null;

        // Compare the ticket's granted authority against a fresh principal
        // rebuilt from the currently scoped role/group/permission assignments.
        // Never trust roles/permissions from a persisted cookie as live grants.
        var current = await principals.CreateAsync(
            user, context, tenant,
            authenticationTimeUnixSeconds: null, cancellationToken);

        var issuedGrants = Fingerprint(principal);
        var currentGrants = Fingerprint(current);
        if (!string.Equals(issuedGrants, currentGrants, StringComparison.Ordinal))
            return null;

        return new BffSessionAccessSnapshot(user.SecurityStamp, issuedGrants);
    }

    public async Task<bool> IsCurrentAsync(
        ClaimsPrincipal principal,
        BffSessionAccessSnapshot issued,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(issued.SecurityStamp) ||
            string.IsNullOrWhiteSpace(issued.GrantsFingerprint))
            return false;

        var current = await CaptureAsync(principal, cancellationToken);
        return current is not null &&
            string.Equals(current.SecurityStamp, issued.SecurityStamp,
                StringComparison.Ordinal) &&
            string.Equals(current.GrantsFingerprint, issued.GrantsFingerprint,
                StringComparison.Ordinal);
    }

    private static string Fingerprint(ClaimsPrincipal principal)
    {
        static string[] DistinctSorted(IEnumerable<string> values)
            => values.Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.ToUpperInvariant())
                .ToArray();

        var grants = new
        {
            roles = DistinctSorted(principal.FindAll("role").Select(x => x.Value)),
            permissions = DistinctSorted(principal.FindAll("permission").Select(x => x.Value))
        };
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(grants))));
    }
}
