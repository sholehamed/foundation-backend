# FR-17E-1 — Centrally issued coupons, settled-use ledger, and zero-total checkout

**Date:** 2026-11-11  
**Backend:** [PR #138](https://github.com/sholehamed/foundation-backend/pull/138)  
**Angular:** [PR #65](https://github.com/sholehamed/foundation-frontend/pull/65)  
**Parent FR-17C:** [issue #133](https://github.com/sholehamed/foundation-backend/issues/133)  
**Status:** PR review/CI. Verify latest PR heads and post-merge `main` before treating as released.

## Purpose and trustworthy boundaries

Plan publication, Tenant order placement and subscription activation already use centrally defined code-owned commercial resources, immutable PlanVersions and server-priced IRR amounts. This step adds coupons without creating an unauthorized parallel path to commercial entitlement.

**Do not treat a coupon as an entitlement.** An active Tenant subscription still requires a **Settled order** with exact same TenantId and immutable PlanVersionId, a settled coupon-redemption row for discounts, a valid time window and deployed Commercial Module definitions. A Tenant employee must still have an independently assigned Permission. Buying a Plan never creates UserPermission, RolePermission or legacy TenantModule rows.

## Entities, migration and keys

Migration `20261111160000_couponredemptions` adds:

- `CatalogDiscountCoupons`, global/System-owned: Id, SHA-256 `CodeHash`, admin Title, Kind (`Percentage` / `FixedIRR`), Value decimal(19,2), optional pinned Published `PlanVersionId`, mandatory `IRR`, UTC StartsAt/EndsAt, `MaxUses`, `MaxUsesPerTenant`, `IsActive`, existing Audit/RowVersion metadata. Unique CodeHash.
- `CatalogCouponRedemptions`, **Tenant-filtered**: TenantId, CouponId, OrderId, positive DiscountAmount, UTC RedeemedAtUtc and existing Audit/RowVersion fields. Unique OrderId so one Order cannot claim multiple coupon redemptions, FK restrict to Tenant, Coupon and Order, plus indexed CouponId/TenantId/time for usage counts.
- Nullable `CouponId` FK on existing `TenantPlanOrders`: a Pending order can refer to a coupon but has **not redeemed** anything. Existing orders need no backfill; null means no coupon.

Coupons are issued by the System server using a cryptographic RNG (12 bytes = 96 bits rendered as 24 uppercase hexadecimal characters). The server stores only a **SHA-256 fingerprint** of the generated high-entropy code. The plaintext is returned **once** in the protected issuance response with no-store header, never stored in an entity, listed or logged by the coupon read model. A user-chosen short predictable code is intentionally unsupported. Safe-keep the issued code outside the app; lost codes cannot be recovered from the database. DB administrators with access to all data/DDL are a separate trust boundary.

## Admin operations (System only)

All endpoints are below `/api/system/PlanCatalog`, existing `SystemAdministrator` ASP.NET policy and a live persisted Master SuperAdmin check.

| Method | Relative URL | Permission | Function |
|---|---|---|---|
| GET | `coupons` | `PLAN-CATALOG.COUPONS` | List definitions and **settled** usage counts only, no secret |
| POST | `coupons` | `PLAN-CATALOG.COUPONS` | Issue random code and return `secretCode` once |
| POST | `coupons/{couponId}/disable` | `PLAN-CATALOG.COUPONS` | Disable future uses, preserve settled redemptions |

The new permission is centrally code-owned in the System `plan-catalog` Resource. It is checked against **live Master RBAC even if optional generic granular enforcement is disabled**. A fresh bootstrap seeds all System rights; an existing database needs reviewed Feature Registry apply + audited Master grant, **without re-granting an explicitly revoked right**.

Issue body example:

```json
{
  "title": "Opening campaign",
  "kind": "Percentage",
  "value": 100,
  "planVersionId": null,
  "startsAtUtc": "2026-11-11T08:00:00Z",
  "endsAtUtc": "2026-12-01T08:00:00Z",
  "maxUses": 20,
  "maxUsesPerTenant": 1
}
```

Percentage Value 0 < value <= 100; FixedIRR Value > 0; all money remains decimal **IRR**. A fixed amount greater than the price is **clamped** to gross; net never becomes negative. Percentage discount is rounded to the nearest whole IRR, midpoint **away from zero**, before clamping; no floating-point amount calculations in .NET. Actual tax, fractional IRR offer policies and invoicing will need their own reviewed financial spec before launch.

## Tenant checkout with an optional code

The existing authenticated Owner-only, rate-limited `POST /api/tenant/workspace/subscription/orders` now accepts an optional `couponCode` (exactly 24 hex characters) in addition to `{planVersionId,billingCycle,idempotencyKey}`. **TenantId, price, discount, settlement flag and Permission remain server-owned**.

1. Start the existing SQL Server `SERIALIZABLE` transaction; ensure the current Tenant owner is authorized at the endpoint.
2. Repeat matching idempotency key => same Order only when PlanVersion, cycle **and coupon fingerprint match**. Different coupon with a reused key fails `idempotency_conflict`.
3. If another pending quote exists for Tenant, repeat the exact plan/cycle/coupon combination or reject `pending_order_exists` (no silent coupon switch on pending quote).
4. Check there is no active Tenant subscription, read only the vetted Published PlanVersion and its actual **active IRR offer**; reject unregistered business Module source.
5. With code, find the coupon by SHA-256 fingerprint under SQL `UPDLOCK,HOLDLOCK`, validate active UTC period, optional exact PlanVersion scope, IRR kind/value and unused **global and per-Tenant settled caps**. Counts use `IgnoreQueryFilters(["tenant"])` exclusively for global accounting.
6. Compute discount server-side, snapshot gross/discount/net on Order and pin CouponId. A *pending* order does not add a redemption row; a zero-net coupon purchase proceeds immediately to Step 7.
7. **Net = 0**: in the **same** serializable SQL transaction, insert Order `Settled`, `SettlementKind=ZeroTotalCoupon`, `SettledAtUtc=now`, a server-derived `SettlementReference=coupon:<orderId>`, a single `CatalogCouponRedemption`, and the Active TenantSubscription for exactly the Order/PlanVersion/Tenant. Save and commit atomically; no gateway or manually claimed payment.
8. **Net > 0**: insert `Pending` discounted quote and return. There is no active subscription and **no consumed coupon** until System SuperAdmin separately verifies actual external receipt and performs final settlement.

Because the coupon row is locked for update while usage counts are checked and redemptions inserted under a serializable transaction, two simultaneous final settlements for the last global/per-Tenant use cannot both commit. SQL OrderId unique redemption index and one-active-Subscription-per-Tenant index provide additional defense. Deadlocks or unique races must fail closed and be retried with the **same idempotency key**; do not retry with a new code/quote amount.

## Discounted manual receipt settlement

`TenantPlanCheckoutService.AttestExternalSettlementAsync` still requires live `PLAN-CATALOG.SETTLE` and explicit `ExternalManual` operator receipt attestation. For a discounted Pending Order it:

- Checks `GrossAmount = NetAmount + DiscountAmount`, Net>0, IRR and actual verified receipt.
- Locks the pinned Coupon row and revalidates **current coupon eligibility**, global/per-Tenant remaining settled uses and exact original discount amount.
- Inserts one redemption with same TenantId/CouponId/OrderId, updates Order to Settled, and activates the linked subscription in the **same SQL transaction**.
- A coupon that expired, was disabled or exhausted between quote and manual receipt **cannot** be redeemed: the quote needs cancellation/reissue through a future audited order lifecycle. A Pending quote **does not reserve capacity**.

If an order has no coupon, the previous positive Net/Gross manual path is unchanged. If an order has a coupon but no settled redemption, the entitlement shadow rejects it.

## Visibility and no-fake-sale guarantee

The Tenant Angular order form optionally accepts a secret code; it never posts discount amount. It presents the returned order status:
- `zero_total_settled_and_activated` — only after confirmed backend atomic zero-total settlement
- `created_pending` — no subscription yet, due after discount and pending receipt check
- `existing_order` / `existing_pending` — idempotent replay
- invalid/expired/exhausted coupon — fail closed, no paid license.

System Plan Catalog now has a coupon issue/list/disable section. UI requests `validDays` and computes the UTC range without forcing a Gregorian date picker on a Persian-language admin; backend contract also supports explicit UTC range for future Jalali scheduling. Original code shown once; usage counts represent settled receipts, not reserved quotes.

**Important:** `ModuleResourceContracts.Current()` still includes **zero deployed Commercial Modules**, so production Plan publication/purchase remains blocked even when coupon definitions exist. The positive SQL acceptance tests use explicitly supplied **test-only Commercial resource contracts**, not fake production data.

## Test requirements

- Migrate disposable SQL DB and check no pending EF model changes.
- 100% coupon creates one Settled ZeroTotalCoupon Order, one redemption, one subscription with no gateway, at most one use; retried matching key returns original order.
- Two Tenant test: Tenant B cannot exceed a global limit already redeemed by Tenant A; Tenant B sees only its own orders.
- Existing subscription shadow requires exactly one matching redemption for discounted orders.
- Fixed amount partial discount creates Pending quote but **zero redeemed uses** until System operator confirms externally verified positive payment; then one redemption and one subscription.
- Invalid code/date/PlanVersion/usage cap never creates an Order or entitlement; no TenantModule, UserPermission or RolePermission granted.
- Angular API tests confirm no client-supplied amount, TenantId, `isPaid` or discount value.

## Unfinished hardening before production rollout

1. **No commercial source modules yet:** publication and sales remain disabled. First actual commercial EndpointGroup/Permissions must be delivered and verified end-to-end.
2. Currency/tax/invoice/IRR round-off policies and scheduled-sale availability need financial review. A published PlanVersion remains immutable; sale retirement belongs in a separately audited overlay.
3. The current quoted, not-yet-settled partial discount is validated again at settlement. **No Pending order cancellation/expiry UI or quote timeout** exists; this can block a Tenant from changing to a different coupon until a staff-managed resolution is added.
4. Strong append-only operator settlement evidence, gateway receipt verification, refunds, reactivation/renewal and concurrent multi-context SQL load testing are pending. An explicit operator attestation is not bank verification.
5. Existing active Subscription unique index prevents a new Active row until the old status is transitioned, including expired-in-time rows; future lifecycle must handle it.
6. Database privileges should prevent unreviewed direct DML to fake Settled/Redemption; SQL shape checks are not proof of banking activity.
7. This feature does **not** wire commercial entitlement into Tenant user Permission or usage-limit enforcement yet. Permissions remain separately delegated. Actual source runtime gating is a controlled future rollout.
8. PR CI (Build, SQL/HTTP, Angular Unit/Chromium) and **post-merge main** must both be checked; CI isn't Staging deployment.
