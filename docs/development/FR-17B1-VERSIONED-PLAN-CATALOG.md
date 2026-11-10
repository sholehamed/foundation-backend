# FR-17B-1 — Persistent versioned Plan Catalog and read-only inheritance projection

**State:** PR #128 implementation awaiting CI verification; schema + reader only. **Date:** 2026-11-10. **Backend:** Foundation / Identity. **Related:** [Modular entitlement architecture](../architecture/modular-entitlements-plans-limits.md), [FR-17A-2 typed contracts](FR-17A2-TYPED-RESOURCE-CONTRACTS.md).

## Scope and important boundary

The owner requires **fully DB-driven plan cards**, parent plans, selectable modules/typed benefits, and pricing that can vary independently of inherited module lists. A plan purchase should later entitle the Tenant to a bounded Module/Resource/Permission catalog, but **only a separate Tenant-local role grant lets a user execute an operation**. A voucher/discount will settle an Order, not inject RolePermissions.

This slice persists a System-owned Plan Catalog. It does **not** register an API or frontend plan editor; there is no Subscription, checkout, payment, coupon, active Tenant entitlement, quota enforcement, dynamic tenant menu activation, plan seeding, or commercial role grant.

## Schema (migration `20261110230000_versionedplancatalog`)

Seven **empty** SQL Server tables are created by a standard EF Core Migration. Naming intentionally prefixes `Catalog` to distinguish plan content from the later operational Billing/Subscriptions. All records are globally System-owned, using the project audit/rowversion conventions, without TenantId or tenant-scoped query filters.

| Table | Purpose | Key relationships/constraints |
| --- | --- | --- |
| `CatalogPlans` | Stable identity, code, name, optional parent plan, active state | Unique Code, nullable restricted ParentPlanId, no direct self-parent |
| `CatalogPlanVersions` | Immutable-by-policy publication revision and pinned parent *version* | Unique PlanId+VersionNumber; restricted ParentPlanVersionId; Draft/Published/Retired status; PublishedAt required when Published; fingerprint column |
| `CatalogPlanVersionModules` | Incremental modules for one revision | Unique PlanVersionId+ModuleId; FK to existing Modules |
| `CatalogCapabilityDefinitions` | Code-owned typed feature, quota, limit or allowed mode descriptor | Unique Code; ModuleId and optional ResourceId FKs; Kind/ValueKind/MergePolicy constrained strings; Title/Unit |
| `CatalogPlanVersionCapabilityValues` | Per-revision typed allowance or override | Unique PlanVersionId+CapabilityDefinitionId; SQL CHECK: **exactly one** of BooleanValue, IntegerValue, ModeSetJson, IsUnlimited; nonnegative integer |
| `CatalogPlanPriceOffers` | Independent amount/currency/billing cycle for one revision | Unique PlanVersionId+BillingCycle+Currency; nonnegative decimal(19,2), UTC sale interval |
| `CatalogPlanDisplayItems` | Ordered, localized-ready card content | Kind Module/Capability/Benefit, required matching FK only for the first two, Title/Description/Order/Highlight |

All new links use Restrict on delete. No Module/Resource/Permission/Role/Tenant/TenantModules rows are updated. The migration is **additive** and may be rolled back only if these new tables can safely be removed; do not run Down after populating real commercial contracts without approval.

**Important:** A database CHECK cannot guarantee same-Module ownership of CapabilityDefinition.ResourceId, no cross-Plan cycle, or that a displayed Module was purchased. The read resolver validates these; future *publication command* must validate them transactionally, including immutable published content, before plan editors can write. The schema is not equivalent to a trusted publisher.

**Money:** Amount is expressed in the configured `Currency` currency unit, **IRR != Toman**. Checkout will require official currency exponent, VAT, tax and rounding policy, quote/invoice snapshots and idempotency. Price precision (19,2) is provisionally suitable for display, not a greenlight for financial settlement.

## Source and tested inheritance semantics

Read-only service: `PublishedPlanCatalogReader.ReadAsync(planVersionId, codeResources, codeCapabilities, nowUtc)`.

