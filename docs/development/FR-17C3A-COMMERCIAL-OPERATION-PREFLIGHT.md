# FR-17C-3A — Composed Commercial Operation Preflight (backend only)

**Date:** 2026-11-11  
**Parent:** [FR-17C](https://github.com/sholehamed/foundation-backend/issues/133)  
**Implementation:** `src/modules/Modules.System.Identity/Web/Util/TenantCommercialOperationPreflight.cs`  
**Status:** code-only preflight and disposable SQL tests; no commercial application EndpointGroup deployed.

## Why this exists

An active purchased PlanVersion is a **Tenant commercial entitlement**, never a blanket user grant. Equally, a Tenant role with `APPOINTMENTS.CREATE` must never activate a module that the Tenant did not license. The effective operation boundary is **both** conditions plus source-declared resource/feature/limit/usage rules.

The previous `EffectivePermissionEvaluator` and `TenantCommercialEntitlementReader` are distinct and remain unchanged. This new service composes them **without enabling any existing live endpoint or breaking System/tenant administrative access**.

## Operation preflight decision sequence

1. Require a **source-owned** operation `EndpointKey` declared under a `Commercial` Module, Resource and Permission. Validate all source Resource/Capability/Operation ownership. No arbitrary HTTP/DB-provided operation or executable rule.
2. Require authenticated current **writable Tenant context** with user, HomeTenant, active Tenant and scope aligned. System/Master context cannot access commercial tenant operations through this path.
3. Require live SQL `Module.IsActive`, `Resource.IsActive`, `Permission.IsActive`, matching exactly the source Module/Resource/Permission ownership.
4. Check current actor's **live** scoped delegated Permission via existing `EffectivePermissionEvaluator`. Direct deny, inactive user/tenant, missing grants all remain authoritative.
5. Re-read active **verified, same-Tenant settled Order + Subscription** from `TenantCommercialEntitlementReader`, pinned immutable PlanVersion and deployed trusted Commercial manifest. No active license or missing Module denies.
6. For each source-required capability, require the typed value in the effective PlanVersion. Missing required allowance is a **denial**, not implicit Unlimited. Check `Feature` and `AllowedModes` through the existing `OperationLimitPolicy`.
7. **Metered** `CountLimit` / `PeriodQuota` values are validated, but actual consumption is **NOT** evaluated against a fabricated count or an HTTP value. Preflight returns `MeteringRequired` plus exact capability keys. The business command must call `TenantAtomicUsageService` inside the **same SQL Server Serializable business transaction**, applying all metered capabilities before commit. Non-metered operations return `Ready` (still subject to business policies).

Results are intentionally discriminated:

| State | Meaning | Can command be committed immediately? |
| --- | --- | --- |
| `Denied` | Missing grant/license/resource/feature/mode, malformed catalog or wrong scope | No |
| `MeteringRequired` | All prechecks passed, but authoritative CountLimit/Quota checks remain | **No** until atomic meter checks and domain write succeed together |
| `Ready` | Licensed, delegated, source-registered, enabled non-metered preflight passed | Only after other business policies and scoped domain validations |

**Important:** `Ready` is a service-level preflight status, not an ASP.NET Core authorization handler. It does not automatically protect any HTTP endpoint. No frontend state, menu or button is a security boundary.

## SQL acceptance

`tests/Shinera.Identity.SqlServerTests/CommercialOperationPreflightSqlTests.cs` applies **real EF migrations to a disposable localhost SQL Server database**. A test-only `appointments/bookings` Commercial manifest is constructed with two Tenants and their users. Checks include:

- Tenant A with a verified settled order and active subscription can perform **unmetered READ preflight** only with an explicit delegated READ permission.
- Purchasing a Module does **not** grant CREATE to Tenant A; after explicit delegation CREATE still returns **MeteringRequired** for monthly quota.
- Tenant B may have the very same Permission but no active Subscription and remains denied; a foreign principal cannot borrow Tenant B's authorization.
- AllowedModes requires a named entitled mode; missing/unsupported modes deny.
- Direct user-level deny overrides the license and prior grant. An expired subscription denies despite the historical order and Permission.
- Production manifest has **zero Commercial operations** and refuses any attempted use; no fake commercial business resource, seed, or new Tenant grant is introduced.
- A System principal or invalid operation contract is denied.

The SQL tests are included in the existing `bff-atomic-sqlserver` GitHub Actions mandatory test filter. Their SQL fixtures are isolated test data only.

## What is *not* delivered yet

- No API middleware, no EndpointGroup tied to a real business module, no production authorization cutover or flag.
- No multi-capability **combined** transaction coordinator (the current atomic meter API checks a single cap per call; the orchestrating business handler must ensure all consume/check operations and domain writes share a transaction, acquire locks in deterministic order).
- No async quota reservation lease, no renewal/carryover period policy factory, no read-only current usage API or Angular usage widget.
- Branch-level business resource scope must be enforced by the eventual domain adapter, not guessed from user/tenant-level grants.
- No staging or production deployment is inferred from green unit/SQL CI.

## Next steps

**FR-17C-3B:** Introduce a trusted code-owned business operation execution hook that cannot commit until preflight **AND** all real meter/domain policy checks pass. For live Commercial Modules, add two-Tenant authenticated HTTP negative tests, branch scope handling and explicit rollout flag; preserve System Core and Tenant billing/recovery outside commercial gate.

**FR-17D-2B:** Review subscription-anchored quota periods, authoritative scoped usage projections and `GET` endpoint for usage status. Only then add shared Angular usage/remaining widgets. Avoid binding an unverified PlanVersion to an invented capacity count.
