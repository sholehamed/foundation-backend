# Foundation — Modular Entitlements, Plans, Limits and Workflow Extensions

**Status:** Proposed design baseline; **not implemented**. **Date:** 2026-11-10. **Owner:** Foundation architecture. **Tracking:** FR-17A onwards. This is the canonical cross-cutting design for future PRs; no release or migration is authorized by this document.

## 0. Product intention / intended semantics

Foundation is a reusable, tenant-aware software platform. A plan sells a **bounded set of capabilities**, not a collection of user roles. A tenant acquires modules/capabilities through a paid, discounted, complimentary, or externally settled subscription; its administrators can then delegate **only a subset** of those acquired permissions to internal roles/users. Workflow behavior, quotas and user interface should follow the same effective entitlement without duplicated versions of business workflows.

Definitions and navigation are centrally governed by **System Admin**; a Tenant Owner never authors global Modules, Resources, Permission definitions, API/UI descriptors, Menu definitions, Plans or Features. Tenant admins may choose role/user grants only from their eligible catalog. No entitlement should silently grant a role to all tenant users. System/Identity/OIDC/security/bootstrap functions are platform control-plane functions, not saleable customer product features.

## 1. Grounded current state (main at 5f88e137, 2026-11-10)

- `Module` (globally keyed `Code`) contains `Resource` (unique by `ModuleId, Code`); **Resource is logically a .NET Controller/Minimal API EndpointGroup**, NOT a business entity, page or generic noun. A single Resource owns permission definitions, and an API group may expose many mapped endpoints.
- `Permission` has `Code` and optional `ResourceId`; `ApiResource` is an individual method/route/endpoint descriptor and `UiResource` is PAGE/ACTION/FIELD/WIDGET. The Permission-to-API/UI links are stored in `PermissionApiResources` and `PermissionUiResources`. `Menu` references `PermissionId`, category/parent and route. DB assignments are `RolePermissions`, `UserRoles`, `UserPermissions`, groups. System grants live in stable Master Tenant scope.
- Current System registry uses resource codes like `system/users`, `system/roles`. Current Tenant workspace puts 17 PAGE/ACTION permissions under one `tenant-workspace/tenant-administration` resource; that **does not yet reflect a one-EndpointGroup-per-Resource intent**. It needs an inventory and explicit migration, not rename-in-place.
- `TenantModules` has TenantId, ModuleId, IsEnabled, CustomExpiryDate, IsTrial; this is **not** yet a persisted subscription/entitlement source of truth.
- `BusinessProfile.RequestedPlanKey`, `RequestedBillingCycle` and `PromoCode` are **registration intent only**. `PublicSignup.Register` creates a real Tenant, Owner role and 17 core Tenant admin permissions but explicitly confers **no paid subscription**. Do not conflate “Tenant exists” or “Owner has administrative permissions” with “purchased commercial modules.”
- Current `EffectivePermissionEvaluator` checks user, tenant, scoped role/group/user grants and direct deny. It **does not** validate commercial subscriptions/modules/limits; `PermissionApiResource` mapping does not by itself enforce policies. Existing System granular endpoint enforcement remains gated and Tenant dynamic menu rollout remains separately gated.
- FR-16 first-install Seed only applies to fresh catalog; historical DB reconciliation must use reviewed, fingerprint-locked, additive processes with backups. Never “fix” deployed databases by deleting and re-seeding.

## 2. Canonical vocabulary and ownership

