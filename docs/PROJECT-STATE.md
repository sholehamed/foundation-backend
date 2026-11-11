## FR-17C-3C — Module-owned DbContext transactional adapter and signed HTTP acceptance (2026-11-11)

- Added `TenantCommercialOperationExecutor.ExecuteModuleAsync` with `CommercialModuleSqlUnitOfWork(SqlConnection,SqlTransaction)`, `CommercialModuleCountRequirement` and source-owned domain callback. Commercial modules no longer need to place their entity model or active-count query in `IdentityDbContext`; independent EF Core module contexts enlist via `UseTransactionAsync` on the **same SQL Server connection/Serializable transaction**. Identity retains license/Permission/capability checks, app locks, quota ledger, and sole Commit ownership. Existing internal `ExecuteAsync` remains supported.
- Real disposable SQL fixture tests independent module `DbContext` commit, replay, post-flush rollback, domain CountLimit denial. Isolated test-assembly ASP.NET `TestServer` verifies cryptographically signed JWT 401 vs 403, licensed/granted happy path, forged body TenantId isolation, unlicensed/grant and no-grant cases, replay/exhaustion. **This is not production OpenIddict/BFF E2E** and no Commercial endpoint, catalog seed, migrations or modules are added to production. [Security, adoption and test guide](development/FR-17C3C-MODULE-UOW-SIGNED-HTTP.md).
- Next: first actual code-owned Commercial Domain+EndpointGroup, genuine Web API JWT/Angular browser tests; source-specific CountLimit display adapter, Outbox, renewal/carryover. CI success, main merge and actual deployed Staging are distinct release milestones.

## FR-17C-3B — Atomic source-owned Commercial Operation Executor (2026-11-11)

- Added opt-in `TenantCommercialOperationExecutor`: one SQL Server Serializable transaction contains real signed writable Tenant scope, active deployed Commercial Module/Resource/Permission, live delegated user grant and verified subscription/typed Features/Modes, exact operation-bound CountLimit/PeriodQuota adapters, trusted subscription-anchored UTC period, SQL application locks + quota ledger, and the actual module domain write + `SaveChangesAsync`. Only SQL commit returns success. No bound metered capability means no usage ceiling, **not** a license/permission bypass; required missing allowance denies. Duplicate committed quota key rejects before business mutation. Any failing limit/domain mutation rolls back all charges and writes.
- Existing `CommercialOperationPreflightSqlTests` extended with **test-only Commercial module** and disposable SQL table for two-Tenant license/grant checks, replay/conflict, simulated exception rollback, finite quota+active CountLimit concurrent contention, no-meter allowed mode and unknown adapters. No real saleable Module, migrations, roles, permissions, API cutover or frontend changes. [Implementation/security handoff](development/FR-17C3B-TRANSACTIONAL-COMMERCIAL-EXECUTOR.md). CI and Staging are distinct validation stages; required PR-head and main SQL CI must be observed before calling this shipped.
- Next: real business Domain+EndpointGroup adapter with signed HTTP E2E when first commercial module exists; trusted Outbox/saga for external effects; real CountLimit usage provider/read API, and renewal/carryover semantics. No client-side write authorization.

## FR-17D-2B — Source-defined quota periods and owner-only usage projection (2026-11-11)

- Added `CapabilityContract.QuotaPeriod` optional source-owned `SubscriptionTerm/MonthlyAnniversary/YearlyAnniversary` cadence and `QuotaPeriodResolver` UTC anchored intervals (end-of-month/leap safe). Existing declarations without cadence remain compatible, but their consumption is **unmeasurable**, not silently monthly/unlimited.
- Added read-only `TenantUsageProjectionReader` and Owner-scoped `GET /api/tenant/workspace/subscription/usage` (no TenantId parameter, Cache-Control no-store), requiring verified settled Subscription/current PlanVersion and trusted commercial source manifest. PeriodQuota reads SQL ledger balances and exact period; CountLimit reports domain count unavailable until its adapter exists. Missing periods and mismatched SQL window fail explicitly. No grants, usage writes, fake commercial Modules, paid middleware or migrations.
- Unit UTC/anniversary tests and three-Tenant disposable SQL tests (actual A/B consumption and C unlicensed), required CI filter, [implementation and API handoff](development/FR-17D2B-SUBSCRIPTION-USAGE-PROJECTION.md). Next Angular true usage component with owner-only 403 handling and FR-17C-3B source-bound transaction executor/real HTTP. Verify PR-head/main CI independently.

## FR-17C-3A — Composed Tenant Commercial Operation preflight (2026-11-11)

- Added read-only `TenantCommercialOperationPreflight`: checks code-owned Commercial Module/Resource/Permission/operation ownership against live SQL, writable scoped Tenant identity, delegated `EffectivePermissionEvaluator` grant/direct-deny, verified same-Tenant settled Subscription and typed required Features/Modes. Required but missing PlanVersion Capability denies. Operations with CountLimit/PeriodQuota return **MeteringRequired**, never a false allow without an authoritative atomic usage check. Operations without a usage ceiling return **Ready** only after entitlement+permission+feature gate; this does not enable any HTTP endpoint or bypass business policies.
- Disposable SQL two-Tenant tests: licensed/no grant, unlicensed/with grant, direct deny, expired subscription, allowed modes, metering requirement and no production Commercial Module. Existing System/Tenant control plane and Angular unchanged; no seed, migration or commercial deployment. [Technical contract/next acceptance](development/FR-17C3A-COMMERCIAL-OPERATION-PREFLIGHT.md). Must verify PR-head and main CI separately before claiming green.
- Next: FR-17C-3B code-owned business operation adapter with same-transaction all-meter+domain writes and authenticated HTTP tests; FR-17D-2B trusted quota period, tenant-scoped usage API and reusable Angular usage widget.

## FR-17E-1 — Coupon issuance and zero-total settlement (2026-11-11)

