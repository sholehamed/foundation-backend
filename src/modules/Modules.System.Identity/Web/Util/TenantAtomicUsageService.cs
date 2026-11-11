using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Result of SQL-backed usage decisions. A successful meter write is still
/// UNCOMMITTED until the caller commits its authoritative business transaction.
/// </summary>
public sealed record TenantUsageResult(bool Ok, string Code,
    long? Consumed = null, long? Remaining = null);

/// <summary>
/// FR-17D-2: atomic, tenant-scoped PeriodQuota consumption and CountLimit
/// guards using transaction-owned SQL Server application locks.
/// IMPORTANT: this is infrastructure, NOT a user authorization/entitlement
/// endpoint. Callers MUST validate active paid entitlement and delegated
/// permission before calling, and mutate business data in the SAME serializable
/// DbContext transaction. No HTTP request may supply tenant or usage values.
/// </summary>
public sealed class TenantAtomicUsageService(
    IdentityDbContext db, ITenantContext tenantContext)
{
    private TenantUsageResult? ValidateTransaction(out Guid tenantId, out Guid actor)
    {
        tenantId = Guid.Empty;
        actor = Guid.Empty;
        if (tenantContext.IsSystemContext ||
            tenantContext.ActiveTenantId is not Guid current ||
            current == Guid.Empty ||
            !tenantContext.WritableTenantIds.Contains(current) ||
            !tenantContext.ReadableTenantIds.Contains(current) ||
            tenantContext.UserId is not Guid user || user == Guid.Empty)
            return new(false, "current_writable_tenant_required");

        tenantId = current;
        actor = user;
        if (!db.Database.IsSqlServer() ||
            db.Database.CurrentTransaction?.GetDbTransaction() is not SqlTransaction tx ||
            tx.IsolationLevel != IsolationLevel.Serializable)
            return new(false, "serializable_sql_transaction_required");
        return null;
    }

    private static bool ValidBinding(
        ResourceOperationContract operation, CapabilityContract definition,
        CapabilityKind expected)
    {
        return operation.RequiredCapabilities is { Count: > 0 } &&
               definition.Kind == expected &&
               definition.ValueKind == CapabilityValueKind.Integer &&
               !string.IsNullOrWhiteSpace(definition.Code) &&
               definition.Code.Length <= 150 &&
               operation.RequiredCapabilities.Contains(
                   definition.Code, StringComparer.OrdinalIgnoreCase) &&
               string.Equals(operation.ModuleCode, definition.ModuleCode,
                   StringComparison.OrdinalIgnoreCase) &&
               (definition.ResourceCode is null ||
                string.Equals(operation.ResourceCode, definition.ResourceCode,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static bool ValidAllowance(ResolvedCatalogCapability allowance,
        string code)
    {
        return string.Equals(allowance.Code, code, StringComparison.OrdinalIgnoreCase) &&
               allowance.BooleanValue is null &&
               (allowance.Modes?.Count ?? 0) == 0 &&
               (allowance.IsUnlimited ? allowance.IntegerValue is null :
                   allowance.IntegerValue is >= 0);
    }

    private SqlCommand Command(string sql, Guid tenantId, string code,
        DateTime startUtc, string? operationKey = null)
    {
        var cmd = new SqlCommand(sql,
            (SqlConnection)db.Database.GetDbConnection(),
            (SqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction());
        cmd.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenantId;
        cmd.Parameters.Add("@code", SqlDbType.NVarChar, 150).Value = code;
        cmd.Parameters.Add("@start", SqlDbType.DateTime2).Value = startUtc;
        if (operationKey is not null)
            cmd.Parameters.Add("@operation", SqlDbType.NVarChar, 128).Value = operationKey;
        return cmd;
    }

    private async Task<bool> LockAsync(Guid tenantId, string code,
        DateTime? periodStartUtc, CancellationToken ct)
    {
        // SQL Server sp_getapplock hashes resource names to lock resources,
        // serializing all cooperating writers across different DbContexts.
        var key = (periodStartUtc.HasValue ? "quota:" : "count:") +
                  tenantId.ToString("N") + ":" + code.ToLowerInvariant() +
                  (periodStartUtc.HasValue
                      ? ":" + periodStartUtc.Value.ToString("yyyyMMddHHmmssfffffff",
                          System.Globalization.CultureInfo.InvariantCulture)
                      : "");
        if (key.Length > 255) return false;
        await using var cmd = new SqlCommand(
            "DECLARE @result int; EXEC @result = sys.sp_getapplock " +
            "@Resource=@lockName, @LockMode=N'Exclusive', " +
            "@LockOwner=N'Transaction', @LockTimeout=10000; SELECT @result;",
            (SqlConnection)db.Database.GetDbConnection(),
            (SqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction());
        cmd.Parameters.Add("@lockName", SqlDbType.NVarChar, 255).Value = key;
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int codeValue && codeValue >= 0;
    }

    private async Task<bool> TenantIsActiveAsync(Guid tenantId, CancellationToken ct)
        => await db.Tenants.AsNoTracking()
            .AnyAsync(x => x.Id == tenantId && x.IsActive, ct);

    /// <summary>
    /// Only a code-owned domain adapter may call this with an authoritative
    /// active-row COUNT callback. This obtains a transaction-owned lock before
    /// reading, and the caller MUST write the domain change before committing
    /// that SAME transaction. All competing writers must use this same key.
    /// </summary>
    public async Task<TenantUsageResult> CheckCountWithinTransactionAsync(
        ResourceOperationContract operation, CapabilityContract definition,
        ResolvedCatalogCapability allowance, long delta,
        Func<CancellationToken, Task<long>> readAuthoritativeCount,
        CancellationToken ct = default)
    {
        var invalid = ValidateTransaction(out var tenantId, out _);
        if (invalid is not null) return invalid;
        if (!ValidBinding(operation, definition, CapabilityKind.CountLimit) ||
            !ValidAllowance(allowance, definition.Code) ||
            readAuthoritativeCount is null)
            return new(false, "invalid_count_contract");
        if (!await TenantIsActiveAsync(tenantId, ct))
            return new(false, "tenant_inactive");
        if (!await LockAsync(tenantId, definition.Code, null, ct))
            return new(false, "count_lock_unavailable");

        // The callback must query authoritative business state through the
        // caller's same DbContext/connection/serializable transaction.
        var current = await readAuthoritativeCount(ct);
        var decision = OperationLimitPolicy.Evaluate(operation, [definition],
            [allowance], [new(definition.Code, current, delta)]);
        if (!decision.Permitted)
            return new(false, decision.Code, current);
        return new(true, "within_count_limit", current);
    }

    /// <summary>
    /// Consume quota in the caller's active SERIALIZABLE SQL transaction. Do
    /// domain writes before committing that same transaction. SQL rollback
    /// reverses both quota and business mutations automatically.
    /// The period boundaries must come from trusted subscription policy,
    /// NEVER from client input.
    /// </summary>
    public async Task<TenantUsageResult> ConsumeWithinTransactionAsync(
        ResourceOperationContract operation, CapabilityContract definition,
        ResolvedCatalogCapability allowance, DateTime periodStartUtc,
        DateTime periodEndUtc, string operationKey, long units,
        CancellationToken ct = default)
    {
        var invalid = ValidateTransaction(out var tenantId, out var actor);
        if (invalid is not null) return invalid;
        if (!ValidBinding(operation, definition, CapabilityKind.PeriodQuota) ||
            !ValidAllowance(allowance, definition.Code))
            return new(false, "invalid_quota_contract");
        if (units <= 0 || string.IsNullOrWhiteSpace(operationKey) ||
            operationKey.Length is < 8 or > 128 ||
            operationKey.Any(char.IsControl))
            return new(false, "invalid_usage_operation");

        var now = DateTime.UtcNow;
        if (periodStartUtc.Kind != DateTimeKind.Utc ||
            periodEndUtc.Kind != DateTimeKind.Utc ||
            periodStartUtc > now || periodEndUtc <= now ||
            periodEndUtc <= periodStartUtc)
            return new(false, "invalid_current_period");
        if (!await TenantIsActiveAsync(tenantId, ct))
            return new(false, "tenant_inactive");
        if (!await LockAsync(tenantId, definition.Code, periodStartUtc, ct))
            return new(false, "quota_lock_unavailable");

        await using (var replay = Command("""
            SELECT EventKind, Units FROM dbo.TenantUsageEvents WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId=@tenant AND CapabilityCode=@code
              AND PeriodStartUtc=@start AND OperationKey=@operation;
            """, tenantId, definition.Code, periodStartUtc, operationKey))
        {
            var consumed = (long?)null;
            var reversed = false;
            await using var reader = await replay.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.GetString(0) == "Consume") consumed = reader.GetInt64(1);
                if (reader.GetString(0) == "Reverse") reversed = true;
            }
            if (consumed.HasValue)
            {
                if (consumed.Value != units)
                    return new(false, "idempotency_conflict");
                return reversed
                    ? new(false, "already_reversed")
                    : new(true, "already_consumed");
            }
            if (reversed) return new(false, "invalid_usage_history");
        }

        long used = 0;
        DateTime? storedEnd = null;
        await using (var balance = Command("""
            SELECT ConsumedUnits, PeriodEndUtc
            FROM dbo.TenantUsageBalances WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId=@tenant AND CapabilityCode=@code AND PeriodStartUtc=@start;
            """, tenantId, definition.Code, periodStartUtc))
        {
            await using var reader = await balance.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                used = reader.GetInt64(0);
                storedEnd = reader.GetDateTime(1);
            }
        }
        if (storedEnd.HasValue && storedEnd.Value != periodEndUtc)
            return new(false, "period_definition_conflict");

        var decision = OperationLimitPolicy.Evaluate(operation,
            [definition], [allowance],
            [new(definition.Code, used, units)]);
        if (!decision.Permitted)
            return new(false, decision.Code, used,
                allowance.IsUnlimited ? null :
                Math.Max(0, allowance.IntegerValue!.Value - used));

        long next;
        try { next = checked(used + units); }
        catch (OverflowException) { return new(false, "usage_overflow"); }

        if (storedEnd is null)
        {
            await using var create = Command("""
                INSERT dbo.TenantUsageBalances
                    (TenantId, CapabilityCode, PeriodStartUtc, PeriodEndUtc, ConsumedUnits)
                VALUES (@tenant, @code, @start, @end, @next);
                """, tenantId, definition.Code, periodStartUtc);
            create.Parameters.Add("@end", SqlDbType.DateTime2).Value = periodEndUtc;
            create.Parameters.Add("@next", SqlDbType.BigInt).Value = next;
            await create.ExecuteNonQueryAsync(ct);
        }
        else
        {
            await using var update = Command("""
                UPDATE dbo.TenantUsageBalances
                SET ConsumedUnits=@next, UpdatedAtUtc=SYSUTCDATETIME()
                WHERE TenantId=@tenant AND CapabilityCode=@code AND PeriodStartUtc=@start;
                """, tenantId, definition.Code, periodStartUtc);
            update.Parameters.Add("@next", SqlDbType.BigInt).Value = next;
            if (await update.ExecuteNonQueryAsync(ct) != 1)
                return new(false, "usage_balance_missing");
        }
        await using (var write = Command("""
            INSERT dbo.TenantUsageEvents
                (TenantId, CapabilityCode, PeriodStartUtc,
                 OperationKey, EventKind, Units, ActorUserId)
            VALUES (@tenant, @code, @start, @operation, N'Consume', @units, @actor);
            """, tenantId, definition.Code, periodStartUtc, operationKey))
        {
            write.Parameters.Add("@units", SqlDbType.BigInt).Value = units;
            write.Parameters.Add("@actor", SqlDbType.UniqueIdentifier).Value = actor;
            await write.ExecuteNonQueryAsync(ct);
        }
        return new(true, "consumed", next,
            allowance.IsUnlimited ? null : allowance.IntegerValue!.Value - next);
    }

    /// <summary>
    /// Append one reversal for exactly the amount of an existing Consume event.
    /// Never trust a client-supplied reversal amount. Historical period
    /// correction is allowed, without implicitly reopening that period.
    /// </summary>
    public async Task<TenantUsageResult> ReverseWithinTransactionAsync(
        string capabilityCode, DateTime periodStartUtc, string operationKey,
        CancellationToken ct = default)
    {
        var invalid = ValidateTransaction(out var tenantId, out var actor);
        if (invalid is not null) return invalid;
        if (string.IsNullOrWhiteSpace(capabilityCode) ||
            capabilityCode.Length > 150 ||
            periodStartUtc.Kind != DateTimeKind.Utc ||
            string.IsNullOrWhiteSpace(operationKey) ||
            operationKey.Length is < 8 or > 128)
            return new(false, "invalid_reversal");
        if (!await TenantIsActiveAsync(tenantId, ct))
            return new(false, "tenant_inactive");
        if (!await LockAsync(tenantId, capabilityCode, periodStartUtc, ct))
            return new(false, "quota_lock_unavailable");

        long? consumed = null;
        var reversed = false;
        await using (var read = Command("""
            SELECT EventKind, Units
            FROM dbo.TenantUsageEvents WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId=@tenant AND CapabilityCode=@code
              AND PeriodStartUtc=@start AND OperationKey=@operation;
            """, tenantId, capabilityCode, periodStartUtc, operationKey))
        {
            await using var reader = await read.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.GetString(0) == "Consume") consumed = reader.GetInt64(1);
                if (reader.GetString(0) == "Reverse") reversed = true;
            }
        }
        if (!consumed.HasValue) return new(false, "consume_event_missing");
        if (reversed) return new(true, "already_reversed");

        await using (var update = Command("""
            UPDATE dbo.TenantUsageBalances
            SET ConsumedUnits=ConsumedUnits-@units, UpdatedAtUtc=SYSUTCDATETIME()
            WHERE TenantId=@tenant AND CapabilityCode=@code
              AND PeriodStartUtc=@start AND ConsumedUnits>=@units;
            """, tenantId, capabilityCode, periodStartUtc))
        {
            update.Parameters.Add("@units", SqlDbType.BigInt).Value = consumed.Value;
            if (await update.ExecuteNonQueryAsync(ct) != 1)
                return new(false, "usage_balance_inconsistent");
        }
        await using (var write = Command("""
            INSERT dbo.TenantUsageEvents
                (TenantId, CapabilityCode, PeriodStartUtc,
                 OperationKey, EventKind, Units, ActorUserId)
            VALUES (@tenant, @code, @start, @operation, N'Reverse', @units, @actor);
            """, tenantId, capabilityCode, periodStartUtc, operationKey))
        {
            write.Parameters.Add("@units", SqlDbType.BigInt).Value = consumed.Value;
            write.Parameters.Add("@actor", SqlDbType.UniqueIdentifier).Value = actor;
            await write.ExecuteNonQueryAsync(ct);
        }
        return new(true, "reversed");
    }
}
