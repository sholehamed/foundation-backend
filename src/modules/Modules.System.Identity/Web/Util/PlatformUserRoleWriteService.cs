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
/// Deliberately narrow direct UserRole management for non-privileged System roles.
/// A live System SuperAdmin may grant or revoke a non-SuperAdmin master role for
/// another System user. SuperAdmin bootstrap/rotation is NOT a request operation.
/// </summary>
public sealed class PlatformUserRoleWriteService(
    IdentityDbContext database,
    AuthorizedPlatformMasterWrite gateway,
    TrustedTenantWriteScope trustedWrites,
    ILogger<PlatformUserRoleWriteService> logger)
{
    public async Task<bool> ChangeAsync(ClaimsPrincipal principal,
        Guid targetUserId, Guid roleId, bool assign,
        CancellationToken cancellationToken = default)
    {
        if (targetUserId == Guid.Empty || roleId == Guid.Empty ||
            !Guid.TryParse(principal.FindFirstValue(OpenIddictConstants.Claims.Subject), out var actorId) ||
            actorId == targetUserId)
            throw new UnauthorizedAccessException("Self-assignment and invalid role targets are forbidden.");

        var storage = DefaultSeedData.MasterTenant.Id;
        var strategy = database.Database.CreateExecutionStrategy();

        // All live authorization and target checks occur inside the same
        // serializable transaction as the grant mutation. Retry the whole unit
        // with the SQL Server execution strategy, never just SaveChanges.
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            using var master = await gateway.BeginAsync(principal,
                cancellationToken: cancellationToken);

            var user = await database.Users.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == targetUserId &&
                    x.UserType == UserType.System && x.TenantId == null, cancellationToken);
            if (user is null ||
                (assign && (!user.IsActive ||
                            (user.IsLockedOut &&
                             (user.LockoutEndUtc is null || user.LockoutEndUtc > DateTime.UtcNow)))))
                throw new UnauthorizedAccessException("Target is not an eligible System user.");

            var role = await database.Roles.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == roleId && x.TenantId == storage,
                    cancellationToken);
            if (role is null || (assign && !role.IsActive) ||
                string.Equals(role.NormalizedName?.Trim(), "SUPERADMIN",
                    StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Privileged, inactive or foreign role cannot be assigned.");

            var existing = await database.UserRoles
                .SingleOrDefaultAsync(x => x.TenantId == storage &&
                    x.UserId == targetUserId && x.RoleId == roleId, cancellationToken);

            var changed = assign ? existing is null : existing is not null;
            if (changed)
            {
                using var intent = trustedWrites.BeginPlatformGrantMutation(
                    PlatformGrantMutationKind.UserRoles, targetUserId, roleId);
                if (assign)
                    database.UserRoles.Add(new UserRole(storage, targetUserId, roleId));
                else
                    database.UserRoles.Remove(existing!);

                // SaveChanges enforces both the fixed master scope and exact
                // UserRole pair. Unrelated tracked grants fail closed.
                await database.SaveChangesAsync(cancellationToken);
                await PlatformGrantAuditWriter.RecordAsync(database, actorId,
                    "UserRole", targetUserId, roleId,
                    assign ? "grant" : "revoke",
                    previousGrant: !assign, currentGrant: assign,
                    cancellationToken: cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning(
                "Platform UserRole {Action} {Outcome}: actor {ActorId}, target {TargetUserId}, role {RoleId}",
                assign ? "grant" : "revoke", changed ? "changed" : "unchanged",
                actorId, targetUserId, roleId);
            return changed;
        });
    }
}