- [Backend PR #138](https://github.com/sholehamed/foundation-backend/pull/138): new System-owned Coupon definitions (high entropy one-time secret with SHA256 fingerprint at rest, percent/fixed IRR, UTC window, exact optional pinned PlanVersion, max global/per-Tenant settled uses, disable). Scoped CouponRedemption ledger records only confirmed settlements. Existing Owner PlanOrder POST accepts optional code; zero Net triggers **one atomic transaction** to insert Settled Order, redemption and Active Subscription without gateway, and positive Net stays Pending with coupon use consumed **only at verified external manual settlement**. Entitlement Shadow requires a matching settled redemption for discounted Orders. Code-owned System `PLAN-CATALOG.COUPONS` permission and strict Master authorization added. [Full SQL, API and security handoff](development/FR-17E1-CATALOG-COUPONS-ATOMIC-REDEMPTION.md).
- Companion [Angular PR #65](https://github.com/sholehamed/foundation-frontend/pull/65) adds coupon issue/list/disable and optional Tenant coupon entry. SQL tests verify 100% zero, one redemption, per-tenant/global cap, partial deferred redemption, no unauthorized grants. This is **not** an automated payment gateway or an entitlement-to-Permission enforcement rollout.
- **Fail-closed remains mandatory:** no production vetted Commercial Module is registered; real sales still cannot publish/activate from fake DB content. Existing DB Master grants require reviewed Registry/RolePermission reconciliation. CI and Staging verified independently, not inferred.

## FR-17D-2A — Atomic tenant Usage SQL and CountLimit guard (2026-11-11)

- Additive raw-SQL EF migration creates `TenantUsageBalances` (Tenant+Capability+Period) and append-only `TenantUsageEvents` (Consume/Reverse; unique idempotency per event kind). `TenantAtomicUsageService` requires writable signed Tenant context and caller-owned SQL Server Serializable transaction; transaction-owned `sp_getapplock` serializes concurrent quota consumption and CountLimit authoritative domain callbacks. Consumed usage and domain write MUST commit together; rollback reverts both. Zero/finite/Unlimited behaviors follow FR-17D-1. No commercial Module or paid grants are fabricated.
- [Detailed contracts and tests](development/FR-17D2A-ATOMIC-USAGE-SQL.md). Disposable real SQL Server tests cover parallel last-unit races, count capacity races, idempotency, rollback, reversals, append-only audit and Tenant isolation. No HTTP route, permission/licensing runtime switch, app seed, or Angular modifications. Real commercialization requires FR-17C-3 composition and subscription-bound periods; asynchronous multi-txn reservations remain separate. CI evidence and deployment state must be checked independently.

## FR-17D-1 — Optional Operation Limit evaluation contract (2026-11-11)

- New pure `OperationLimitPolicy` and xUnit tests distinguish *no declared usage ceiling* (`no_usage_limit`) from *explicit Unlimited*, finite 0/positive, missing required PlanVersion capability, invalid values and absent authoritative usage (fail closed). Supports Feature, CountLimit, PeriodQuota and AllowedModes, safe decrement after downgrade and checked arithmetic; declaration is source-owned at Module/Resource/operation level.
- This is **not runtime authorization or metering**: no quota SQL tables, concurrency guard, HTTP enforcement, paid Module, plan checkout bypass or Angular changes. Legacy System/Tenant core access remains untouched. No commercial Module is seeded or fabricated.
- [Full implementation contract, test matrix and FR-17D-2 backlog](development/FR-17D1-OPERATION-LIMIT-POLICY.md). Next: SQL Server serializable CountLimit writes and quota ledger, two-Tenant concurrency/idempotency acceptance, then effective Entitlement AND Permission gate and Angular usage view. Check CI before merge/deployment.

## FR-17C-2A — Server-priced Owner Orders and System external settlement (2026-11-11)

- [PR #135](https://github.com/sholehamed/foundation-backend/pull/135) introduces Owner-only, rate-limited, idempotent `GET/POST /api/tenant/workspace/subscription/orders` (price/currency resolved only from the currently published and source-registered Commercial PlanVersion). Creating an order is always **Pending**. System Master SuperAdmin gets code-owned `PLAN-CATALOG.SETTLE`, a pending Order queue and an explicit external-receipt attestation endpoint. A validated positive IRR Order and matching Tenant/PlanVersion Subscription are settled+activated together in a Serializable SQL transaction; no TenantModule, RolePermission, UserPermission or automatic sale. [Complete protocol and limitations](development/FR-17C2A-TENANT-ORDERS-MANUAL-SETTLEMENT.md).
- [Angular companion PR #64](https://github.com/sholehamed/foundation-frontend/pull/64) adds dynamic Owner order form and System Admin manual receipt queue. User-provided prices are never trusted. New SQL+HTTP+Angular tests cover idempotent quote, real server amount, settlement replay, Tenant separation and noncommercial denial.
- **IMPORTANT** zero real deployed Commercial Modules are currently registered, so live checkout must reject all products. This is not functioning 100% discount/coupon settlement and not automated bank/gateway verification. Strong append-only payment evidence, reliable renewal, composite ownership constraints, source Commercial Module onboarding and Permission conjunction remain next steps. Existing System Admin grants need a reviewed Registry apply + `SETTLE` grant, no silent regrant.

## FR-17C-1 — Tenant Subscription storage and shadow commercial entitlement (2026-11-11)

- [Backend PR #134](https://github.com/sholehamed/foundation-backend/pull/134) introduces two additive tenant-scoped tables (`TenantPlanOrders`, `TenantSubscriptions`), code-owned read-only `TenantCommercialEntitlementReader`, and authenticated current-Tenant `GET /api/tenant/workspace/subscription/me`. It demands same-Tenant/same-Version **Settled** order with correct IRR amounts and settlement evidence, Active in-period Subscription, valid published PlanVersion and deployed code-owned commercial Module manifest. Unsettled/forged/mismatched orders and inactive plans fail closed; no License/RolePermission/TenantModules grants are issued. See [detailed architecture, schema and SQL acceptance](development/FR-17C1-SUBSCRIPTION-SHADOW.md).
- [Frontend PR #63](https://github.com/sholehamed/foundation-frontend/pull/63) updates Tenant dashboard to read truthful shadow status, without licensing operations or silently treating RequestedPlanKey as payment.
- **Not yet implemented:** any Order/settlement/subscription write API, coupon redemption, checkout, auto-activation, runtime Permission conjunction, commercial Module declarations or quota ledger. SQL checks validate settlement *shape*, not trusted payment evidence. CI/staging must be verified separately before claiming operational status.

## FR-17B-2C — SQL Server immutable Published Plan snapshots (2026-11-11)

- [PR #132](https://github.com/sholehamed/foundation-backend/pull/132) adds reviewed SQL Server DML triggers for CatalogPlans, CatalogPlanVersions, CatalogPlanVersionModules, CatalogPlanVersionCapabilityValues, CatalogPlanPriceOffers, and CatalogPlanDisplayItems. Direct DML against Published/Retired versions is denied; Draft→Published remains legal, Draft editing and revision-copy are preserved. Trigger-aware EF Core configurations + matching ModelSnapshot prevent SQL Server OUTPUT errors. Real disposable SQL tests verify direct SQL rejection, no partial mixed-row updates, no forged Published inserts, and no commercial grants. See [detailed migration, security and rollback guidance](development/FR-17B2C-IMMUTABLE-PLAN-SNAPSHOTS.md).
- Companion Angular [PR #62](https://github.com/sholehamed/foundation-frontend/pull/62) marks historical published snapshots as locked and offers revision-only editing.
- **Not yet commercial**: zero code-owned Commercial Modules and no Subscription, Entitlement, Coupon or verified checkout. Published status/Plan identity cannot be directly retired/disabled; future audited availability overlay and privilege-restricted publication procedure are required. CI must be checked at PR head and post-merge main separately; no staging claim.

## FR-17B-2B — First-party System Plan Catalog admin workflows (2026-11-10)

- [PR #131](https://github.com/sholehamed/foundation-backend/pull/131) adds centrally owned System `plan-catalog` Resource with READ/CREATE/UPDATE/PUBLISH permissions. Fresh install now registers **14 System root pages**, Master SuperAdmin grants and unchanged **six** Tenant admin menus/17 Tenant keys. Existing populated databases require reviewed System Registry apply and auditable Master role permissions; no silent regrant after revocation.
- New `SystemPlanCatalogAdminService` and live-SuperAdmin-gated `PlanCatalog` EndpointGroup provide SQL Server draft Plan creation, new revision copied from published content, persisted edit, code-owned commercial Module references, validated IRR price/display/typed value fields, read-only published parent pin preview and SHA-256 approval fingerprint, SERIALIZABLE publication requiring exact fingerprint. Invalid/unknown/noncommercial Modules fail closed; no Subscription or role grant. Angular companion [PR #61](https://github.com/sholehamed/foundation-frontend/pull/61) adds plan editor, prices and review UI.
- **Current expected behavior:** zero deployed Commercial Modules => Plan Draft creation works but no plan can publish; PublicPlanCards remain empty. Publication write handlers reject changes to published versions. **Unfinished:** DB-level immutable snapshot triggers, cross-connection concurrent publish tests, existing-database admin grant rollout procedure, commercial Module endpoints, Entitlement, quota, discounts, payment/checkout and Staging proof. [Full handoff](development/FR-17B2B-SYSTEM-PLAN-CATALOG-ADMIN.md). Merge only after current-head CI green.

## FR-17B-2A — Published Plan Cards public read and signup compatibility (2026-11-10)

- Implemented PR pending CI: anonymous rate-limited `GET /api/public/plans` from database published PlanVersions, latest valid revision per plan, filtered live offers, validated code-owned Commercial modules, exact IRR prices and dynamic display rows. Non-code-owned modules are not shown. `PublicSignup` accepts `unselected` core-only onboarding and validates new plan codes against currently published offers, with all existing legacy keys still valid; no paid subscription or user grant is created.
- Source security analyzer explicitly audits PublicPlans anonymous+rate-limit; added real disposable SQL tests for latest/public/empty/noncommercial cases. Angular companion in foundation-frontend replaces fixed landing and signup plan arrays; see [handoff](development/FR-17B2A-PUBLIC-PLAN-CARDS.md).
- **Not yet implemented**: System Admin draft editor/Publish endpoint, commercial source registry, active product catalog seed, subscription/entitlement or coupon/checkout. Until those are added, safe endpoint may return empty and frontend shows no saleable plan. Do not claim live sale or staging proof based on CI.

## FR-17B-1 — Persistent Plan Catalog with pinned parent revisions (2026-11-10)

- [PR #128](https://github.com/sholehamed/foundation-backend/pull/128) implements **seven additive globally owned Catalog tables** using SQL Server EF migration `20261110230000_versionedplancatalog`: stable Plans, published PlanVersions with pinned ParentPlanVersion, PlanVersionModules, typed CapabilityDefinitions/Values, PriceOffers, ordered database-owned PlanDisplayItems. [Schema and acceptance details](development/FR-17B1-VERSIONED-PLAN-CATALOG.md).
- New read-only `PublishedPlanCatalogReader` validates source-owned **Commercial** Module/Capability contracts, resolves parent version snapshots (not mutable parent pointers), unions Modules, merges typed limits/quotas/modes, exposes only current published offers, rejects unentitled Module/Feature display items. No RolePermission/Subscription/TenantModules grants. Disposable SQL Server acceptance added to existing CI suite.
- **Not implemented:** transactional draft/publish mutation API, immutable snapshot write guard, Angular public PlanCard API, TenantSubscriptions, entitlement runtime gate, usage ledger, coupon/checkout/payment. Existing `BusinessProfile.RequestedPlanKey` is only intent. Only schema/projection and tests are in PR #128; await **current-head** Build/SQL/HTTP checks before merge.
- **Next:** FR-17B-2 complete safe publisher, immutable published-version snapshot and dynamic public PlanCard API, then FR-17C Subscription/Entitlement and permission conjunction. Operator rollout still requires reviewed migration and real deployment evidence.

## FR-17A-2 — Typed Resource/Permission and future capability contracts (2026-11-10)

- [PR #127](https://github.com/sholehamed/foundation-backend/pull/127) adds `ModuleResourceContracts` with four Module classifications, source-derived 13 System Resources plus 17 Tenant-core Permission definitions, strict Resource/Permission owner validation and typed Capability/Limit/Quota/AllowedMode + per-operation binding descriptors. Existing `SystemEndpointRegistryIntegrity` now reports `ResourceContractIssues` and will refuse readiness for invalid source contract, without changing existing System grant/protocol policy.
- [Specification and test matrix](development/FR-17A2-TYPED-RESOURCE-CONTRACTS.md), unit tests for invalid cross-resource/permission/capability/type/merge/binding ownership. No SQL migration, seed/grants, paid entitlement gate, limits consumption, checkout, billing activation or deployed flags. Await verified PR CI before merge; System first-login and Tenant Owner bootstrap remain as before.
- Next FR-17B: validated typed persistence/plan versions and read-only/shadow entitlement evaluation; separate later quota and paid authorization enforcement. User indicated development data does not need special preservation, but no destructive SQL operation is introduced in FR-17A-2.

## FR-17A-1 — Executable, no-write Module/Resource/EndpointGroup inventory (2026-11-10)

- [PR #126](https://github.com/sholehamed/foundation-backend/pull/126) adds `scripts/security/fr17a_resource_inventory.py`: reuse strict FR-14A endpoint security audit; deterministic JSON ties 13 System Resources to endpoint groups, API endpoint keys and PermissionCodes, explicitly records shared `menu-management`, 17 Tenant admin definitions, and separate Auth/Connect/PublicSignup/BFF/media/dev surfaces. Mutation tests reject new unreviewed Resource sharing, mismatched metadata and changed central Tenant catalog.
- Read-only [SQL target inventory](sql/fr-17a-module-resource-entitlement-inventory.sql) detects nonempty/legacy catalog, broken Resource/Permission ownership, API/UI link drift, menu mappings and legacy TenantModules vs signup Plan intent. CI archives source inventory artifact, no direct SQL target access. [Runbook and remaining gaps](development/FR-17A-RESOURCE-INVENTORY.md).
- **No runtime Authorization, Seed, grants, migrations, subscription, plan activation, billing or feature flags changed.** Target SQL/EndpointDataSource deployment parity not verified. Await PR CI and human-approved existing-DB inventory before any migration or resource regrouping.

## FR-17A — Modular entitlements, plans, limits and workflow extensibility: design baseline (2026-11-10)

- **Documentation-only, proposal, not deployed.** Owner clarified: Resource is approximately a Controller/Minimal API EndpointGroup; Permissions/APIs/UI roll up through Resources into centrally System-owned Modules; a purchased/versioned Plan makes eligible commercial modules/capabilities available to a Tenant, and Tenant Owner separately delegates eligible Permission grants. Module purchase never grants all users access. Core System management and Tenant account/billing recovery stay accessible without a paid plan.
- Added [full architecture](architecture/modular-entitlements-plans-limits.md), [FR-17A handoff and acceptance](development/FR-17A-MODULAR-ENTITLEMENTS-HANDOFF.md), and [proposed ADR](decisions/ADR-0002-commercial-entitlement-vs-authorization.md). Includes dynamic DB-owned plan-card display, parent inheritance pinned to published versions, explicit typed limits/quota/modes, workflow Policy vs Outbox vs required Saga, a concurrent usage ledger and order/discount/zero-amount/external-settlement state machine.
- **Important as-built gap:** Existing TenantModules is not a subscription; BusinessProfile.RequestedPlanKey is only signup intent; 17 initial Tenant Owner rights are administrative role grants, not purchased entitlements. Current EffectivePermissionEvaluator has no subscription gate. Some existing Tenant permissions share tenant-administration Resource; no data/permissions were silently moved.
- **FR-17A-1 source inventory started:** [read-only inventory](development/FR-17A-RESOURCE-INVENTORY.md) matches the 13 code-owned System Resources to their EndpointGroup files, identifies the shared `menu-management` Resource across Menus/MenuCategories, and records Tenant `tenant-administration` and PublicSignup exceptions. Actual persisted SQL, non-Identity groups and Auth/BFF route classification remain unverified.
- **Next:** FR-17A-1 read-only EndpointGroup/Resource/SQL inventory (and TenantModules usage), reviewed classification and migration proposal; then typed metadata and regression tests. No EF migrations, code paths, seed writes, grants, flags, plan activation, payments, CI claim or Staging/Production action in this docs PR.

## FR-16C — initial administrator login must show all permitted menus (2026-11-10)

- First-install contract extends beyond SQL seed: local Development System Admin first login must have exactly **13 System menu roots and 13 active PAGE rights**; real newly registered Tenant Owner first login exactly **six Tenant menus and 17/17 Tenant PAGE/ACTION permissions**, without self-grant or any System rights. `tests/http/public_signup_e2e.py` is a real signed HTTP/SQL acceptance added to the existing main/PR workflow (not merely a mock fixture).
- Fresh non-Development installation may configure an explicit **one-time strong initial System administrator secret** in private deployment settings to create the first active System User and Master-scoped `SUPERADMIN` assignment. No hard-coded Production password, no recreation/reactivation on restart, no grants to existing Tenant. [Runbook](development/FR-16C-FIRST-LOGIN-ADMIN-READY.md). Shipped Tenant dynamic-menu flag remains OFF pending controlled rollout; existing static sidebar respects live permissions. **Remaining:** green CI and browser run, actual Production configuration/operator provision, no automatic existing-DB privilege backfill.

## FR-16B follow-up — real E2E fixture Owner grants match new Tenant signup (2026-11-10)

- The initial main FR-16B full-stack browser run exposed that test-only `seed-tenant-iam` still built a legacy Owner without explicit Tenant RolePermissions, so the permission-filtered sidebar exposed only three ungated links. Corrected test-only SQL fixture to grant exactly the 17 reviewed globally owned Tenant permissions to its disposable Owner, no System permissions/staff grants; added real signed HTTP assertions for exact six Owner and three staff menu roots. See [FR-16B follow-up](development/FR-16B-AUTHORITATIVE-SEED-REFACTOR.md). No production Seed or real Tenant grants are modified by this fixture fix.

## FR-16B — remove unusable sample SeedData and consolidate executable first-install seed (2026-11-10)

- Replaced historical 16 KB `DefaultSeedData` demo lists and obsolete `/system/admin/...` routes with a minimal stable Master Tenant ID factory and Development System admin blueprint. Startup now has **one authoritative, executable** System/Tenant bootstrap path and no commented-out placeholder CRUD seed calls. System definitions live in `FeatureRegistryDefinitions`; Tenant permissions/navigation remain centrally authored. Master Tenant ID and E2E fixture IDs are preserved.
- First-install FR-16 System/Tenant role grants and new Tenant Owner grants remain intact; restarts never regrant revoked permissions. Production initial account provisioning stays secure and separate; dynamic Tenant navigation remains OFF outside runner-only pilot. [Runbook](development/FR-16B-AUTHORITATIVE-SEED-REFACTOR.md). Next: re-run real SQL/HTTP/Chromium CI, separately review legacy existing-database bootstrap.

## FR-16 — initial System/Tenant administrator menu and permission seed (2026-11-10)

- New **clean-DB-only** bootstrap creates 13 central System roots, 17 Tenant PAGE/ACTION definitions, six System-owned Tenant menus and Master-scoped System SuperAdmin role/permissions before legacy unowned platform catalog seeding. Requires Permission.Code migration; no System privileges ever assigned to Tenant role. New Tenant signup atomically grants 17 centrally reviewed Tenant privileges to its brand-new Owner role under narrow first-signup interceptor exception; subsequent Tenant grant writes remain audited/CAS-protected.
- Restart/legacy DB no-op: no repair, reactivation or regrant of permissions; missing/inconsistent existing catalogs require operator approval. No remote deployment or feature toggle, no insecure Production password creation. [Detailed scope](development/FR-16-FIRST-INSTALL-ADMIN-ACCESS-BOOTSTRAP.md). **Remaining:** green real SQL/HTTP/browser CI, separate secure Production initial System user provision, no dynamic sidebar pilot toggle until environment approval.

## FR-15D — dynamic Tenant menu ON only inside trusted Actions disposable Chromium E2E (2026-11-10)

- Existing FR-15C ephemeral full-stack acceptance now turns `tenantDynamicNavigationPilotEnabled` **ON only in the temporary Angular checkout**, seeds the six reviewed global System-owned Tenant Menu rows **only in isolated SQL Server fixture** after HTTP Tenant Permission Catalog setup, and exercises actual Angular/Backend BFF navigation. Browser verifies staff Members link appears on real Role grant, disappears on revocation + fresh login, no unauthorized Roles/Groups links and System central gate isolation. Signed HTTP and live browser tests remain authoritative.
- All checked-in Frontend/deployed flags stay OFF; the SqlMutator fixture is hard-restricted to trusted-main GitHub Actions and the exact disposable SQL DB, never a production API. No global grants or remotely hosted Staging are touched. [Runbook](development/FR-15D-ACTIONS-DYNAMIC-MENU-REAL-BROWSER.md). **Next:** green main live-browser run, independent real Staging central catalog review before any deployment pilot.

## FR-15C — GitHub Actions launches ephemeral full-stack Staging (2026-11-10)

- Introduced a **manual main-only** `FR-15C Launch Ephemeral Staging` workflow, also triggered once by its initial merge, which invokes existing trusted `FR-12G-C live HTTPS BFF browser E2E` through a scoped Actions token. The actual browser run already provisions disposable SQL Server, Kestrel/Identity, Angular, local HTTPS, two Tenant contexts and Playwright; launcher summary links to its uniquely matched run. **A successful dispatch is not a successful acceptance test.**
- This is an ephemeral CI environment only. **No persistent/public Staging**, no cloud/VPS deploy, no DB on a remote host, no Auto-Apply or grants, and **dynamic Tenant menu pilot remains OFF**. Manual FR-15A external Staging gate is still separate. [Operation and safety](development/FR-15C-ACTIONS-EPHEMERAL-STAGING.md).

## FR-14C — four-way live SQL acceptance before dynamic menu activation (2026-11-10)

- Added **positive and adversarial** real disposable SQL Server acceptance of the consolidated System/Tenant/Menu/Endpoint registry: clean catalog but missing six Tenant Menus stays red, explicitly registered six System-owned Menus becomes green, unknown route/foreign PAGE Permission link/disabled System API each independently returns red, and fixture-only cleanup restores green. No role/user grants or real environment modifications. [Detailed evidence and Staging hold](development/FR-14C-FOUR-WAY-SQL-ACCEPTANCE.md).
- **Next gate remains real deployed Staging**: provide approved DNS/TLS/isolated SQL/keyring/session store, run FR-15A real-host anonymous probes and live SuperAdmin read-only consolidated audit, approve any required central remediation and two-Tenant BFF/browser acceptance. Do not turn on dynamic sidebar automatically.

## FR-14C — consolidated central access operator gate (2026-11-10)

- Added **read-only**, live System SuperAdmin-only `GET /System/FeatureRegistry/central-access/readiness`, combining System Registry Preview, 17-definition Tenant Permission Catalog Preview, exactly six central Tenant Menu route checks and runtime System Endpoint Integrity. The gate is green only if **all four** are fully registered, nonempty, consistent and without pending drift; returns blocking check IDs and both SHA-256 preview fingerprints.
- Unit, real disposable SQL Server and real signed Kestrel HTTP tests cover anonymous/Tenant denial, separate System allow, fail-closed missing menus or endpoint registrations, no writes or grants, and consistency with four independent reports. [Operator runbook](development/FR-14C-CONSOLIDATED-CENTRAL-ACCESS-GATE.md).
- This gate reports **code/database consistency**, NOT Staging certification or rollout approval. There are no SQL migrations, flags, automated Registry Apply or Tenant menu authorship changes. **Next:** PR/head/main CI verification, approved environment diagnostics and human-controlled central catalog/menu reconciliation before separate dynamic sidebar pilot.

## FR-14C — Tenant catalog Permission/UI link drift guard (2026-11-10)

- Central Tenant Permission Catalog Preview now rejects foreign and extra `PermissionUiResource` links on either end of its 17 PAGE/ACTION definitions; fingerprints include all adjacent links, so a stale approved Apply cannot hide a new foreign mapping. Central Tenant Navigation Readiness rejects PAGE UI links other than the exact intended single Permission.
- Added disposable SQL Server adversarial regression: live cross-linked Roles/Members PAGE, stale fingerprint refusal, mismatched link with missing intended link, no automatic grant/repair, and restored readiness only after test-fixture cleanup. [Design and operator gate](development/FR-14C-TENANT-UI-LINK-INTEGRITY.md).
- No changes to Tenant Menu authorship (System Admin only), flags, staging/production, SQL migrations or grants. **Next**: green PR-head + main CI, cross-checked full Registry/Navigation/Endpoint reports and operator-approved catalog/Menu reconciliation before dynamic navigation pilot.

## FR-14C — System Registry Preview/Apply signed HTTP denial gate (2026-11-10)

- Real disposable System Admin JWT HTTP E2E now requires the Registry Preview fingerprint (64 chars) and explicitly checks 401 anonymous, 403 Tenant principals, 409 for a live System SuperAdmin while System Apply remains default-OFF, followed by an identical read-only fingerprint. No actual central write, flag or deployment. [Contract](development/FR-14C-REVIEWED-SYSTEM-REGISTRY-APPLY.md).
- Next: latest-head main CI, then operator-approved central menu/permissions readiness on a real target before dynamic navigation pilot.

## FR-14C — reviewed System Registry Apply transaction (2026-11-10)

- System Feature Registry Preview now carries a SHA-256 fingerprint of live endpoint and database catalog state. The HTTP Apply requires explicit 64-hex fingerprint, persisted System SuperAdmin, independently default-OFF write gate and a SERIALIZABLE SQL recheck before committing additive definitions. Stale/foreign/invalid approval fails closed; no role grants or automatic flag changes.
- Real SQL acceptance covers stale and changed-Present fingerprints, success/idempotence and actor denial. The Angular System Admin Apply UI must be updated in the matching frontend PR before any operator roll-out. [Full contract](development/FR-14C-REVIEWED-SYSTEM-REGISTRY-APPLY.md).
- Next: green Backend+Frontend CI, target environment System Menu/Permission/API reconciliation, two-Tenant browser verification. Do **not** turn on Tenant dynamic sidebar yet.

## FR-14C — central System Registry Preview rejects legacy unowned permissions (2026-11-10)

- System Registry Preview now flags null/foreign/inactive/duplicate Permission.Code ownership and foreign/multiple root PAGE, nested PAGE/ACTION or API permission links as **Conflict**. Neither an operator Apply nor a feature flag may silently adopt these records; tests cover failure and explicit fixture repair.
- No SQL migration, live Apply, grants or menu flag. [Operator conflict/repair contract](development/FR-14C-CENTRAL-REGISTRY-PREVIEW-DRIFT.md). Next: fingerprint-locked System Apply with SQL transaction, then central readiness review before the disabled dynamic menu pilot.

## FR-14C — strict System feature Permission/Resource ownership (2026-11-10)

- Fixed a central authorization drift gap: neither System UI nor the default-OFF granular System endpoint guard may treat a bare/foreign/duplicate Permission.Code as centrally owned. Both now require an active code-owned Resource+Module and exact Permission.ResourceId, with duplicate codes denied. No flag activation, grants, DB migration, Staging or Production changes.
- Unit regressions cover null, foreign, inactive and duplicate ownership; [detailed security boundary](development/FR-14C-SYSTEM-FEATURE-OWNERSHIP-AUTHORIZATION.md). Next: green CI, operator-preview Apply freshness, then review live central Registry/Navigation readiness before any dynamic sidebar pilot.

## FR-15B — GitHub Actions real integration E2E verified green (2026-11-10)

- Backend main `21e4496dd4c3fb840757ae0e5debca2b5fac6561` passed [real HTTPS + OIDC + SQL + Chromium Actions run #42781976862](https://github.com/sholehamed/foundation-backend/actions/runs/42781976862): **135 real signed HTTP checks**, **2/2 Chromium browser tests**, and correct Tenant Owner 401/403 denial of three System-only Registry routes plus second-Tenant isolation. [Main BFF/SQL CI #42781977240](https://github.com/sholehamed/foundation-backend/actions/runs/42781977240) also passed.
- Initial browser run exposed a 401-vs-403 test expectation mismatch, fixed in PR #109 with no API authorization change. [Evidence and live-vs-Staging distinction](development/FR-15B-LIVE-ACTIONS-ACCEPTANCE.md).
- **Remaining:** Actual approved Staging DNS/TLS/SQL + backup/restore, keyring/BFF session store, read-only FR-15A live probe, deployed browser acceptance and operator-controlled central Catalog/Menu reconciliation; no Production activation.

## FR-15B — live GitHub Actions HTTPS/SQL/Chromium two-Tenant acceptance (2026-11-10)

- Added live Chromium negative checks: a real Tenant Owner BFF cookie cannot access three System-only central Registry diagnostics (403), and must be denied on the second Tenant host's protected Members API (401/403). Existing Code+PKCE, CSRF, cookies and live grants/revoke tests remain intact.
- `FR-12G-C live HTTPS BFF browser E2E` runs on trusted Backend main after a `tests/browser/**` change; this verifies a **disposable, real integration environment**, not a deployed Staging server. `FR-15A` separately accepts a manual read-only probe of an approved actual Staging installation. [Scope and evidence](development/FR-15B-LIVE-ACTIONS-ACCEPTANCE.md).
- Next gates: actual hosted Staging prerequisites, manual read-only probe, operator-led Catalog/Menu reconciliation and authenticated two-Tenant browser UAT on deployed Staging. No Production release.

## FR-15A — Staging two-Tenant read-only gate (2026-11-10)

- Prepared a manual main-only GitHub Actions workflow for an operator-supplied non-secret real Staging manifest. Requires explicit probe consent, exact dedicated HTTPS System/Identity/Tenant A/Tenant B hosts, six anonymous OIDC/BFF/Workspace probes and a sanitized non-secret evidence artifact. Offline schema and negative security tests run in regular CI. No automatic fallback to example hosts.
- **No real Staging environment provisioned or checked, no Migration/Registry Apply/grants/flag changes, no Production.** Next: operator DNS/TLS, isolated SQL/backup+restore, keyring/session store and signed deployment approval; then run the manual read-only workflow before authenticated two-Tenant Chromium and data integrity certification. [Runbook](development/FR-15A-TWO-TENANT-STAGING-READONLY-GATE.md).

## FR-14C — real disposable central registry HTTP/SQL acceptance (2026-11-10)

- Extended signed System Admin / Tenant Owner / staff HTTP E2E with three read-only central System registry checks and explicit anonymous/Tenant 401/403 denials; validates all six Tenant menu routes and the runtime System endpoint integrity result against real Kestrel/SQL Server. Does **not** auto-Apply System Registry, create central menus, grant users, activate pilot flags or deploy Staging.
- [Disposable acceptance contract](development/FR-14C-DISPOSABLE-CENTRAL-READINESS-E2E.md). Next: green PR-head/main CI, Chromium regression, approved Staging provision and human-controlled central reconciliation. Staging/Production unchanged.

## FR-14C — System endpoint/Permission/Resource runtime integrity (2026-11-10)

- New read-only System SuperAdmin endpoint **/System/FeatureRegistry/system-endpoints/integrity** inspects live ASP.NET Core FeatureEndpointMetadata, executable granular policy + SystemAdministrator policy, and corresponding global Module/Resource/Permission/ApiResource/PermissionApiResource SQL records. Detects wrong/stale/inactive API keys, HTTP verb/route drift, extra grant links, unannotated System Administrator endpoints and orphaned active System APIs. Explicitly separates three reviewed internal System routes and out-of-scope auth/BFF/Tenant/media protocols.
- Real SQL Server and unit adversarial tests; existing required SQL registry test filter covers new case. No Apply, grants, migrations, flag activation or deployment. [Implementation and operator gates](development/FR-14C-SYSTEM-ENDPOINT-REGISTRY-INTEGRITY.md).
- Next: run audits on an approved deployed host, reconcile System registry operator-controlled records, inspect subscriptions/grants, certify Tenant/BFF browser access, then FR-14C-3 Branch RBAC. Staging/Production deferred.

## FR-14C — Central Tenant navigation readiness audit (2026-11-10)

- Implemented **read-only System SuperAdmin** `GET /System/FeatureRegistry/tenant-navigation/readiness` (under configured API prefix), comparing all six canonical Tenant dashboard Menu routes against the single System-owned Menu/Category/Page/Permission registry. Diagnoses missing, duplicate or hidden menus, malformed ancestors, unregistered routes, broken Tenant PAGE ownership/UI links and ungated external links; **no automatic Menu or grant creation**.
- Added disposable SQL Server regression and route policy unit contract. Existing separate System Feature Registry and Tenant Permission Catalog Preview/Apply remain unchanged and explicit. [Operator guide](development/FR-14C-CENTRAL-TENANT-NAVIGATION-READINESS.md).
- Next: System Admin reconciliation of reported missing Tenant menu rows, full resource/endpoint parity review, approval of cross-Tenant browser/BFF test before enabling the default-OFF Angular dynamic menu pilot. No Staging/Production/Apply operations.

## FR-14C — Tenant role grant integrity after central Registry correction (2026-11-10)

- System Admin remains sole owner of definitions. A local Tenant Owner's role editor now **quarantines** an ordinary Tenant role containing any legacy/out-of-catalog/inactive/foreign-owned Permission link. The editor reports `canEdit:false`, and the CAS/audited PUT denies rather than silently preserving or deleting the unknown link.
- Added real disposable SQL regression covering a System permission manually linked to a Tenant role, denied Owner editing with no mutation/audit, and recovery only after explicit test-side cleanup. No migration, grant/flag Apply, data modifications or operational deployment.
- [Detailed contract and operator checks](development/FR-14C-TENANT-ROLE-GRANT-INTEGRITY.md). Next: centralized Registry completeness, grant entitlement review, read-only dynamic menu browser pilot, Branch-level RBAC. Staging/Production remain deferred.

## FR-14C architecture correction — System-only menu ownership (2026-11-10)

- **Confirmed:** System Admin alone defines Menu, Page, Route, Resource, Action, Endpoint and permission catalog; Tenant Owner assigns only permitted, centrally registered capabilities to local roles and users. There is no Tenant menu authoring. The FR-14C-2A/2B owned menu implementation was superseded and removed from source; previously merged PRs remain historical records.
- FR-14C-1 read-only central Menu projection with live Tenant PAGE filtering remains. The Tenant-owned menu reader/writer, API, two pending migrations and Owner NAVIGATION.READ/MANAGE catalog definitions were retired. Dynamic Angular menu pilot remains OFF.
- [Canonical architecture and database preflight](development/FR-14C-CENTRAL-MENU-AUTHORITY.md): verify whether any database already applied retired migration IDs before deploying; never auto-drop or delete audit data. Staging/Production unchanged. Next: System Registry completeness, Tenant grant boundaries and branch-scoped RBAC.

## FR-14C-2B — Tenant Owner navigation authoring (2026-11-09)

- Dedicated default-OFF HTTPS Tenant navigation catalog GET + category/item POST/PUT API. Real local Owner and live MANAGE ACTION required for writes; registered READ PAGE required for editor access. Strict DTO allowlists, immutable Tenant/Category, route/parent validation, SQL RowVersion CAS, reorder and soft disable, no DELETE.
- New append-only TenantNavigationAuditEntries SQL migration 20261109090000 with transaction-bound before/after version and retention guard; existing owned-catalog migration 20261109080000 still unapplied. SQL and HTTP/contract tests included in CI. See [security and operations](development/FR-14C2B-TENANT-NAVIGATION-OWNER-EDITOR.md).
- Explicit exclusions: non-Owner delegated editing, historical global-menu import, branch membership/RBAC, operational grant Apply and flags, live proxy/Chrome certification. Staging/Production deferred.

## FR-14C-2A — isolated Tenant-owned menu catalog (2026-11-09)

- Created additive `TenantNavigationCategories` / `TenantNavigationItems` schema with composite Tenant/Category/Parent FKs, a per-Tenant unique Route index, RowVersion, and guarded non-destructive rollback. Global System Menu/MenuCategory unchanged; **no legacy migration/backfill**.
- Existing Tenant navigation endpoints can explicitly select the parameterized owned-only SQL projection via **default-OFF** `Identity:TenantNavigation:OwnedCatalogEnabled`. PAGE permission decisions remain live; empty/missing catalog never falls back to global menus. No Tenant menu writes, grants, branch authorization or deployment are enabled.
- Added pure security and disposable SQL FK/isolation/deny/revocation tests and wired the new SQL test suite into required PR CI. [Contract and remaining gates](development/FR-14C2A-TENANT-OWNED-NAVIGATION.md).
- Next FR-14C-2B: audited CAS + delegated Editor/Owner menu authoring and explicit legacy import preview. FR-14C-3: actual BranchMembership and branch-scoped roles. Staging stays deferred.

## FR-14C-1 — dynamic Tenant navigation aligned to live PAGE guards (2026-11-09)

- Authenticated `/tenant/workspace/navigation/me` and existing Tenant `/System/Menus/me` now return only code-owned safe Tenant paths. Members, Roles and Groups require the same **live Tenant PAGE evaluator** used at their API/Angular guards, even when the global `Menu.PermissionId` is null. Wrong RoleScope, foreign Tenant/subject, explicit Deny, revoked Group/Role and unknown paths fail closed; invalid parent folders disappear.
- In-memory and disposable SQL tests cover Tenant isolation, direct Deny, wrong role scope, group revocation and 403/no-store. No grant, migration or Menu mutation; Tenant Menu catalog itself is still global rather than owned. [FR-14C-1 contract](development/FR-14C1-TENANT-NAVIGATION-BOUNDARIES.md).
- **Next:** explicit Tenant/branch-owned menu schema and migration review, delegation boundaries and branch-scoped membership/RBAC. Staging and Production remain deferred.

## FR-14B-3 — guarded System account lifecycle (2026-11-09)

- Added opt-in, HTTPS-only, SystemAdministrator-scoped GET /Admins/{id}/lifecycle and CAS-versioned PUT /Admins/{id}/profile, /state and /lock. Target must be non-self, System/TenantId-null and not a direct/inherited SuperAdmin. Older unsafe generic User/Admin mutation routes remain 403.
- SQL SERIALIZABLE conditional updates and append-only ledger record immutable actor/target/action, changed field identifiers, before/after state and concurrency version, with SecurityStamp rotation and one-use proof revocation. Pending activation bypass prohibited; legacy unversioned/recovery accounts need separately approved remediation.
- Migration 20261109070000 adds audit columns/actions but is NOT deployed. Feature Identity:PlatformAccounts:LifecycleEnabled is default OFF. SQL tests include stale CAS, actor revocation, protected targets and audit rollback. [Security contract](development/FR-14B3-SYSTEM-ACCOUNT-LIFECYCLE.md).
- Remaining: reviewed Angular admin editors, deployed browser/BFF session tests, first-login/recovery operational plan, preflight migration/grant and Staging once approved.

## FR-14B-2B — System initial credential and forced change (2026-11-09)

- Opt-in and HTTPS-restricted System initial secret issuance (60 minutes, five attempts), SQL hash-only one-use 10-minute challenge, activation with new hash, SecurityStamp/ConcurrencyStamp rotation and no OAuth/Identity/BFF session before fresh login. The code-owned Admins issuance action is separate from legacy unsafe mutations. Audit is append-only and transactional; the new migration is **not** applied outside disposable CI.
- Public System first-login resolve/complete are exact-root-host and independently rate-limited. The UI companion remains default OFF. Normal System and Tenant login behavior is unchanged when disabled; zero automatic grants, no superadmin bootstrap.
- **Staging/Production deferred**; FR-14B-3 profile/state/lock, BFF identity-host System flow, operational grants/migration/certification and recovery remain. [Full contract](development/FR-14B2B-SYSTEM-FIRST-LOGIN.md).

## FR-14B-2A — Inert System account creation (2026-11-09)

- Opt-in HTTPS `POST /api/System/Admins` now uses a dedicated scoped writer, **not** legacy `UserCreateCommand`. New accounts are `System`, `TenantId=null`, `IsActive=false`, with random undisclosed password hash, no role/grant and no login capability. Reject unknown JSON fields, preserve live Master SuperAdmin check, serializable write, 5/min rate limit, 400/403/409 and no-store response.
- New **operator-gated migration (not deployed)** adds filtered System-email unique index and append-only `SystemAccountLifecycleAuditEntries`. SQL tests verify transactional audit and rollback, namespace isolation, revocation and zero grants. Feature `Identity:PlatformAccounts:PendingCreationEnabled` defaults OFF. Legacy Users create and all other account lifecycle writes still return 403.
- **Not finished:** FR-14B-2B System one-time initial credential + forced change before login, followed by FR-14B-3 audited update/state/lock and Angular UI. Staging/Production remain deferred. [Detailed design and test contract](development/FR-14B2A-PENDING-SYSTEM-ACCOUNTS.md).

## FR-14B-1 — legacy System account write containment (2026-11-09)

- Both System `Users` and `Admins` lifecycle routes now fail closed with explicit 403 for five unsafe legacy operations each: create/update/delete/change-state/change-lock. The old shared User commands hard-code TenantId or overwrite PasswordHash and must not be dispatched by privileged HTTP APIs.
- Existing audited role/permission grant APIs remain available. A C# endpoint regression plus Python source/mutation gate runs under normal CI; no new account lifecycle CRUD is claimed.
- **Next:** an audited System-only account lifecycle, forced first-login credential rotation, self/SuperAdmin protection, exact SQL CAS and active-session invalidation; then guarded Angular editors. [Security note](development/FR-14B1-LEGACY-ACCOUNT-WRITE-CONTAINMENT.md).
- Staging deferred; no migration, grants, feature toggle or deployment.

## FR-14A — authorization endpoint/route inventory and enforceable opt-in System grants (2026-11-09)

- Reviewed and source-classified all Identity System, Tenant Workspace, public signup, current-user navigation and Identity OAuth mappings. CI also classifies BFF cookie/CSRF, authenticated media and Development-only Scalar routes, publishing a per-mapping JSON inventory artifact. Mutation tests reject missing `SystemAdministrator`, unregistered/wrong Resource metadata, new unguarded Tenant routes and unthrottled public signup.
- `WithFeatureEndpoint` no longer supplies merely descriptive metadata: it attaches `SystemFeatureEndpointPermissionRequirement` to every mapped System feature, with code-owned Resource/Permission pairing and the existing live Master-scope `EffectivePermissionEvaluator` when `Identity:PlatformPermissions:EnforcePermissionEndpoints=true`. Default flag OFF, existing live coarse `SystemAdministrator` policy intact; no grant migration or toggle changes. Direct deny and inactive/foreign actor tests included.
- The audit **does not** claim System granular permission rollout, Tenant delegated authorization/branch semantics, complete Identity/BFF protocol certification, or file Tenant ownership. Media GUID access requires its own explicit FR-14D review; unsafe System User/Admin writes remain FR-14B, Tenant menus/scope FR-14C, upstream BFF/session hardening BFF-05/06. [Full scope/matrix/risks](development/FR-14A-AUTHORIZATION-SURFACE-REVIEW.md). **Staging and Production untouched.**

## FR-13C-2E — member effective Tenant permission explanations (2026-11-09)

- Owner-only read endpoint for an individual ordinary Tenant member: reports all 17 code-owned PAGE/ACTION permission decisions using the *actual live* `TenantWorkspacePermissionEvaluator`, together with bounded provenance for direct UserPermission allow/deny, active direct UserRole, inherited UserGroup→GroupRole and legacy Owner fallback. No writes, grants, tokens, arbitrary Tenant IDs or new permission catalog entries.
- Explicitly distinguishes the raw permission decision from the extra Owner/writable/target/feature gates on privileged endpoints; cannot assume permission alone guarantees write access.
- Disposable SQL equivalence and signed HTTP real-token inheritance/revoke tests; [security contract](development/FR-13C2E-TENANT-EFFECTIVE-PERMISSIONS.md). Companion Angular route in progress. **Staging deferred.**

## FR-13C-2D — Group security history and legacy-link review (2026-11-09)

- Added local Owner-only `GET /api/tenant/workspace/groups/{id}/audit?page=1` requiring **Groups.Read + Members.Read + Roles.Read**; 20-row unified append-only lifecycle, UserGroup and GroupRole audit with explicit TenantId/GroupId SQL filtering and no-cache response. Separate read-only integrity counters flag legacy Owner/non-local/unknown-audit-provenance links without modifying any records.
- New disposable SQL and real signed HTTP verification for 401/403/404/400, mixed timeline pagination and foreign ledger ID isolation. Companion Angular guarded history page and tests. [Detailed contract](development/FR-13C2D-GROUP-SECURITY-REVIEW.md).
- **No migration, automatic clean-up, Registry Apply or deployment; Staging remains deferred.** Future reconciliation requires a separate human-reviewed plan.

## FR-13C-2C — Tenant GroupRole grants (2026-11-09)

- Reviewed `TENANT.GROUPS.ASSIGN-ROLES` ACTION, Owner-only group/role-pair SQL CAS grant/revoke, live tenant authorization, extra actor-member/Owner-group denial, immutable grant history with rollback and 20-row Group role reader. Raw generic GroupRole and UserPermission writes stay denied.
- Backend SQL Server + signed HTTP acceptance, Angular companion guarded route planned; [contract](development/FR-13C2C-TENANT-GROUP-ROLE-GRANTS.md). Staging stays deferred. Next: broader security history and legacy membership reconciliation.

## FR-13C-2B — single-member Tenant Group grants (2026-11-09)

- New explicit reviewed `TENANT.GROUPS.ASSIGN-MEMBERS` ACTION and owner-only, tenant-scoped, SQL CAS/audited UserGroup grant and revoke. Group+single-user temporary write intent, 20-row eligible-member reader, no Owner/self/foreign targets, no bulk membership truncation. Existing GroupRole/UserPermission generic writes remain denied.
- Disposable SQL and signed HTTP regression coverage for stale version, role inheritance/revocation, 403 boundaries, immutable audit and rollback. [Contract](development/FR-13C2B-TENANT-GROUP-MEMBERS.md).
- Next: separate GroupRole audited assignment, then broader Tenant Admin. **Staging deferred**; no operational settings touched.

## FR-13C-2A — Tenant Group lifecycle (2026-11-09)

- Added separate Owner-only Tenant Group create/update/activate/deactivate endpoints with live `TENANT.GROUPS.READ` plus three exact ACTIONs, writable home scope and direct deny precedence. SQL SERIALIZABLE transaction + RowVersion CAS and new append-only `TenantGroupChangeAuditEntries` ledger. Existing UserGroup/GroupRole privilege links remain fail-closed and are NEVER created or modified here. Catalog expands 12 -> 15; no automatic Apply or grants.
- Existing Group directory now returns SQL `version` and Angular has a dedicated create/edit/state page with confirmation, 403/409 handling and no membership controls yet. [Implementation and boundary](development/FR-13C2A-TENANT-GROUP-LIFECYCLE.md). Next FR-13C-2B: audited membership and GroupRole intent. Staging/Production untouched.

## FR-13C-1 — read-only Tenant member security audit (2026-11-09)

- Added an Owner-only, authenticated, Tenant-scoped read of existing append-only lifecycle and direct member-role ledgers. New `GET /api/tenant/workspace/members/audit` uses live `TENANT.MEMBERS.READ` + current local Owner role, explicit SQL TenantId joins, bounded 20-row paging and optional member filtering. No credential fields or SQL mutation; System/non-Owner/mismatched-Tenant access denied.
- Companion Angular Owner links, guarded route and Persian activity history; no new static menu item, permission catalog, SQL migration, deployment configuration or Staging. [Contract and tests](development/FR-13C1-TENANT-MEMBER-AUDIT.md). Next: group management CRUD + membership.

## FR-13C — real disposable HTTPS/Chromium first-login E2E (2026-11-09)

- Extended existing trusted-main-only FR-12G-C Action; real local HTTPS Identity + Tenant, SQL Server, Angular form, real OpenIddict Code+PKCE and BFF session. New browser scenario covers Owner atomic create of email-less inactive staff, one-time credential, forced password change inside ordinary Identity login, no pre-change session, CSRF rejection, consumed-secret replay denial, fresh login and real role grant/revoke effects (401 stale ticket, 403 fresh ticket, UI access decision).
- First-login feature flags enabled ONLY for disposable Action runtime; normal Frontend/Backend Production and Staging flags unchanged. One-time secret is never printed; sensitive first-login test isolated in its own Playwright file with tracing/screenshot/video disabled. Existing BFF E2E preserved.
- **Verified live acceptance:** Backend main `919f636b97c34b7886bda87964fe523758654b90`; [GitHub Action run #42601232203](https://github.com/sholehamed/foundation-backend/actions/runs/42601232203) **success** on 2026-11-09. Real SQL fixture and HTTP IAM 51 checks passed; Chromium/HTTPS Playwright **2/2**: original BFF (13.6 s) and email-less mandatory first-login with grant/revoke (13.1 s). This is disposable CI infrastructure, NOT a remotely deployed Staging or Production server. [Implementation/runbook](development/FR-13C-DISPOSABLE-LIVE-FIRST-LOGIN-E2E.md).

## FR-13B-3A — email-less local Tenant staff creation (2026-11-09)

- [Issue #78](https://github.com/sholehamed/foundation-backend/issues/78): New SQL migration makes `User.Email` and `NormalizedEmail` nullable, and rebuilds Tenant normalized email unique index with an explicit `NormalizedEmail IS NOT NULL` filter. Tenant Owner's pending and atomic initial-password creation accept `email:null`; UserType System, public Owner registration and other email-mandatory paths retain existing validators. A blank email is never replaced by a fake one.
- Email invitation explicitly refuses staff without a real recipient before SQL token/audit; direct and BFF first-login initial password + forced reset remain viable. Real SQL tests cover multiple nulls, duplicate real emails, SQL filtered index, no-email activation, no-auto-role/EmailConfirmed, invitation refusal and invalid address. Down migration refuses rollback until email-less rows are resolved using genuine emails.
- No Staging or Production DB migrated or default-OFF first-login/BFF feature flag enabled. Separate [runbook and acceptance gates](development/FR-13B3A-EMAILLESS-TENANT-STAFF.md). Cross-repo Angular companion required for UI use.

## FR-13B-3C — Identity-host BFF first-login (2026-11-09)

- Continued FR-13B-3B into the separate Identity-host browser sign-in, [Issue #81](https://github.com/sholehamed/foundation-backend/issues/81). Same CAPTCHA+antiforgery interactive-login 204 for normal credentials; 200 only for proven temporary Tenant password, yielding restricted 10-minute change challenge, never an Identity or BFF login session. A new Identity-host XSRF-protected completion endpoint uses the existing SQL atomic single-use password activation and forces a fresh normal Identity login before OIDC code issuance.
- Both first-login and BFF flags remain OFF by default. No migrations, Registry Apply, grants, production/staging rollout, SMTP, or real end-to-end OIDC activation done by code merge. [Security contract](development/FR-13B3C-BFF-IDENTITY-FORCED-CHANGE.md). Existing Tenant direct login and email invite remain intact.

## FR-13B-3B — unified Tenant Login (2026-11-09)

- [Issue #79](https://github.com/sholehamed/foundation-backend/issues/79): Owner optional atomic inactive member + initial password, and new HTTPS Tenant login decision `/api/public/tenant-login/resolve`. Server produces password-change-only challenge **only** for a valid 4x6 one-time secret; normal credentials fall through unchanged. No authenticated session before reset; new password then fresh normal login.
- SQL transaction checks both code-owned Tenant Actions and writes `create_inactive` + `temp_issued` audit atomically. Existing 12-key catalog, pending email invitation, BFF flags and prior `UserCredentialAuthenticator` preserved. Feature `Identity:TenantFirstLogin:Enabled` OFF until approved staging. [Complete contract](development/FR-13B3B-UNIFIED-TENANT-LOGIN.md).
- Legacy Identity-host BFF interactive Login still needs separate integration. No real staging deployment, new SQL migration, Registry Apply/grants or distributed throttling has been performed in this development PR. Email-less members remain issue #78.

## FR-13B-3 — offline first-login credential (2026-11-09)

- [Issue #76](https://github.com/sholehamed/foundation-backend/issues/76): an optional, independent Owner-only temporary password issuance ACTION, 60-minute per-user credential stored hashed in SQL, 5 wrong-attempt cap, 10-minute single-use restricted first-login challenge, transactional permanent password/IsActive change + security-stamp rotation + append-only audit. No normal JWT/cookie/session is issued until a new ordinary login.
- Explicit Backend toggle `Identity:TenantFirstLogin:Enabled` defaults OFF, Tenant catalog increases 11 -> 12, migration `20261109030000_tenantmemberfirstlogin` requires separately authorized operator apply. Email invitation and BFF feature defaults remain unchanged.
- [Full FR-13B-3 security design/CI acceptance](development/FR-13B3-TEMP-PASSWORD-FIRST-LOGIN.md). Companion Angular PR and real Staging HTTPS/browser acceptance must be separately verified, no Production enablement.

## FR-13B-2 — secure Tenant staff invitation and activation (2026-11-09)

- Separate [FR-13B-2 #74](https://github.com/sholehamed/foundation-backend/issues/74), continuing completed inactive member creation #72: owner-gated `TENANT.MEMBERS.INVITE` ACTION, hash-only 30-minute single-use SMTP invitation, SQL Server transactional issue/audit/submission + anonymous HTTPS password setup/activation; no Owner-visible token, no automatic role grants.
- Backend dev PR uses default-OFF `Identity:TenantInvitations:Enabled`; SMTP secrets and deployment are NOT provisioned. New SQL migration and reviewed Tenant catalog Preview/Apply+Owner privileges are required before operator opt-in; no production migration or feature toggle changes.
- Companion Angular public activation route and pending-member Invite UX plus cross-repo browser E2E still required; [detailed contract](development/FR-13B2-INVITATION-ACTIVATION.md). Production BFF/Cookie pilot remains OFF.
- Do not interpret passing local SQL/HTTP tests as proven email delivery or a working staged end-to-end user journey.

## FR-13B-1 — Owner-controlled inactive Tenant member creation (2026-11-09)

- [Issue #72](https://github.com/sholehamed/foundation-backend/issues/72): Add code-owned TENANT.MEMBERS.CREATE ACTION, owner-only live scoped POST /api/tenant/workspace/members, disabled User with random undisclosed password hash, zero role/group/user grants, 10/min authenticated-user rate limit, append-only TenantMemberLifecycleAuditEntries in SQL transaction. SQL migration and operator catalog Preview/Apply are NOT automatic.
- FR-13B-1 intentionally **does not** invite, activate, email or grant login access; user remains IsActive=false. Separate secure one-time invitation/activation and live staff browser E2E are required before calling the Tenant Admin onboarding journey complete.
- Tests: disposable SQL + existing Identity/Registry + FR-12F catalog count; PR CI must pass before merge. [Detailed acceptance](development/FR-13B1-INACTIVE-TENANT-MEMBER-CREATE.md).
- Staging (FR-13A) remains unprovisioned; Production BFF/Registry/UI defaults unchanged.

## FR-13A — operational staging prerequisites (2026-11-09)

- New tracker: [FR-13 #70](https://github.com/sholehamed/foundation-backend/issues/70). The **staging environment is NOT provisioned** and Production BFF/UI/registry flags stay OFF.
- This PR adds `scripts/staging/preflight.py`, a non-secret sample manifest, offline unit/CI tests and optional anonymous HTTPS/GET-only probes. The script cannot activate features, apply migrations/grants, or verify live deployment configurations without separate operator evidence.
- The real disposable [FR-12G-C Chromium/HTTPS/Code+PKCE/BFF/SQL CI #42520612905](https://github.com/sholehamed/foundation-backend/actions/runs/42520612905) is GREEN on backend main `258039b25fb4653c98a179911d9d7f7dcecffd2e`, but it is NOT a staged deployment approval. Matching Frontend baseline is `1cbea70c5921148cba4b343a7fdd6f485d04f21b`.
- Next: operator-approved staging infrastructure, SQL backup/migration/catalog inventory and grants, then FR-13B Tenant employee creation/invitation, full Owner->staff role->UI/API->revoke scenario.
- [Staging runbook and gates](development/FR-13A-STAGING-READINESS.md).

# Foundation Backend — Project State & Handoff

## FR-08B — Tenant Admin scoped workspace starter (2026-11-08)

- PR/CI pending: [Tenant Workspace read contract](development/FR-08B-TENANT-WORKSPACE-STARTER.md). Existing read-only /api/tenant/workspace adds real branchesCount, and new GET /api/tenant/workspace/branches reads only current ActiveTenantId without caller-provided tenant identifiers. Requires authenticated tenant context, readable active tenant. No TenantAdministrator policy or mutation permissions claimed yet.
- Angular companion retains /dashboard after login and introduces read-only Business and Branches pages under tenantSessionGuard. Billing state is still pending_activation placeholder; requested plans are not paid/activated. No SystemAdministrator routes or production database changes.


## FR-08A — System Administrator dashboard (2026-11-08)

- PR/CI pending: [FR-08A contract and Tenant Admin baseline](development/FR-08A-SYSTEM-ADMIN-DASHBOARD.md). Protected dashboard endpoint preserves legacy properties and adds active/inactive business tenant counts (Master excluded), UserType statistics, groups/modules and latest six business records. No synthetic revenue, migrations, BFF or permission changes.
- Angular companion enhances system landing dashboard with true data, responsive UI and failed/retry states. Tenant Admin /dashboard remains a separate future scoped slice.


## FR-07A — one-request System UI permission snapshot (2026-11-08)

- Added SystemAdministrator-only `GET /api/System/UiAccess/me`, returning 80 code-owned live UI decisions (45 PAGE + 35 ACTION) with exact matching active metadata, current-user Master-scoped grants, direct deny precedence and no-store. Existing single-key endpoint retains compatibility; no caller-controlled grants or tenant identity.
- Registered 26 previously missing CRUD/state/lock/sync/membership ACTIONs, all permission-linked and no new menus/grants. Admin/User sensitive actions default inactive. CI gates include updated Identity and disposable SQL Server Registry counts.
- Companion Angular guards all 45 executable pages and uses reusable action controls with one request per navigation; rollout flag remains default-OFF until explicit Registry Apply/grants. API policies remain separately authoritative.
- [Implementation/rollout reference](development/FR-07A-BULK-SYSTEM-UI-ACCESS.md).


## FR-06A — live System UI key checks (2026-11-07)

- **First opt-in route/action pilot:** [FR-06A runbook](development/FR-06A-LIVE-SYSTEM-UI-ACCESS.md). New `GET /api/System/UiAccess/me/{key}` under live `SystemAdministrator` returns one current-user UI permission decision with no-store and no role/user/tenant parameters. It requires verified active root PAGE + optional child PAGE/ACTION, single correct PermissionUiResource link and live System master-role/direct/group grant, direct deny precedence. Unknown/disabled/misconfigured entries fail closed.
- Angular companion guards **Role Permissions** route and Save only behind an explicit default-OFF pilot flag to avoid surprising existing admins before Registry Apply/grant rollout. UI visibility is NOT server API authorization. No BFF, production DB, grants, existing coarse/granular API flag changes. CI pending.


## FR-05A — code-owned nested System UI elements and ACTION metadata (2026-11-07)

- **Backend implementation / CI gate:** [FR-05A runbook](development/FR-05A-NESTED-UI-REGISTRY.md). Registers **32 nested PAGEs + 9 ACTIONs** as code-owned UI resources linked to their exact permission (no nested menus or role/user grants). Admin/User create/edit and related role actions start **inactive** because their write workflows remain unverified. Existing disabled rows are preserved.
- Manifest schema now **v2** (`uiElements` added). Angular must deploy a paired route-only child-pattern index and validate backend/actual executable routes; v1 frontend intentionally blocks Apply against v2 Backend. Existing `SystemAdministrator`/BFF/permission enforcement flags and production data unchanged.
- Preview conflicts fail closed for wrong child UI owner/type/route, duplicate keys, or unrelated PermissionUiResource associations. No SQL Server registry rollout certification yet.


## FR-02B-2 — code-owned catalog covers every System root page (2026-11-07)

- **Backend implementation / CI PR gate:** [FR-02B-2 runbook](development/FR-02B2-SYSTEM-PAGES.md). All **13** real Angular System root pages now have code-owned Feature definitions, `Resource.Code`, scoped view Permission.Code, page key, Route and default permission-bound menu. **82 descriptive endpoint metadata** mappings added for System Admins, Groups, Modules, Tenants, Resources, API/UI resources, MenuCategories/Menus, Dashboard, and the Registry itself. Previous Users/Roles/Permissions mappings remain. `Menus/me` (System/Tenant user nav) and `Tenants/resolve` (AllowAnonymous) are intentionally outside System-only operation metadata.
- Existing `GET /api/System/FeatureRegistry/manifest` exports 13 known features; no HTTP schema change. Preview/Apply controls still require live SystemAdministrator, explicit `Identity:FeatureRegistry:EnableApply` for Apply (default OFF). No automatic permission grants, policy enforcement changes, migrations, BFF or Auth changes.
- **Risk/gates:** Existing manual permissionless menu rows and cross-resource Permission.Code ownership can block preview; review data rather than disable safety validation. Registry addition does **not** repair Admins create/update or certify production DB reconciliation. No live production DB touched.


## FR-02B-1 — backend-owned manifest + third System Users feature (2026-11-07)

- **PR / CI gate:** [FR-02B-1 backend contract](development/FR-02B1-USERS-MANIFEST.md). Source-owned Feature Registry now declares **System Users** in addition to Roles and Permissions; all 13 `Users` endpoints carry stable descriptive metadata. `GET /api/System/FeatureRegistry/manifest` is read-only, SystemAdministrator-only, schemaVersion 1 and contains stable feature/page keys, required view Permission.Code, backend route and resource identity. Angular will validate these routes at runtime before offering Apply, instead of copying permission codes in its TypeScript manifest.
- Preview/Apply security remains **unchanged**: no default enablement, no automatic role/user grants, no enforcement change from existing coarse SystemAdministrator, no production migration. Existing `USERS.READ` owned by a different Resource must block Apply. Real DB inventory + SQL Server sync tests still required.


## FR-02A — code-owned Feature Registry preview and additive apply (2026-11-07)

- **First executable backend vertical (PR pending CI):** [FR-02A runbook](development/FR-02A-FEATURE-REGISTRY.md). `FeatureRegistryDefinitions` declares System Roles/Permissions code-owned module/resource/UI page/read permission/menu keys. Explicit endpoint metadata on Roles/Permissions CRUD/read routes provides stable API keys independently of URL. The `FeatureRegistrySyncService` plans read-only differences and optionally inserts missing definitions/links. No automatic grants or deletions. Admin-only `GET /api/System/FeatureRegistry/preview`; `POST .../apply` requires `Identity:FeatureRegistry:EnableApply=true` (OFF by default).
- **Safety:** Existing disabled/manual records are preserved and key conflicts block Apply. Legacy ApiResource route-derived sync no longer auto-deprecates/reactivates records. Only Roles/Permissions are in this first manifest; no general feature auto-generation claim. A deployed database must be inventoried before production apply.
- **Dependency:** separate Angular build-time manifest and admin Preview/Apply UI PR; current-user Sidebar intentionally remains grant-filtered (new `*.READ` grants require explicit approval). No BFF or authorization enforcement flag changes.


## FR-04A — current-user navigation from live DB (2026-11-07)

- **Backend PR under test:** [Current-user navigation contract](architecture/current-user-navigation.md). `GET /api/System/Menus/me` requires an authenticated principal (System or Tenant), never a caller-supplied RoleId. Bounded direct/role/group grant aggregation with user deny precedence, active menu/category filter, strict parent tree and route-scope checks. Empty catalog returns []; invalid identity/scope returns 403; unauthenticated returns 401. Legacy SystemAdministrator menu administration and roleId GET remain unchanged.
- **Deployment note:** production startup does not seed static Menu resources; new frontend intentionally will not display admin fallback links on empty/error. The System administrator must configure menu categories/menus and explicitly grant permissions. No DB migrations or auto grants.
- **Not yet complete:** official Menu.Scope/feature registry, TenantPermissionRestriction semantics, server-enforced Angular feature route catalog, production DB inventory and BFF migration.


## FR-01B — shared live permission evaluator and token snapshot code (2026-11-07)

- **PR pending CI:** [FR-01B architecture & test handoff](development/FR-01B-PERMISSION-EVALUATOR.md). Existing System/Tenant `Require*Permission` authorization handlers now use a common `EffectivePermissionEvaluator` for active account/scope, explicit direct deny, direct grant and active role/group grants, keeping API authorization DB-authoritative.
- Adds additive access-token `permission_code` values (stable `Permission.Code`) while preserving existing legacy display-name `permission` claims; user denies are excluded from both snapshots. Neither claim authorizes protected APIs.
- Still pending: data inventory in real SQL Server, TenantPermissionRestriction interpretation, safe current-user navigation, feature manifest/discovery, Sidebar fallback removal and BFF browser migration. No EF migrations, role grant writes or auth policy cutover in this slice.


## FR-01A — Menu read contract and read-only SQL catalog inventory (2026-11-07)

- **Implementation in progress / PR CI gate:** [FR-01A handoff](development/FR-01A-IDENTITY-CATALOG-AND-MENU-CONTRACT.md), [read-only SQL inventory](sql/fr-01-identity-catalog-inventory.sql). Canonical Menu GetById/PagedList responses now include `parentId`, `route`, `externalUrl` (plus `parentTitle` in paged list); old response `pMenuId` / `url` / `pMenu` fields remain as read-only deprecated aliases. Category menu rows now include `externalUrl`. No grants, migrations, current-user navigation, BFF or System permission enforcement changes.
- **Source verification:** Historical `DefaultSeedData` module/resource/menu records are *not* seeded on normal startup; these calls are commented in `DefaultIdentitySeedContributor`. Production Permission.Code uniqueness migration requires explicit reviewed opt-in; script reports its status. Script has not run against a live deployment; do not treat its checks as verified DB facts.
- **Security next:** audit the Permission.Code/Name claim discrepancy, menu parent cycles, persisted key collisions, and role/group/tenant effective-grant resolution before introducing automatic catalog reconciliation.


## Feature Registry FR-00 — draft implementation (2026-11-07)

- **Proposed architecture and phased handoff:** [Feature Registry & Authorization](architecture/feature-registry-authorization.md). Existing Modules/Resources/API+UI Resources/Permissions/Menus, scoped System/Tenant authorization and Angular Sidebar are inventoried. This is not a completed registry or automatic grant system.
- **Draft code slice:** Menu update now targets the Menu entity rather than Users, accepts current frontend `parentId`/`route`/`externalUrl` names, and has handler regression tests. No database migration, menu query/Sidebar behavior, grant enforcement, auth cookie or BFF runtime changes.
- **Next gates:** CI verification; inspect actual DB catalog/seeding and read DTO compatibility; Permission.Code vs Name consolidation; user-scoped navigation evaluator; idempotent endpoint/UI discovery and frontend manifest, with deny-by-default and no auto grants.


## Identity UI ↔ BFF login contract v1 — 2026-11-06

- **Contract frozen:** [Identity UI ↔ BFF login](architecture/identity-ui-bff-login-contract.md): use the **existing Angular SignInComponent** on an Identity-only origin with same-origin `/api/system/Auth/interactive-login` (CAPTCHA, XSRF, Identity cookie), resume Code+PKCE, then BFF host-scoped opaque Cookie and future Angular `/bff/session`. No second Angular project or Razor login required.
- **Defense in depth:** Backend BFF rejects reserved Identity/auth/infrastructure hosts as tenant workspaces, and Angular will not bootstrap TenantContext on the Identity origin. Automated host-boundary tests added.
- **Scope:** contract and host hardening only; legacy Angular Password Grant/localStorage/interceptors remain unchanged, BFF disabled by default. Full browser/SQL HA/proxy/keyring/upstream token revocation and BFF-06/07 release gates remain open.


## Verified merged BFF-05.4a local logout-all and BFF-05.5b.3 SQL cleanup — 2026-11-06

- **Merged SQL cleanup:** [PR #34](https://github.com/sholehamed/foundation-backend/pull/34) at `576b7941fada322fc32def221836ff211f469574`, [final CI #42218028623](https://github.com/sholehamed/foundation-backend/actions/runs/42218028623) full build, **94 BFF + 60 Identity + 12 real SQL Server** tests. Bounded maintenance-only DELETE retains every ticket row for 48 hours after original absolute expiry and preserves fresh active/revoked rows. Must schedule externally with separate maintenance principal.
- **Merged account-local logout-all:** [PR #35](https://github.com/sholehamed/foundation-backend/pull/35) at `18775eeb80ea6740d37943b516060172290e882e`, [final PR-head CI #42219967286](https://github.com/sholehamed/foundation-backend/actions/runs/42219967286) full build, **96 BFF + 60 Identity + 17 real SQL Server** tests (173 total, zero failures). `POST /bff/auth/logout-all` uses host-bound cookie, Origin/CSRF and verified `sub` to SQL-revoke all **existing** sessions for the subject. SQL v1→v2 migration revokes legacy active tickets missing ownership, adds checked subject binding/index and passes real SQL upgrade/security tests.
- **Not a full signout guarantee:** upstream Identity SSO cookie, access/refresh token families and future new OIDC authorizations are NOT revoked by this local endpoint; refresh token concurrency, live OIDC callback races, production Data Protection keyring encryption/rotation, SQL HA/failover, real browser/proxy and Angular BFF-06 plus BFF-07 remain. Browser opt-in default `Identity:Bff:Enabled=false`; Angular Password Grant/localStorage unchanged.
- Handoff: [logout-all and schema migration](development/BFF-05-4A-LOGOUT-ALL.md), [SQL cleanup operations](development/BFF-05-5C-SQL-CLEANUP.md), [BFF-05 master #20](https://github.com/sholehamed/foundation-backend/issues/20), [frontend BFF-06 #10](https://github.com/sholehamed/foundation-frontend/issues/10). Both initial F00 #2 PRs remain open.


## BFF-05.4a account-scoped SQL logout-all — draft PR #35 (2026-11-06)

- [PR #35](https://github.com/sholehamed/foundation-backend/pull/35) introduces `POST /bff/auth/logout-all`, requiring a live host-bound cookie, same-origin/CSRF and using the **authenticated OIDC `sub` only**. Web SQL adapter revokes all existing subject-owned sessions in atomic SQL UPDATE, clears encrypted ticket ciphertext, and denies late renewals. `IBffAccountSessionRevoker` keeps BFF module independent of SQL implementation.
- SQL migration adds `SubjectId` ownership/index/trusted CHECK and **intentionally revokes legacy active tickets missing a trustworthy owner**, requiring re-login on upgrade. Existing SQL store checks protected ticket subject against DB owner. Unit HTTP + real disposable SQL Server tests added; **latest PR-head CI pending**. [Detailed security and migration note](development/BFF-05-4A-LOGOUT-ALL.md).
- This is browser BFF **local logout-all of existing sessions**, not upstream OpenIddict refresh-token/SSO revocation, not prevention of future logins. BFF still disabled by default; Angular Password Grant/localStorage unchanged; real browser, proxy, production HA and keyring gates remain.


## BFF-05.5b.3 — SQL cleanup implementation checkpoint (2026-11-06)

- [Draft PR #34](https://github.com/sholehamed/foundation-backend/pull/34) implements `SqlServerBffSessionCleanup.DeleteExpiredBatchAsync`: parameterized maximum 1,000 rows, 48-hour post-absolute-expiry retention using SQL Server UTC, bounded SQL DELETE; no automatic web-host job and no additional runtime database privileges. An operator-managed maintenance SQL principal requires separate DELETE rights.
- Real SQL Server integration tests exercise old revoked/expired rows versus young active/revoked rows, batch limits/idempotence, inside-retention protection and no stale renewal resurrection. Latest PR-head CI remains the acceptance gate; document detailed [operations handoff](../development/BFF-05-5C-SQL-CLEANUP.md).
- This is NOT the BFF-05.4 logout-all/Identity token revocation or production SQL HA/keyring/browser certification; BFF opt-in stays off and Angular unchanged.


## Verified BFF-05.5b.2 — merged SQL Server transactional atomic store (2026-11-06)

- **Merged:** [PR #32](https://github.com/sholehamed/foundation-backend/pull/32) at `c2ebf86053c17fb87309ec630f932ac32701a127`. [Final latest-head CI #42215944644](https://github.com/sholehamed/foundation-backend/actions/runs/42215944644) passed solution build, **94/94 BFF + 60/60 Identity + 9/9 real SQL Server integration tests** (163 total, zero failures).
- **Shipped:** SQL Server provider implements `IBffAtomicTicketStore` in Web: server-protected opaque cookie ticket, SQL conditional UPDATE-only renewal against existing active session, atomic UPDATE revoked-row + ciphertext removal, 24-hour immutable absolute session lifetime, manual idempotent SQL schema and Startup Guard rejecting missing/untrusted constraints. Feature requires explicit `Identity:Bff:SqlServer:Enabled` and a secret-managed `ConnectionStrings:BffSessions`; global BFF opt-in remains `false`.
- **Verified integration:** disposable SQL Server 2022 CI service, forced concurrent row-lock renew-vs-logout, replay rejection, expiry/tamper/outage/schema tests, **two independent TestServers sharing persisted Data Protection keyring plus real SQL database**, cookie works cross-host but fails after logout and even after both hosts restart. This is not proof of production SQL Always On/failover or HTTPS browser/reverse-proxy behavior.
- **Still blocked:** production encrypted/rotated shared keyring, SQL HA/failover/cleanup and deployment probes, logout-all and upstream Identity refresh-token revocation, coordinated refresh rotation, OIDC callback race, real browser/SQL Server/proxy E2E, Angular BFF-06 migration and final BFF-07 legacy-client/password-grant closeout. **Do not enable BFF in Production or migrate Angular yet.**
- Follow [SQL implementation and deployment runbook](development/BFF-05-5B-SQL-ATOMIC-SESSION.md), [backend master #20](https://github.com/sholehamed/foundation-backend/issues/20), [SQL production certification #30](https://github.com/sholehamed/foundation-backend/issues/30), [frontend BFF-06 #10](https://github.com/sholehamed/foundation-frontend/issues/10). Original F00 PR #2 remains open and unchanged.


## BFF-05.5b.2 SQL Server implementation — PR #32 code-head CI verified (2026-11-06)

- [PR #32](https://github.com/sholehamed/foundation-backend/pull/32) contains the **real** Web-owned `SqlServerBffAtomicTicketStore`: an opaque protected Ticket INSERT, SQL conditional UPDATE-only renewal and atomic revoked-row UPDATE, 24h fixed absolute deadline, idempotent manual SQL schema, fail-closed **Startup schema/constraint preflight**, no Redis dependency. Production registration still requires explicit `Identity:Bff:SqlServer:Enabled` and secret-managed `ConnectionStrings:BffSessions`.
- [Code-head CI #42215510887](https://github.com/sholehamed/foundation-backend/actions/runs/42215510887) reports **9 real SQL Server tests** passing; includes cross-connection row-lock race and **two independent TestServers sharing a persisted Data Protection keyring**: cookie works across hosts, logout on one blocks the other and a restarted third host. The earlier code-head [CI #42214992134](https://github.com/sholehamed/foundation-backend/actions/runs/42214992134) ran all **94 BFF + 60 Identity** tests and full solution build. Await latest documentation-head CI before merge.
- Security limitation: the SQL Server instance is a disposable CI service, **not a proven production failover network topology**. Keyring sharing in the test does **not** certify encrypted, rotated production key storage. Existing BFF disabled; frontend Password Grant/localStorage unchanged. Logout-all/upstream token revocation, refresh coordination and browser/proxy deployment acceptance remain open. [Detailed SQL handoff](development/BFF-05-5B-SQL-ATOMIC-SESSION.md), [BFF master #20](https://github.com/sholehamed/foundation-backend/issues/20), [SQL provider #30](https://github.com/sholehamed/foundation-backend/issues/30).


## Active BFF-05.5b.2 SQL Server transactional session store — 2026-11-06

- [Draft PR #32](https://github.com/sholehamed/foundation-backend/pull/32) introduces the actual `SqlServerBffAtomicTicketStore` Web adapter behind `IBffAtomicTicketStore`: protected server-side tickets, 24h original lifetime, one-row conditional **UPDATE-only** renewal and atomic SQL revocation (payload cleared) on the same primary; independently managed idempotent DDL and explicit secret-managed SQL connection.
- New `tests/Foundation.Bff.SqlServerTests` runs disposable SQL Server database tests (cross-connection renew/revoke race, expired/revoked/missing keys, tampering/SQL failure, deadline cap, schema idempotence). Separate SQL Server 2022 GitHub Actions service job added. **Await final latest-HEAD CI before claiming merged.**
- SQL provider requires `Identity:Bff:SqlServer:Enabled=true` *and* `Identity:Bff:Enabled=true`, with `ConnectionStrings:BffSessions`; no automatic prod DDL. Existing BFF remains OFF by default, Angular Password Grant/localStorage/guards untouched. Shared Data Protection keyring and production failover/browser gates still open. See [technical rollout and limitations](development/BFF-05-5B-SQL-ATOMIC-SESSION.md), [master #20](https://github.com/sholehamed/foundation-backend/issues/20), [SQL #30](https://github.com/sholehamed/foundation-backend/issues/30).


## Verified BFF-05.5a + BFF-05.5b.1 merged checkpoint — 2026-11-06

- **Merged race mitigation:** [PR #28](https://github.com/sholehamed/foundation-backend/pull/28) at `ccbaddcad3d8e7c413a505ba7c8e2e0a04e59298`. [Final PR-head CI #42210307369](https://github.com/sholehamed/foundation-backend/actions/runs/42210307369) passed solution build, **88 BFF + 60 Identity tests**. Protected session tickets carry a 24-hour original-issuance absolute ceiling, revoked keys receive a 48-hour tombstone, and Retrieve/Renew validate revocation before/after cached reads/writes; deterministic concurrency tests prove the single-shared-consistent-cache interleavings.
- **Merged non-Development guard:** [PR #29](https://github.com/sholehamed/foundation-backend/pull/29) at `b9a6bb77cedcc55511f2d45edff28667b20f5734`. [Final PR-head CI #42211389822](https://github.com/sholehamed/foundation-backend/actions/runs/42211389822): full solution build, **94 BFF + 60 Identity tests**. Opt-in BFF startup in Staging/Production now rejects a plain `IDistributedCache` without a registered singleton `IBffAtomicTicketStore`; its provider is actually selected for Cookie `ITicketStore`. Development/default-off behavior remains unchanged.
- **Important:** `IBffAtomicTicketStore` is a CONTRACT, not a concrete reviewed provider. **No SQL Server/Redis atomic backend exists yet**; an implementation registered by DI alone is not proof of its atomicity. [Issue #30](https://github.com/sholehamed/foundation-backend/issues/30) tracks transactional SQL Server provider, cross-instance consistency/eviction/failover and Data Protection keyring tests. BFF browser opt-in remains OFF until these and other BFF-05 gates are satisfied.
- **Remaining:** logout-all/upstream OIDC token revocation, refresh coordination, idle policy, actual SQL Server/browser/trusted reverse proxy and multi-node E2E, stamp-change race during callback. Angular Password Grant/localStorage/guards unchanged; [frontend BFF-06 #10](https://github.com/sholehamed/foundation-frontend/issues/10) blocked, initial F00 PR #2 untouched.
- **Handoff docs:** [renewal races](development/BFF-05-5-TICKET-REVOCATION-RACES.md), [startup guard](development/BFF-05-5B-ATOMIC-PROVIDER-GATE.md), [master BFF-05 issue](https://github.com/sholehamed/foundation-backend/issues/20).


## BFF-05.5 progression — merged race mitigation, atomic provider gate under review (2026-11-06)

- **Merged BFF-05.5a:** [PR #28](https://github.com/sholehamed/foundation-backend/pull/28) at `ccbaddcad3d8e7c413a505ba7c8e2e0a04e59298`; [final PR-head CI #42210307369](https://github.com/sholehamed/foundation-backend/actions/runs/42210307369) green: build, **88 BFF + 60 Identity tests**. Tombstone-before-delete, before/after cached reads/renewals and bounded 24-hour session expiry prevent late renewal resurrection under one strongly consistent shared cache. This is *not* a verified Redis/SQL multi-node atomic provider.
- **Draft BFF-05.5b.1:** [PR #29](https://github.com/sholehamed/foundation-backend/pull/29) adds `IBffAtomicTicketStore` and refuses enabled BFF startup in **Staging/Production** unless a singleton atomic provider is explicitly registered and used for `ITicketStore`. A SQL provider need not also register `IDistributedCache`. `Development` can still use the tombstone-backed default; disabled BFF is unchanged. Latest-head CI pending; a test-only interface stub does not satisfy Production security review.
- **Implementation outstanding:** [Issue #30](https://github.com/sholehamed/foundation-backend/issues/30) tracks the real transactional **SQL Server** atomic session provider, cross-instance interleavings, durability/eviction/failover, restart/keyring/SQL tests. No real provider is included in PR #29; BFF is **still disabled by default**, Angular Password Grant/localStorage unchanged. Backend [Issue #20](https://github.com/sholehamed/foundation-backend/issues/20) remains the master gate; original F00 PR #2 remains open.


## Active BFF-05.5a — no ticket resurrection during logout/renewal (draft PR #28)

- [PR #28](https://github.com/sholehamed/foundation-backend/pull/28) adds a **server-side 48-hour revocation tombstone** and immutable **24-hour maximum ticket lifetime** to BFF's `ITicketStore`. Renewal checks whether the original key is active and verifies revocation after the write; reads check revocation before and after cached bytes. This prevents stale read/write resurrection on a single strongly-consistent shared cache.
- New deterministic two-store concurrency tests cover delayed renewal writes, delayed stale reads, replay, absolute expiry and invalid renewal baseline. **Await latest PR-head CI before marking implemented.**
- `IDistributedCache` itself provides no atomic compare-and-set or eviction guarantees. This is **not** a production-ready multi-node/Redis/SQL Server/failover acceptance. See [security design and limitations](development/BFF-05-5-TICKET-REVOCATION-RACES.md); prove provider-level atomicity/consistency or use a dedicated atomic backend before enabling BFF.
- Baseline: merged [PR #26](https://github.com/sholehamed/foundation-backend/pull/26) real OIDC Web validator test, CI 82 BFF + 60 Identity. Backend BFF opt-in still disabled; Angular Password Grant/localStorage unchanged. Original F00 PR #2 remains open.


## Verified merged BFF-05.3 callback gate — 2026-11-06

- **Merged:** [PR #26](https://github.com/sholehamed/foundation-backend/pull/26) at `fa76eb952d99353d1f66e9249ec63cc6e9ef4e21`; [CI #42208199510](https://github.com/sholehamed/foundation-backend/actions/runs/42208199510) passed solution build, **82/82 BFF** and **60/60 Identity** tests.
- Real Foundation OpenIddict two-TestServer Integration runs the **Web-owned live DB validator during OIDC callback and later cookie reads** for System and Tenant. Stamp rotation invalidates the protected server-side ticket; restoring the stamp does not resurrect the cookie.
- **Not yet proven:** stamp-change race between token exchange and BFF baseline capture, outage behavior, distributed ticket renew-vs-logout, upstream token revocation/logout-all, absolute/idle expiry, real browser/SQL Server/trusted proxy and multi-node keyring/cache.
- BFF remains disabled by default; Angular Password Grant/localStorage unchanged. Continue from [backend Issue #20](https://github.com/sholehamed/foundation-backend/issues/20) and [frontend Issue #10](https://github.com/sholehamed/foundation-frontend/issues/10). The original F00 PR #2 remains open.


## BFF-05.3 real callback + Web adapter integration gate — 2026-11-06

- [Draft PR #26](https://github.com/sholehamed/foundation-backend/pull/26) runs actual Foundation OpenIddict Identity and BFF middleware on two independent TestServers **with the production Web-owned live Identity validator wired into the BFF host**. It shares an explicit EF InMemory root only for test DB access, not an issuer/token mock.
- [PR code-head CI #42207855770](https://github.com/sholehamed/foundation-backend/actions/runs/42207855770): build green, **82 BFF + 60 Identity** tests. New System and Tenant tests prove real code+PKCE/metadata/JWKS/UserInfo/callback produces a server-side ticket with captured DB baseline; a subsequent stamp change makes `GET /bff/session` return 401, and reverting the stamp cannot resurrect the removed ticket.
- This closes the **two-TestServer callback + Web live validator integration test**, not the deployed-browser/SQL Server integration or concurrency guarantee. A very narrow race between IdP exchange and BFF baseline capture, concurrent session renewal/logout, upstream token revocation/logout-all, absolute/idle expiry, real browser/proxy and multi-node shared cache/keyring remain release gates.
- `Identity:Bff:Enabled=false` remains default; Angular continues legacy Password Grant/localStorage without runtime changes. Merge only after final documentation-head CI.


## Current BFF-05.3 merge checkpoint — 2026-11-06

- **Merged:** [BFF-05.2 PR #23](https://github.com/sholehamed/foundation-backend/pull/23), commit `88239b7d3454fd03b87aa7bc20e70d1a820b088a` (server-side CSRF bootstrap, current-cookie logout, 82 BFF / 50 Identity tests).
- **Merged:** [BFF-05.3 PR #24](https://github.com/sholehamed/foundation-backend/pull/24), commit `a676919d0d7d777c2c72895c7e1c9a9cd7987aa7`. [Final PR-head CI #42206724851](https://github.com/sholehamed/foundation-backend/actions/runs/42206724851): solution build green, **82/82 BFF and 58/58 Identity tests**.
- New Web-host Identity bridge captures security stamp and scoped role/permission fingerprint in the **server-protected ticket**, and validates current User/Tenant/lockout/stamp/role/grant state on each BFF cookie read. Missing baseline or invalid DB authorization fails closed; tested directly and via HTTP CookieAuthentication middleware. BFF module itself has no Identity DB reference.
- **Remaining gate:** actual OIDC callback + Web adapter integration must be tested with the real OpenIddict two-host flow. Separate release blockers remain: race between code exchange and stamp capture, concurrent logout/renewal, durable shared session store and keyring, upstream token revocation/logout-all, expiry, real SQL Server/browser/trusted proxy and Angular BFF-06.
- `Identity:Bff:Enabled=false` remains the default, existing Angular Password Grant/localStorage untouched. Initial F00 baseline PR #2 stays open.


## BFF-05.3 verified adapter checkpoint — 2026-11-06

- [Draft PR #24](https://github.com/sholehamed/foundation-backend/pull/24) includes a BFF-only abstract validation contract and a Web-host Identity DB adapter. The production Web composition registers OIDC ticket capture and cookie `OnValidatePrincipal` live user/Tenant/security-stamp/grant checks only when `Identity:Bff:Enabled=true`.
- [Code-head CI #42206181883](https://github.com/sholehamed/foundation-backend/actions/runs/42206181883): **82 BFF + 58 Identity tests**, full solution build passed. Added direct adapter DB tests and HTTP CookieAuthentication tests for System/Tenant, security-stamp rotation, tenant deactivation and removed-session replay.
- The HTTP tests issue a **test-only baseline ticket**, not an actual OIDC callback; the new OIDC `OnTicketReceived` adapter hook still needs true end-to-end verification with the live Foundation Identity server, as do stamp-exchange callback races.
- Do **not** assert production readiness: BFF flag stays disabled, Angular legacy Password Grant/localStorage unchanged. Logout-all, remote token revocation, renewal races, actual browser, SQL Server and multi-node infrastructure still pending.


## Active BFF-05.3 Web-host live validation (draft PR #24, CI pending)

- [BFF-05.2 PR #23](https://github.com/sholehamed/foundation-backend/pull/23) merged at `88239b7d3454fd03b87aa7bc20e70d1a820b088a`; latest PR-head [CI #42203066912](https://github.com/sholehamed/foundation-backend/actions/runs/42203066912): build, **82 BFF + 50 Identity** tests successful.
- [Draft PR #24](https://github.com/sholehamed/foundation-backend/pull/24) adds a narrow BFF contract and Web-owned Identity DB adapter for live account, tenant, security-stamp and scoped-grant revalidation. Local session snapshots remain exclusively in the server-side protected ticket, never the browser.
- See [BFF-05.3 design](development/BFF-05-3-LIVE-IDENTITY.md). Do not merge before current-head CI and Web event integration tests. `Identity:Bff:Enabled` remains false, Angular legacy login unchanged; distributed races, upstream token revocation, logout-all, browser E2E and SQL Server remain gated.

> Last verified: 2026-11-06  
> Repo: `sholehamed/foundation-backend` · integration branch: `main`  
> **Read this first in a new chat.** Always verify current GitHub head/CI before writing.

## Current checkpoint — BFF-05.2 verified implementation (2026-11-06)

- [PR #22](https://github.com/sholehamed/foundation-backend/pull/22) merged the real Foundation OpenIddict Identity + BFF two-TestServer flow at `f53c5e04d899b09d33133f57beafe234b671501c`.
- [PR #23](https://github.com/sholehamed/foundation-backend/pull/23) implements cookie-only CSRF bootstrap and current-session logout. Code head `2abab6b3973580298db01005b97f617f1014ff5f` passed [CI 42202732554](https://github.com/sholehamed/foundation-backend/actions/runs/42202732554): full solution build, **82 BFF + 50 Identity tests**, zero failures. Final documentation-head checks and merge state are available on the PR.
- Verified System/Tenant lifecycle through real OpenIddict code exchange: CSRF bootstrap, rejected missing-token logout, successful local ticket removal, replay rejection, preservation of a second ticket for the same user, and fresh login via the separate Identity cookie. Boundary tests also cover GET logout refusal, missing antiforgery cookie, cross-host CSRF bootstrap, HTTPS, Referer fallback, Origin/port and Fetch Metadata.
- This is **current-session local logout only**. It does not revoke upstream tokens, other sessions or the Identity cookie. Two-TestServer tests exclude the interactive password/CAPTCHA UI, browser cookie enforcement, SQL Server and deployed infrastructure.
- Frontend handoff [PR #12](https://github.com/sholehamed/foundation-frontend/pull/12) merged at `73b5d9ba46425a8951d7eda81c0573559d4ffc34`, after [CI 42202665599](https://github.com/sholehamed/foundation-frontend/actions/runs/42202665599) passed tests and build. Angular runtime still uses legacy Password Grant/localStorage. BFF remains disabled by default.
- **Next:** live user/tenant/security-stamp/grant revalidation at the BFF cookie boundary; then account-wide/upstream revocation, bounded idle/absolute lifetime and refresh coordination. Test logout-versus-renewal concurrency: the current cache store's unconditional renewal must not be treated as proof against ticket resurrection during concurrent requests.
- **Remaining release gates:** durable shared cache/keyring, restart/outage behavior, trusted proxy and SQL Server, real System/Tenant browser E2E, then Angular BFF-06 migration and legacy-client closeout. Both F00 PRs remain open.
- The sections below preserve historical checkpoints. This current checkpoint supersedes their old pending/merged status descriptions.


## Earlier merged checkpoint — BFF-05.1 (2026-11-06)

- **Merged:** [BFF-04.3 PR #18](https://github.com/sholehamed/foundation-backend/pull/18) at `3f90363227ed5497c82a378c9f73093c46adcad4`; [CI 42143363623](https://github.com/sholehamed/foundation-backend/actions/runs/42143363623) passed signed fake-issuer OIDC callback tests.
- **Merged:** [BFF-05.1 PR #19](https://github.com/sholehamed/foundation-backend/pull/19) at `bcfc109488be75981dee7f3aead91e1bca38d976`; [latest PR-head CI 42194597675](https://github.com/sholehamed/foundation-backend/actions/runs/42194597675): full solution Build (0 errors; 263 pre-existing warnings), **58/58 BFF** and **42/42 Identity** tests passed.
- **What now exists (opt-in):** GET `/bff/session`, explicitly cookie-scheme authenticated (bearer alone is 401); safe no-store System/Tenant JSON; current-host/callback/tenant binding checked on every session read. 401 anonymous/invalid, 403 host mismatch. Fake test cookie issuance is not a real Identity↔BFF E2E.
- **Still blocked:** real two-host OpenIddict Identity↔BFF E2E ([Issue #12](https://github.com/sholehamed/foundation-backend/issues/12)); CSRF/logout/revocation/live user and tenant DB checks and durable infrastructure ([Issue #20](https://github.com/sholehamed/foundation-backend/issues/20)). `Identity:Bff:Enabled=false` by default. Frontend BFF-06 ([Issue #10](https://github.com/sholehamed/foundation-frontend/issues/10)) is not started; Angular Password Grant/localStorage are unchanged.
- **Two F00 baseline PRs remain open by explicit decision.** All later slices require independent PR, tests and documentation. This section supersedes older snapshots that call PR #16 or #18 Draft.

## Earlier merged checkpoint — BFF-04.2

- **Merged to `main`:** [PR #16](https://github.com/sholehamed/foundation-backend/pull/16) at `c4f6a81c302f3524bed3e58931221874be16928f`. Last verified PR-head [CI 42140557977](https://github.com/sholehamed/foundation-backend/actions/runs/42140557977): **46/46 BFF** and **42/42 Identity** tests plus full solution build passed. This supersedes historical draft statuses lower in this file.
- **Included:** opt-in BFF OIDC Authorization Code + PKCE challenge, configured callback middleware, protected server-side ticket store and isolated Secure/HttpOnly host-only cookie scheme; Identity code-grant token endpoint passthrough and current-user revalidation; strict host-derived System/Tenant login context and validated return paths. Broad CORS credentials were not enabled.
- **Still not accepted as complete BFF-04:** no real two-host OIDC callback E2E or actual cookie-issuance/browser verification. `Identity:Bff:Enabled` is **false by default**. Do not enable in production or migrate existing Angular token storage on this evidence alone.
- **Next task:** implement authentic Identity Server ↔ BFF callback integration tests covering protected browser cookie, invalid/missing nonce/state/correlation, code replay, account/tenant mismatch and logout/expiration. Then BFF-05 (`/bff/session`, CSRF, revocation, fresh host/context validation) and BFF-06 Angular migration after cross-repo E2E.
- **Frontend dependency:** [Frontend PR #8](https://github.com/sholehamed/foundation-frontend/pull/8) merged at `e94c144e4293faa297290aa608c5e7409aeaa1c2`, 22 unit tests + development build passed; it handles only same-origin Identity UI continuation and preserves legacy Password Grant.
- **Code map and test/deployment instructions:** [BFF-04.2 implementation handoff](development/BFF-04-IMPLEMENTATION.md).

## Approved architecture

- .NET 10 Modular Monolith. `src/Domain`, `src/Application`, `src/Infrastructure` and `src/Web` are for the consuming application; `src/modules` and shared projects are future reusable libraries.
- `Modules.System.Identity` owns users, tenants, roles, permissions, OpenIddict Authorization Server. `Modules.System.Bff` is a separate optional browser-facing confidential OIDC client and server-side browser session boundary; `src/Web` composes them.
- Target browser pattern: **Full BFF**, Authorization Code + PKCE, server-held access/refresh/ID tokens, host-only Secure HttpOnly cookie `__Host-Foundation.Bff`. Identity Provider's separate cookie is `__Host-Foundation.Identity`.
- System accounts and Tenant accounts require explicit identity context and database-based tenant/permission checks. No cross-tenant grants from any role, group or nested permission.
- Legacy Password Grant stays available until existing consumers and Angular migrate successfully. Angular must **not** switch until BFF callback/session/revocation and real E2E pass.
- Reusable Angular library extraction is **postponed** until app features are complete and stable.

## Git & test checkpoint

| Milestone | State | Evidence |
| --- | --- | --- |
| BFF-01: protected server-side ticket store | **Merged** | [PR #6](https://github.com/sholehamed/foundation-backend/pull/6), commit `80f15d6c5bf59e6c557561fe97a2efbf1b5ee74a` |
| BFF-02: Identity interactive login cookie | **Merged** | [PR #8](https://github.com/sholehamed/foundation-backend/pull/8), commit `4a798da96c735be0bc0823c82c02a495f4a9c4a8` |
| BFF-02 handoff docs | **Merged** | [PR #10](https://github.com/sholehamed/foundation-backend/pull/10), commit `02a29943c178d2e835577a329d01c69f68560cad` |
| BFF-03: OpenIddict Authorization Code + PKCE and scoped validation | **Merged** | [PR #11](https://github.com/sholehamed/foundation-backend/pull/11), commit `c201e4c67d18c9d7896887a688eb31d3340d7930` |
| BFF-03 final-head CI | **Green** | [GitHub Actions 42130167589](https://github.com/sholehamed/foundation-backend/actions/runs/42130167589): solution Restore+Build, **6 BFF tests** and **41 Identity tests** passed |
| BFF-04: confidential client callback and BFF session | **Security primitive merged; callback/session not implemented** | [PR #14](https://github.com/sholehamed/foundation-backend/pull/14), [Issue #12](https://github.com/sholehamed/foundation-backend/issues/12) |
| BFF-05: session API/CSRF/revocation | **Not implemented** | [Roadmap](PROJECT-ROADMAP.md) |
| BFF-06: Angular migration | **Not started** | [Frontend status](https://github.com/sholehamed/foundation-frontend/blob/main/docs/PROJECT-STATE.md) |

## Implemented modules / code map

### BFF-01

- `src/modules/Modules.System.Bff/DistributedBffTicketStore.cs`: ASP.NET Core `ITicketStore` with `IDistributedCache`, protected ticket bytes via Data Protection, opaque randomly generated session IDs, expiry and server-side deletion.
- `BffServiceCollectionExtensions.cs`: opt-in registration of an independent BFF cookie scheme, antiforgery services.
- **Not composed into `src/Web` yet**. No application BFF browser cookie is emitted.

### BFF-02

- `Modules.System.Identity/Web/Util/IdentityServerCookieDefaults.cs`: dedicated Identity Provider cookie.
- `GET /api/system/Auth/csrf`: CSRF bootstrap; `POST /api/system/Auth/interactive-login`: CAPTCHA and anti-forgery verified sign-in; emits only Identity Server cookie, no OAuth token.
- Login rate limit. Shared credential authenticator for legacy Password Grant and interactive Identity session.

### BFF-03

- `Modules.System.Identity/Application/Services/IdentityPrincipalFactory.cs`: builds roles and permissions scoped to **master System tenant** or **owning Tenant**, including nested RolePermission/GroupRole.
- `IdentitySessionValidator.cs`: checks active user and tenant, cookie account context and current security stamp; rehydrates grants to avoid stale cookie permissions.
- `Infrastructure/Persistence/OpenIddictBffClientSeeder.cs`: optional confidential BFF client with exact registered callbacks, PKCE, external client secret; refuse silent changes to existing client registrations. `Identity:Bff:Enabled` remains false by default.
- `Web/Endpoints/Connect.cs`: OpenIddict GET `/api/connect/authorize`; explicit Identity cookie; restrict to enabled confidential BFF client, scoped identity context, validated redirect/scope/PKCE. OpenIddict issues one-time Authorization Code.
- `tests/Shinera.Identity.Tests/IdentitySessionAndScopeTests.cs` and `OpenIddictAuthorizationHttpTests.cs`: revoked user/stamp/tenant, cross-scope role isolation; TestServer Code+PKCE exchange, replay rejection, wrong verifier, unknown client, bad redirect, unknown scope, missing PKCE, account context and POST refusal.
- `prompt=login`, `max_age` currently fail closed rather than loop; GET-only authorization endpoint. See [BFF-03 implementation record](development/BFF-03-IMPLEMENTATION.md) for limitations.

## BFF-04 in progress — OIDC integration (opt-in, incomplete)

- First security slice merged to `main`: [PR #14](https://github.com/sholehamed/foundation-backend/pull/14), commit `53732242450bd90c206e29738007372bbe2dde1b`, [CI 42131501508](https://github.com/sholehamed/foundation-backend/actions/runs/42131501508) (**35 BFF + 41 Identity tests passed**). This first slice adds `Modules.System.Bff.Security.BffLocalReturnUrl` with unit tests; it validates same-origin relative return URLs for the future OIDC browser login challenge.
- PR #14 by itself introduced no browser login. Draft PR #16 adds the opt-in login challenge, callback handler registration and BFF ticket-backed cookie configuration. A real callback round-trip has **not** been verified; do not mark BFF-04 completed.
- The BFF challenge must carry the validated return URL in protected OIDC `AuthenticationProperties`; Identity login flow and tenant context must remain verifiable independently.
- Next implementation work is the **full actual OIDC callback, token-exchange and server-side browser session integration test**, tracked by [issue #12](https://github.com/sholehamed/foundation-backend/issues/12).

## Outstanding risks / next actions

1. **Next: [BFF-04 Issue #12](https://github.com/sholehamed/foundation-backend/issues/12).** Verify the opt-in client and cookie pipeline in a full callback round-trip with a real identity TestServer; review failures and replay before activating. Work in a separate PR after PR #16.
2. **Safe interactive login continuation:** Angular [Draft PR #8](https://github.com/sholehamed/foundation-frontend/pull/8) introduces a dedicated allowlisted OIDC return flow (not merged at the time of this snapshot). Do not relax open redirect checks.
3. BFF-03's code+PKCE tests use isolated ASP.NET Core TestServer. **Real deployed Identity UI/BFF integration, SQL Server, multi-instance storage, trusted forwarded host, third-party/custom-domain browser paths and full Angular login are not proven.**
4. BFF-04/05 must implement signed OIDC state/nonce verification through platform middleware, session/host binding, 401/403 JSON, CSRF for unsafe methods, logout/revocation, keyring and store lifecycle, refresh coordination.
5. Angular still uses Password Grant and localStorage tokens. Leave it unchanged until BFF end-to-end safety gate is fulfilled.
6. Known legacy repo security/debt: environment/domain couplings, forwarded-header proxy trust, signing credentials, broad NuGet warning suppression, dev-seeded credentials. Review separately; don't confuse BFF unit-test success with production-hardening.
7. Before merging each slice: update this file, [ROADMAP](PROJECT-ROADMAP.md), [architecture](architecture/browser-bff.md), [HTTP contract](reference/browser-auth-contract.md) and module README; link actual CI run and code.

## Resume protocol

1. Read this file, [ROADMAP](PROJECT-ROADMAP.md), [ADR-0001](decisions/ADR-0001-browser-bff.md), and [BFF-03 implementation record](development/BFF-03-IMPLEMENTATION.md).
2. Query actual GitHub `main`, open PRs and current CI — docs are a *snapshot*, not a substitute for live evidence.
3. Work on a new feature branch. Implement a small vertical slice, add tests, update docs in the same PR, and verify CI before merge.
4. Maintain existing browser Password Grant until new BFF consumer has working integration; never expose OAuth tokens to browser as a workaround.

## BFF-04 current Draft PR checkpoint — 2026-11-06

- **PR:** [#16 — BFF-04.2 OIDC callback and ticket-backed session](https://github.com/sholehamed/foundation-backend/pull/16), branch `feat/bff-04-oidc-callback-session`. **Draft; not merged; BFF login NOT production-ready.**
- **Current code:** opt-in `AddBffAuthentication` registers the confidential ASP.NET Core OIDC handler (`Authorization Code`, PKCE, callback `/signin-oidc`), isolated `BffCookie` scheme, server-side ticket store; `MapBffEndpoints` adds `GET /bff/auth/login`. Context comes from a validated host, not untrusted query parameter. OIDC middleware is responsible for state/nonce handling.
- **Test failures (actual evidence):** [CI #42135641269](https://github.com/sholehamed/foundation-backend/actions/runs/42135641269) built successfully; 3 of 46 BFF tests failed with HTTP `500` rather than expected redirect/403. With test-only developer diagnostics, [CI #42138226481](https://github.com/sholehamed/foundation-backend/actions/runs/42138226481) exposed `IDX20803/IDX20804`: isolated TestServer attempted DNS metadata discovery against the fake `https://identity.example.com/.well-known/openid-configuration`.
- **Fix under verification:** test harness now explicitly injects `StaticConfigurationManager<OpenIdConnectConfiguration>` instead of attempting network discovery; invalid tenant-query context returns an explicit HTTP `403` without relying on default Forbid authentication scheme. A new [CI run](https://github.com/sholehamed/foundation-backend/actions/runs/42138452399) validates the changes. Do not mark complete or merge until it passes.
- **Open gates:** callback/token exchange not proven with real Identity Server or SQL Server; tenant-host session boundary and cookie issuance need real integration tests; live Angular continuation, CSRF, logout and revocation remain unimplemented. Existing Angular Password Grant remains unchanged.
- **Next:** inspect green CI or failure logs, fix underlying code/tests without weakening assertions, document actual test counts and then prepare final review. Never merge draft or failed CI.


### 2026-11-06 — BFF-04.2 challenge test recovery

- The three unexpected HTTP 500 failures were traced to isolated OIDC TestServer metadata lookup against a fake Identity hostname and an unqualified Forbid response on a tenant mismatch. Production discovery has **not** been replaced with static metadata.
- Fixes: explicit HTTP 403 for host/tenant mismatch, `StaticConfigurationManager<OpenIdConnectConfiguration>` in the test harness only, diagnostics on unexpected TestServer 500.
- [CI #42138452399](https://github.com/sholehamed/foundation-backend/actions/runs/42138452399) **passed** (solution Build; 46/46 BFF tests and 42/42 Identity tests). This confirms the isolated HTTP challenge test fix, **not** full deployed Identity/BFF callback integration.
- BFF-04 remains **Draft PR #16**, not merged. Callback/token-exchange E2E, browser cookie issuance, live session lifecycle and multi-host verification are still required before acceptance.

## Latest BFF-04.2 review — 2026-11-06

- **Current branch under review:** [Draft PR #16](https://github.com/sholehamed/foundation-backend/pull/16) adds opt-in `/bff/auth/login`, confidential OIDC challenge/callback registration, validated host/System/Tenant context, and a protected distributed ticket-backed `__Host-Foundation.Bff` cookie scheme. `Identity:Bff:Enabled` remains **false** by default. The login challenge is verified with TestServer; the actual cross-origin OIDC callback/browser-cookie round trip is **not yet integration-tested**.
- **Latest CI evidence before docs update:** [run 42140046994](https://github.com/sholehamed/foundation-backend/actions/runs/42140046994), full solution build, **46/46 BFF** and **42/42 Identity** tests passed. Recheck latest-head CI after each commit.
- **Security review adjustment:** removed `.AllowCredentials()` from the broad pre-existing CORS origin allowlist. Interactive Identity UI and `/api/connect/authorize` must be served on the **same origin**; no credentialed cross-origin workaround for CSRF/XSRF cookies.
- **Not complete:** no proof of a real browser callback/token exchange and cookie issuance; no `/bff/session`, CSRF-protected BFF write APIs, logout/revocation, distributed refresh coordination or full Angular migration.
- **Required next PR:** TestServer integration of full Authorization Code callback and server-held tokens (positive + failure/replay/cross-tenant tests), then implement BFF-05 session lifecycle and full-stack E2E. A CI-green challenge-only slice must not be described as production-ready.

## Active BFF-04.3 callback HTTP integration tests — PR #18 (Draft, not merged)

- [PR #18](https://github.com/sholehamed/foundation-backend/pull/18), branch `test/bff-04-real-oidc-callback`, extends `tests/Foundation.Bff.Tests/BffOidcCallbackHttpTests.cs` with an in-process **fake OIDC issuer** that exposes discovery/JWKS, signs RS256 ID tokens and handles code+PKCE token exchange and UserInfo. The **actual ASP.NET Core BFF OIDC middleware** processes the callback; the fake issuer does not represent the real Foundation Identity module.
- Acceptance assertions: OIDC callback redirects to the validated return path, protected `__Host-Foundation.Bff` host-only Secure/HttpOnly cookie contains no OAuth credentials, server ticket retains access/ID tokens, invalid state/nonce, tenant mismatch and reused code fail closed.
- Tests are isolated from real DNS/production credentials. No production middleware options or cookie restrictions were relaxed. Latest-head CI must pass before any merge.
- **Still required after PR #18:** End-to-end tests with two real Foundation TestServer hosts (OpenIddict Identity + BFF), including actual token endpoint passthrough, current user/tenant revalidation, identity cookies, and final browser session flows; then BFF-05 APIs, CSRF, logout, revocation and Angular migration.

## BFF-05.1 read-only session slice — merged as PR #19

- After [BFF-04.3 PR #18](https://github.com/sholehamed/foundation-backend/pull/18) passed [CI 42143363623](https://github.com/sholehamed/foundation-backend/actions/runs/42143363623), it was merged to `main` at `3f90363227ed5497c82a378c9f73093c46adcad4`. It proves ASP.NET Core OIDC callback with a **fake signed issuer**, not the real Foundation Identity/BFF two-host flow.
- [Merged PR #19](https://github.com/sholehamed/foundation-backend/pull/19) adds opt-in `GET /bff/session`, explicitly authenticating **only** `Foundation.Bff` cookie. A valid ticket is checked again against the current registered host, System/Tenant account context, callback origin and tenant claims. Responses are no-store and contain only minimal identity/context JSON; no OAuth tokens. Unauthenticated returns 401; mismatched host/account returns 403.
- Tests exercise anonymous/bearer-only requests, valid Tenant and System cookies, wrong-host replay, corrupt cookies and disabled feature. Cookie issuance in these tests is test-only, not proof of a production Identity Server login.
- **Gates remaining:** real OpenIddict Identity↔BFF two-host E2E, independent current-user/tenant DB revalidation, session expiration/revocation, CSRF-protected logout and write APIs, distributed cache/keyring failure modes. BFF remains `Enabled=false`; Angular Password Grant and localStorage are unchanged. PR-head CI 42194597675 passed: 58/58 BFF and 42/42 Identity tests.

## Continuation review — 2026-11-06

- Reviewed PR #23 head `663ebd201527b89e2126d14f35dc328de240d9d9`: [CI 42200256418](https://github.com/sholehamed/foundation-backend/actions/runs/42200256418) passed full solution build, **68 BFF + 48 Identity tests**. No unresolved review threads at review time.
- Added regression coverage for logout replay, preserving an independent ticket, request token without antiforgery cookie, GET logout refusal, cross-host CSRF bootstrap, HTTPS enforcement, Referer fallback, opaque Origin, port mismatch and Fetch Metadata boundaries. These additions require a new green PR-head CI before merge.
- Next implementation slice: live user/tenant/security-stamp/grant revalidation behind the BFF cookie boundary, followed by account-wide revocation and refresh coordination. Angular migration remains blocked; frontend handoff must reflect PR #22 and this slice. Keep both F00 PRs open.
