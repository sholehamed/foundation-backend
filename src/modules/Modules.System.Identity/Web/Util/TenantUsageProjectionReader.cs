using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Web.Util;

public sealed record TenantCapabilityUsage(
    string Code,
    string Kind,
    string State,
    bool IsUnlimited,
    long? Limit,
    long? CurrentUsage,
    long? Remaining,
    DateTime? PeriodStartUtc,
    DateTime? PeriodEndUtc,
    string? PeriodCadence,
    bool OverLimit);

public sealed record TenantUsageProjection(
    string Status,
    string Reason,
    Guid? SubscriptionId,
    IReadOnlyList<TenantCapabilityUsage> Items);

/// <summary>
/// FR-17D-2B: read-only, source-validated and Tenant-scoped usage projection.
/// A verified live commercial Subscription is required. This is not a write
/// gate, it cannot create meters, grant permissions, or infer entity Counts.
/// The authenticated OWNER-only HTTP endpoint is responsible for its
/// independently checked business-administration permission.
/// </summary>
public sealed class TenantUsageProjectionReader(
    IdentityDbContext db, ITenantContext current,
    IReadOnlyCollection<ResourceContract>? sourceResources = null,
    IReadOnlyCollection<CapabilityContract>? sourceCapabilities = null)
{
    private readonly IReadOnlyCollection<ResourceContract> resources =
        sourceResources ?? ModuleResourceContracts.Current();
    private readonly IReadOnlyCollection<CapabilityContract> definitions =
        sourceCapabilities ?? [];

    public async Task<TenantUsageProjection> ReadCurrentAsync(
        DateTime nowUtc, CancellationToken ct = default)
    {
        if (nowUtc.Kind != DateTimeKind.Utc ||
            current.IsSystemContext ||
            current.ActiveTenantId is not Guid tenantId ||
            tenantId == Guid.Empty ||
            current.UserId is not Guid userId || userId == Guid.Empty ||
            current.HomeTenantId != tenantId ||
            !current.WritableTenantIds.Contains(tenantId) ||
            !current.ReadableTenantIds.Contains(tenantId))
            return new("Unavailable", "tenant_owner_context_required", null, []);

        // Never expose source-independent DB plan values under a forged module.
        if (ModuleResourceContracts.Validate(resources, definitions).Count != 0)
            return new("Unavailable", "invalid_source_contracts", null, []);

        TenantCommercialEntitlementSnapshot entitlement;
        try
        {
            entitlement = await new TenantCommercialEntitlementReader(db, current)
                .ReadCurrentAsync(nowUtc, resources, definitions, ct);
        }
        catch (UnauthorizedAccessException)
        {
            return new("Unavailable", "tenant_scope_invalid", null, []);
        }

        if (!entitlement.HasCommercialEntitlement ||
            entitlement.SubscriptionId is not Guid subscriptionId ||
            entitlement.StartsAtUtc is null ||
            entitlement.EndsAtUtc is null)
            return new(entitlement.Status, entitlement.Reason, null, []);

        if (!db.Database.IsSqlServer())
            return new("Unavailable", "sql_server_required", null, []);

        var startsUtc = DateTime.SpecifyKind(
            entitlement.StartsAtUtc.Value, DateTimeKind.Utc);
        var endsUtc = DateTime.SpecifyKind(
            entitlement.EndsAtUtc.Value, DateTimeKind.Utc);

        var declared = definitions.ToDictionary(x => x.Code,
            StringComparer.OrdinalIgnoreCase);
        var entries = new List<TenantCapabilityUsage>();

        // This is intentionally a separate read-only connection. It cannot
        // hold domain locks or perform usage charges.
        await using var connection = new SqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        foreach (var allowance in entitlement.Capabilities.OrderBy(x => x.Code,
                     StringComparer.Ordinal))
        {
            // PublishedPlanCatalogReader already validated the source-owned
            // Commercial membership and value definitions.
            if (!declared.TryGetValue(allowance.Code, out var definition) ||
                definition.Kind is not (CapabilityKind.PeriodQuota or CapabilityKind.CountLimit) ||
                !entitlement.ModuleCodes.Contains(definition.ModuleCode,
                    StringComparer.OrdinalIgnoreCase))
                continue;

            if (allowance.BooleanValue is not null ||
                (allowance.Modes?.Count ?? 0) != 0 ||
                (allowance.IsUnlimited
                    ? allowance.IntegerValue is not null
                    : allowance.IntegerValue is not >= 0))
            {
                entries.Add(new(allowance.Code, definition.Kind.ToString(),
                    "InvalidAllowance", allowance.IsUnlimited, null, null,
                    null, null, null, null, false));
                continue;
            }

            // CountLimit is an actual count of domain records, not a
            // Consumption Ledger entry. No generic SQL table can infer that
            // count; a future domain-specific adapter must supply it.
            if (definition.Kind == CapabilityKind.CountLimit)
            {
                entries.Add(new(allowance.Code, "CountLimit",
                    "DomainCountUnavailable", allowance.IsUnlimited,
                    allowance.IntegerValue, null, null, null, null, null, false));
                continue;
            }

            if (definition.QuotaPeriod is null)
            {
                entries.Add(new(allowance.Code, "PeriodQuota",
                    "PeriodNotConfigured", allowance.IsUnlimited,
                    allowance.IntegerValue, null, null, null, null, null, false));
                continue;
            }

            var period = QuotaPeriodResolver.Resolve(
                startsUtc, endsUtc, nowUtc, definition.QuotaPeriod);
            if (period is null)
            {
                entries.Add(new(allowance.Code, "PeriodQuota",
                    "PeriodUnavailable", allowance.IsUnlimited,
                    allowance.IntegerValue, null, null, null, null,
                    definition.QuotaPeriod.ToString(), false));
                continue;
            }

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT ConsumedUnits, PeriodEndUtc
                FROM dbo.TenantUsageBalances
                WHERE TenantId = @tenant AND CapabilityCode = @code
                  AND PeriodStartUtc = @start;
                """;
            cmd.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenantId;
            cmd.Parameters.Add("@code", SqlDbType.NVarChar, 150).Value = allowance.Code;
            cmd.Parameters.Add("@start", SqlDbType.DateTime2).Value = period.StartUtc;

            long used = 0;
            DateTime? storedEnd = null;
            await using (var result = await cmd.ExecuteReaderAsync(
                CommandBehavior.SingleRow, ct))
            {
                if (await result.ReadAsync(ct))
                {
                    used = result.GetInt64(0);
                    storedEnd = result.GetDateTime(1);
                }
            }
            if (used < 0 || (storedEnd.HasValue &&
                            storedEnd.Value != period.EndUtc))
            {
                entries.Add(new(allowance.Code, "PeriodQuota",
                    "PeriodConflict", allowance.IsUnlimited, allowance.IntegerValue,
                    null, null, period.StartUtc, period.EndUtc,
                    period.Cadence.ToString(), false));
                continue;
            }

            var remaining = allowance.IsUnlimited
                ? (long?)null
                : Math.Max(0, allowance.IntegerValue!.Value - used);
            entries.Add(new(allowance.Code, "PeriodQuota", "Measured",
                allowance.IsUnlimited, allowance.IntegerValue, used, remaining,
                period.StartUtc, period.EndUtc, period.Cadence.ToString(),
                !allowance.IsUnlimited && used > allowance.IntegerValue!.Value));
        }

        return new("Active", "authoritative_current_usage", subscriptionId, entries);
    }
}
