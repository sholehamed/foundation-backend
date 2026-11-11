## FR-17F-1 — Real-data administrative dashboard metrics (2026-11-11)

- Existing System dashboard extended with explicit persisted PlanVersion / Pending Order / Settled Order / Active-in-period Subscription **counts**, not fake GMV or billing analytics. SQL two-Tenant tests, no schema/grant/route changes. [Handoff](development/FR-17F1-SYSTEM-COMMERCIAL-DASHBOARD.md). Companion Angular System/Tenant executive dashboard uses genuine API state and responsive presentation. Next product-level real Commercial Module choice/EndpointGroup registration and full OpenIddict/BFF acceptance, actual CountLimit domain providers and subscription lifecycle hardening.

## FR-17C-3C — Shared SQL transaction bridge for independent commercial modules (2026-11-11)

- `ExecuteModuleAsync` exposes a carefully scoped `SqlConnection/SqlTransaction` to a trusted module-owned DbContext/Count adapter, never Identity entities; required license/RBAC/typed capability/quota checks and domain writes stay in one Serializable SQL commit/rollback. Disposable SQL suite tests module-owned EF persistence and rollback; test-assembly-only signed JWT TestServer verifies authenticated 401/403/200/409 with two Tenants and no production test endpoint. [Handoff](development/FR-17C3C-MODULE-UOW-SIGNED-HTTP.md).
- Next: real Commercial module source/Domain/EndpointGroup and full deployed Web API/BFF E2E, domain CountLimit live projections, Outbox for external effects and subscription lifecycle semantics. No real paid runtime module or catalog grant is seeded automatically.

## FR-17C-3B — Trusted Commercial write execution within one SQL transaction (2026-11-11)

- New opt-in `TenantCommercialOperationExecutor` composes scoped resource Permission+verified settled Subscription+Features/Modes+all exact source-bound CountLimits/PeriodQuotas using one serializable SQL transaction and guarded domain callback. Quota replay does not rerun business writes; read-only usage API remains distinct. SQL tests add count+quota parallel contention, two-Tenant denied license, callback rollback, duplicate requests and no-limit modes. [Detailed handoff](development/FR-17C3B-TRANSACTIONAL-COMMERCIAL-EXECUTOR.md).
- Still **not** a deployed commercial HTTP command or global policy switch: no real Commercial module exists; none is seeded. Next code-owned domain adapters and HTTP E2E with first real Module, CountLimit active count projections, Outbox/reservation for async workflows, subscription renewal/downgrade rules. Maintain backend/frontend separate tests and review CI before merge.

## FR-17D-2B — Read-only authoritative tenant usage and subscription cadence (2026-11-11)

- Source-owned optional `QuotaPeriodCadence` (subscription term/monthly/yearly anniversary), UTC immutable start anchor and clipped subscription end. Read-only SQL projection of `TenantUsageBalances` scoped to signed current HomeTenant, current settled Subscription, owner-only GET route; uses explicit null for unmeasured CountLimits and missing cadence, 0 for no committed balance, SQL period mismatch denial. No new SQL schema/grants or fake commercial Modules. [Acceptance and API contract](development/FR-17D2B-SUBSCRIPTION-USAGE-PROJECTION.md).
- Next: consume API in Angular for owner-only live meters, but no write authorization from UI; code-owned transactional operation executor binding verified Subscription window + grant + atomic CountLimit/Quota to business writes and two-Tenant signed HTTP tests; renewal/proration and async Reservations later.

## FR-17C-3A — Subscription AND delegated permission and typed capability preflight (2026-11-11)

- Source-owned `TenantCommercialOperationPreflight` combines live persisted Resource/Permission, scoped Tenant-user grants, actual settled Subscription, and typed Features/Modes. It never maps an inactive commercial module or expired subscription to a permission; explicitly returns `MeteringRequired` for CountLimit/PeriodQuota with no invented usage. Added disposable SQL two-Tenant negative/positive tests. [Handoff](development/FR-17C3A-COMMERCIAL-OPERATION-PREFLIGHT.md).
- This is **not** live business HTTP authorization or a metered operation commit hook; no commercial business Module exists in deployed source. Next FR-17C-3B transactional trusted business operation integration and HTTP E2E, FR-17D-2B per-subscription period policy + read-only usage API and Angular component.

## FR-17E-1 — Percentage/fixed coupons and atomic zero-total checkout (2026-11-11)