1. Inputs include explicit **source-owned, validated commercial Resource/Capability contracts**. Database-created arbitrary features do not become executable behavior; unknown modules or `system`/`tenant-workspace` core modules are rejected from commercial plans.
2. Walk `ParentPlanVersionId` (not live `CatalogPlan.ParentPlanId`) with cycle/depth guard, requiring published (or historically retired, pinned parent) revisions with published metadata and 64-digit hex fingerprint. The selected version itself must be Published and Plan active.
3. Union included modules across all pinned ancestor versions. Each module must exist, be active and be classified **Commercial** by code catalog. **No inherited module removal in v1**.
4. Check each capability against source definitions: Code, Module, Resource owner, Kind, ValueKind and MergePolicy. Child values compose with the parent using type-specific policies: `Override`, `Additive`, `Maximum`, `SetUnion`, `DenyWins` (boolean); Unlimited is explicit, not zero/null.
5. Return **only the selected published version's active and time-valid offers**, never a sum of parent price offers. BillingCycle, Currency and Amount remain server-owned.
6. Validate that `Module` and `Capability` display items reference an actually resolved entitlement. A `Benefit` is presentation-only and cannot open an API permission. Sort items by DisplayOrder.
7. The returned projection contains PlanCode, PlanVersionId, display copy/badge/order/recommended, resolved ModuleCodes, typed effective capability values, offers and display rows; it has no RolePermission or Subscription ID and performs **zero writes**.

Example: Plan Starter v1 has Appointments and 100/month, Plan Pro v1 pins Starter v1 and adds Staff plus 25/month; Pro resolves 125/month using Additive. Publishing Starter v2 with 10,000/month **does not change** the existing Pro v1.

## Existing behavior retained

- `PublicSignup` still treats `RequestedPlanKey`, `RequestedBillingCycle` and `PromoCode` as **intent only**. No product is bought or enabled at signup.
- System SuperAdmin initial 13 menu roots and Tenant Owner 17 admin permissions / six menus remain unchanged.
- Existing `TenantModules` remains **legacy flags, not a subscription source of truth**.
- No `RolePermissions`, `UserPermissions`, `Menus` or permission seed are changed. Core account/recovery/Billing access remains independent of commercial status.
- `ModuleResourceContracts` remains the code-owned source validator; adding real commercial Modules should occur through reviewed manifest/Module registry work, not untrusted DB text.

## Tests / CI

`tests/Shinera.Identity.SqlServerTests/PlanCatalogSqlServerTests.cs` runs only against a disposable, random-named local SQL Server DB. The existing workflow `.github/workflows/bff-ci.yml` includes this suite in its SQL Server job.

Acceptance:
- Real `Database.MigrateAsync` creates all seven tables with FK/index/SQL CHECK definitions.
- Parent Starter v1 → Child Pro v1 inheritance and typed monthly quota, active employee Limit and recurring Feature are resolved correctly.
- Child price and card labels derive from DB, without frontend constants, and a marketing Benefit cannot grant privileges.
- Parent v2 publication does not mutate or re-resolve Child v1 against v2.
- Unpublished or noncommercial modules are rejected; invalid negative amount and orphan capability FK are rejected by SQL.
- No RolePermission, UserPermission or TenantModules grants are created as side effects.

The CI result must be checked for the **last PR head commit**, not a previous push, before merging. A CI ephemeral DB is not evidence of deployed production SQL readiness.

## Important remaining work / rollout

**FR-17B-2 (next):** audited System Admin draft editor + reviewed `Publish` transaction, tree cycle detection, child-parent version pin consistency, immutable published content (enforced at write boundary and with targeted SQL integrity), deterministic canonical content fingerprint, subscription/consumer-safe public PlanCards API, localizations and public rate limiting. Separate lock/transaction and published stale-edit tests.

**FR-17C:** subscription source of truth + typed EffectiveEntitlementService in OFF/shadow mode with cross-Tenant tests; later join to user Permission evaluator. Old `TenantModules` cannot bypass paid limits.

**FR-17D/E and later:** quota usage ledger/concurrent limit enforcement, workflow policy/Outbox, purchase/discount/zero-total settlement and actual payment adapter, with tenant UI + System admin pages, verified callbacks, and explicit cancellation/renewal/downgrade semantics.

Product decisions D-01..D-10 (see [architecture](../architecture/modular-entitlements-plans-limits.md#11-explicit-decisions-to-settle-before-codemigrations)) remain proposals, not automatically accepted by a passing migration.
