using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Infrastructure.Persistence.Interceptors;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Executes legacy Role commands under the verified, bounded Master gateway.
/// RolePermission replacements and audit events share one serializable transaction.
/// </summary>
public sealed class PlatformRoleWriteExecutor(
    AuthorizedPlatformMasterWrite gateway,
    IdentityDbContext database,
    TrustedTenantWriteScope trustedWrites,
    ILogger<PlatformRoleWriteExecutor> logger)
{
    public async Task<T> ExecuteAsync<T>(
        ClaimsPrincipal principal,
        string operation,
        Func<Task<T>> execute,
        Guid? targetRoleId = null,
        CancellationToken cancellationToken = default,
        string? newRoleName = null,
        IReadOnlyCollection<Guid>? expectedPermissionIds = null)
    {
        if (operation != "roles.permissions.replace")
            return await ExecuteAuthorizedAsync(principal, operation, execute,
                targetRoleId, cancellationToken, newRoleName, expectedPermissionIds);

        // The entire unit (live administrator check, target validation,
        // association replacement and audit writes) is retried together.
        var strategy = database.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var result = await ExecuteAuthorizedAsync(principal, operation, execute,
                targetRoleId, cancellationToken, newRoleName, expectedPermissionIds);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private async Task<T> ExecuteAuthorizedAsync<T>(
        ClaimsPrincipal principal,
        string operation,
        Func<Task<T>> execute,
        Guid? targetRoleId,
        CancellationToken cancellationToken,
        string? newRoleName,
        IReadOnlyCollection<Guid>? expectedPermissionIds)
    {
        using var authorized = await gateway.BeginAsync(principal, targetRoleId, cancellationToken);

        if (string.Equals(newRoleName?.Trim(), "superadmin", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The platform SuperAdmin role is reserved.");

        if (targetRoleId.HasValue)
        {
            var normalizedName = await database.Roles.AsNoTracking()
                .Where(role => role.Id == targetRoleId.Value &&
                               role.TenantId == DefaultSeedData.MasterTenant.Id)
                .Select(role => role.NormalizedName)
                .SingleOrDefaultAsync(cancellationToken);

            if (string.Equals(normalizedName?.Trim(), "SUPERADMIN",
                    StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException(
                    "The platform SuperAdmin role cannot be modified through ordinary CRUD.");
        }

        using var grant = operation == "roles.permissions.replace"
            ? trustedWrites.BeginPlatformGrantMutation(
                PlatformGrantMutationKind.RolePermissions,
                targetRoleId ?? throw new UnauthorizedAccessException("Role target is required."))
            : null;

        var actor = principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
        logger.LogInformation("Platform role operation {Operation} started by {ActorId} for {RoleId}",
            operation, actor, targetRoleId);
        try
        {
            if (operation == "roles.permissions.replace")
            {
                if (targetRoleId is null ||
                    !Guid.TryParse(actor, out var actorId) ||
                    database.Database.CurrentTransaction is null)
                    throw new UnauthorizedAccessException(
                        "A verified actor and transaction are required for role permission audit.");

                var roleId = targetRoleId.Value;
                var master = DefaultSeedData.MasterTenant.Id;
                var previous = await database.RolePermissions.AsNoTracking()
                    .Where(x => x.TenantId == master && x.RoleId == roleId)
                    .Select(x => x.PermissionId).ToListAsync(cancellationToken);

                var changedResult = await execute();

                var current = await database.RolePermissions.AsNoTracking()
                    .Where(x => x.TenantId == master && x.RoleId == roleId)
                    .Select(x => x.PermissionId).ToListAsync(cancellationToken);

                // When called through the HTTP endpoint, verify that the
                // final persisted set matches the reviewed request exactly.
                // A pre-tracked grant for the same role must not hitchhike.
                if (expectedPermissionIds is not null &&
                    (expectedPermissionIds.Any(x => x == Guid.Empty) ||
                     !current.ToHashSet().SetEquals(expectedPermissionIds)))
                    throw new UnauthorizedAccessException(
                        "Role permission mutation differs from the validated request.");

                foreach (var removed in previous.Except(current))
                    await PlatformGrantAuditWriter.RecordAsync(database, actorId,
                        "RolePermission", roleId, removed, "revoke",
                        previousGrant: true, currentGrant: false,
                        cancellationToken: cancellationToken);
                foreach (var added in current.Except(previous))
                    await PlatformGrantAuditWriter.RecordAsync(database, actorId,
                        "RolePermission", roleId, added, "grant",
                        previousGrant: false, currentGrant: true,
                        cancellationToken: cancellationToken);

                logger.LogInformation(
                    "Audited platform role permission replacement for {RoleId} by {ActorId}",
                    roleId, actorId);
                return changedResult;
            }

            var result = await execute();
            logger.LogInformation("Platform role operation {Operation} completed by {ActorId}",
                operation, actor);
            return result;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Platform role operation {Operation} failed for actor {ActorId}",
                operation, actor);
            throw;
        }
    }

    public Task ExecuteAsync(
        ClaimsPrincipal principal,
        string operation,
        Func<Task> execute,
        Guid? targetRoleId = null,
        CancellationToken cancellationToken = default,
        string? newRoleName = null,
        IReadOnlyCollection<Guid>? expectedPermissionIds = null)
        => ExecuteAsync(principal, operation, async () =>
        {
            await execute();
            return true;
        }, targetRoleId, cancellationToken, newRoleName, expectedPermissionIds);
}
