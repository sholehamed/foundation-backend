# FR-17D-1 — Optional Resource/Operation Limits: Foundation Policy Contract

**Date:** 2026-11-11  
**Scope:** Pure .NET evaluation and unit tests only. No new tables, no production entitlement enforcement, no quota ledger, no commercial Module seed.  
**Design:** [modular-entitlements-plans-limits.md](../architecture/modular-entitlements-plans-limits.md).

## Owner-approved invariant: omitted limit != invalid configured limit

Foundation shall offer a reusable limit framework to ANY centrally registered eligible Module/Resource/operation. Nothing is automatically capped merely because the Resource exists.

| State | Meaning | Effect on usage ceiling |
| --- | --- | --- |
| Operation has **no bound CountLimit/PeriodQuota** | No usage restriction was authored for this operation | No usage ceiling; other bound features/modes are still evaluated |
| A **registered/bound required** limit is absent from effective published PlanVersion | Incomplete/invalid commercial entitlement configuration | **DENY**, never silently use unlimited |
| Explicit finite integer > 0 | Bound capacity/usage ceiling | Compare authoritative usage + change atomically before write (future FR-17D-2) |
| Explicit finite **0** | No new consumption allowed | DENY growth/positive quota charges |
| Explicit tagged **Unlimited** | Deliberate uncapped PlanVersion allowance | No meter ceiling; no authorization bypass |
| Negative, null without Unlimited, wrong type, duplicate value | Invalid configuration | DENY |
| Tenant is not licensed / Module is absent / Permission missing | Commercial/security check, independent of usage ceiling | DENY at effective access gate (future FR-17C-3) |

**Terminology:** `no_usage_limit` is NOT `user_authorized`, `module_licensed`, or `feature_enabled`. The current `OperationLimitPolicy` is a pure checker and NEVER authorizes HTTP traffic. System/Tenant core, login, billing, recovery and basic workspace access cannot be locked behind commercial subscriptions.

## Source ownership and granularity

- `ModuleResourceContracts.Current()` describes only deployed code-owned System/Tenant-core resources. No commercial product is fabricated. A future module declares its `ResourceContract`, `CapabilityContract` and `ResourceOperationContract` in code.
- A Resource still corresponds to Controller/EndpointGroup, not a database table. A limit may be shared across many operations: e.g. `staff.active.max` is checked by Create/Activate/Restore, not READ or screen display.
- One operation may have multiple required capabilities, and reads require none by default. Bind the required capability explicitly in code; arbitrary DB metadata cannot attach executable limits or predicates to endpoints.
- System Admin configures values of known definitions in draft PlanVersions, not arbitrary SQL predicates, C# expressions or policy code.

## New evaluator

`src/modules/Modules.System.Identity/Web/Util/OperationLimitPolicy.cs` evaluates an explicit `ResourceOperationContract` against *code-owned* `CapabilityContract` definitions, effective validated `ResolvedCatalogCapability` values, and trusted `OperationUsageSample` snapshots.

Rules:
1. No bound capabilities => `Permitted=true, Code=no_usage_limit` in the **usage layer only**. The caller still MUST check tenant subscription, scoped user Permission, direct deny, feature and business policy.
2. Any declared required capability absent from the Plan => `required_capability_missing`. A malformed/duplicate/foreign requirement => fail closed.
3. `Feature` requires explicit true. `AllowedModes` requires a supplied valid mode in the entitled set.
4. Finite `CountLimit` requires authoritative current count and a signed delta (growth / no-change / cleanup). Growth must not exceed the cap. Non-growth operations can clean up even if a downgrade leaves the tenant above the new limit.
5. Finite `PeriodQuota` requires current usage and a **strictly positive** consumption delta. Do not use a zero-cost call to perform a billable command.
6. Unbounded limits are represented by the **explicit** `IsUnlimited=true` tag; there is no magic 0, -1 or missing-value sentinel.
7. Numeric overflow, negative usage, absent usage for finite limits, invalid type and duplicate inputs fail closed. All denial codes are stable strings suitable for later API mapping, not exceptions that leak data.

Unit coverage: `tests/Shinera.Identity.Tests/OperationLimitPolicyTests.cs`.

## Critical distinction: evaluation is NOT enforcement

This first slice intentionally has **no** active integration into `EffectivePermissionEvaluator`, HTTP route guard, Plan checkout or UI. A pure method operating on an input snapshot is neither an atomic reservation nor authoritative proof of consumption. It must NOT be used directly as a pre-save check followed by an unrelated `SaveChangesAsync` (TOCTOU race). Do not treat green unit tests as proof of SQL Server concurrency safety.

## FR-17D-2 implementation acceptance (next)

1. Source-owned `ICountLimitUsageProvider` for each domain-specific active-record predicate. Check count + domain write under a single SQL Server serializable/controlled lock boundary keyed by tenant and capability (no Redis-only counters).
2. SQL Server `UsageBalance` / append-only `UsageLedger` with tenant+capability+period+idempotency uniqueness, atomic reservation/commit/reversal, no charges for failed writes and no double-consumption for retries.
3. Explicit UTC periods and renewal anchor; upgrade/downgrade policy preserving historical ledgers and pending operations. No arbitrary auto-reset during an active period.
4. Correct effective runtime gate: authenticated Tenant + active Commercial Module + scoped Permission/direct deny + code-owned required Feature/Modes + counted limit/quota + business rules. Core administration excluded from commercial paywall.
5. Two-Tenant SQL integration: isolation, parallel over-cap attempts, duplicate command replays, rollback/reversal, period boundary, 0/unlimited/downgrade scenarios.
6. Read-only tenant usage projection/API and reusable Angular presentation after authoritative meters exist. No frontend-enforced limits.

## Rollout and non-goals

No migration/seed, no initial commercial Module, no changes to existing role grants, dynamic menus, BFF auth, existing EndpointGroup registry or payment workflow. This PR is merge-safe ahead of commercial domain development. Subsequent enforcement must be behind independently reviewed tests and rollout diagnostics.