| Term | Meaning / owner | Runtime role |
| --- | --- | --- |
| Platform Module | Code-owned sellable product boundary, contains grouped Resources; System-owned catalog | Base entitlement unit, with optional finer capabilities |
| Resource | Controller/EndpointGroup-level logical grouping in a Module; System-owned | Scope for Permission definitions and endpoint metadata |
| Permission | A stable operation contract under a Resource, e.g. `APPOINTMENTS.CREATE`; System-owned | Tenant-admin-delegable security capability |
| ApiResource | One endpoint key + method/route under Resource; System-owned | Metadata/endpoint authorization binding, not a grant |
| UiResource | PAGE/ACTION/FIELD/WIDGET keyed element under Resource; System-owned | Visibility/UX hint using resolved access, not API security |
| Menu | Central route/category/tree, referencing defined permissions; System-owned | Filtered navigation projection |
| Capability Definition | Typed entitlement parameter: Enabled, Limit, Quota or Mode; code-defined with catalog projection | Decision input for behavior and metering |
| Plan / Plan Version | Commercial offering; stable plan identity and immutable published revision | Defines module set and typed capability values |
| Subscription | Tenant's time-bound commercial agreement referring to an immutable version | Validity/term status, not a Permission grant |
| Add-on / Override | Additional explicit, time-bound authorized entitlement source | Controlled composition with base subscription |
| Entitlement | Effective license/availability for this Tenant, module, feature, quota and mode | First runtime gate (not a user grant) |
| User authorization | Effective scoped role/group/direct-user Permission; direct deny has precedence | Second runtime gate |
| Tenant Configuration | Business choice *within* entitled modes, e.g. optional vs mandatory deposit | Determines behavior without changing plan |
| Usage ledger | Durable consumption/reservation/reversal events in a billing period | Enforces quota, never inferred from UI |
| Discount / Redemption | Checkout pricing reduction and audit of uses | Affects order amount only; never directly grants entitlement |

**Permission layering:** A Permission is *declared under* a Module via Resource; the Module/Plan makes it **eligible** for the Tenant. The Tenant Owner can assign an eligible Permission to users/roles. There is still a scoped `RolePermission` link to represent the owner's delegation; Module membership must not be mistaken for a blanket UserPermission.

## 3. Authorization and entitlement invariant

For each protected tenant operation, accept only if all conditions hold:

`Allowed = ValidPrincipalAndTenantScope AND ActiveCodeOwnedModuleResourcePermission AND ValidTenantEntitlement(Module, capability context) AND EffectiveUserGrant(Permission) AND !DirectDeny AND BusinessPolicy AND WithinLimit/Quota`

- Root platform/System Administrator access to System control-plane is independently guarded, and not billed via a customer plan. Never allow System permissions through a customer plan.
- Billing access (e.g. purchase/renewal, own subscription status, account recovery) cannot be locked behind a paid entitlement; maintain a narrowly specified administrative bootstrap/core access allowlist. Existing 17 first-signup Owner admin grants are **administrative role assignments**, not purchased features. Separate control-plane Tenant permissions from saleable product resources before enabling entitlement enforcement.
- No access through direct HTTP if the module is off, even if a stale `RolePermission` remains; menu/UiAccess should use the same effective gate. Frontend-only hiding or a 200 navigation response is not authorization.
- Active subscription is necessary but not sufficient; role assignment, direct deny, scope, feature enabled, resource active and applicable limits remain authoritative.
- Expiry/downgrade does **not** delete historical role assignments or business data. It suspends their *effective* use; exact re-upgrade semantics must be tested and visible to administrators, not an automatic accidental backdoor.
- For code-owned endpoints: explicit stable metadata (`ModuleCode, ResourceCode, PermissionCode`) must be validated. Route paths are mutable metadata, never authorization identities. Avoid a generic URL-based permission middleware.
- A module may be commercially entitled while not deployed/installed: **deny** and surface an operator diagnostic; do not fabricate an endpoint or mark purchase fulfilled until product readiness is established.

## 4. Capability and limit model

Recommended data contract (proposed): `CapabilityDefinition(Code, ModuleCode, Kind, ValueType, Unit, PeriodKind?, AggregationPolicy, DefaultDenyValue, ValidationRule, UiMetadata)`. Keys are immutable and code-owned; display title/help/ordering may be database-managed. No executable expressions, C# types, URLs, SQL or scripts from untrusted DB rows. System Admin may configure **values** for approved capability definitions, not invent runtime logic.

