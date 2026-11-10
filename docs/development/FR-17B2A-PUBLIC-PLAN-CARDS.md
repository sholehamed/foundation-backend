# FR-17B-2A — Public Published Plan Cards, Registration Intent, Angular Consumer

**Date:** 2026-11-10. **Status:** Implementation in backend PR under review; not yet merged at document creation. **Schema prerequisite:** FR-17B-1 (merged PR #128).

## Goal / user requirement
Every customer-facing PlanCard field (version, title, subtitle, badge, sorted modules/features/benefits, price amount, currency, billing cycle) comes **from database-owned published plan revisions**, not hard-coded four Angular plan arrays. A child plan's capabilities are evaluated from its **pinned parent version**. System Admin owns definitions; Tenant signup only requests a plan and **does not** gain a paid subscription.

## Public API: `GET /api/public/plans`

- Registered explicitly by `PublicPlans.Map` in Identity startup; anonymous and fixed-window IP rate limited to 60 requests/minute via `public-plans`; request/response are no-store.
- `PublicPlans.ReadCardsAsync` is a tested pure read projection over `CatalogPlanVersions` and `PublishedPlanCatalogReader`. Only latest **published** revision per plan is considered. Draft/future revisions excluded. An inactive plan or an empty/unavailable offer is not displayed.
- A price contains **amount and true ISO currency**; specifically IRR is displayed as **ریال**, not تومان. Checkout will later handle money rounding and verified payment.
- `features` inherit display items from the exact pinned published parent-version chain; a child may override a Module/Capability item with the same stable reference. A child cannot silently claim a disabled inherited Feature or zero allowance: these descriptions are filtered by the final effective typed capability values.
- `features` is an ordered projection of vetted `CatalogPlanDisplayItems` (Module, Capability, Benefit). The card **never** provides user permissions or activates a Module.
- A broken or undeployed commercial Module is excluded. The Source Registry is currently `ModuleResourceContracts.Current()` and intentionally declares only 13 System / 17 Tenant-core permissions, **zero commercial modules**. Therefore the public API intentionally returns `[]` until actual code-owned commercial modules and published catalog data exist. This is a fail-closed rollout gate, **not** a backend failure or license purchased.
- Corrupt plan versions are skipped with sanitized server-side logging (no internal metadata leaks to the public). API returns an empty array if no plans were published; never fabricates prices or Benefit rows.
- Max 50 display cards; HTTP GET is read-only. A future admin editor must ensure immutable publication, parent-cycle checks and version consistency (FR-17B-2B).

### DTO

```json
[
  {
    "code": "pro",
    "versionId": "plan-version-guid",
    "title": "پلن حرفه‌ای",
    "subtitle": "نمونه",
    "badge": "پیشنهادی",
    "isRecommended": true,
    "displayOrder": 1,
    "prices": [
      { "billingCycle": "monthly", "currency": "IRR", "amount": 2000000 }
    ],
    "features": [
      { "kind": "Benefit", "title": "پشتیبانی ویژه", "description": null,
        "isHighlighted": true }
    ]
  }
]
```

This JSON is an **illustrative fixture**, never a product currently live for sale.

## Signup compatibility and safety

Previously `PublicSignup.Validate` recognized only 4 hard-coded PlanKeys. This PR keeps those keys valid for existing onboarding but also accepts:
- a syntactically valid (max 20 chars, due to existing `BusinessProfiles.RequestedPlanKey` column) new PlanKey **only when it is on the public published catalog and has the selected BillingCycle offer**;
- the special `unselected` key for basic tenant/Owner creation **without purchase or commercial entitlement**.

The Signup endpoint still creates a Tenant, Owner, admin grants and an intent-only BusinessProfile inside its existing transaction. It does **not** verify payment, create Subscription or issue commercial RolePermissions. If a stale or nonexistent public card was deep-linked, validation fails instead of silently selecting `solo-pro`.

## Angular companion (separate PR/repo)

- Shared `PublicPlansService` for home pricing section **and** signup selection; no local prices or lists of purchasable plans.
- Public pricing section preserves previous design classes but renders cards based on API, with visible loading, empty and retryable error states. CTA includes exact code and billing cycle.
- Signup displays only offered cycles and real currency; honors URL PlanCode if present; unknown plan does not default to another; if no offers are present, basic signup remains possible via explicit `unselected` option, not a fake paid plan.
- No `branchLimit` is inferred from marketing text; it must later come from the typed Capability/Limit projection.
- No subscription/payment is claimed by the UI. Front and backend CI must pass independently, and live cross-repo HTTP smoke still requires deployment.

## Security checks and rollout gates

1. System/tenant core Modules cannot be advertised as commercial without code-owned registration.
2. Metadata in DB must not introduce executable features or roles.
3. No plan catalog record directly grants UserPermission/RolePermission.
4. Public rate limiter and route-source auditor `fr14a_audit.py` explicitly classify this GET as read-only anonymous public boundary; loss of `AllowAnonymous` or rate policy must fail CI.
5. A failed GET returns no fabricated card price. A clicked PlanKey is only an onboarding intent, never proof of purchase.
6. Keep FR-17B-1 SQL migration applied and tested; do not turn on subscription enforcement or add paid bootstrap to signup.

## Next steps

**FR-17B-2B:** System Admin draft/Publish service/API/UI with strict live administrator gate, cycle detection and descendant version pin, deterministic content fingerprint, immutable published catalog enforcement, source commercial manifest and first catalog fixture. Then **FR-17C** Subscription and effective entitlement shadow evaluation; later discounts/settlement/payment. Plan cards can stay empty until products really exist.

Tracking: update `docs/PROJECT-STATE.md`, `docs/PROJECT-ROADMAP.md` and frontend corresponding docs every slice. SQL, signed HTTP and Angular tests are not replaced by a screenshot.
