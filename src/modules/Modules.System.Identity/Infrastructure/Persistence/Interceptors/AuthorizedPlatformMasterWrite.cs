using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Infrastructure.Persistence.Interceptors;

/// <summary>
/// A narrow, audited gateway into Master Tenant *role definition* writes.
/// Validates live account and Master-scoped authorization before opening the
/// existing trusted write scope. No untrusted caller can specify a target Tenant.
/// </summary>
public sealed class AuthorizedPlatformMasterWrite(
    IdentityDbContext db,
    ITenantContext subject,
    TrustedTenantWriteScope trustedWrites,
    ILogger<AuthorizedPlatformMasterWrite> logger)
{
    public async Task<IDisposable> BeginAsync(ClaimsPrincipal principal,
        Guid? targetRoleId = null, CancellationToken cancellationToken = default)
    {
        if (!SystemAuthorizationPolicies.IsSystemAdministrator(principal) ||
            !Guid.TryParse(principal.FindFirstValue(OpenIddictConstants.Claims.Subject), out var actorId) ||
            !subject.IsSystemContext || !subject.IsSuperAdmin ||
            subject.UserId != actorId)
            throw new UnauthorizedAccessException("A validated platform administrator is required.");

        var masterTenantId = DefaultSeedData.MasterTenant.Id;

        var active = await db.Users.AsNoTracking().AnyAsync(x =>
            x.Id == actorId && x.UserType == UserType.System && x.TenantId == null &&
            x.IsActive && (!x.IsLockedOut || x.LockoutEndUtc <= DateTime.UtcNow),
            cancellationToken);
        var masterActive = await db.Tenants.AsNoTracking()
            .AnyAsync(x => x.Id == masterTenantId && x.IsActive, cancellationToken);
        var assigned = await db.UserRoles.IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .AnyAsync(x => x.UserId == actorId && x.TenantId == masterTenantId &&
                x.Role.TenantId == masterTenantId && x.Role.IsActive &&
                x.Role.NormalizedName == "SUPERADMIN", cancellationToken);

        if (!active || !masterActive || !assigned)
        {
            logger.LogWarning("Denied master write for System actor {ActorId}", actorId);
            throw new UnauthorizedAccessException("Platform role or account has been revoked.");
        }

        // Verify role ownership before opening the privileged read/write scopes.
        // A client-controlled role identifier must never select a foreign tenant.
        if (targetRoleId.HasValue && !await db.Roles.IgnoreQueryFilters(["tenant"])
            .AsNoTracking().AnyAsync(x => x.Id == targetRoleId.Value &&
                x.TenantId == masterTenantId, cancellationToken))
        {
            logger.LogWarning("Denied platform write for foreign or missing role {RoleId}", targetRoleId);
            throw new UnauthorizedAccessException("Role is outside the platform storage scope.");
        }

        // Read visibility and write authority are two separate, individually
        // disposed scopes. Disabling the read filter NEVER bypasses write checks.
        var readScope = subject.DisableFilter();
        try
        {
            var writeScope = trustedWrites.BeginPlatformMasterWrite(
                subject, actorId, masterTenantId);
            logger.LogWarning("Authorized master write scope opened for actor {ActorId}", actorId);
            return new CombinedScope(writeScope, readScope, logger, actorId);
        }
        catch
        {
            readScope.Dispose();
            throw;
        }
    }

    private sealed class CombinedScope(
        IDisposable write, IDisposable read,
        ILogger<AuthorizedPlatformMasterWrite> logger, Guid actorId) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { write.Dispose(); }
            finally
            {
                read.Dispose();
                logger.LogWarning("Authorized master write scope closed for actor {ActorId}", actorId);
            }
        }
    }
}
