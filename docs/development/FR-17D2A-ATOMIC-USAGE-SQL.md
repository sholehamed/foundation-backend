# FR-17D-2A — Atomic Usage and CountLimit SQL Server Primitives

**Implementation branch:** `feat/fr17d2-atomic-usage-sql`  
**Date:** 2026-11-11  
**Status:** PR validation required before merge. **No live commercial gates enabled.**

## Domain and trust boundary

Foundation now has `OperationLimitPolicy` (FR-17D-1), which distinguishes:
- No operation-bound limit = **no consumption ceiling**, not a Permission/Entitlement grant.
- A *required* bound capability missing from a published PlanVersion = **deny**, never silently unlimited.
- Finite `0`, positive finite, explicitly tagged `Unlimited`; failure on malformed allowance.

FR-17D-2A adds **transactional metering primitives**, NOT a billing license authority, HTTP middleware, rate limiter, RolePermission mutation or active paywall. Since zero production Commercial Modules exist today, tests use vetted test-only capability declarations and do **not** publish fake products.

## Persistence (SQL-only EF Migration)

Migration: `20261111180000_atomicusagemetering.cs`.

| Table | Keys / semantics |
| --- | --- |
| `dbo.TenantUsageBalances` | PK `(TenantId, CapabilityCode, PeriodStartUtc)`, `PeriodEndUtc`, nonnegative bigint `ConsumedUnits`, SQL timestamp and rowversion, FK Tenant |
| `dbo.TenantUsageEvents` | append-only events `Consume` or `Reverse`, unique `(TenantId, CapabilityCode, PeriodStartUtc, OperationKey, EventKind)`, positive units, actor, timestamp, FK to corresponding balance |

An AFTER UPDATE/DELETE SQL trigger prevents in-place event-history rewriting. No tables are mapped to EF Core entities; this migration is an SQL-owned bounded context accessed via parameterized `Microsoft.Data.SqlClient` in `TenantAtomicUsageService`. `IdentityDbContextModelSnapshot` does not acquire a second representation of the same schema. `Down` drops only new tables and trigger, after explicit backup/review.

## Transactional contract

The **caller** starts `IdentityDbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable)` and owns its commit/rollback. The service rejects non-SQL Server/non-Serializable/no transaction, System principal, missing writable current Tenant, inactive Tenant, invalid operation-capability ownership, malformed limit and stale quota period. TenantId and actor are always derived from trusted ITenantContext, not arguments.

`ConsumeWithinTransactionAsync`:
1. Require source-owned operation -> `PeriodQuota` capability and a trusted effective allowance.
2. Require an active UTC period whose boundaries were computed from trusted subscription policy, a stable operation key, and positive units.
3. Obtain a SQL `sp_getapplock` **transaction-owned Exclusive** lock on Tenant+Capability+Period across processes.
4. Check unique prior `Consume` and `Reverse` events; same key+units replays without a second charge, changed units conflict, reversal forbids reconsumption.
5. Read balance using `UPDLOCK, HOLDLOCK`, reject conflicting period definitions, overflow, zero/finite cap exceedance, or malformed values.
6. Insert/update balance and append a `Consume` event **within the caller's transaction**.
7. Caller writes authoritative business state **and only then commits the same SQL transaction**. Any exception/rollback removes both meter writes and domain writes.

`ReverseWithinTransactionAsync`: obtains the same lock; only an existing Consume may be reversed; derives units exclusively from the committed original, appends a `Reverse` event, subtracts safely, and is idempotent. This is *correction/compensation*, not reopening an expired period.

`CheckCountWithinTransactionAsync`: obtains Tenant+Capability Exclusive application lock, then invokes a code-owned domain count callback in the **same DbContext transaction** and checks a signed delta. The caller must mutate matching rows before committing this transaction. All writers of that entity that affect count MUST cooperate with this same keyed locking protocol; an unguarded writer is not magically prevented by the service.

**Important:** `CountLimit` is not `PeriodQuota`: active-record counts must come from the domain truth source, not `TenantUsageBalances`. A non-growth cleanup remains allowed after downgrade, including when current count exceeds new capacity.

## Limitations / required follow-up

- This is single-transaction **consume-and-commit**, not a durable cross-transaction reservation lease. Asynchronous work that spans transactions requires `Reserved/Committed/Released`, expiry and an outbox/recovery worker before production use.
- The entitlement/permission gate is deliberately **not wired** yet: a later `FR-17C-3` integration must obtain **real** `TenantCommercialEntitlementReader` snapshot, code-owned operation, scoped delegated permission/direct deny, tenant branch/resource constraints, features/modes, and only then invoke the meter. In this PR a trusted source-supplied allowance is accepted solely by unexposed infrastructure. It cannot by itself prove a paid license or authorize HTTP.
- Period origin and timing rules (billing anchor, DST, upgrades/renewals, carryovers) need a reviewed policy provider before a production endpoint passes boundaries; never accept period from HTTP.
- For multiple quotas consumed by one business command, use one serializable transaction; obtain locks in deterministic sorted capability order to avoid deadlocks.
- SQL `sp_getapplock` covers participating application writers, not direct privileged SQL writers bypassing application code. Prevent untrusted direct DML with least-privilege database access.
- No Angular usage endpoint or UI yet, because an exposed balance without properly resolved effective entitlements and rights would be misleading.
- No automatic maintenance jobs, no persistence of commercial Module definitions, no grants, no Staging/production deployment claim.

## SQL Server acceptance tests

`tests/Shinera.Identity.SqlServerTests/AtomicUsageSqlServerTests.cs` runs against a unique disposable localhost-only SQL Server database, applying **real EF migrations**. Covers:

1. Finite cap, exact boundary and idempotent replay (same key + same amount), conflict (changed amount).
2. SQL append-only history guard and explicit reversals.
3. Business rollback leaving neither ledger nor balance charge.
4. Two concurrent transactions trying to consume last unit; exactly one succeeds.
5. Two-Tenant independent balances/operation keys and hidden history.
6. Missing trusted scoped context / missing serializable SQL transaction fails closed.
7. Concurrent `CountLimit` against a test-only authoritative active-items table: at most one activation and cleanup after downgrade.

Run from CI with `SHINERA_SQLSERVER_TEST_CONNECTION` pointed to an **isolated localhost master** instance. Never run these tests against staging or production.

## Next slices

- FR-17D-2B: durable reservation lifecycle for async jobs (if needed), explicit period policy factory and authoritative usage read API.
- FR-17C-3: effective access `Entitlement AND Permission AND Scope AND Feature/Mode`, safe rollout, real HTTP multi-tenant tests.
- Frontend: read-only usage/remaining UI using server-computed, permission-filtered snapshots; no client-side enforcement.
