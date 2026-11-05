using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// SQL-backed append-only audit. The caller must already hold the validated
/// master write scope and its SQL Server transaction. All values except the
/// fixed, code-controlled operation names are parameterized by EF.
/// Any audit insert failure must fail the entire grant transaction.
/// </summary>
internal static class PlatformGrantAuditWriter
{
    internal static Task RecordAsync(
        IdentityDbContext database, Guid actorUserId, string kind,
        Guid targetId, Guid relatedId, string action,
        bool? previousGrant, bool? currentGrant,
        CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Platform grant audit requires the same transaction as the grant write.");

        if (actorUserId == Guid.Empty || targetId == Guid.Empty ||
            relatedId == Guid.Empty ||
            kind is not ("UserRole" or "GroupRole" or "UserPermission" or "UserGroup" or "RolePermission") ||
            action is not ("grant" or "revoke" or "allow" or "deny" or "clear"))
            throw new InvalidOperationException("Invalid platform grant audit payload.");

        var id = Guid.NewGuid();
        var masterId = DefaultSeedData.MasterTenant.Id;
        return database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [dbo].[PlatformGrantAuditEntries]
                ([Id], [TenantId], [ActorUserId], [Kind], [TargetId],
                 [RelatedId], [Action], [PreviousGrant], [CurrentGrant])
            VALUES
                ({id}, {masterId}, {actorUserId}, {kind}, {targetId},
                 {relatedId}, {action}, {previousGrant}, {currentGrant})
            """, cancellationToken);
    }
}