| Kind | Example | Semantics |
| --- | --- | --- |
| Enabled | `appointments.recurring = true` | Boolean opt-in feature |
| CountLimit | `staff.active.max = 5` | Max concurrent qualifying rows (based on authoritative status predicate) |
| PeriodQuota | `sms.sent.monthly = 500` | Allocated consumption per defined billing/civil period |
| Mode / AllowedModes | `appointments.confirmation = [manual, auto]` | Enabled choices; Tenant selects one available business mode |
| DisplayOnlyBenefit | `support.priority` | Marketing/support promise; NOT automatically a security permission |

- **Unlimited** is an explicit tagged value, not `0`, `-1`, or null. `0` means zero allowance. Missing/invalid is fail-closed unless explicitly marked free/core.
- Each CountLimit must define exact count predicate: e.g. `staff.active.max` counts active members only; define suspended/invited/pending states and who owns them. Each quota must define period start, time zone policy, usage unit, rounding, event id and reversals.
- Race safety: count-limit mutation checks and write are coordinated via SQL transaction + appropriate serialization/lock/CAS for a tenant+key; metered quotas reserve/deduct atomically using idempotency key and usage ledger (unique Tenant, capability, operation). A Redis counter alone is not authoritative.
- Quota periods: use UTC instants and explicit billing cycle anchor; DST-safe boundaries calculated once. Changing a subscription midperiod requires a documented prorate/carryover/replace policy, not an implicit reset.
- Hard cap vs soft warning must be **typed** and server-enforced; warning-only limits must never be mislabeled as enforced product entitlements.
- Read endpoints may show current usage and availability but cannot grant it. Responses can include `currentUsage`, `limit`, `remaining`, `periodEndUtc`, `source` without exposing other Tenants.

## 5. Workflow composition: avoid double/triple implementation

Choose extension mechanism by the business consistency boundary, **not** by which Module is enabled:

1. **Preconditions / validation / policy:** synchronous rule injected into core flow (e.g. mandatory deposit before confirmation, booking rules, capacity). Strategy/Policy keyed by vetted capability and tenant-selected mode. Evaluate effective entitlement per invocation. Explicit fail-closed behavior for unsupported policy combinations.
2. **In-transaction domain effect:** deterministic same-transaction core state transitions with idempotency and no network I/O. Use narrow interfaces; don't let optional modules reach into unrelated aggregate tables directly.
3. **After-commit optional side effect:** domain/integration events with transactional Outbox, at-least-once delivery, idempotent consumers, retry/dead-letter (e.g. SMS notification, analytics).
4. **Cross-module long-running required workflow:** explicit persisted orchestration/state machine/saga with compensation and pending/failed states (e.g. mandatory deposit awaiting verified payment). Do **not** confirm booking then hope a notification-style event will collect required money.

Example: `AppointmentCreate` validates tenant entitlement + `APPOINTMENTS.CREATE` + booking limit, saves `PendingPayment` if deposit is mandatory, otherwise `Confirmed`. Payment settlement callback from trusted server moves pending to confirmed exactly once; optional SMS reacts to `AppointmentConfirmed` via Outbox only if SMS capability is active at send time per policy. A disabled SMS module must not block booking. Billing payment gateway for **Foundation subscription** is distinct from a customer-facing salon deposit payment feature.

Tenant configuration: `RequireDeposit` can only be set when `deposit` feature is entitled; effective behavior = entitled feature ∩ allowed modes ∩ valid tenant configuration ∩ business state. Turning off the feature must define behavior of pending transactions explicitly.

## 6. Plan inheritance, versions, dynamic display

