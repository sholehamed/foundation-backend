# FR-17F-1 — Verified System Admin commercial lifecycle overview

**Date:** 2026-11-11  
**Scope:** additive, read-only fields to the existing System Admin Dashboard. No new route, subscription write, permission grant, billing settlement, migration, Commercial product or menu.

## Contract

Existing protected `GET /api/system/Dashboard/super-dashboard` continues to require the live `SystemAdministrator` authorization policy and uses `Cache-Control: no-store`. Its existing fields and shape remain stable. Add four **non-financial** counters:

| JSON field | Source and meaning |
| --- | --- |
| `publishedPlanVersions` | Number of SQL `CatalogPlanVersions` with stored `Status="Published"`; a published version is not proof that a real Commercial module is onboarded or saleable |
| `pendingPlanOrders` | Stored non-Master Tenant `TenantPlanOrders` with `Status="Pending"`; quote requests, **not revenue** |
| `settledPlanOrders` | Stored non-Master Tenant orders with `Status="Settled"`; server-side order status, **not a bank reconciliation or recognized revenue** |
| `activeSubscriptionPeriods` | Stored non-Master Tenant `TenantSubscriptions` with `Status="Active"` and `StartsAtUtc <= serverUtcNow < EndsAtUtc`; not a substitute for commercial Entitlement + delegated Permission |

### Scope and security

- All queries are performed in the existing `SuperDashboardQueryHandler`, behind the existing privileged `SystemAdministrator` endpoint. There is no Tenant ID supplied in the HTTP request.
- `IIdentityDbContext` now exposes `TenantPlanOrders` and `TenantSubscriptions` as queryable sets; the implementing `IdentityDbContext` already had them, so this introduces **no EF schema change**.
- For system-only aggregate counts, the named `tenant` EF filter is explicitly bypassed and Master Tenant records are excluded. Ordinary Tenant endpoint/roles continue to respect their own existing scoped isolation.
- Do not join guessed payment ledger, estimate profit, or fabricate financial charts. The four counters are statuses from persisted SQL, not a new settlement proof.
- Existing System dashboard, Tenant permissions, commercial license gate, Plan versions and commerce writes are unchanged.

### SQL acceptance

`PlanCatalogSqlServerTests.System_dashboard_reports_only_recorded_plan_and_subscription_state` creates disposable SQL Tenant A+B, a Draft then Published version, one Pending and two Settled orders, one active current and one expired historical subscription. It checks all four counters, that Master is excluded, and that cancellation changes only the active-period count while Settled order history is preserved.

### Angular integration

Companion Angular dashboard panel uses these four fields **only when they exist** on server response, to remain compatible during a staged backend/frontend deployment. It explicitly labels them as operational stored-state counters, and never renders a fabricated zero if the older backend does not yet support these fields.

This is a dashboard enhancement, **not** the first real source-owned Commercial business module. Such a module needs an explicitly chosen business Domain/EndpointGroup with true source-owned contracts, central System approval and full OpenIddict/BFF HTTP E2E before a paid module can be activated.
