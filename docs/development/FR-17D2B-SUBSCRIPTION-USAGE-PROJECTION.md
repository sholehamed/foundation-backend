# FR-17D-2B — Subscription-anchored quota windows and read-only tenant usage

**Date:** 2026-11-11  
**Scope:** read-only Tenant Owner usage endpoint, code-owned UTC quota window calculation, real SQL metering projection and tests. **No commercial Module has been created, no existing RBAC grants or enforcement behavior changes.**

## Goal and ownership

Plans carry typed **allowance values**; deployed commercial modules must declare the quota reset **cadence in source code**. The backend, not a browser-provided period, resolves current usage. A Tenant Owner can inspect the authenticated Tenant's usage without receiving permissions that they did not already have.

- `CapabilityContract.QuotaPeriod: QuotaPeriodCadence?` is optional to preserve existing published source contracts. Choices: `SubscriptionTerm`, `MonthlyAnniversary`, `YearlyAnniversary`.
- The cadence is **only valid for PeriodQuota**. An incorrect non-quota cadence fails registry validation.
- A PeriodQuota without a source-defined cadence produces `PeriodNotConfigured` (usage and remaining **null**) rather than guessing "monthly" from `.monthly` in its key or inferring "Unlimited".
- Date boundaries are UTC instants anchored to the **original** `TenantSubscription.StartsAtUtc` and clipped by `EndsAtUtc`. Each subsequent anniversary is computed from the original start: e.g. Jan 31 -> Feb 28 -> Mar 31, no rolling drift. Annual leap day is handled similarly. Periods use half-open `[StartUtc,EndUtc)`; on a boundary a new period begins. Expired subscriptions are not current.
- SQL Server's `datetime2` loses `DateTime.Kind`, so values recorded as UTC by the audited subscription lifecycle are marked UTC on read **without host-local time conversion**. Unknown or non-UTC *caller* dates are rejected by the pure resolver.

## GET contract

Authenticated, **Tenant Owner only**, no TenantId route/body/query parameter:

`GET /api/tenant/workspace/subscription/usage`

- Existing `TenantWorkspace.CanCheckoutAsync` is reused as a narrowly scoped Owner + active user + writable HomeTenant authorization gate; it includes the existing local Owner live RBAC check. Missing/invalid credentials are subject to `RequireAuthorization`; a non-owner receives **403**.
- Server returns `Cache-Control: no-store`.
- `TenantUsageProjectionReader` independently verifies writable HomeTenant, code-owned Module/Resource/Capability manifest and the **real** settled Order + current active Subscription via `TenantCommercialEntitlementReader`. A PlanKey, pending quote, coupon code or legacy TenantModules flag is insufficient.
- If no verified eligible commercial module exists, result contains zero items. **No fabricated product, usage or subscription.**
- Read-only SQL `TenantUsageBalances` is queried by the server-derived TenantId + declared CapabilityCode + calculated period start. The matching `PeriodEndUtc` is verified against the calculated window; mismatches return `PeriodConflict`, **not an incorrect zero-consumption figure**.
- An absent balance row means **zero committed units** for a valid current period. Ledger `ConsumedUnits` already reflects committed consume/reversal events; no extra storage is required.
- `CountLimit` never derives a count from the quota ledger. Until a domain-defined active-row CountProvider is shipped, return `DomainCountUnavailable` with `currentUsage=null`, `remaining=null`, keeping the known plan limit. Unknown period returns `PeriodNotConfigured` with null counters.
- All bigint unit amounts (`limit`, `currentUsage`, `remaining`) are invariant **decimal strings**, not JavaScript JSON numbers: this preserves exact values above 2^53-1. Missing or Unlimited values remain null.
- Explicit `Unlimited` gives `isUnlimited=true`, `limit=null`, `remaining=null`; the actual committed usage may still be reported if its period exists. Finite zero is zero, not unlimited. If committed usage exceeds a downgraded finite limit, remaining is 0 and `overLimit=true`, never negative.

DTO:
```json
{
  "status": "Active",
  "reason": "authoritative_current_usage",
  "subscriptionId": "guid-of-current-subscription",
  "items": [
    {
      "code": "appointments.monthly",
      "kind": "PeriodQuota",
      "state": "Measured",
      "isUnlimited": false,
      "limit": "500",
      "currentUsage": "37",
      "remaining": "463",
      "periodStartUtc": "2026-11-05T12:00:00Z",
      "periodEndUtc": "2026-12-05T12:00:00Z",
      "periodCadence": "MonthlyAnniversary",
      "overLimit": false
    }
  ]
}
```
The sample describes response shape, **not a production Tenant's current data**.

## Security, transaction and rollout boundaries

- The new endpoint is only a **read-only observability projection**. It does not make purchased modules available to staff, grant `RolePermission`, mutate `UsageBalance`, bypass `TenantCommercialOperationPreflight`, or authorize a write.
- All future command handlers must still evaluate real delegated Permission+Entitlement and call `TenantAtomicUsageService` in the **same Serializable SQL transaction** as the business write. The currently existing primitive accepts a trusted period from a code-owned adapter; **this PR does not automatically bind its caller to the resolver**, so direct production use without a vetted adapter is forbidden. A future code-owned operation executor must derive current window from the same verified subscription snapshot before consumption.
- A read can become stale immediately after a concurrent write. It is for display only; client UI must never enforce quotas or rely on its remaining number as a guaranteed reservation.
- No raw SQL client-specified TenantId; the query is parameterized and the endpoint derives identity from the signed BFF/Tenant session.
- No user-facing CountLimit usage claims until a real domain-specific active-count provider exists.
- No new DB migration, catalog seed, wholesale authorization cutover or staging/production switch.

## Acceptance

- Pure unit tests: monthly Jan-31 anniversary drift, yearly Feb-29 anniversary, mid-month timestamp boundary, subscription expiry, UTC-kind enforcement, missing cadence and invalid non-PeriodQuota period declarations.
- Disposable real SQL test: three Tenants, verified active paid A+B and unlicensed C; separate month quota A=3 and B=1; explicit Unlimited with actual recorded units; CountLimit unmeasured; unset cadence unmeasured; zero when no balance; period mismatch fails closed; production source manifest empty; System/foreign Tenant context cannot read.
- Mandatory BFF SQL job updated with `TenantUsageProjectionSqlTests`, while Angular must wait for a compatible owner-only consumer.
- Review current-head Build/Identity unit/SQL/Tenant IAM checks before merge, then separately verify `main`.

## Next

1. Owner-scoped Angular true usage panel, restricted 403 state, `fa-IR` UTC-period display, no client-side authorization.
2. Source-vetted active CountLimit providers per business domain.
3. Atomic production command executor tying the actual subscription-based window to `TenantAtomicUsageService` and the corresponding domain write; enforce both Permission and Entitlement at signed HTTP boundary, with concurrency and two-Tenant tests.
4. Business lifecycle policy for upgrade/downgrade, renewals, carryover and asynchronous reservation workflows. No hidden reset or automatic carryover.
