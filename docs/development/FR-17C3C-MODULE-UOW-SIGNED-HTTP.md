# FR-17C-3C — Commercial module SQL Unit of Work and isolated signed HTTP acceptance

**Date:** 2026-11-11  
**Implementation:** `TenantCommercialOperationExecutor.ExecuteModuleAsync` and disposable SQL/HTTP acceptance in `Shinera.Identity.SqlServerTests`.  
**Source of truth:** Production still has **zero source-owned Commercial modules**. No fabricated Plan, menu, permission, product, endpoint, test token or fixture is registered by the real Web API.

## Why this slice exists

FR-17C-3B composed licensing/RBAC/capability and atomic CountLimit/PeriodQuota transactions, but the trusted domain callback depended on **IdentityDbContext**. That is inappropriate for independently versioned business modules which own their own Domain/Persistence assembly and entity model.

FR-17C-3C adds a narrow and explicit cross-DbContext SQL Server boundary. It does **not** ask every module to share Identity entities or duplicate the licensing engine.

### New contracts

```csharp
public sealed record CommercialModuleSqlUnitOfWork(
    SqlConnection Connection, SqlTransaction Transaction);

public sealed record CommercialModuleCountRequirement(
    string CapabilityCode, long Delta,
    Func<CommercialModuleSqlUnitOfWork, CancellationToken, Task<long>> ReadAuthoritativeCount);

Task<CommercialOperationResult> TenantCommercialOperationExecutor.ExecuteModuleAsync(
    string endpointKey, string idempotencyKey,
    IReadOnlyCollection<CommercialModuleCountRequirement> counts,
    IReadOnlyCollection<CommercialQuotaRequirement> quotas,
    Func<CommercialModuleSqlUnitOfWork, CancellationToken, Task> applyModuleWrite,
    string? selectedMode = null, CancellationToken ct = default);
```

The existing `ExecuteAsync(IdentityDbContext,...)` remains backward compatible for internal use. The module-facing overload does not expose an Identity entity/DbContext. It delegates the **entire** existing trusted decision sequence to the same Executor: registered commercial Module/Resource/Permission/EndpointKey, current writable signed Tenant, effective delegated user permission/direct deny, verified settled active Subscription/immutable PlanVersion, required Feature/AllowedModes and all Count/PeriodQuota. Both module counts and writes receive **the exact live SQL connection and SqlTransaction** used by the licensing context.

### Module adapter example (not a shipped module)

```csharp
async Task Apply(CommercialModuleSqlUnitOfWork uow, CancellationToken ct)
{
    var options = new DbContextOptionsBuilder<MyModuleDbContext>()
        .UseSqlServer(uow.Connection)
        .Options;
    await using var moduleDb = new MyModuleDbContext(options);
    await moduleDb.Database.UseTransactionAsync(uow.Transaction, ct);
    // ... check module-owned business rules, add tenant-scoped entities ...
    await moduleDb.SaveChangesAsync(ct); // flush, DO NOT commit
}

// Trusted source-owned handler, not an HTTP-provided EndpointKey:
var decision = await executor.ExecuteModuleAsync(
    "MODULE.RESOURCE.CREATE", operationKey, moduleCountRequirements,
    moduleQuotaRequirements, Apply, ct: ct);
```

**Foundation alone owns COMMIT/ROLLBACK.** Module `DbContext` is merely enlisted and flushed, never allowed to commit, change transaction isolation, close the shared connection, return before a pending external action, or start an independent transaction. The module must use **the same SQL Server database**; a second server/connection is not atomically included. External emails, payments and queues require an Outbox after commit.

The transaction remains Serializable and respects deterministic per-capability application lock acquisition. A module Count adapter reads **authoritative qualifying active records** in its own DbContext using the shared transaction. Any failure/limit denial/replayed quota rolls back both module DB writes and usage events/balance. `ExecuteModuleAsync` never accepts a browser-supplied tenant, resource permission declaration, period or limit.

### HTTP acceptance isolation

`CommercialSignedHttpHarness` lives in the **SQL Server test assembly only**, using a private `WebApplication` TestServer. It has a **temporary random HMAC key**, issuer, audience, lifetime and signature verification via real ASP.NET Core JwtBearer authentication/authorization middleware. The only commercial POST route is mapped **inside this test host**. It is not imported, seeded, enabled by configuration or mapped inside the deployable `src/Web` application.

The signed user/tenant claims choose `ITenantContext`; `clientTenantId` in the request body is intentionally ignored and cannot change the signed Tenant. A fixed code-owned `EndpointKey` invokes the normal production Executor plus a test-only SQL table/domain callback. The HTTP suite asserts:
- anonymous request -> 401;
- token with a different signing key -> 401;
- validly signed Tenant member **without CREATE Permission** -> 403;
- validly signed Tenant B with CREATE grant **but no active settled Subscription** -> 403;
- licensed Tenant A valid signed user -> successful business write and one quota charge even if the request body tries to select B;
- exact-key replay and over-limit -> Conflict and no extra domain write/charge;
- Tenant B has zero billing events, even after forged request-body TenantId.

This proves the authentication middleware **in an isolated test harness**, not the deployed production OpenIddict issuer, BFF Cookie/CSRF pipeline or browser integration. Those must be exercised against a real code-owned Commercial EndpointGroup when a genuine business module is implemented.

### Real SQL integration acceptance

The pre-existing disposable `CommercialOperationPreflightSqlTests` fixture now includes:
- separate `TestCommercialModuleDb` with **its own EF Core entity model**, owning test-only `dbo.FR17C3B_TestWrites`, never added to IdentityDbContext or production migrations;
- successful `ExecuteModuleAsync` with `UseTransaction` and `SaveChangesAsync` through the shared SQL connection/transaction;
- key replay avoids a second module entity and a second ledger charge;
- deliberate exception **after the module DbContext has flushed**: both module row and quota are rolled back;
- an authoritative count through the module's DbContext when the CountLimit is already full: deny without a new entity or quota;
- the signed HTTP acceptance described above using the same disposable fixture.

The mandatory `bff-atomic-sqlserver` workflow already includes `CommercialOperationPreflightSqlTests`; the HTTP harness is invoked from that suite. No new environment credential, hosted API test route, or server secret is introduced.

## Handoff and next steps

1. Build the first **actual, separately deployed, source-owned Commercial Module** with its own domain, scope/branch filtering and EndpointGroup. Register code-owned Resources/Permissions/Features/Modes and specify Count/PeriodQuota per operation.
2. Integrate this contract in the module's handler and register its real `TenantScoped` API route with signed BFF/OpenIddict authentication. Add full **real Web API signed JWT and Angular BFF browser E2E** for anonymous, foreign tenant, no grant, no license, no limit, exhausted limit, CountLimit downgrade and concurrency.
3. Implement real CountLimit read projection from each domain's active record truth source. Never show a fabricated value in Angular.
4. Harden asynchronous side effects through an Outbox/reservations and review subscriptions renewal/carryover and idempotency of **unmetered** actions. This change does **not** implement cross-database distributed transactions.
5. Protect readonly/rollback tests in CI; green ephemeral SQL/TestServer is not production/Staging proof.

**Important safety rule:** No commercial `Enabled` feature flag or paywall is switched on by this PR. All System Admin, Tenant Admin, signup, billing, recovery and dynamic menu control-plane operations remain outside the commercial gate.