- [Backend PR #138](https://github.com/sholehamed/foundation-backend/pull/138): System-generated one-time 96-bit codes, hashed catalog, UTC eligibility, one-use/global and per-Tenant settled caps, coupon-to-order pin and redemption ledger. Existing Tenant checkout supports optional code; zero-net completes Order+Redemption+Subscription atomically; partial discounts consume once at System external receipt settlement. [Detailed contract and release risks](development/FR-17E1-CATALOG-COUPONS-ATOMIC-REDEMPTION.md).
- [Frontend PR #65](https://github.com/sholehamed/foundation-frontend/pull/65): System Admin coupon issue/list/disable; Tenant checkout optional code and accurate server-confirmed zero/pending state. Both PRs gated on current-head Build/SQL/HTTP/Angular/Chromium, then post-merge main.
- Next: code-owned actual Commercial Module/Resources/Permissions and end-to-end Staging flow; order expiration/cancel/refunds, append-only manual settlement proof, explicit invoice and tax policy, trusted payment adapter, renewal; then Subscription AND Tenant DelegatedPermission runtime gates and Limit/Quota integration. **Do not** treat coupons or a purchased Plan as blanket user Permission.

## FR-17D-2A — SQL Server tenant quota ledger and atomic CountLimit guard (2026-11-11)

- SQL-only migration, tenant-scoped balance/append-only usage events, trusted-internal `TenantAtomicUsageService` using serializable caller-owned domain transaction and `sp_getapplock` per Tenant/Capability/Period. Supports count callback under same lock, quota Consume/Reverse, idempotency/rollback/parallel writes; disposable SQL tests added. [Handoff](development/FR-17D2A-ATOMIC-USAGE-SQL.md).
- **Not yet live enforcement:** cannot treat a trusted internal snapshot as commercial purchase proof. Next: FR-17C-3 real Entitlement AND delegated Permission/Scope gate + two-Tenant HTTP tests, reviewed period anchor/renewal rules, optional async Reservation lease protocol, scoped usage API and Angular usage visualizations. No invented commercial Module or grants.

## FR-17D-1 — Optional Resource/operation limit decision engine (2026-11-11)

- Pure code-owned `OperationLimitPolicy` and xUnit matrix added for absent binding (no usage ceiling only), finite/zero/explicit Unlimited limits, required capability absent from plan (deny), invalid/missing authoritative usage (deny), signed CountLimit cleanup, positive PeriodQuota consumption and allowed modes. No commercial source Module, runtime authorization changes or plan grants. [Handoff and next SQL acceptance](development/FR-17D1-OPERATION-LIMIT-POLICY.md).
- Next **FR-17D-2**: atomic SQL Server authoritative CountLimit + idempotent period usage ledger, two-Tenant simultaneous-request tests and rollback; **FR-17C-3** composition with Subscription/Permission/Branch under rollout, then frontend usage API/UI. No live activation or production-validated cap yet.

## FR-17C-2A — Owner quote and operator-attested external payment (2026-11-11)

- [Backend PR #135](https://github.com/sholehamed/foundation-backend/pull/135) implements safe Pending order creation from immutable published Plan price, tenant-scoped idempotency, System Admin-only reviewed out-of-band positive-amount settlement and atomic Order→Active Subscription issuance. [Technical handoff and test cases](development/FR-17C2A-TENANT-ORDERS-MANUAL-SETTLEMENT.md). [Angular #64](https://github.com/sholehamed/foundation-frontend/pull/64) implements the Tenant and SuperAdmin workflow.
- **Not operational for commercial sale** until a deployed code-owned Commercial Module/Controller Resource is registered and published. Settlement currently records a System operator attestation, not a bank response. Next FR-17C-2B: immutable settlement audit, refund/reconciliation, expiration/renewal, SQL composite Tenant/Plan ownership and protected quote lifecycle. FR-17E: centrally managed 100%/fixed/percent coupon, atomic redemption, zero-total Settled Order without gateway. FR-17C-3: entitlement+delegated permission conjunction under controlled rollout; FR-17D: Limit/Quota policies and atomic usage ledger.

## FR-17C-1 — Subscription order/source-of-truth storage and read-only shadow (2026-11-11)

- [PR #134](https://github.com/sholehamed/foundation-backend/pull/134) adds tenant-isolated Order and Subscription SQL schema, one-active-per-Tenant and order-source uniqueness, IRR quote/discount arithmetic, settlement proof fields and a fail-closed shadow entitlement reader. Source-authenticated `/tenant/workspace/subscription/me` reports NoSubscription/Pending/Active/Inactive/Invalid/Unavailable but **does not enforce or grant paid permissions**. [Technical handoff](development/FR-17C1-SUBSCRIPTION-SHADOW.md).
- [Angular #63](https://github.com/sholehamed/foundation-frontend/pull/63) shows true license status on Tenant dashboard, with no client-crafted TenantId.
- Next: FR-17C-2 trusted scoped subscription/settlement lifecycle, SQL owner-version composite integrity and audited Orders; real code-owned commercial Module + verified Plan Catalog publication, runtime Permission AND Entitlement in shadow/flagged rollout, FR-17D atomic quotas, FR-17E fixed/100% coupon and verified zero-total checkout. No active paid Subscription is seeded or offered by current signup flow.

## FR-17B-2C — Published PlanVersion immutability hardening (2026-11-11)

- [Backend PR #132](https://github.com/sholehamed/foundation-backend/pull/132): SQL Server DML triggers for Plan/PlanVersion plus all four version-owned detail tables; draft-only editing and Draft→Published publication preserved; multi-row atomic rejection and direct SQL modification regressions; EF model and snapshot carry matching trigger metadata. [Handoff](development/FR-17B2C-IMMUTABLE-PLAN-SNAPSHOTS.md).
- [Angular PR #62](https://github.com/sholehamed/foundation-frontend/pull/62): Published Plan shows locked/revision-only UX and tests. No new permission or endpoint.
- Next before sale: trusted/restricted SQL publication + separate audited sale-availability/retirement model; code-owned real Commercial Module EndpointGroups, typed capability enforcement and first active Plan example; existing Master Registry Apply/RolePermission rollout. Then FR-17C Subscriber source of truth + effective entitlements, FR-17D Limit/Quota, FR-17E discount/zero-total/payment. No paywall/Grant during Plan catalog work.

## FR-17B-2B — System Plan Catalog draft/publish + Angular System Admin (2026-11-10)

- [PR #131](https://github.com/sholehamed/foundation-backend/pull/131) adds central `plan-catalog` Resource with four fine-grained System permissions and initial System Admin navigation/grants on **fresh install only**. Adds typed draft read/edit/version creation and reviewed hash-bound SQL Server publication endpoints, no tenant purchase or user grants. [Detailed implementation and security caveats](development/FR-17B2B-SYSTEM-PLAN-CATALOG-ADMIN.md).
- Frontend [PR #61](https://github.com/sholehamed/foundation-frontend/pull/61) System Admin page for new Plan, pinned parent, draft editing, IRR price cards, source-approved Module selection, read-only Publish Preview and guarded Publish action. Zero commercial source modules means selling is **not operational** yet; no fake plan inventory.
- Next B2B hardening: enforce published content immutability at SQL permissions/trigger layer, audited concurrency/CAS HTTP tests, existing-db controlled permission rollout, and registered real Commercial Modules. Later FR-17C subscription source-of-truth and effective entitlement, FR-17D quotas and FR-17E verified discount/zero-amount settlement/payment adapter. Do not enable paid entitlements from catalog alone.

## FR-17B-2A — Plan card read API and Angular consumer (2026-11-10)

- Introduces a public rate-limited latest-published PlanCatalog projection, exact price/currency/cycle, dynamic Module/Capability/Benefit card copy, safe signup `unselected` intent and validation of new PlanKeys against current published offer. Cross-repo Angular UI consumes the same DTO and has retry/empty states. [Design, security and acceptance](development/FR-17B2A-PUBLIC-PLAN-CARDS.md).
- Next **FR-17B-2B** transactional System Admin draft/edit/publish with content fingerprint, immutable published snapshots, code-owned commercial Module registration, and tested System menu/permissions. **FR-17C** follows with Subscription/Entitlements; then quotas and verified checkout/discounts. No catalog publication, paid permissions, grant or payment occurs here.

## FR-17B-1 — Persisted parent-versioned Plan Catalog and dynamic card data contract (2026-11-10)

- [PR #128](https://github.com/sholehamed/foundation-backend/pull/128) contains seven new additive SQL tables with FKs/unique/check constraints; a read-only pinned PlanVersion inheritance resolver with Module union, typed quota/feature/limit merge, source-owned Commercial eligibility, price filters and DB-driven card items. [Full handoff](development/FR-17B1-VERSIONED-PLAN-CATALOG.md).
- Tests on disposable SQL verify Parent v2 cannot silently mutate child v1 rights/price; SQL rejects negative plan price and foreign capability owners; no Seed, grants, TenantModules, subscriptions or paid runtime enforcement. Never assume CI ephemeral success is a real Staging deployment.
- FR-17B-2 next: implement System Admin-controlled draft/publish transaction, content fingerprint/immutable published versions and rate-limited public plan-card projection. FR-17C next: scoped subscription state + typed shadow entitlement evaluator. FR-17D+: quotas, permission conjunction and Billing workflow.

## FR-17A-2 — Typed Module/Resource/Permission and future operation capability contracts (2026-11-10)

- [PR #127](https://github.com/sholehamed/foundation-backend/pull/127) adds centrally code-owned **typed classification** of System PlatformCore, TenantCoreAdministration, Commercial, InternalOnly modules and Module→Resource (Controller/EndpointGroup)→Permission registry, derived from the 13 actual System and 17 Tenant administrative definitions. Duplicate Resource/Permission owners and class drift fail validation. [Detailed contract and tests](development/FR-17A2-TYPED-RESOURCE-CONTRACTS.md).
- Introduces validated typed Capability/CountLimit/PeriodQuota/AllowedModes descriptors and **Resource-operation bindings** for future plans; reads and writes within the same Resource can require different entitlements. Adds `resourceContractIssues` to existing read-only System endpoint integrity report and readiness calculation; maintains exact live SQL/EndpointDataSource owner checks.
- **Not implemented:** Plan/Subscription/Checkout, paid entitlement evaluation, quota counters/usage ledger, automatic grants, catalog writes, SQL migration or API enforcement cutover. Next FR-17B is additive PlanVersion/Capability typed storage, immutable parent inheritance and SHADOW entitlement tests; no commercial flag until verified.

## FR-17A-1 — Audited source Resource inventory and read-only SQL preflight (2026-11-10)

- [PR #126](https://github.com/sholehamed/foundation-backend/pull/126) implements reproducible read-only inventory built on existing FR-14A route/security parser: maps System EndpointGroups to centrally owned Resources/Permissions; records Tenant administration and identity/BFF/media/dev exceptions; fails on unreviewed cross-group Resource ownership. Includes security mutation tests and CI JSON artifact.
- [Target SQL read-only script](sql/fr-17a-module-resource-entitlement-inventory.sql) inventories Module/Resource/Permission/ApiResource/UiResource relationships and legacy TenantModules/RequestedPlanKey without claiming Subscription exists. No live target DB accessed, no migrations, rights, flags or commercial runtime added. [Detailed inventory and next gate](development/FR-17A-RESOURCE-INVENTORY.md).
- **Next:** approved target SQL inventory + code/runtime EndpointDataSource parity + Resource grouping decision before FR-17A-2 typed metadata validator and later Entitlement/Plan migrations.

## FR-17A — Proposed modular product architecture and gated implementation sequence (2026-11-10)

- **Design-only PR, not runtime shipped.** [Canonical architecture](architecture/modular-entitlements-plans-limits.md), [execution checklist](development/FR-17A-MODULAR-ENTITLEMENTS-HANDOFF.md) and [proposed ADR-0002](decisions/ADR-0002-commercial-entitlement-vs-authorization.md) formalize the owner's Resource=Controller/EndpointGroup semantics, centrally owned Module/Resource/Permission/UI/API catalogs, and PlanVersion-based Tenant Entitlement independent of Tenant Owner RBAC grants.
- Covers parent-plan inheritance pinned to immutable published versions, typed Feature/CountLimit/PeriodQuota/AllowedModes, dynamic public PlanCards, upgrade/downgrade provenance, zero-total coupon checkout, verified off-platform settlement, concurrency-safe usage ledger and workflow policy/outbox/saga composition. Existing TenantModules is NOT a subscription, RequestedPlanKey is intent only, and no paid entitlement is created at public signup. System core and Tenant billing/recovery bootstrap access stay independent of purchased modules.
- **Order:** FR-17A-1 EndpointGroup/Resource+SQL inventory and mapping → FR-17A-2 typed metadata + read-only parity validation → FR-17B typed Capability/PlanVersion schema → FR-17C subscriptions/entitlement resolver (default OFF/shadow) → FR-17D scoped RBAC/entitlement conjunction → FR-17E quota/workflow contracts → FR-18 public card/admin plan UI → FR-19 checkout/discount/zero-cost/offline settlement → FR-20 payment adapter/subscription transitions → FR-21 staging/BFF/browser rollout. These milestone labels are provisional until issue planning. No Migration, seed, permission grant, flag, Staging or Production change has been made in FR-17A documentation.
- **First read-only source mapping landed in documentation:** [Resource/EndpointGroup inventory](development/FR-17A-RESOURCE-INVENTORY.md) records the 13 System resources, shared Menus/MenuCategories ownership, Tenant workspace aggregate and noncommercial signup; live target SQL and all route metadata are outstanding.
- **Next implementable task:** inventory existing EndpointGroup-to-Resource/Permission mapping and legacy TenantModules on target SQL; prepare a conflict-safe, non-destructive migration design, before implementing billing tables. Owner confirmation pending on the documented D-01..D-10 product semantics and reactivation behavior.

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

## FR-14C — System endpoint graph integrity diagnostic (2026-11-10)

- Added live, **read-only** System endpoint metadata vs global SQL API/Resource/Permission/link reconciliation with explicit issue codes, without Tenant-owned menus or any automatic Apply. Unregistered admin endpoints and extra PermissionApiResource links fail closed. [System endpoint integrity contract](development/FR-14C-SYSTEM-ENDPOINT-REGISTRY-INTEGRITY.md).
- Still pending: per-environment inspection and reconciliation, Tenant authorization/entitlement and BFF/browser verification, System feature grant rollout and FR-14C-3 Branch scopes. Staging remains deferred.

## FR-14C — System-owned Tenant menu readiness gate (2026-11-10)

- Read-only, privileged audit for exactly six canonical Tenant routes checks global Menu, Category, live PAGE Permission, UI link, unregistered Tenant routes and unguarded external links. This fills the gap between System Feature Registry and Tenant Permission Catalog Preview/Apply **without granting Tenant any definition capability**. [FR-14C readiness contract](development/FR-14C-CENTRAL-TENANT-NAVIGATION-READINESS.md).
- Still pending: operator-managed central Menu registration and review, full endpoint/feature parity, two-Tenant browser pilot, grant entitlement design and FR-14C-3 branch-scoped access. All activation flags OFF; Staging deferred.

## FR-14C — System-owned Tenant permission boundary (2026-11-10)

- Scoped hardening: Tenant role editor fails closed when a role contains any Permission link outside the active central `tenant-workspace/tenant-administration` allowlisted catalog. No data is repaired implicitly, and cross-Tenant assignment/Owner controls remain unchanged. [Tests and rollout gate](development/FR-14C-TENANT-ROLE-GRANT-INTEGRITY.md).
- Remaining: review all System-owned Menu/Page/Resource/Endpoint mappings, operator-approved reconciliation of historic rogue links, entitlement boundaries, read-only sidebar pilot and FR-14C-3 branch-scoped access. No Staging/Production action.

## FR-14C architecture correction — System-only menu ownership (2026-11-10)

- **Confirmed:** System Admin alone defines Menu, Page, Route, Resource, Action, Endpoint and permission catalog; Tenant Owner assigns only permitted, centrally registered capabilities to local roles and users. There is no Tenant menu authoring. The FR-14C-2A/2B owned menu implementation was superseded and removed from source; previously merged PRs remain historical records.
- FR-14C-1 read-only central Menu projection with live Tenant PAGE filtering remains. The Tenant-owned menu reader/writer, API, two pending migrations and Owner NAVIGATION.READ/MANAGE catalog definitions were retired. Dynamic Angular menu pilot remains OFF.
- [Canonical architecture and database preflight](development/FR-14C-CENTRAL-MENU-AUTHORITY.md): verify whether any database already applied retired migration IDs before deploying; never auto-drop or delete audit data. Staging/Production unchanged. Next: System Registry completeness, Tenant grant boundaries and branch-scoped RBAC.

## FR-14C-2B — Owner-only Tenant navigation editor backend (2026-11-09)

- Safe owner-scoped category/item configuration with versioned reorder/enable/disable, SQL immutable audit and new code-owned READ/MANAGE catalog permissions. Backend default OFF. Frontend companion must be validated and separately flagged.
- Before operational release: approve/apply both migrations and permission catalog, seed owned menus, perform two-Tenant negative tests and browser/BFF acceptance. Remaining FR-14C-2C delegated management/import; FR-14C-3 BranchMembership. [Protocol](development/FR-14C2B-TENANT-NAVIGATION-OWNER-EDITOR.md).

## FR-14C-2A — separate Tenant-owned menu storage, read-only pilot (2026-11-09)

- New additive SQL catalog supports Tenant-owned category and menu trees with composite same-Tenant/same-category foreign keys; secure read-only opt-in never falls back to global menus. Existing default-off Angular dynamic menu pilot from FR-14C-1 needs no activation for this change.
- **Still open:** owned menu editor APIs and explicit reviewed grants/audit/CAS; approved legacy global Menu classification and import; branch membership and delegated Role scope; live browser/BFF tests and Migration/flag rollout. No Staging or Production activity. [FR-14C-2A design](development/FR-14C2A-TENANT-OWNED-NAVIGATION.md).

## FR-14C-1 — live authorized Tenant menu projection (2026-11-09)

- Existing database-backed Menu tree can now be exposed to the Tenant workspace through a read-only Tenant endpoint and a code-owned, fail-closed route/page map; direct denials and Tenant RoleScope are respected. Companion Angular dynamic navigation pilot remains independently **OFF** and does not replace underlying API authorization.
- **Not complete:** per-Tenant/Menu ownership data model, branch assignment/restrictions, delegated role administration and actual operator catalog seed/apply. [Scope/acceptance](development/FR-14C1-TENANT-NAVIGATION-BOUNDARIES.md). No Staging or DB changes.

## FR-14B-3 — auditable System account lifecycle backend (2026-11-09)

- New dedicated System-only profile/state/lock/read endpoints behind independent default-OFF feature flag; SQL CAS with concurrent version, live Master actor/target checks, self/SuperAdmin protection, SecurityStamp invalidation, and append-only audit migration/test suite. Legacy generic account writes remain forbidden.
- Next: Angular editor UX with version Conflict handling, actual System/browser HTTP positive/negative tests, deployment migration/flag planning, account recovery and reserved SuperAdmin rotation. Staging and Production remain deferred. [Details](development/FR-14B3-SYSTEM-ACCOUNT-LIFECYCLE.md).

## FR-14B-2B — First System password, forced rotation, fresh login (2026-11-09)

- System-only inactive target receives a short-lived, one-time initial secret with a separate SQL proof challenge. Completion atomically rotates credentials and activates the account, without issuing tokens/cookies. Default-OFF HTTP/Angular flow and additional audit/migration kept independent of Tenant first-login. [Design and acceptance](development/FR-14B2B-SYSTEM-FIRST-LOGIN.md).
- Next after latest-head CI: FR-14B-3 bounded System profile, state and lock CAS plus browser acceptance, privileged bootstrap rotation and deployed proxy/Staging certification. No Staging or feature toggles now.

## FR-14B-2A — Safe pending System identity creation (2026-11-09)

- Dedicated opt-in/HTTPS Master-scoped System pending-account creation with enforced inactive state, non-disclosed random password hash, zero auto-grants, strict 4-field request and atomic append-only audit; SQL filtered unique System email migration added but **not deployed**. Other unsafe lifecycle operations remain 403. [Contract and operations gate](development/FR-14B2A-PENDING-SYSTEM-ACCOUNTS.md).
- **Next:** FR-14B-2B expiring one-time System credential and challenge-based forced password change without unrestricted login; FR-14B-3 audited, CAS-safe profile, state and lock CRUD. No Staging, permission Apply or flag activation.

## FR-14B-1 — containment before System account CRUD (2026-11-09)

- Fail-closed the ten historically unsafe System Users/Admins lifecycle write mappings (no legacy CQRS dispatch), preserving separately audited grant APIs. Prevent re-opening via C# endpoint tests and mutation-tested Python CI inventory. This is **only the containment slice**, not operational CRUD.
- Continue FR-14B with a new System-only account contract and first-login credential rotation, audit and SQL CAS, then positive HTTP/Angular acceptance before enabling editors. [Detailed decisions and remaining work](development/FR-14B1-LEGACY-ACCOUNT-WRITE-CONTAINMENT.md). Staging remains deferred.

## FR-14A — source-complete security contract and opt-in live endpoint enforcement (2026-11-09)

- **This phase's exit criteria:** all known Identity System / Tenant / signup / OAuth routes, BFF lifecycle routes, authenticated FileStorage and Development Scalar routes explicitly classified, with CI source mutation tests and downloadable per-endpoint inventory. Angular companion validates 13 System roots + 32 nested views and Tenant admin inherited/independent PAGE guards.
- New mandatory `SystemFeatureEndpointPermissionRequirement` on every `WithFeatureEndpoint`: when the already existing Master rollout flag is independently approved and enabled, each registered API demands a live matching Resource.Permission in addition to coarse SystemAdministrator (including direct-deny precedence). Flag stays **OFF**, catalog Apply and grants remain manual; no staging or production operations.
- **Explicit uncovered authorization work** (do not merge into a false 'done'): FR-14B unsafe legacy System User/Admin mutation contracts + positive CRUD; FR-14C tenant menu ownership and delegated role/branch restrictions; FR-14D cross-Tenant media resource ownership validation; BFF-05/06/07 deployed/proxy/session hardening; FR-13 Staging approval. [Detailed matrix and acceptance](development/FR-14A-AUTHORIZATION-SURFACE-REVIEW.md).

## FR-13C-2D — Group security audit and legacy assessment (2026-11-09)

- Implemented next Owner-only read model: combined Group lifecycle, UserGroup membership and GroupRole history in one bounded timeline, with no TenantId leaks or sensitive data. Read-only current-link integrity counters distinguish invalid/Owner targets from **unattributed old links**; a missing event is *not* proof of unauthorized access. No automatic repair. [Acceptance contract](development/FR-13C2D-GROUP-SECURITY-REVIEW.md).
- Next: review integrity anomalies with a human/operator, establish explicit exported snapshot + signed correction plan and rollback before any mutation; keep Staging suspended until explicitly reauthorized.

## FR-13C-2A — Tenant Group management without grant mutation (2026-11-09)

- Implement Owner-only Group lifecycle (create, edit, active state) as independent 3-ACTION Tenant feature, transactionally audited with 8-byte RowVersion CAS. Full SQL/HTTP and Angular tests. No UserGroup/GroupRole shortcuts or automatic grants. [Acceptance contract](development/FR-13C2A-TENANT-GROUP-LIFECYCLE.md).
- Next FR-13C-2B: separate target-bound, audited membership + GroupRole write APIs, explicit independent permissions and immediate inherited-access revocation checks, then UI. Staging remains deferred by user request.

## FR-13C-1 — Tenant member security history (2026-11-09)

- With Staging intentionally deferred, implement a small Owner-only audit viewer for existing lifecycle and member-role ledgers, strict TenantId SQL isolation, bounded paging, and matching guarded Angular route. No catalog mutation or Production rollout. [Detailed contract](development/FR-13C1-TENANT-MEMBER-AUDIT.md).
- Next independent feature slice: Tenant Groups management (group CRUD, role/membership assignment, explicit reviewed ACTION permissions and audit). Broader System Admin writes and multi-node BFF operational certification remain separate.

## FR-13B-3B — unify initial password into normal Tenant Login (2026-11-09)

- Implement and test [#79](https://github.com/sholehamed/foundation-backend/issues/79): in-place forced change UX triggered by server verified temp credential; new atomic Owner create + credential issuance (two existing live Actions), SQL rollback, strict login fallback. [Contract](development/FR-13B3B-UNIFIED-TENANT-LOGIN.md).
- Next acceptance gates: BFF Identity-host interactive flow (separate signed continuation), email-less Tenant members #78 and real TLS Staging two-account browser acceptance. Defaults OFF; no production SQL/catalog grant or toggle changes.

## FR-13B-3 — temporary credential first-login branch (2026-11-09)

- Next reviewed [Issue #76](https://github.com/sholehamed/foundation-backend/issues/76): Owner creates inactive staff, manually issues unique short-lived temporary credential (never shared across users), staff enters **separate** first-login flow, receives only password-change challenge not JWT, changes permanent password, and logs in again normally. This route complements existing SMTP invitation activation. [Security acceptance](development/FR-13B3-TEMP-PASSWORD-FIRST-LOGIN.md).
- Operator gate remains: isolated Staging, approved SQL migration and feature registry apply/grants, review response-body logging and secure physical handoff. Never enable production via code merge alone.

## FR-13B-2 — one-time staff invitation and account activation (2026-11-09)

- [Issue #74](https://github.com/sholehamed/foundation-backend/issues/74): implement reviewed SMTP delivery, token-hash-only SQL storage, 30min TTL, one-time activation, secure password setup and append-only audit; never include token in Owner API. Detailed [FR-13B-2 design/acceptance](development/FR-13B2-INVITATION-ACTIVATION.md).
- Next dependency: companion Angular activation page and member Invite controls, then true HTTPS/Chromium multi-user staging acceptance; gateway distributed rate-limit and real SMTP relay configuration still unresolved. No staging deployed / no Production rollout authorized.

## FR-13B-1 — begin real Tenant staff onboarding (2026-11-09)

- [Issue #72](https://github.com/sholehamed/foundation-backend/issues/72): reviewed Owner-only inactive Tenant member create endpoint and Angular directory companion, SQL audit/validation/conflict testing. No invitation is sent and no account can log in. Refer to [FR-13B-1](development/FR-13B1-INACTIVE-TENANT-MEMBER-CREATE.md).
- **Next release gate:** secure token-based invitation/activation and password setup; only after that can active staff receive Role grants, navigate scoped menus, and complete real browser BFF role revoke acceptance. Never infer successful onboarding from creation of a disabled User.

## FR-13 — operational staging and Tenant Admin acceptance (2026-11-09)

Tracked in [FR-13 #70](https://github.com/sholehamed/foundation-backend/issues/70).
1. **FR-13A (current PR):** a fail-closed, read-only staging manifest checker, optional anonymous HTTPS probes, CI coverage and human staging rollout checklist. This does not create staging, migrate SQL, apply feature registry, grant permissions or enable Production BFF.
2. **Staging deployment (unprovisioned):** operator-defined DNS/TLS/proxy/SQL/keyring/session store, backup and test restore, approved migrations, reviewed Registry Preview/Apply and role grants. Verify real deployed flags before opt-in browser tests.
3. **FR-13B:** Tenant employee create/invite/activate and Tenant scoped menus, followed by System Admin -> Owner -> Staff -> role grant/revoke end-to-end UI+API+SQL audit. Do not reuse System Administrator write endpoints to manage tenant employees.
4. **FR-13C:** audit UI, groups, missing System Admin mutations, multi-node BFF durability and explicit Production release gate.

[Detailed FR-13A staging acceptance](development/FR-13A-STAGING-READINESS.md).

# Foundation Backend — BFF implementation roadmap

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

> Last updated: 2026-11-06  
> Parent decision: [ADR-0001 — Full BFF](decisions/ADR-0001-browser-bff.md)  
> Current state: [PROJECT-STATE.md](PROJECT-STATE.md)

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

## Earlier merged checkpoint — BFF-04.2 (2026-11-06)

- [PR #16](https://github.com/sholehamed/foundation-backend/pull/16) merged at `c4f6a81c302f3524bed3e58931221874be16928f`; [CI 42140557977](https://github.com/sholehamed/foundation-backend/actions/runs/42140557977) green: 46 BFF + 42 Identity tests.
- The opt-in client/challenge/callback registration and protected ticket infrastructure now exist on `main`, **disabled by default**. The code-grant token exchange is now handled in the actual Identity passthrough endpoint.
- BFF-04 remains **in progress** until real callback + cookie E2E proves the protocol end-to-end. BFF-05 session APIs, CSRF, revocation and BFF-06 Angular migration remain pending.
- Next technical slice: [BFF-04 full callback integration verification](development/BFF-04-IMPLEMENTATION.md). Keep legacy browser Password Grant unchanged until BFF-05/06 are E2E validated.

## Delivery rules

A slice is complete only when code, tests, integration evidence and documentation are complete. Keep Identity Server session and BFF application session separate. Do not migrate Angular early, do not expose tokens to browser JavaScript, and do not remove legacy Password Grant until dependent clients are migrated and verified.

## Milestones

### BFF-01 — protected server-side ticket store
**Status: Implemented / merged**

- PR #6 merged to main at `80f15d6c5bf59e6c557561fe97a2efbf1b5ee74a`.
- `ITicketStore` adapter over `IDistributedCache`, Data Protection, opaque keys, expiry/removal and isolated BFF cookie registration.
- Six automated tests passed.
- The module remains out of Web composition until the complete login callback path is ready.

### BFF-02 — Identity interactive session
**Status: Implemented / merged; CI green**

- PR #8: [interactive Identity cookie and reusable credential authentication](https://github.com/sholehamed/foundation-backend/pull/8) — merged at `4a798da96c735be0bc0823c82c02a495f4a9c4a8`.
- Dedicated `__Host-Foundation.Identity` cookie.
- Shared authenticator for existing Password Grant and interactive sign-in.
- CAPTCHA, antiforgery bootstrap/validation, login IP rate limit.
- System/Tenant account selection and lockout behavior tests.
- No OAuth token returned from interactive login.
- Does not yet authenticate `/api/connect/authorize`, issue an authorization code or create a BFF cookie.

### BFF-03 — OpenIddict authorization endpoint and first-party client registration
[Issue #9](https://github.com/sholehamed/foundation-backend/issues/9) · [Merged PR #11](https://github.com/sholehamed/foundation-backend/pull/11) · **Status: Implemented / merged; isolated HTTP protocol CI green**

Scope:
- Define/register a confidential first-party BFF client with exact callback URI(s), authorization-code and refresh-token permissions, allowed scopes, and a secret/key sourced only from secure configuration.
- Handle `/api/connect/authorize` passthrough by explicitly authenticating `Foundation.IdentityServer` cookie; never rely on the global bearer default scheme.
- Validate current user active/locked state, security stamp, tenant active state, account type and tenant context before code issuance.
- Narrow System role/permission claims to the master system scope; prevent Tenant claims from being carried into a System authorization context.
- For unauthenticated requests, redirect only to a configured same-origin/allowlisted Identity login UI with a protected return path; reject open redirects.
- For an authenticated request, issue the OpenIddict authorization response using the validated request and PKCE. Never hand-roll code generation, state, nonce or cryptographic validation.
- Decide and document first-party consent behavior; do not silently auto-consent arbitrary clients.

Acceptance:
- Integration tests cover System and Tenant authorization, wrong tenant, inactive tenant/user, lockout/security-stamp change, unregistered client/redirect, missing or invalid PKCE, unsupported scopes and unauthenticated login redirect.
- Authorization code is one-time and cannot be replayed.
- Existing Password/Refresh grants and existing token consumers remain unchanged.
- CI green and all docs/checkpoint updated.

### BFF-04 — confidential OIDC client, callback and BFF session
**In progress — PR #14 and PR #16 merged, opt-in client registered; full browser callback + cookie E2E NOT verified** · [Issue #12](https://github.com/sholehamed/foundation-backend/issues/12)

Scope:
- [First security slice] `Modules.System.Bff.Security.BffLocalReturnUrl`: conservative rooted same-origin return-path validator with tests. Must be used by future OIDC challenge; does not issue cookie or Token.
- Register OIDC client middleware in `Modules.System.Bff` using a confidential client and Authorization Code + PKCE.
- Challenge route with state/nonce and validated relative return URL.
- Callback validates correlation/state/nonce/issuer and performs server-to-server code exchange.
- Store protected ticket and tokens via `DistributedBffTicketStore`; issue only `__Host-Foundation.Bff` opaque cookie.
- Regenerate session identifier after login; enforce idle and absolute expiration.
- The module is composed into Web in PR #16 only behind `Identity:Bff:Enabled=false` default. Do not enable without callback/cookie integration tests and deployment review.

Acceptance:
- Browser never receives OAuth tokens in body, URL, JS storage or readable cookie.
- Invalid state/nonce, replayed callback, bad redirect and token exchange failures fail closed.
- Ticket store survives app restart when backed by shared durable cache and Data Protection key ring.

### BFF-05 — session API, CSRF, logout, revocation and tenant/host binding
**In progress: BFF-05.1 read-only endpoint merged; full lifecycle blocked on BFF-04 two-host E2E and further security gates**

Scope:
- `GET /bff/session` returns minimal safe user/context data, 401 when missing/expired/revoked, no-store always.
- CSRF bootstrap and validation on every unsafe cookie-authenticated operation.
- Logout current session and logout-all; revoke ticket and server-held tokens.
- Bind BFF ticket account type/tenant to a verified request host and current tenant status; fail closed on mismatch.
- Revalidate security stamp and important grants at bounded intervals and on high-risk actions.
- Explicit 401 JSON / 403 behavior; no HTML redirects from API calls.

Acceptance:
- Anonymous/authenticated/revoked/expired session tests.
- CSRF missing/invalid/valid tests.
- Cross-tenant cookie replay and spoofed Host/forwarded-host tests.
- Revocation and multi-session tests.

### BFF-06 — Angular migration
**Blocked on BFF-05 and cross-repo E2E**

Scope:
- Replace token persistence/interceptors with same-origin cookie requests.
- Bootstrap from `GET /bff/session`; use in-memory user/context state.
- Update guards and logout to BFF endpoints.
- Remove access/refresh token from localStorage and browser-readable state.
- Keep explicit bearer path only for native/service clients.

Acceptance:
- End-to-end System and Tenant login/logout on supported browsers.
- Reload restores session via cookie without JS token.
- Existing API permissions and tenant routing remain correct.
- No browser OAuth token appears in network response storage or logs.

### BFF-07 — migration closeout and production hardening
**Blocked on BFF-06**

- Inventory and migrate every registered legacy Password Grant client.
- Disable Password Grant only after compatibility tests and consumer sign-off; retain only supported native/service flows.
- Production signing/encryption credentials, BFF client credentials, Data Protection key ring and distributed cache configured through secret management.
- Test multi-instance operation, store outage, concurrent refresh/rotation, logout/revocation, deployment restart and incident logging.
- Complete threat review, deployment runbook and operational alerts.

## Test and documentation checkpoint for BFF-03

- Scoped principal factory, cookie revalidation, configurable BFF client seeder and authorization endpoint are implemented on the branch.
- Scoped identity-validation unit tests and the isolated HTTP Authorization Code + PKCE TestServer suite all passed in [final CI 42130167589](https://github.com/sholehamed/foundation-backend/actions/runs/42130167589).
- The existing Angular password-grant login does **not** complete the Authorization Code return URL; this needs an explicit continuation design before browser E2E.
- See [BFF-03 implementation notes](development/BFF-03-IMPLEMENTATION.md) and [project state](PROJECT-STATE.md).
- Never mark a slice complete just because code was committed.

## Carry-forward notes

- Keep `Modules.System.Identity` and `Modules.System.Bff` separate.
- Identity Server cookie and BFF cookie must never be conflated.
- The BFF module is optional and must not override global OpenIddict bearer authentication for native/API routes.
- Tenant ID in request headers/URL is context only, never proof of membership.
- Update [PROJECT-STATE.md](PROJECT-STATE.md) after every merged slice and update this roadmap if scope/order changes.

### BFF-03 acceptance update

The authorization endpoint currently handles GET; POST is explicitly refused to avoid losing form parameters on the login continuation path. Tests cover this behavior. Browser-based Identity login continuation and a full Angular BFF session are **not** part of BFF-03 and remain explicit blockers for BFF-04/05/06.

## BFF-03 completed checkpoint

- PR #11 merged to main: `c201e4c67d18c9d7896887a688eb31d3340d7930`.
- CI [42130167589](https://github.com/sholehamed/foundation-backend/actions/runs/42130167589) green, Restore/Build, 6 BFF tests, 41 Identity tests.
- **Next**: [BFF-04 #12](https://github.com/sholehamed/foundation-backend/issues/12), confidential client challenge/callback, secure ticket-backed browser session; do not migrate Angular or claim full BFF yet.

## BFF-04 first-slice CI checkpoint

- [PR #14](https://github.com/sholehamed/foundation-backend/pull/14) merged at `53732242450bd90c206e29738007372bbe2dde1b`.
- [CI 42131501508](https://github.com/sholehamed/foundation-backend/actions/runs/42131501508) passed full solution Build, **35 BFF tests** and **41 Identity tests** (0 failures).
- What shipped: conservative relative same-origin return path validator and tests. **Not shipped**: browser login challenge, OIDC callback, server-held token exchange, BFF session cookie, Angular migration.
- Next BFF-04 slice: integrate opt-in OIDC client/callback with secure AuthenticationProperties and ticket store, then full integration testing. Track with [issue #12](https://github.com/sholehamed/foundation-backend/issues/12).

## Active BFF-04.2 PR #16 — continuation checkpoint (2026-11-06)

- Branch: `feat/bff-04-oidc-callback-session`; [Draft PR #16](https://github.com/sholehamed/foundation-backend/pull/16).
- Implemented in branch, **not merged**: opt-in OIDC handler and `GET /bff/auth/login` challenge, PKCE, callback path, host-bound login context, ticket store and validation hooks.
- Verification: Build passed at [42135641269](https://github.com/sholehamed/foundation-backend/actions/runs/42135641269), but three BFF HTTP challenge tests returned `500`. [42138226481](https://github.com/sholehamed/foundation-backend/actions/runs/42138226481) identified unintended OIDC metadata fetch from fake test IdP. Test-only fixed configuration manager and explicit 403 change are verified at [42138452399](https://github.com/sholehamed/foundation-backend/actions/runs/42138452399).
- Acceptance still requires real callback/code exchange, token containment in server-side ticket, tenant-host validation, negative state/nonce/correlation tests, persistence behavior, and careful OIDC return-flow proof. Do not advance Angular until these gates pass.


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

## BFF-05.1 — isolated read-only session endpoint (merged PR #19)

- Foundation's signed fake-issuer callback PR #18 is merged (`3f90363227ed5497c82a378c9f73093c46adcad4`, [CI 42143363623](https://github.com/sholehamed/foundation-backend/actions/runs/42143363623)). This does **not** replace the pending two-real-host OpenIddict integration gate.
- [PR #19](https://github.com/sholehamed/foundation-backend/pull/19) introduced an opt-in, cookie-scheme-only `GET /bff/session` plus per-read registered-host/account/tenant context validation and no-store safe JSON. Isolated HTTP tests do not claim full auth E2E.
- BFF-05 remains incomplete until CSRF, logout/revoke-all, expiration, current DB-grant revalidation and distributed-store checks. BFF-06 Angular migration remains blocked.

## Continuation review — 2026-11-06

- Reviewed PR #23 head `663ebd201527b89e2126d14f35dc328de240d9d9`: [CI 42200256418](https://github.com/sholehamed/foundation-backend/actions/runs/42200256418) passed full solution build, **68 BFF + 48 Identity tests**. No unresolved review threads at review time.
- Added regression coverage for logout replay, preserving an independent ticket, request token without antiforgery cookie, GET logout refusal, cross-host CSRF bootstrap, HTTPS enforcement, Referer fallback, opaque Origin, port mismatch and Fetch Metadata boundaries. These additions require a new green PR-head CI before merge.
- Next implementation slice: live user/tenant/security-stamp/grant revalidation behind the BFF cookie boundary, followed by account-wide revocation and refresh coordination. Angular migration remains blocked; frontend handoff must reflect PR #22 and this slice. Keep both F00 PRs open.
