# FR-17C-3B — Safe commercial command execution with real SQL atomic metering

**Date:** 2026-11-11  
**Scope:** reusable, code-owned tenant business command executor and SQL integration tests. No production commercial Module, EndpointGroup, test seed, new Permission or Angular code.  
**Dependencies:** [FR-17C-3A](FR-17C3A-COMMERCIAL-OPERATION-PREFLIGHT.md), [FR-17D-2A](FR-17D2A-ATOMIC-USAGE-SQL.md), [FR-17D-2B](FR-17D2B-SUBSCRIPTION-USAGE-PROJECTION.md).

## Problem closed by this slice

A licensing preflight alone is not a transactional authorization for a write. The code-owned business action must not be allowed to run before the **effective user grant, valid settled subscription, all required Features/Modes, all CountLimits/PeriodQuotas and authoritative domain rules** have passed. SQL consumption must commit **together** with the domain mutation. Period selection cannot be provided by HTTP or guessed from capability names.

`TenantCommercialOperationExecutor.ExecuteAsync` is an opt-in, unexposed backend primitive for future deployed Commercial modules. It does **not** attach a global middleware to current System/Tenant core endpoints. The default production source manifest still contains no Commercial Module or operation; no fake sold products are made available.

## Inputs and trusted origins

| Input | Owner | Enforcement |
| --- | --- | --- |
| `endpointKey` | Fixed in the **source-owned** EndpointGroup/handler | Must match `ResourceOperationContract` and vetted Commercial Module/Resource/Permission |
| `sourceResources`, `sourceCapabilities`, `sourceOperations` | Deployed module assembly, injected by trusted code | Registry ownership/type validation; never supplied by DB, HTTP, HTML or a Tenant |
| `ITenantContext` | Authenticated BFF/Tenant principal | Must match writable signed current HomeTenant and valid User |
| `idempotencyKey` | Stable logical business command key (can originate from client request under validated policy) | Unique per Tenant+Capability+period; committed replay returns `duplicate_operation` **before** domain callback; changed charge returns `idempotency_conflict` |
| `CommercialCountRequirement(CapabilityCode,Delta,ReadAuthoritativeCount)` | Module's code-owned domain adapter | Same DbContext/Serializable transaction + `sp_getapplock`; only exact applicable finite CountLimits; signed delta for activate/deactivate |
| `CommercialQuotaRequirement(CapabilityCode,Units)` | Module's code-owned business unit calculator | Positive count; exact applicable PeriodQuota; explicit Unlimited still recorded in usage ledger |
| `applyDomainWrite(IdentityDbContext,CancellationToken)` | Module's trusted domain handler | Same DbContext + transaction, writes authoritative data without externally visible side effects |
| `selectedMode` | Tenant business configuration or request choice | Source-defined AllowedModes checked against verified PlanVersion; not an executable rule |

The **PlanVersion** supplies allowance values; the source-declared `QuotaPeriodCadence` and verified `TenantSubscription.StartsAtUtc/EndsAtUtc` determine the real UTC period. The caller cannot override start/end.

## In-transaction order

1. Reject non-SQL Server / existing transaction, malformed or duplicate supplied count/quota adapters and bad source registry. Do not guess or synthesize a Commercial Module.
2. Open fresh **SQL Server Serializable** transaction on the handler's `IdentityDbContext`.
3. Execute `TenantCommercialOperationPreflight.CheckWriteAsync` **inside this transaction**: active code+SQL Resource and Permission, writable tenant context, live user effective Permission/direct-deny, verified settled Order and current Subscription, required Feature/AllowedModes; required but missing PlanVersion allowance fails closed.
4. Re-read the same validated Tenant entitlement within this transaction to obtain immutable source-verified typed PlanVersion values and pinned subscription period.
5. Require the **full exact set** of metered source capabilities, reject unexpected adapters. Explicit Unlimited `CountLimit` needs no domain count; `PeriodQuota` always has a positive unit charge (even Unlimited, to preserve measured consumption). Missing finite Count adapter/PeriodQuota adapter/source period denies.
6. Apply CountLimit and PeriodQuota checks in **deterministic capability-key order** to reduce lock inversion across multi-meter operations. Each meter uses transaction-scoped SQL application locks, authoritative count or durable idempotent quota ledger.
7. If a quota charge returns `already_consumed` for a logical retry, return `duplicate_operation` (no second domain write). Changed units under same key fail with `idempotency_conflict`. Any other denial means **no domain write** and the current transaction is rolled back.
8. If all checks pass, invoke the trusted business callback, then `db.SaveChangesAsync`. Recheck subscription end instant before commit. **Commit one transaction**; any exception, failed callback, late expiry, concurrent capacity denial or constraint conflict rolls back domain mutations AND ledger.
9. Return `Committed=true` **only when SQL CommitAsync succeeded**. Errors never imply a successful command.

