using Infrastructure.SharedKernel.Observability;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// A code-owned domain adapter must read qualifying active records through the
/// very same DbContext and transaction. Delta is the signed count change.
/// </summary>
public sealed record CommercialCountRequirement(
    string CapabilityCode, long Delta,
    Func<IdentityDbContext, CancellationToken, Task<long>> ReadAuthoritativeCount);

/// <summary>
/// The trusted business adapter supplies the units charged by an operation,
/// never a price, client-supplied counter or caller-selected billing window.
/// </summary>
public sealed record CommercialQuotaRequirement(string CapabilityCode, long Units);

/// <summary>
/// Access to a single, open SQL Server transaction shared with the licensing
/// DbContext. A module may create its own EF Core DbContext using Connection
/// and enlist via Database.UseTransactionAsync(Transaction). Never connect
/// separately or commit/rollback this transaction from the module.
/// </summary>
public sealed record CommercialModuleSqlUnitOfWork(
    SqlConnection Connection, SqlTransaction Transaction);

/// <summary>
/// A source-owned module's active entity count. No IdentityDbContext is
/// exposed, so a business module can count through its own DbContext/Dapper
/// using precisely the transaction managed by the central executor.
/// </summary>
public sealed record CommercialModuleCountRequirement(
    string CapabilityCode, long Delta,
    Func<CommercialModuleSqlUnitOfWork, CancellationToken, Task<long>>
        ReadAuthoritativeCount);

public sealed record CommercialOperationResult(bool Committed, string Code,
    string? CapabilityCode = null);

