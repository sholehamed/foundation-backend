# Foundation Backend — Project State & Handoff

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
