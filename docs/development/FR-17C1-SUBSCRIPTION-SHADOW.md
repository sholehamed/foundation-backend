# FR-17C-1 — Tenant Subscription storage and Shadow commercial entitlement reader

**Date:** 2026-11-11  
**Backend:** [PR #134](https://github.com/sholehamed/foundation-backend/pull/134)  
**Angular:** [PR #63](https://github.com/sholehamed/foundation-frontend/pull/63)  
**Parent work:** [FR-17C issue #133](https://github.com/sholehamed/foundation-backend/issues/133)  
**State:** Implementation in PR, subject to latest-head CI and post-merge main verification.

## Goal and explicit limits

Foundation already has an immutable, centrally managed Plan/PlanVersion hierarchy. A PlanVersion bundles code-owned Commercial Modules (with Resource ≈ Controller/EndpointGroup) and typed Feature/Limit/Quota definitions. This step introduces the **separate operational records required for a Tenant's license** and a read-only, tenant-isolated resolver.

**Do not confuse these states:**

| Concept | Stored in | Effect |
|---|---|---|
| Signup intent | `BusinessProfile.RequestedPlanKey`, `PromoCode` | **No commercial right, no money paid** |
| Published product | `CatalogPlanVersions` + pinned `CatalogPlanVersionModules/Capabilities/Prices` | What System Admin offers for sale; **no Tenant access** |
| Order / settlement evidence | `TenantPlanOrders` | Records quote and later verified settled source, **not alone an active license** |
| Tenant commercial license period | `TenantSubscriptions` | Only **Active, in-period, same-tenant, same-PlanVersion Settled** order is eligible |
| User permissions | Existing centrally defined Permission ↔ Tenant local Role/User assignments | Actual user authorization; **NOT issued by buying a Plan** |
| Legacy `TenantModules` | Existing flags | **Not a source of commercial entitlement**, never written here |

The commercial license read result is called **Shadow**: it is *observable status only*. This PR never joins it into `EffectivePermissionEvaluator`, never alters role/permission/menus, and never changes a Tenant's runtime operations. **The registry still has zero deployed Commercial Modules, so the production shadow returns no eligible paid Modules even with test/hand-written order records.**

## EF domain and SQL schema

Migration `20261111120000_tenantsubscriptionshadow` adds two empty SQL Server tables and their indexes/FKs/check constraints. Both entities implement `IMustHaveTenant` and receive the existing named tenant query filter. The database is shared, but every HTTP read **derives the ActiveTenantId from authenticated tenant context** and additionally constrains its query; there is no client TenantId parameter.

### TenantPlanOrders

- Id, TenantId FK (Restrict), PlanVersionId FK to immutable catalog (Restrict).
- `Status`: Pending, Settled, Cancelled, Refunded. **No Order creation/settlement API yet**.
- `GrossAmount`, `DiscountAmount`, `NetAmount` decimal(19,2) **in IRR**, check 0 ≤ Discount ≤ Gross and Net = Gross − Discount.
- `BillingCycle`: monthly/yearly; `Currency`: IRR; UTC `QuotedAtUtc`.
- `SettledAtUtc`, `SettlementKind`, `SettlementReference` required for Settled. Net=0 requires kind `ZeroTotalCoupon`; Net>0 requires `GatewayVerified` or `ExternalManual`. These database checks validate a declared settlement **shape, not actual payment verification**; only future audited server workflows can generate genuine evidence.
- Nullable `IdempotencyKey`, max 100, unique per Tenant when present. A future checkout must use this for duplicate-click retries and transactional redemption.

### TenantSubscriptions

- Id, TenantId FK, immutable PlanVersionId FK, nullable `SourceOrderId` FK (Restrict).
- `Status`: Pending, Active, Expired, Cancelled; UTC `StartsAtUtc`, `EndsAtUtc`, optional Activated/Cancelled timestamps. SQL CHECK: EndsAt > StartsAt.
- Active requires SourceOrderId and ActivatedAtUtc; a unique filtered index `UX_TenantSubscriptions_OneActive` permits only **one** Active row per Tenant. A separate unique filtered index on SourceOrderId prevents allocating one Order to multiple Subscriptions.
- **Important**: An expired-in-time row still marked `Active` occupies the unique filtered slot; a future renewal/upgrade worker must transition state in a **single audited transaction** before inserting its replacement. Do not remove the index simply to work around this.
- Application read double-checks order TenantId, PlanVersionId, state, settlement dates/kind/reference, quote arithmetic and valid license period. A foreign-Tenant SourceOrderId FK alone is not sufficient—**runtime rejects cross-Tenant use**. A future composite FK or DB trigger can additionally enforce matching ownership.

Both tables use the existing AuditableEntity/RowVersion pattern and FK Restrict; no commercial license is created by migrations, seed or signup.

## Shadow Resolver contract

`TenantCommercialEntitlementReader.ReadCurrentAsync(nowUtc, codeResources?, codeCapabilities?)`:
1. Reject System and anonymous/missing Tenant context (never takes TenantId from caller).
2. Check current Tenant is active.
3. Read current scoped Subscription. No Active means `NotSubscribed` or `Pending`; either returns empty ModuleCodes/Capabilities.
4. Active but expired, future-dated or not activated returns `Inactive`, no rights.
5. Load `TenantPlanOrder` scoped to **same** current Tenant and pinned PlanVersion. No proof of Settled order, mismatched identifiers or invalid settlement evidence means `Invalid`, no rights. A promo code entered at signup is not evidence.
6. Delegate Published Plan validation to `PublishedPlanCatalogReader`, using validated **code-owned Commercial Resource and Capability manifest**. Unknown, missing, inactive or undeployed module/capability returns `Unavailable`, empty rights.
7. Return `Active` with typed effective Capability values and ModuleCodes **only** if every requirement passes.

Default input manifests are `ModuleResourceContracts.Current()` and no commercial Capability declarations. Explicit *test-only* Commercial contracts can be supplied to test positive behavior without declaring fake business modules in production.

### Authenticated HTTP API

`GET /api/tenant/workspace/subscription/me` (no TenantId, no body or query). Existing Tenant auth policy is required; System context and nonreadable active tenant are 403, no-store response. DTO:
```json
{
  "status": "NotSubscribed",
  "reason": "no_active_subscription",
  "subscriptionId": null,
  "planVersionId": null,
  "startsAtUtc": null,
  "endsAtUtc": null,
  "hasCommercialEntitlement": false,
  "moduleCodes": [],
  "capabilities": []
}
```

`hasCommercialEntitlement` and ModuleCodes are **display/read-model data**, never a UI permission-grant or backend authorization bypass. The existing `/tenant/workspace` response retains legacy `subscriptionStatus = pending_activation` for backwards compatibility, but the new Angular dashboard **reads the separate truthful endpoint** instead of displaying the old placeholder. Do not delete the old field until consumer migration is complete.

## Verification

Real disposable SQL Server PlanCatalog acceptance tests:
- Fresh schema and model-snapshot compatibility via `Database.HasPendingModelChanges` / `MigrateAsync`.
- Two active Tenants, distinct orders/Subscriptions, test-only vetted Commercial module: Tenant A's valid settled Order and Active in-period Subscription resolves modules; Tenant B with Pending Order does **not**, even with legacy `TenantModules.IsEnabled = true`.
- A forged Active state pointing to a **different Tenant's** separate settled Order is rejected, and an Active state pointing to Tenant B's own Pending Order is rejected.
- System context cannot invoke tenant shadow reader as a Tenant.
- Invalid discount/negative Net order and Active Subscription without SourceOrderId fail SQL constraints.
- No RolePermission/UserPermission grants or legacy TenantModules changes by the resolver.
- Existing System/Tenant auth, SQL migrations/Feature Registry, BFF atomicity and HTTP tests remain green.

Angular companion reads the same route and renders explicit pending/no-subscription/inactive/invalid/active statuses. It does **not** show static or fabricated “در انتظار فعال‌سازی” for every Tenant; it does not hide menus or activate purchases.

## Rollout, limitations and next slices

1. No live data migration or backfill. `Down` drops both tables: **destructive once real Orders/Subscriptions exist**. Review backups and release sequencing before rollback.
2. This step intentionally exposes **no** order write, coupon redemption, license activation, cancellation or payment endpoint. A DB admin can insert fake Settled orders: SQL checks are not cryptographic proof; production application credentials and settlement command boundaries must be locked down before purchases.
3. Do not enable paid runtime permission enforcement until two-Tenant HTTP authorization/denial tests and approved rollout. Long-term effective access = commercial Tenant entitlement **AND** Tenant delegated permission **AND** resource/branch scope **AND** typed capability/usage policy.
4. Before real sales: implement a real code-owned Commercial Module with controller/EndpointGroup resources, central Permission/UI bindings; a safe sale-availability/retirement overlay separate from immutable PlanVersion.
5. **FR-17C-2**: signed/authenticated current subscription state and System Admin subscription lifecycle read models, guard integration in OFF/shadow mode, verified settlement-only write commands.
6. **FR-17D**: atomic CountLimit/PeriodQuota usage/concurrency, reusable workflow policy hooks.
7. **FR-17E**: actual Checkout + Discounts. The planned fixed IRR or percent coupon can have UTC start/expiry, max/global/per-Tenant uses and targeted PlanVersions; verify/counter-increment atomically. A 100% discount produces a **real zero-net Settled Order without gateway**; a nonzero offline payment requires audited externally verified receipt. Order quote/tax/rounding and idempotency snapshots are future work, **not done in this PR**.
8. On populated DBs, `PLAN-CATALOG.*` System master permissions still require reviewed sync/grants. CI success is not staging deployment.

**Data correctness risk to close next:** SQL currently validates Active subscription's SourceOrderId non-null but cannot enforce exact TenantId+PlanVersionId match against the referenced Order in one CHECK. Reader verifies it. Add a composite FK or serializable activation procedure with SQL ownership integrity before enabling checkout.