/// <summary>
/// FR-17C-3B: opt-in executor for future SOURCE-OWNED commercial commands.
/// All security checks, metered limits and domain writes share a SQL Server
/// SERIALIZABLE transaction; any denial/exception rolls them all back.
/// No production Commercial operation is registered by this class.
/// </summary>
public sealed class TenantCommercialOperationExecutor(
    IdentityDbContext db, ITenantContext context,
    IReadOnlyCollection<ResourceContract>? sourceResources = null,
    IReadOnlyCollection<CapabilityContract>? sourceCapabilities = null,
    IReadOnlyCollection<ResourceOperationContract>? sourceOperations = null)
{
    private readonly IReadOnlyCollection<ResourceContract> resources =
        sourceResources ?? ModuleResourceContracts.Current();
    private readonly IReadOnlyCollection<CapabilityContract> capabilities =
        sourceCapabilities ?? [];
    private readonly IReadOnlyCollection<ResourceOperationContract> operations =
        sourceOperations ?? [];

    private static CommercialOperationResult Denied(string code, string? capability = null) =>
        new(false, code, capability);

    /// <summary>
    /// FR-17C-3C shared-connection boundary for a module with its OWN
    /// DbContext. No data-model dependency on IdentityDbContext leaks into
    /// the module; both contexts must enlist in the same SqlTransaction.
    /// The existing core ExecuteAsync remains for current callers.
    ///
    /// The module callback must flush its own SaveChangesAsync before
    /// returning. Transaction commit remains exclusively Foundation-owned.
    /// </summary>
    public Task<CommercialOperationResult> ExecuteModuleAsync(
        string endpointKey,
        string idempotencyKey,
        IReadOnlyCollection<CommercialModuleCountRequirement> counts,
        IReadOnlyCollection<CommercialQuotaRequirement> quotas,
        Func<CommercialModuleSqlUnitOfWork, CancellationToken, Task> applyModuleWrite,
        string? selectedMode = null,
        CancellationToken ct = default)
    {
        if (counts is null || quotas is null || applyModuleWrite is null ||
            counts.Any(c => c.ReadAuthoritativeCount is null))
            return Task.FromResult(Denied("invalid_module_adapter"));

        var adapted = counts.Select(requirement => new CommercialCountRequirement(
            requirement.CapabilityCode, requirement.Delta,
            (identity, token) => requirement.ReadAuthoritativeCount(
                GetSharedUnitOfWork(identity), token))).ToArray();

        return ExecuteAsync(endpointKey, idempotencyKey, adapted, quotas,
            (identity, token) =>
                applyModuleWrite(GetSharedUnitOfWork(identity), token),
            selectedMode, ct);
    }

    private static CommercialModuleSqlUnitOfWork GetSharedUnitOfWork(
        IdentityDbContext identity)
    {
        if (identity.Database.CurrentTransaction?.GetDbTransaction()
                is not SqlTransaction transaction ||
            identity.Database.GetDbConnection() is not SqlConnection connection ||
            transaction.Connection != connection ||
            transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException(
                "Commercial module must share the active serializable SQL transaction.");

        return new(connection, transaction);
    }

    /// <summary>
    /// The operation key must be the code-owned EndpointKey (never supplied by
    /// HTTP). IdempotencyKey can originate with a request, but has to identify
    /// the same intended domain command on retries.
    ///
    /// The callback MUST use the passed IdentityDbContext and its active
    /// transaction, perform no external side effects, and rely on its own
    /// domain constraints/unique keys for non-quota idempotency. The executor
    /// rejects a replay of a committed quota charge BEFORE calling the callback,
    /// preventing duplicate business writes through a successful quota retry.
    /// </summary>
    public async Task<CommercialOperationResult> ExecuteAsync(
        string endpointKey,
        string idempotencyKey,
        IReadOnlyCollection<CommercialCountRequirement> counts,
        IReadOnlyCollection<CommercialQuotaRequirement> quotas,
        Func<IdentityDbContext, CancellationToken, Task> applyDomainWrite,
        string? selectedMode = null,
        CancellationToken ct = default)
    {
        if (!db.Database.IsSqlServer() ||
            db.Database.CurrentTransaction is not null)
            return Denied("isolated_sql_transaction_required");
        if (string.IsNullOrWhiteSpace(endpointKey) ||
            counts is null || quotas is null || applyDomainWrite is null)
            return Denied("invalid_operation_request");
        if (ModuleResourceContracts.Validate(resources, capabilities, operations).Count != 0)
            return Denied("invalid_source_catalog");

        // The per-operation adapters must be explicit and complete. Optional
        // CountLimit with an explicit Unlimited value does not require a count.
        // All PeriodQuotas are metered even when Unlimited (for reporting).
        if (counts.Any(x => x.ReadAuthoritativeCount is null) ||
            counts.GroupBy(x => x.CapabilityCode, StringComparer.OrdinalIgnoreCase)
                .Any(x => x.Key is null || x.Count() != 1) ||
            quotas.GroupBy(x => x.CapabilityCode, StringComparer.OrdinalIgnoreCase)
                .Any(x => x.Key is null || x.Count() != 1) ||
            counts.Any(x => string.IsNullOrWhiteSpace(x.CapabilityCode)) ||
            quotas.Any(x => string.IsNullOrWhiteSpace(x.CapabilityCode) || x.Units <= 0))
            return Denied("invalid_meter_requirements");

        var operation = operations.SingleOrDefault(x =>
            string.Equals(x.EndpointKey, endpointKey, StringComparison.OrdinalIgnoreCase));
        if (operation is null)
            return Denied("operation_not_registered");

        // Explicit bounded retries for SQL deadlocks can be added at the
        // application command boundary, with a NEW DbContext per attempt.
        // Never automatically rerun an arbitrary domain callback here.
        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        // This activity spans the existing serializable transaction and is
        // deliberately non-authoritative: early returns indicate no confirmed
        // commit, not proof of database rollback.
        using var transactionTrace = SourceOwnedTransactionTrace.Start("commercial_atomic");
        var access = await new TenantCommercialOperationPreflight(
            db, context, resources, capabilities, operations)
            .CheckWriteAsync(endpointKey, selectedMode, ct);
        if (access.State == "Denied")
            return Denied(access.Code);

        TenantCommercialEntitlementSnapshot entitlement;
        try
        {
            entitlement = await new TenantCommercialEntitlementReader(db, context)
                .ReadCurrentAsync(DateTime.UtcNow, resources, capabilities, ct);
        }
        catch (UnauthorizedAccessException)
        {
            return Denied("tenant_context_invalid");
        }

        if (!entitlement.HasCommercialEntitlement ||
            entitlement.SubscriptionId is null ||
            entitlement.StartsAtUtc is null || entitlement.EndsAtUtc is null ||
            !entitlement.ModuleCodes.Contains(operation.ModuleCode,
                StringComparer.OrdinalIgnoreCase))
            return Denied("module_not_entitled");

        var definitions = capabilities.ToDictionary(x => x.Code,
            StringComparer.OrdinalIgnoreCase);
        var values = entitlement.Capabilities.ToDictionary(x => x.Code,
            StringComparer.OrdinalIgnoreCase);
        var countByCode = counts.ToDictionary(x => x.CapabilityCode,
            StringComparer.OrdinalIgnoreCase);
        var quotaByCode = quotas.ToDictionary(x => x.CapabilityCode,
            StringComparer.OrdinalIgnoreCase);
        var required = new HashSet<string>(operation.RequiredCapabilities,
            StringComparer.OrdinalIgnoreCase);

        // An unbound Resource/operation does not inherit an artificial cap.
        // Conversely a configured, required cap missing from the plan is DENY.
        // Extra adapter inputs must not silently charge unrelated operations.
        if (countByCode.Keys.Concat(quotaByCode.Keys).Any(x => !required.Contains(x)))
            return Denied("unexpected_meter_requirement");

        var meterItems = new List<(string Code, CapabilityContract Definition,
            ResolvedCatalogCapability Allowance)>();
        foreach (var code in access.MeteredCapabilities)
        {
            if (!definitions.TryGetValue(code, out var definition) ||
                !values.TryGetValue(code, out var allowance))
                return Denied("required_capability_missing", code);
            if (definition.Kind == CapabilityKind.CountLimit)
            {
                if (!allowance.IsUnlimited && !countByCode.ContainsKey(code))
                    return Denied("missing_count_adapter", code);
                if (quotaByCode.ContainsKey(code) ||
                    (allowance.IsUnlimited && countByCode.ContainsKey(code)))
                    return Denied("unexpected_meter_requirement", code);
            }
            else if (definition.Kind == CapabilityKind.PeriodQuota)
            {
                if (!quotaByCode.ContainsKey(code))
                    return Denied("missing_quota_adapter", code);
                if (countByCode.ContainsKey(code))
                    return Denied("unexpected_meter_requirement", code);
                if (definition.QuotaPeriod is null)
                    return Denied("quota_period_not_configured", code);
            }
            else
                return Denied("invalid_meter_kind", code);
            meterItems.Add((code, definition, allowance));
        }

        // A Feature or AllowedModes requirement cannot be smuggled into the
        // metering adapter list even if the operation also requires that key.
        if (countByCode.Keys.Any(x => !meterItems.Any(item =>
                item.Definition.Kind == CapabilityKind.CountLimit &&
                string.Equals(item.Code, x, StringComparison.OrdinalIgnoreCase))) ||
            quotaByCode.Keys.Any(x => !meterItems.Any(item =>
                item.Definition.Kind == CapabilityKind.PeriodQuota &&
                string.Equals(item.Code, x, StringComparison.OrdinalIgnoreCase))))
            return Denied("unexpected_meter_requirement");

        // Ensure neither caller omitted a required check nor fabricated a
        // MeteringRequired state. All acquisition order is source-code-based
        // and stable across requests: prevents reverse-order app-lock deadlocks.
        foreach (var item in meterItems.OrderBy(x => x.Code,
                     StringComparer.OrdinalIgnoreCase))
        {
            var meter = new TenantAtomicUsageService(db, context);
            TenantUsageResult result;
            if (item.Definition.Kind == CapabilityKind.CountLimit)
            {
                if (item.Allowance.IsUnlimited)
                    continue; // Explicit Unlimited means no count read needed.

                var adapter = countByCode[item.Code];
                result = await meter.CheckCountWithinTransactionAsync(
                    operation, item.Definition, item.Allowance, adapter.Delta,
                    token => adapter.ReadAuthoritativeCount(db, token), ct);
            }
            else
            {
                var startsUtc = DateTime.SpecifyKind(
                    entitlement.StartsAtUtc.Value, DateTimeKind.Utc);
                var endsUtc = DateTime.SpecifyKind(
                    entitlement.EndsAtUtc.Value, DateTimeKind.Utc);
                var window = QuotaPeriodResolver.Resolve(
                    startsUtc, endsUtc, DateTime.UtcNow,
                    item.Definition.QuotaPeriod);
                if (window is null)
                    return Denied("quota_period_unavailable", item.Code);

                var charge = quotaByCode[item.Code];
                result = await meter.ConsumeWithinTransactionAsync(
                    operation, item.Definition, item.Allowance,
                    window.StartUtc, window.EndUtc,
                    idempotencyKey, charge.Units, ct);
                // Critical: a replay MUST NOT execute the domain callback
                // again, even if the quota meter returns Ok=true on replay.
                if (result.Code == "already_consumed")
                    return Denied("duplicate_operation", item.Code);
            }

            if (!result.Ok)
                return Denied(result.Code, item.Code);
        }

        // A subscription could expire while a long-running command performs
        // SQL work; do not commit past its paid period. Existing role/plan
        // rows are read in the same Serializable transaction.
        if (DateTime.UtcNow >=
            DateTime.SpecifyKind(entitlement.EndsAtUtc.Value, DateTimeKind.Utc))
            return Denied("subscription_expired_during_execution");

        // Only the business module provides the mutation; never execute SQL
        // supplied from Plan catalog, browser, dynamic Menu or UI metadata.
        await applyDomainWrite(db, ct);
        await db.SaveChangesAsync(ct);

        if (DateTime.UtcNow >=
            DateTime.SpecifyKind(entitlement.EndsAtUtc.Value, DateTimeKind.Utc))
            return Denied("subscription_expired_during_execution");

        transactionTrace.CommitAttempted();
        await tx.CommitAsync(ct);
        transactionTrace.CommitConfirmed();
        return new(true, "commercial_operation_committed");
    }
}
