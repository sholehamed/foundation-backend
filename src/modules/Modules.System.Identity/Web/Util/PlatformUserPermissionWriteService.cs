using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Infrastructure.Persistence.Interceptors;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Explicit allow, deny and removal of one non-catalog System user permission.
/// SUPERADMIN subjects, self-mutations and reserved authorization catalog keys
/// remain outside this HTTP workflow. Direct deny overrides role/group allows.
/// </summary>
public sealed class PlatformUserPermissionWriteService(
    IdentityDbContext database,
    AuthorizedPlatformMasterWrite gateway,
    TrustedTenantWriteScope trustedWrites,
    ILogger<PlatformUserPermissionWriteService> logger)
{
    /// <param name="isGranted">
    /// True = direct allow, false = direct deny, null = remove direct override
    /// and fall back to role/group permissions.
    /// </param>
    public async Task<bool> ChangeAsync(
        ClaimsPrincipal principal, Guid targetUserId, Guid permissionId,
        bool? isGranted, CancellationToken cancellationToken = default)
    {
        if (targetUserId == Guid.Empty || permissionId == Guid.Empty ||
            !Guid.TryParse(principal.FindFirstValue(OpenIddictConstants.Claims.Subject),
                out var actorId) || actorId == targetUserId)
            throw new UnauthorizedAccessException(
                "Self permission changes and missing targets are forbidden.");

        var masterId = DefaultSeedData.MasterTenant.Id;
        var strategy = database.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            using var authorized = await gateway.BeginAsync(
                principal, cancellationToken: cancellationToken);

            var target = await database.Users.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == targetUserId &&
                    x.UserType == UserType.System && x.TenantId == null,
                    cancellationToken);
            if (target is null ||
                (isGranted.HasValue &&
                    (!target.IsActive ||
                     (target.IsLockedOut &&
                      (target.LockoutEndUtc is null ||
                       target.LockoutEndUtc > DateTime.UtcNow)))))
                throw new UnauthorizedAccessException("Target is not an eligible System account.");

            // A privileged subject cannot have its effective authorization
            // adjusted by a routine user-permission override.
            var privilegedTarget = await database.UserRoles.AsNoTracking()
                .AnyAsync(x => x.UserId == targetUserId &&
                    x.TenantId == masterId && x.Role.TenantId == masterId &&
                    x.Role.NormalizedName == "SUPERADMIN", cancellationToken);
            if (privilegedTarget)
                throw new UnauthorizedAccessException(
                    "SuperAdmin permission overrides require reviewed bootstrap.");

            // Permission definitions are global; the grant itself is stored
            // exclusively in the Master Tenant. Reserved platform authorization
            // catalog permissions cannot be assigned or denied here.
            var permission = await database.Permissions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == permissionId, cancellationToken);
            if (permission is null ||
                PlatformPermissionCatalog.IsReservedCode(permission.Code) ||
                (isGranted.HasValue && !permission.IsActive))
                throw new UnauthorizedAccessException(
                    "Reserved, inactive or missing permission cannot be overridden.");

            var existing = await database.UserPermissions.SingleOrDefaultAsync(
                x => x.TenantId == masterId && x.UserId == targetUserId &&
                     x.PermissionId == permissionId, cancellationToken);

            if (existing is null && !isGranted.HasValue ||
                existing is not null && existing.IsGranted == isGranted)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            using var intent = trustedWrites.BeginPlatformGrantMutation(
                PlatformGrantMutationKind.UserPermissions, targetUserId, permissionId);

            // Never MODIFY the grant row: the interceptor disallows in-place
            // assignment rewrites. Remove old override before inserting its
            // replacement to respect the unique (TenantId, UserId, PermissionId).
            // The two SaveChanges calls share one transaction and roll back
            // atomically if any later operation fails.
            if (existing is not null)
            {
                database.UserPermissions.Remove(existing);
                await database.SaveChangesAsync(cancellationToken);
            }
            if (isGranted.HasValue)
            {
                database.UserPermissions.Add(new UserPermission
                {
                    TenantId = masterId,
                    UserId = targetUserId,
                    PermissionId = permissionId,
                    IsGranted = isGranted.Value
                });
                await database.SaveChangesAsync(cancellationToken);
            }

            // The audit INSERT is in the same transaction as both rows when
            // an Allow <-> Deny replacement requires two SaveChanges calls.
            // Failure here rolls back all grant changes.
            await PlatformGrantAuditWriter.RecordAsync(database, actorId,
                "UserPermission", targetUserId, permissionId,
                isGranted switch { true => "allow", false => "deny", null => "clear" },
                previousGrant: existing?.IsGranted,
                currentGrant: isGranted,
                cancellationToken: cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning(
                "Platform UserPermission {Action}: actor {ActorId}, subject {UserId}, permission {PermissionId}",
                isGranted switch { true => "allow", false => "deny", null => "remove" },
                actorId, targetUserId, permissionId);
            return true;
        });
    }
}
