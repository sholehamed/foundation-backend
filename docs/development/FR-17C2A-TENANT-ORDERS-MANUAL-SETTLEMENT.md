# FR-17C-2A — Tenant Plan Orders & Explicit Manual Settlement

**Date:** 2026-11-11  
**Backend PR:** [#135](https://github.com/sholehamed/foundation-backend/pull/135)  
**Angular PR:** [#64](https://github.com/sholehamed/foundation-frontend/pull/64)  
**Parent:** [FR-17C issue #133](https://github.com/sholehamed/foundation-backend/issues/133)  
**Status:** PR branch; merge only after latest-head SQL, HTTP and Angular CI. No Staging or public launch assumed.

## Why this slice exists

A published Plan is an immutable product snapshot, **not** a Tenant license. FR-17C-1 added scoped order and subscription tables and a read-only shadow entitlement reader. This slice introduces the first server-owned order-writing pathway and a **deliberately manual, trusted System Admin receipt attestation** to move `TenantPlanOrders.Pending` to `Settled` and create `TenantSubscriptions.Active` in the **same SQL Server Serializable transaction**.

It does not connect gateway APIs, redeem coupons, automatically verify bank deposits, modify role grants, or create commercial Modules. The product code currently declares **no Commercial Module**, therefore even an otherwise valid published plan cannot be checked out or activated with the default production manifest. This is intentional fail-closed behavior.

## Who is permitted to do what

| Operation | Boundary | Authorization | Commercial side effect |
|---|---|---|---|
| GET `/api/tenant/workspace/subscription/orders` | Tenant workspace | Authenticated **current active Tenant Owner**, live local User and Tenant, writable/readable HomeTenant | List own latest 50 orders |
| POST `/api/tenant/workspace/subscription/orders` | Tenant workspace | Same owner-only checks + per-user rate limit `tenant-plan-order`, 6 requests/minute | Create **Pending** Order only |
| GET `/api/system/PlanCatalog/orders/pending` | System PlanCatalog | `SystemAdministrator` policy, live Master `SUPERADMIN` SQL check, code-owned `PLAN-CATALOG.READ` | Read at most 100 pending orders across Tenants |
| POST `/api/system/PlanCatalog/orders/{orderId}/confirm-external` | System PlanCatalog | Same live Master SuperAdmin + code-owned **`PLAN-CATALOG.SETTLE`** | Record attested `ExternalManual` settlement and activate one Subscription atomically |

Tenant IDs are derived from authenticated context for Tenant checkout. The POST body **never** includes TenantId, user Permission, amount, currency, discount, net price or `isPaid`. The order queue never lets a Tenant Owner impersonate System Admin.

An existing populated database needs reviewed central Feature Registry sync + Master Role grant for newly introduced `PLAN-CATALOG.SETTLE`. Fresh installation includes the centrally declared System Permission in SuperAdmin bootstrap. Existing revoked permissions must never be re-granted silently.

## Tenant order request/response

`POST /api/tenant/workspace/subscription/orders`:

```json
{
  "planVersionId": "published-version-guid",
  "billingCycle": "monthly",
  "idempotencyKey": "opaque-client-generated-uuid"
}
```

Server validates idempotency key length 16–100 and allowed characters; uses unique `TenantId,IdempotencyKey` index already in FR-17C-1. For the same Tenant+key+version+cycle the result is the same `OrderId`, never a duplicate charge. Reusing the key with different version/cycle returns `idempotency_conflict` (HTTP 409).

The order amount is recalculated by `PublishedPlanCatalogReader` using the pinned immutable version, deployed code-owned Commercial Module/Capability manifest and currently valid **IRR** offer of the requested cycle. It rejects unknown/draft/missing/empty/commercial-source-drift/expired/no-price offers. No price, coupon or amount is accepted from the request. Positive price is mandatory at this stage; zero-price offers return `zero_total_requires_coupon_checkout` instead of creating an incorrectly 'paid' order.

Creating one pending order for a Tenant blocks duplicate unpaid orders: if a **different** idempotency key repeats the same pending PlanVersion/cycle, the server returns the existing order (`existing_pending`); a different PlanVersion/cycle returns `pending_order_exists`. This prevents duplicate quotes even after browser refresh/lost responses, pending a future explicit cancel/expire flow.

Result contains `{ok,code,order,subscriptionId}`; newly created order is **Pending**, discount=0, net=gross, no receipt, no subscription. The old `BusinessProfile.RequestedPlanKey` remains signup intent only.

## System Admin external manual settlement

`POST /api/system/PlanCatalog/orders/{orderId}/confirm-external`:

```json
{
  "receiptReference": "verified-outside-application-receipt-id",
  "paymentVerified": true
}
```

This is an **operator attestation**: the designated Master SuperAdmin must independently verify receipt and full order amount via business/accounting processes before calling. The software does **not** independently validate the financial institution's transaction; setting `paymentVerified: true` is not a payment gateway verification. UI clearly states this and requires an explicit checkbox and receipt number. Only the live Master SuperAdmin can reach the endpoint.

During one Serializable transaction:
1. Check Order is `Pending`, with positive IRR Net and exact no-discount gross, quote time in the past.
2. Check current Tenant is active and has **no other Active** subscription.
3. Reject a repeated receipt reference already used for a different `Settled/ExternalManual` order.
4. Revalidate pinned PlanVersion, deployed Commercial Module source and current exact IRR cycle/price. A disabled or no-longer-saleable offer fails closed; operator must refund or handle separately.
5. Mark order `Settled`, set `SettledAtUtc`, `ExternalManual`, `SettlementReference`, `LastModifiedBy` to the actual System actor, `LastModifiedAt` to settlement UTC.
6. Create `TenantSubscription` referencing exactly **that same Order/Tenant/PlanVersion**, Active starting at settlement UTC and ending after 1 month or 1 year (`AddMonths(1)` / `AddYears(1)`).
7. Save Order+Subscription and commit together. No TenantModule, RolePermission or UserPermission is modified.

Reposting the same receipt to the same already settled order returns `already_settled` and its SubscriptionId; posting a different receipt returns `already_settled_conflict`. Concurrency is serialized by transaction plus existing unique indexes, including the one-active-per-Tenant subscription index. Database errors are converted to HTTP 409.

**Remaining audit limitation:** the order records actor/time/reference but there is no dedicated append-only manually reviewed settlement/audit event table, second approval or external bank proof. Those are required before treating this as unattended production-grade cash reconciliation. Do not falsely label the attestation as provider-verified.

## Current user experience

- Tenant dashboard includes a **lazy** section 'مشاهده و ثبت سفارش پلن', which reads the same published PlanCards as public signup plus the Tenant's scoped order list. Non-owner access is rejected by the server and the component hides itself on 403.
- The Owner chooses a real published PlanVersion + valid billing cycle. The displayed price is informational; POST contains only IDs/cycle/new secure idempotency UUID. The result says **order pending settlement**, not 'subscription active'.
- System PlanCatalog includes a refreshable Pending Order queue with TenantId, immutable PlanVersionId, gross/net amounts in IRR and billing cycle. Operator enters independently checked receipt ID and ticks explicit review confirmation before action is enabled.
- After confirmed server response, Tenant's shadow subscription becomes Active **only if the actual deployed commercial source registry matches**. The system never automatically grants a staff role/permission on checkout.

## Tests / acceptance

SQL disposable PlanCatalog tests cover positive code-owned test fixture:
- A Tenant's server-quoted amount exactly matches published IRR price; discount=0.
- Same idempotency key reuses the order; different cycle conflicts.
- Tenant B cannot see Tenant A's Orders.
- Unattested manual settlement is rejected, attested external receipt activates exactly one subscription, repeated identical request is idempotent, different reference conflicts.
- Audit actor/time and 1-month term persisted; shadow resolver becomes Active **with explicit test-only Commercial manifest**; production manifest still has no commercial modules and denies.
- Active subscription blocks another new paid order; no TenantModules/RolePermission/UserPermission write.
- Draft/noncommercial product cannot create Pending order.

Signed HTTP E2E exercises System/anonymous/Owner crossover and rejects a forged unpublished PlanVersion from Owner. The Angular tests inspect exact HTTP request bodies to ensure no client-set payment, amount, TenantId or role.

## Explicitly next (do not conflate with implementation)

- Actual Commercial Module/Resource/Permission/EndpointGroup registered from deployed business code and published through System Plan Catalog. **Until then, order creation/activation is safely unavailable in the live app.**
- **FR-17C-2B**: tamper-resistant append-only settlement verification events, external payment evidence, refund/cancellation/reconciliation, authenticated multi-principal HTTP activation tests. The current unique Active index rejects renewal even after time expires if Status remains Active; add audited expiration/renewal handling without deleting history. Decide how to honor quoted prices after published sale windows end.
- **FR-17E**: centrally minted percentage/fixed coupons, valid from/to, max global/per-Tenant uses, targeted plans, atomic redemption and zero-net checkout; zero-total must use `ZeroTotalCoupon` and not `ExternalManual`. Never activate through a client boolean. Both coupons and external receipts must lead through the one verified Order→Subscription transaction pathway.
- **FR-17D**: typed CountLimit/Quota and dynamic workflows, atomic usage accounting.
- **FR-17C-3**: effective authorization = `CommercialEntitlement AND Tenant delegated Permission AND scope AND Capabilities`. This slice does **not** alter existing authorization or menus.
- Add composite DB ownership integrity for Order/Tenant/PlanVersion relationship and a separate audited sale-availability/retirement overlay. Existing System permissions need the reviewed rollout process.

Do not infer production deployment from green GitHub Actions. Update `docs/PROJECT-STATE.md` and roadmap in both repos after each completed slice.