- `Plan(Code, ParentPlanId?, DisplaySettings)` is a stable catalog identity. Published `PlanVersion(PlanId, VersionNumber, ParentPlanVersionId?, Status, PublishedAtUtc, ContentFingerprint)` is immutable. A child published revision resolves against a **specific published parent revision** (no moving target). Parent graph must be acyclic and bounded; no self-parent; prevent duplicate stable plan codes and conflicting parent snapshots.
- A published revision has `PlanVersionModules` and `PlanVersionCapabilityValues`. Initial inheritance is **additive for modules** and **explicit typed override for features/limits/modes**. No implicit removal of inherited module in the first release. Effective materialized snapshot: distinct union of modules; children override keys only where the definition permits (e.g. mode subset rules); validate capability belongs to an included module.
- Values must not silently reference a Module disabled or missing from code registry. Descendant updates create new PlanVersion; existing subscriptions retain their original version/snapshot.
- Pricing (`PlanPriceOffers`) is separate from entitlement definition, with currency, billing cycle, effective interval and immutable price quote captured on `Order`; child price is **not** sum of parent prices. All calculations are server-side in a canonical minor-unit money model or reviewed DECIMAL-by-currency approach; for IRR explicit zero fractional minor units. Define rounding, VAT and invoice jurisdiction before implementation.
- The public Angular Plan Card is **data-driven**, not dynamic executable UI. Proposed `GET /api/public/plans` returns published saleable offers: plan/version key, display title/subtitle/badge/theme/order, pricing/cycle/currency, and an ordered array of display items. Distinguish `Module-backed`, `Capability-backed`, `Limit-backed` and `Marketing-only` items; never claim an entitlement in a card unless the published snapshot supports it. Admin can configure text/order/badge and associate to existing vetted definitions, not inject HTML, script or a runtime endpoint.
- Product comparison should display inherited resolved values, optionally highlight “added relative to parent” by diffing snapshots. Do not make frontend recursively resolve inheritance or compute entitlement.

## 7. Proposed persistence boundaries (no migrations in this design PR)

Existing Identity tables remain: `Modules`, `Resources`, `Permissions`, `ApiResources`, `UiResources`, `PermissionApiResources`, `PermissionUiResources`, `Roles`, `RolePermissions`, `UserRoles`, `UserPermissions`, `Menus`, `MenuCategories`, `Tenants`, `TenantModules`, `BusinessProfiles`.

New conceptual tables (final names/indexes to be reviewed before EF migration):

| Aggregate / table | Core fields / constraints |
| --- | --- |
| `CapabilityDefinitions` | unique Code; ModuleId; Kind; ValueType; units, aggregation; IsActive; immutable definition contract |
| `Plans` | unique Code; nullable ParentPlanId; status; title; platform-owned |
| `PlanVersions` | unique (PlanId, Version); ParentPlanVersionId; status; snapshot fingerprint; immutable after publish |
| `PlanVersionModules` | unique (PlanVersionId, ModuleId); explicit membership |
| `PlanVersionCapabilityValues` | unique (PlanVersionId, CapabilityId); typed value + allowed modes |
| `PlanDisplayItems` | PlanVersionId, sort, display kind, referenced Module/Capability optional; localized copy |
| `PlanPriceOffers` | PlanVersionId, BillingCycle, Currency, Amount, SalesWindow, status |
| `TenantSubscriptions` | TenantId, PlanVersionId, status, StartUtc, EndUtc exclusive, renewal/transition refs and RowVersion |
| `SubscriptionAddOns` | SubscriptionId, product/version/module/values, period, state; composable validated entitlements |
| `EntitlementOverrides` | approved grant/source/actor/reason/audit, time-bounded scope; never arbitrary user grants |
| `UsageLedger` + `UsageBalances` | tenant+capability+period+operation unique; reservation/commit/reversal, concurrency-safe balance |
| `Orders` + `OrderLines` | tenant, offer/version quote, pricing/tax/discount snapshot, idempotency and checkout expiry |
| `DiscountCodes` + `DiscountCodeTargets` | hashed/normalized code identifier, type/fixed/percent, validity, global/per-tenant uses, plan restrictions |
| `DiscountRedemptions` | unique order/redemption key; Reserved/Committed/Released, tenant, invoice, actor, timestamps |
| `Invoices` + `PaymentAttempts` + `SettlementRecords` | amount/currency, gateway proof or approved off-platform settlement, reconciliation and audit |

