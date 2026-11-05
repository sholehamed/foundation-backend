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
/// Restricts System group membership to validated Master groups and active
/// System members, and writes membership audit in the same SQL transaction.
/// </summary>
public sealed class PlatformGroupMembershipWriteExecutor(
    AuthorizedPlatformMasterWrite gateway,
    IdentityDbContext database,
    TrustedTenantWriteScope trustedWrites,
    ILogger<PlatformGroupMembershipWriteExecutor> logger)
{
    public async Task ExecuteAsync(
        ClaimsPrincipal principal,
        Guid groupId,
        IReadOnlyCollection<Guid>? userIds,
        Func<Task> execute,
        CancellationToken cancellationToken = default)
    {
        var members = userIds?.Distinct().ToArray() ?? [];
        if (groupId == Guid.Empty || members.Length == 0 ||
            members.Any(id => id == Guid.Empty) ||
            !Guid.TryParse(principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value,
                out var actorId) || members.Contains(actorId))
            throw new UnauthorizedAccessException(
                "Invalid membership list or platform administrator self-membership.");

        // Live administrator, target and membership checks MUST run in the same
        // serializable transaction as the legacy command and audit inserts.
        // SQL Server's retry strategy needs to wrap the whole unit, not only SaveChanges.
        var strategy = database.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            using var scope = await gateway.BeginAsync(principal,
                cancellationToken: cancellationToken);
            var master = DefaultSeedData.MasterTenant.Id;

            var groupIsValid = await database.Groups.AsNoTracking()
                .AnyAsync(g => g.Id == groupId && g.TenantId == master && g.IsActive,
                    cancellationToken);
            if (!groupIsValid)
                throw new UnauthorizedAccessException(
                    "Group is outside the active platform master scope.");

            var isPrivilegedGroup = await database.GroupRoles.AsNoTracking()
                .AnyAsync(link => link.GroupId == groupId &&
                    link.TenantId == master && link.Role != null &&
                    link.Role.TenantId == master &&
                    link.Role.NormalizedName == "SUPERADMIN", cancellationToken);
            if (isPrivilegedGroup)
                throw new UnauthorizedAccessException(
                    "SuperAdmin group membership requires a reviewed bootstrap.");

            var validMembers = await database.Users.AsNoTracking().CountAsync(user =>
                members.Contains(user.Id) &&
                user.UserType == UserType.System &&
                user.TenantId == null && user.IsActive &&
                (!user.IsLockedOut || user.LockoutEndUtc <= DateTime.UtcNow),
                cancellationToken);
            if (validMembers != members.Length)
                throw new UnauthorizedAccessException(
                    "Only active System accounts may join platform groups.");

            using var grant = trustedWrites.BeginPlatformGrantMutation(
                PlatformGrantMutationKind.GroupMembers, groupId);

            // Snapshot the entire group, not only requested user IDs. This
            // prevents a pre-tracked, unvalidated membership for the same group
            // from hitchhiking through the legacy command's SaveChanges.
            var before = await database.UserGroups.AsNoTracking()
                .Where(x => x.TenantId == master && x.GroupId == groupId)
                .Select(x => x.UserId).ToListAsync(cancellationToken);
            var expectedAdditions = members.Except(before).ToHashSet();

            // Legacy handler and audit writer share the exact same scoped DbContext.
            // A failed INSERT or mismatched write rolls back the entire batch.
            await execute();
            var after = await database.UserGroups.AsNoTracking()
                .Where(x => x.TenantId == master && x.GroupId == groupId)
                .Select(x => x.UserId).ToListAsync(cancellationToken);
            var additions = after.Except(before).ToArray();
            if (before.Except(after).Any() ||
                !additions.ToHashSet().SetEquals(expectedAdditions))
                throw new UnauthorizedAccessException(
                    "Group membership mutation differs from the validated request.");

            foreach (var memberId in additions)
                await PlatformGrantAuditWriter.RecordAsync(database, actorId,
                    "UserGroup", groupId, memberId, "grant",
                    previousGrant: false, currentGrant: true,
                    cancellationToken: cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning(
                "Audited platform group membership: actor {ActorId}, group {GroupId}, new members {Count}",
                actorId, groupId, additions.Length);
        });
    }
}
