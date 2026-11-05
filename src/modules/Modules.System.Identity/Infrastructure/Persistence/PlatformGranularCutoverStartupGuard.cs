using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Infrastructure.Persistence;

/// <summary>
/// Fail-closed STARTUP safeguard for enabling granular platform Role/Permission
/// authorization in Production. Read-only checks: NEVER applies grants,
/// mutates feature flags or performs a cutover. It is not an approval service,
/// digital signature verification or a replacement for a production rollout.
/// Granular endpoint policies are mapped once per process; rollback requires
/// restoring flags OFF and restarting ALL application instances.
/// </summary>
public static class PlatformGranularCutoverStartupGuard
{
    private const string Prefix = "Identity:PlatformPermissions:";
    private const string UniquenessMigration = "20261024200000_permissioncodeuniqueness";

    public static async Task ValidateAsync(
        IdentityDbContext db, IConfiguration configuration, bool isProduction,
        CancellationToken ct = default)
    {
        // Development CI explicitly enables granular checks with its disposable
        // fixtures; it must not be subject to Production deployment approval.
        if (!isProduction)
            return;

        var rolesEnabled = configuration.GetValue<bool>(Prefix + "EnforceRoleEndpoints");
        var permissionsEnabled = configuration.GetValue<bool>(Prefix + "EnforcePermissionEndpoints");

        // Safe rollback state. Coarse live SystemAdministrator checks remain
        // active regardless of both granular flags.
        if (!rolesEnabled && !permissionsEnabled)
            return;

        // Explicit default-OFF deployment interlock, entirely independent of
        // the endpoint-specific flags. The reviewed DB + Master bindings guard
        // against accidental application to a different target instance.
        // These values are operational acknowledgements, NOT cryptographic
        // evidence of two-person signoff. No API/CLI sets them automatically.
        if (!configuration.GetValue<bool>(Prefix + "AllowProductionGranularCutover") ||
            !Guid.TryParse(configuration[Prefix + "ReviewedMasterTenantId"],
                out var reviewedMaster) ||
            reviewedMaster != DefaultSeedData.MasterTenant.Id ||
            string.IsNullOrWhiteSpace(configuration[Prefix + "ReviewedDatabaseName"]) ||
            !string.Equals(
                configuration[Prefix + "ReviewedDatabaseName"],
                db.Database.GetDbConnection().Database,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Production granular authorization requires an independently reviewed deployment and exact database/Master acknowledgement.");

        // An absent normalized Permission.Code UNIQUE index allows concurrent
        // catalog drift, so enabling granular policies remains unsafe.
        var applied = await db.Database.GetAppliedMigrationsAsync(ct);
        if (!applied.Contains(UniquenessMigration, StringComparer.Ordinal))
            throw new InvalidOperationException(
                "Production Permission.Code uniqueness migration must be separately approved and applied before granular cutover.");

        // The EF migrations ledger does not prove that SQL Server still
        // enforces the index. A DBA/schema drift could remove or disable the
        // UNIQUE index after the migration was recorded. Fail closed rather
        // than treating historical migration metadata as current integrity.
        var actualUniqueIndex = await db.Database.SqlQueryRaw<int>("""
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM sys.indexes AS i
                JOIN sys.index_columns AS ic
                  ON ic.object_id = i.object_id
                 AND ic.index_id = i.index_id
                JOIN sys.columns AS c
                  ON c.object_id = ic.object_id
                 AND c.column_id = ic.column_id
                WHERE i.object_id = OBJECT_ID(N'[dbo].[Permissions]')
                  AND i.name = N'UX_Permissions_NormalizedCode'
                  AND i.is_unique = 1
                  AND i.is_disabled = 0
                  AND i.has_filter = 0
                  AND ic.key_ordinal = 1
                  AND ic.is_included_column = 0
                  AND c.name = N'NormalizedCode'
                  AND NOT EXISTS (
                      SELECT 1 FROM sys.index_columns AS other
                      WHERE other.object_id = i.object_id
                        AND other.index_id = i.index_id
                        AND other.key_ordinal > 1
                  )
                  AND EXISTS (
                      SELECT 1 FROM sys.computed_columns AS computed
                      WHERE computed.object_id = i.object_id
                        AND computed.name = N'NormalizedCode'
                        AND computed.is_persisted = 1
                  )
            ) AND EXISTS (
                SELECT 1 FROM sys.check_constraints AS ck
                WHERE ck.parent_object_id = OBJECT_ID(N'[dbo].[Permissions]')
                  AND ck.name = N'CK_Permissions_Code_Length'
                  AND ck.is_disabled = 0 AND ck.is_not_trusted = 0
            ) THEN 1 ELSE 0 END AS [Value]
            """).SingleAsync(ct);
        if (actualUniqueIndex != 1)
            throw new InvalidOperationException(
                "Production Permission.Code UNIQUE index/length constraint is absent, disabled or untrusted; granular cutover denied.");

        // Require the complete narrow 5A scope audit. This does NOT guarantee
        // correctness of all identities or the real operator-approved proposal.
        var inventory = await PlatformProductionGrantInventory.ReadAsync(db, ct);
        if (inventory.HasBlockingFindings)
            throw new InvalidOperationException(
                "Production Master authorization inventory has blocking findings. Keep granular flags OFF.");
    }
}