All customer-owned writes must be tenant-isolated and audited. Unique constraints on stable code, plan version and idempotency are mandatory. Prefer controlled schema namespaces (`billing` / `entitlements`) after inspecting base EF migrations; do not create a new competing tenant identity store.

**Source-of-truth decision:** subscription + published snapshot + add-ons + approved overrides + current clock determine Entitlement. `TenantModules` is legacy state to be inventoried; during migration it may become a read-only materialized projection/cache or be retired by a reviewed migration. Never treat independent `TenantModules.IsEnabled` as a bypass to commercial access. No dual-write entitlement truth.

## 8. Subscription and checkout consistency

State sketch: `PendingPayment → Active → Grace? → Suspended/Expired/Canceled` with explicit, audited transitions, interval validity `[StartUtc, EndUtc)`, and no paid access for PendingPayment. Grace is **opt-in by business policy** with an exact list of allowed operations, not an implicit Active. Renew/upgrade/downgrade preserve old versions and historical events. Define proration and leftover quota before implementing those paths.

Checkout flow:
1. Create an idempotent, expiring Order for a published saleable Offer and snapshot prices; never trust Angular money/limit inputs.
2. Validate discount type, target, Start/End UTC, global/per-tenant caps and customer restrictions on the server. Percentage range 0..100; fixed not below 0; clamp net charge at 0; do not allow negative totals. Redemptions reserved/committed exactly once under SQL concurrency constraints, released after unsuccessful checkout per policy.
3. If final amount = 0, settle Order through **NoGatewayZeroAmount** transaction (still record price/discount/invoice evidence) and activate subscription via one idempotent Fulfillment handler.
4. If amount > 0, activate only after **verified** gateway callback or authenticated, audited **manual external settlement** approval with amount/currency/reference. Client redirect alone is not settlement. Handle duplicate/out-of-order callbacks and refund/chargeback with explicit lifecycle rules.
5. **Real 100% discount** means no money received; **external money received** is a separately recorded Settlement source. Do not falsely book full revenue for a fake 100% coupon. The UI can use a one-time 100% code as the convenience route for an offline customer, but financial reporting must distinguish gift/complimentary vs manually collected funds. A discount code by itself never proves a transfer took place.
6. Order, redemption and subscription activation must be transactionally coordinated with retries/idempotency. For external gateway, use verified callback inbox + Outbox to prevent duplicate activation; never depend on a distributed database transaction.

## 9. Effective entitlement computation and composition

Proposed `IEffectiveEntitlementService` returns typed `TenantEntitlementSnapshot` including source references and expiry, plus methods `HasModuleAsync`, `IsFeatureEnabledAsync`, `GetLimitAsync`, `GetAllowedModesAsync`. No API accepts arbitrary tenantId from an untrusted tenant caller; bind it from trusted tenant context. System operator reads require separate audited policies.

Composition order **proposal**: immutable base PlanVersion snapshot → time-valid add-on entitlements → narrowly approved time-valid System overrides → tenant business configuration intersects allowed choices. No user role/group grant can widen a commercial entitlement. At overlapping sources, each CapabilityDefinition declares `Override`, `Sum`, `Max`, `SetUnion` or `DenyWins` *with compatibility rules*; never silently choose a higher limit by generic merge. Track source provenance for diagnostics. Out-of-contract conflicting types deny with integrity status.

Cache effective snapshots only with bounded TTL and explicit invalidation on subscription/entitlement change; sensitive authorization checks require a defined staleness ceiling. Do not cache a previously granted authorization past expiration or explicit disablement. DB remains authoritative. Use a tenant-scoped revision/fingerprint and UTC expiry boundary; invalidation must be tested across app instances.

Tenant-grant editor should display both (a) all centrally registered tenant permissions, (b) currently entitled grantable permissions, plus dormant historic grants as read-only explanatory items. Reject assigning an unentitled Permission even if code exists, and revalidate on save inside transaction.

## 10. Operational security and acceptance