## Precise optional-limit behavior

- **No CountLimit or PeriodQuota bound to the operation**: no consumption ceiling. Nonetheless valid **Module entitlement, Permission and enabled Feature/Mode** are still mandatory.
- **Declared/required but missing typed PlanVersion value**: denied, not Unlimited.
- Finite `0`: growth/positive quota usage denied; positive finite checks capacity.
- Explicit tagged `Unlimited`: quota recorded as consumed but not capped; CountLimit can omit count adapter for performance.
- CountLimit uses domain source-of-truth (e.g. count *active* staff, not API requests), not quota balance. Valid negative/zero delta can reduce or preserve count even when downgraded over cap.
- Multiple independent quotas in one domain command are serialized and committed together, so partial charges cannot survive an operation that later failed.
- Read API `/tenant/workspace/subscription/usage` remains **observability**, not a write-grant. Angular never authorizes operations based on its locally displayed usage.

## SQL acceptance (test fixture only)

`tests/Shinera.Identity.SqlServerTests/CommercialOperationPreflightSqlTests.cs` has an additional test-only `dbo.FR17C3B_TestWrites` table inside the **disposable SQL test database**, never in the application migrations or seeds. The source-only Appointments module/plan/license/grants are created only inside the test.

Acceptance includes:
- Verified Tenant A with explicit delegated Permission commits CREATE and its monthly charge; key replay causes **no second business write**, changed units under the same key return conflict.
- Deliberately throwing from the business mutation after metering rolls back the inserted row and quota.
- A Tenant B user with Permission but no active paid Subscription cannot write or consume.
- Missing quota adapter, missing finite Count adapter or an input adapter targeting an AllowedMode fail before business callback.
- A valid AllowedMode with no CountLimit/PeriodQuota allows an unmetered business write; invalid mode does not.
- Two parallel ACTIVATE commands compete for one remaining active-entity slot **with an additional shared PeriodQuota charge**. Exactly one commits; the losing command neither persists a row nor spends quota. SQL count remains authoritative.

The test is included in the existing mandatory `bff-atomic-sqlserver` CI filter, which already targets `CommercialOperationPreflightSqlTests`.

## Explicit non-goals / required next controls

- This executor is **not called** by a production Commercial HTTP route yet; none exists in Foundation. No commercial product is seeded and no System/Tenant core Permission or menu becomes paid.
- Current SQL tests exercise trusted TenantContext and SQL transactions. Full **authenticated HTTP request** business endpoint tests require a real source-owned Commercial EndpointGroup or an isolated test-only signed-server harness before production activation.
- The domain callback is trusted code; it **must not** call external APIs, queue non-transactional messages or write to a different database/connection. External side effects require an Outbox after commit. Source modules must enforce their own business invariants, branch isolation, request payload validation and replay protection for **unmetered** commands; the quota ledger's operation key is not a general domain idempotency store.
- Multi-domain service/DbContext writes are not covered by a single IdentityDbContext transaction; module onboarding must define its transaction-sharing boundary or add a trusted UnitOfWork. Never assume two separate DB connections atomically commit.
- Database serialized locks protect only cooperating application writers; direct privileged DML bypasses them. Deny direct database writes from ordinary tenant principals.
- For long-running or asynchronous workflows spanning multiple commits, introduce durable `Reserve/Commit/Release` with expiry, Outbox and recovery before use. FR-17D-2A `Consume` alone is not a reservation lease.
- Operational hardening: per-tenant metered concurrency HTTP tests, intentional SQL deadlock retry orchestration using a **new** DbContext (never replay side effects in the same callback), subscription transition/carryover policy, resource-count adapters for real business modules and documented rollout. Do not claim real staging/production enforcement from green CI alone.
