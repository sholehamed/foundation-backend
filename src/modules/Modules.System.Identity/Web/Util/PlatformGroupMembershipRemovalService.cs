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
/// Narrow, idempotent and audited revocation of one System UserGroup link.
/// No client-controlled tenant, unrelated grant mutation or SuperAdmin-group
/// changes are permitted; an inactive group/member can be cleaned up.
/// </summary>
public sealed class PlatformGroupMembershipRemovalService(
    IdentityDbContext database,
    AuthorizedPlatformMasterWrite gateway,
    TrustedTenantWriteScope trustedWrites,
    ILogger<PlatformGroupMembershipRemovalService> logger)
{
    public async Task<bool> RevokeAsync(
        ClaimsPrincipal principal, Guid groupId, Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (groupId == Guid.Empty || userId == Guid.Empty ||
            !Guid.TryParse(principal.FindFirstValue(OpenIddictConstants.Claims.Subject),
                out var actorId) || userId == actorId)
            throw new UnauthorizedAccessException(
                "Invalid target or self-affecting platform membership change.");

        var masterId = DefaultSeedData.MasterTenant.Id;
        var strategy = database.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // Revalidate the persisted administrator grant, target group and
            // user INSIDE the exact SQL transaction that performs the revoke.
            await using var transaction = await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            using var authorized = await gateway.BeginAsync(
                principal, cancellationToken: cancellationToken);

            var masterGroupExists = await database.Groups.AsNoTracking().AnyAsync(
                x => x.Id == groupId && x.TenantId == masterId, cancellationToken);
            if (!masterGroupExists)
                throw new UnauthorizedAccessException(
                    "Group is outside platform Master scope.");

            var reserved = await database.GroupRoles.AsNoTracking().AnyAsync(
                x => x.TenantId == masterId && x.GroupId == groupId &&
                     x.Role != null && x.Role.TenantId == masterId &&
                     x.Role.NormalizedName == "SUPERADMIN", cancellationToken);
            if (reserved)
                throw new UnauthorizedAccessException(
                    "SuperAdmin group membership requires reviewed bootstrap.");

            // Revocation is available even for inactive / locked subjects to
            // clean up stale grants, but never for tenant/foreign users.
            var eligibleTarget = await database.Users.AsNoTracking().AnyAsync(
                x => x.Id == userId && x.UserType == UserType.System &&
                     x.TenantId == null, cancellationToken);
            if (!eligibleTarget)
                throw new UnauthorizedAccessException(
                    "Only null-tenant System members can be revoked.");

            var link = await database.UserGroups.SingleOrDefaultAsync(
                x => x.TenantId == masterId && x.GroupId == groupId &&
                     x.UserId == userId, cancellationToken);
            if (link is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            // The interceptor enforces exact GroupId/UserId on every tracked
            // UserGroup change; other grant families cannot ride along.
            using var intent = trustedWrites.BeginPlatformGrantMutation(
                PlatformGrantMutationKind.GroupMemberRemoval, groupId, userId);
            database.UserGroups.Remove(link);
            await database.SaveChangesAsync(cancellationToken);
            await PlatformGrantAuditWriter.RecordAsync(database, actorId,
                "UserGroup", groupId, userId, "revoke",
                previousGrant: true, currentGrant: false,
                cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogWarning(
                "Audited System group membership revoke: actor {ActorId}, group {GroupId}, user {UserId}",
                actorId, groupId, userId);
            return true;
        });
    }
}