- Roles: System Administrator is sole writer of global catalog and commercial plan/discount/override definitions; Tenant Owner edits only delegated local grants and business preferences; purchaser/renewal endpoints receive separate policy. No privilege escalation via purchase, coupon or forged ModuleCode.
- Existing System control-plane behavior must remain unaffected. Do not turn on `Identity:PlatformPermissions:EnforcePermissionEndpoints`, Tenant dynamic navigation flag, or Billing/Entitlement enforcement just because code/docs are merged.
- Rollout: inventory real SQL including existing `TenantModules` and RolePermissions; add schema-only migrations; code-defined catalogs with preview/fingerprint and reviewed Apply; shadow-only entitlement evaluation; positive and adversarial SQL tests; two-Tenant HTTP/BFF/Chromium acceptance; optional tenant test cohort; controlled flag; operator rollback. Never seed a free commercial subscription for existing Tenants implicitly.
- Tests: closed/partial/disabled modules and missing catalog fail closed; exact Owner core access before payment; System features unaffected; purchased module but no role denies; role but no plan denies; cross-tenant grants never work; expired subscription revokes effective access without deleting grants; limit boundary and concurrent double spend; trial expiry; parent cycles/conflicting overrides; version immutability; zero-price coupon capacity race; callback replay; externally settled audit; non-entitled UI/menu hidden but account/Billing recovery visible; backward compatible old DB.
- No automatic activation of subscription by `PublicSignup`, no auto-upgrade of existing DB; backup, migration review and reproducible rollback gates required.

## 11. Explicit decisions to settle before code/migrations

| ID | Question | Safe provisional behavior |
| --- | --- | --- |
| D-01 | Entire Module vs selected Permissions/Features sold? | Module baseline plus typed per-feature flags/limits; do not sell arbitrary Permission IDs |
| D-02 | Owner access when subscription absent? | Core Tenant administration/purchase/recovery only; commercial routes denied |
| D-03 | After downgrade then re-upgrade, do dormant role assignments revive? | Preserve data; require explicit documented revival policy and UI notice |
| D-04 | If a module is in two entitlement sources, how combine? | Typed per-capability aggregation; deny unregistered/conflicting rules |
| D-05 | Definition of “active employee” and quota period boundary? | Per-feature declared semantics and SQL tests |
| D-06 | Grace period and past-due behavior? | No grace by default |
| D-07 | Parent inheritance override/remove semantics? | Version-pinned additive modules; approved typed values only |
| D-08 | Price, tax, IRR representation, refunds/proration? | No financial migrations until billing/finance review |
| D-09 | Offline/complimentary coupon accounting? | Separate discount from verified external settlement |
| D-10 | Cross-product Module deployment and tenant branches? | No branch license overrides in v1; explicit phase later |

## 12. First executable vertical and exit criteria

Implement **FR-17A catalog mapping first**, without billing runtime:
- Inventory all API EndpointGroups + existing `Resource` rows/permissions, document exceptions and safe migration strategy for `tenant-administration`. Preserve stable Code/ID/grants until reviewed mapping; no destructive migration.
- Define strongly typed metadata contracts (ModuleCode, ResourceCode, PermissionCode and CapabilityDefinition) and validator. Build read-only Preview/diagnostic + deterministic tests. Maintain central System-only write ownership.
- Draft FR-17B typed entitlement schema and seed fixtures only after validating how owner core permissions, missing subscription and existing TenantModules coexist.
- First runnable scenario: a new Tenant has valid core admin access without paid subscription; purchased Appointments module + delegated Create permission allows one appointment; unentitled role cannot call endpoint; quota 1 rejects the second concurrent create; optional notification does not duplicate booking logic.
- Separate subsequent FR slices cover immutable plan versions + public plan cards, subscription entitlement runtime, quota/workflow policy, zero-price discount/offline checkout, payment gateway, UI/rollout.

**Cross-doc links:** [Feature Registry & Authorization](feature-registry-authorization.md), [FR-17A execution and verification checklist](../development/FR-17A-MODULAR-ENTITLEMENTS-HANDOFF.md), [FR-16C first-login seed](../development/FR-16C-FIRST-LOGIN-ADMIN-READY.md).
