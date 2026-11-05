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
/// Narrow platform GroupRole assignment for non-SuperAdmin roles.
/// Master scope alone never authorizes a grant; exact GroupId/RoleId intent
/// and live actor/group/role/member checks are required.
/// </summary>
public sealed class PlatformGroupRoleWriteService(
    IdentityDbContext database,
    AuthorizedPlatformMasterWrite gateway,
    TrustedTenantWriteScope trustedWrites,
    ILogger<PlatformGroupRoleWriteService> logger)
{
    public async Task<bool> ChangeAsync(
        ClaimsPrincipal principal, Guid groupId, Guid roleId, bool assign,
        CancellationToken cancellationToken = default)
    {
        if (groupId == Guid.Empty || roleId == Guid.Empty ||
            !Guid.TryParse(principal.FindFirstValue(OpenIddictConstants.Claims.Subject),
                out var actorId))
            throw new UnauthorizedAccessException("A valid platform actor, group and role are required.");

        var storage = DefaultSeedData.MasterTenant.Id;
        var strategy = database.Database.CreateExecutionStrategy();

        // Retrying the whole serializable transaction keeps actor validation,
        // target validation and the grant change in the same unit of work.
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            using var master = await gateway.BeginAsync(
                principal, cancellationToken: cancellationToken);

            var group = await database.Groups.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == groupId && x.TenantId == storage,
                    cancellationToken);
            if (group is null || (assign && !group.IsActive))
                throw new UnauthorizedAccessException(
                    "Only active master-scoped groups may receive new roles.");

            var role = await database.Roles.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == roleId && x.TenantId == storage,
                    cancellationToken);
            if (role is null || (assign && !role.IsActive) ||
                string.Equals(role.NormalizedName?.Trim(), "SUPERADMIN",
                    StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException(
                    "The requested role is outside the permitted platform grant scope.");

            // A group containing a SUPERADMIN role is reserved for bootstrap,
            // even when the requested mutation concerns a different role.
            var privilegedGroup = await database.GroupRoles.AsNoTracking().AnyAsync(
                x => x.GroupId == groupId && x.TenantId == storage &&
                     x.Role != null && x.Role.TenantId == storage &&
                     x.Role.NormalizedName == "SUPERADMIN", cancellationToken);
            if (privilegedGroup)
                throw new UnauthorizedAccessException(
                    "Privileged group-role management requires a reviewed bootstrap.");

            // Never allow administrators to change their own effective roles
            // through a group that currently includes the acting account.
            var actorIsMember = await database.UserGroups.AsNoTracking().AnyAsync(
                x => x.GroupId == groupId && x.TenantId == storage &&
                     x.UserId == actorId, cancellationToken);
            if (actorIsMember)
                throw new UnauthorizedAccessException(
                    "Self-affecting group-role changes are forbidden.");

            // A group may be empty, but if populated its members must belong
            // exclusively to active, unlocked, null-tenant System accounts.
            if (assign)
            {
                var invalidMember = await database.UserGroups.AsNoTracking().AnyAsync(
                    x => x.GroupId == groupId && x.TenantId == storage &&
                         (x.User.UserType != UserType.System ||
                          x.User.TenantId != null || !x.User.IsActive ||
                          (x.User.IsLockedOut &&
                           (x.User.LockoutEndUtc == null ||
                            x.User.LockoutEndUtc > DateTime.UtcNow))),
                    cancellationToken);
                if (invalidMember)
                    throw new UnauthorizedAccessException(
                        "Only eligible System members can inherit platform roles.");
            }

            var existing = await database.GroupRoles.SingleOrDefaultAsync(
                x => x.GroupId == groupId && x.RoleId == roleId &&
                     x.TenantId == storage, cancellationToken);
            var changed = assign ? existing is null : existing is not null;

            if (changed)
            {
                using var intent = trustedWrites.BeginPlatformGrantMutation(
                    PlatformGrantMutationKind.GroupRoles, groupId, roleId);

                if (assign)
                    database.GroupRoles.Add(new GroupRole
                    {
                        TenantId = storage, GroupId = groupId, RoleId = roleId
                    });
                else
                    database.GroupRoles.Remove(existing!);

                // The interceptor rejects unrelated GroupRole, UserRole,
                // UserPermission, UserGroup or RolePermission changes.
                await database.SaveChangesAsync(cancellationToken);
                await PlatformGrantAuditWriter.RecordAsync(database, actorId,
                    "GroupRole", groupId, roleId,
                    assign ? "grant" : "revoke",
                    previousGrant: !assign, currentGrant: assign,
                    cancellationToken: cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning(
                "Platform GroupRole {Action} {Outcome}: actor {ActorId}, group {GroupId}, role {RoleId}",
                assign ? "grant" : "revoke", changed ? "changed" : "unchanged",
                actorId, groupId, roleId);
            return changed;
        });
    }
}
