# Foundation browser BFF — phased implementation (not production-ready)

## BFF-05.5b.2 SQL Server implementation — PR #32 code-head CI verified (2026-11-06)

- [PR #32](https://github.com/sholehamed/foundation-backend/pull/32) contains the **real** Web-owned `SqlServerBffAtomicTicketStore`: an opaque protected Ticket INSERT, SQL conditional UPDATE-only renewal and atomic revoked-row UPDATE, 24h fixed absolute deadline, idempotent manual SQL schema, fail-closed **Startup schema/constraint preflight**, no Redis dependency. Production registration still requires explicit `Identity:Bff:SqlServer:Enabled` and secret-managed `ConnectionStrings:BffSessions`.
- [Code-head CI #42215510887](https://github.com/sholehamed/foundation-backend/actions/runs/42215510887) reports **9 real SQL Server tests** passing; includes cross-connection row-lock race and **two independent TestServers sharing a persisted Data Protection keyring**: cookie works across hosts, logout on one blocks the other and a restarted third host. The earlier code-head [CI #42214992134](https://github.com/sholehamed/foundation-backend/actions/runs/42214992134) ran all **94 BFF + 60 Identity** tests and full solution build. Await latest documentation-head CI before merge.
- Security limitation: the SQL Server instance is a disposable CI service, **not a proven production failover network topology**. Keyring sharing in the test does **not** certify encrypted, rotated production key storage. Existing BFF disabled; frontend Password Grant/localStorage unchanged. Logout-all/upstream token revocation, refresh coordination and browser/proxy deployment acceptance remain open. [Detailed SQL handoff](../../../docs/development/BFF-05-5B-SQL-ATOMIC-SESSION.md), [BFF master #20](https://github.com/sholehamed/foundation-backend/issues/20), [SQL provider #30](https://github.com/sholehamed/foundation-backend/issues/30).


## Active BFF-05.5b.2 SQL Server transactional session store — 2026-11-06

- [Draft PR #32](https://github.com/sholehamed/foundation-backend/pull/32) introduces the actual `SqlServerBffAtomicTicketStore` Web adapter behind `IBffAtomicTicketStore`: protected server-side tickets, 24h original lifetime, one-row conditional **UPDATE-only** renewal and atomic SQL revocation (payload cleared) on the same primary; independently managed idempotent DDL and explicit secret-managed SQL connection.
- New `tests/Foundation.Bff.SqlServerTests` runs disposable SQL Server database tests (cross-connection renew/revoke race, expired/revoked/missing keys, tampering/SQL failure, deadline cap, schema idempotence). Separate SQL Server 2022 GitHub Actions service job added. **Await final latest-HEAD CI before claiming merged.**
- SQL provider requires `Identity:Bff:SqlServer:Enabled=true` *and* `Identity:Bff:Enabled=true`, with `ConnectionStrings:BffSessions`; no automatic prod DDL. Existing BFF remains OFF by default, Angular Password Grant/localStorage/guards untouched. Shared Data Protection keyring and production failover/browser gates still open. See [technical rollout and limitations](../../../docs/development/BFF-05-5B-SQL-ATOMIC-SESSION.md), [master #20](https://github.com/sholehamed/foundation-backend/issues/20), [SQL #30](https://github.com/sholehamed/foundation-backend/issues/30).


## Verified BFF-05.5a + BFF-05.5b.1 merged checkpoint — 2026-11-06

- **Merged race mitigation:** [PR #28](https://github.com/sholehamed/foundation-backend/pull/28) at `ccbaddcad3d8e7c413a505ba7c8e2e0a04e59298`. [Final PR-head CI #42210307369](https://github.com/sholehamed/foundation-backend/actions/runs/42210307369) passed solution build, **88 BFF + 60 Identity tests**. Protected session tickets carry a 24-hour original-issuance absolute ceiling, revoked keys receive a 48-hour tombstone, and Retrieve/Renew validate revocation before/after cached reads/writes; deterministic concurrency tests prove the single-shared-consistent-cache interleavings.
- **Merged non-Development guard:** [PR #29](https://github.com/sholehamed/foundation-backend/pull/29) at `b9a6bb77cedcc55511f2d45edff28667b20f5734`. [Final PR-head CI #42211389822](https://github.com/sholehamed/foundation-backend/actions/runs/42211389822): full solution build, **94 BFF + 60 Identity tests**. Opt-in BFF startup in Staging/Production now rejects a plain `IDistributedCache` without a registered singleton `IBffAtomicTicketStore`; its provider is actually selected for Cookie `ITicketStore`. Development/default-off behavior remains unchanged.
- **Important:** `IBffAtomicTicketStore` is a CONTRACT, not a concrete reviewed provider. **No SQL Server/Redis atomic backend exists yet**; an implementation registered by DI alone is not proof of its atomicity. [Issue #30](https://github.com/sholehamed/foundation-backend/issues/30) tracks transactional SQL Server provider, cross-instance consistency/eviction/failover and Data Protection keyring tests. BFF browser opt-in remains OFF until these and other BFF-05 gates are satisfied.
- **Remaining:** logout-all/upstream OIDC token revocation, refresh coordination, idle policy, actual SQL Server/browser/trusted reverse proxy and multi-node E2E, stamp-change race during callback. Angular Password Grant/localStorage/guards unchanged; [frontend BFF-06 #10](https://github.com/sholehamed/foundation-frontend/issues/10) blocked, initial F00 PR #2 untouched.
- **Handoff docs:** [renewal races](../../../docs/development/BFF-05-5-TICKET-REVOCATION-RACES.md), [startup guard](../../../docs/development/BFF-05-5B-ATOMIC-PROVIDER-GATE.md), [master BFF-05 issue](https://github.com/sholehamed/foundation-backend/issues/20).


## BFF-05.5 progression — merged race mitigation, atomic provider gate under review (2026-11-06)

- **Merged BFF-05.5a:** [PR #28](https://github.com/sholehamed/foundation-backend/pull/28) at `ccbaddcad3d8e7c413a505ba7c8e2e0a04e59298`; [final PR-head CI #42210307369](https://github.com/sholehamed/foundation-backend/actions/runs/42210307369) green: build, **88 BFF + 60 Identity tests**. Tombstone-before-delete, before/after cached reads/renewals and bounded 24-hour session expiry prevent late renewal resurrection under one strongly consistent shared cache. This is *not* a verified Redis/SQL multi-node atomic provider.
- **Draft BFF-05.5b.1:** [PR #29](https://github.com/sholehamed/foundation-backend/pull/29) adds `IBffAtomicTicketStore` and refuses enabled BFF startup in **Staging/Production** unless a singleton atomic provider is explicitly registered and used for `ITicketStore`. A SQL provider need not also register `IDistributedCache`. `Development` can still use the tombstone-backed default; disabled BFF is unchanged. Latest-head CI pending; a test-only interface stub does not satisfy Production security review.
- **Implementation outstanding:** [Issue #30](https://github.com/sholehamed/foundation-backend/issues/30) tracks the real transactional **SQL Server** atomic session provider, cross-instance interleavings, durability/eviction/failover, restart/keyring/SQL tests. No real provider is included in PR #29; BFF is **still disabled by default**, Angular Password Grant/localStorage unchanged. Backend [Issue #20](https://github.com/sholehamed/foundation-backend/issues/20) remains the master gate; original F00 PR #2 remains open.


## Active BFF-05.5a — no ticket resurrection during logout/renewal (draft PR #28)

- [PR #28](https://github.com/sholehamed/foundation-backend/pull/28) adds a **server-side 48-hour revocation tombstone** and immutable **24-hour maximum ticket lifetime** to BFF's `ITicketStore`. Renewal checks whether the original key is active and verifies revocation after the write; reads check revocation before and after cached bytes. This prevents stale read/write resurrection on a single strongly-consistent shared cache.
- New deterministic two-store concurrency tests cover delayed renewal writes, delayed stale reads, replay, absolute expiry and invalid renewal baseline. **Await latest PR-head CI before marking implemented.**
- `IDistributedCache` itself provides no atomic compare-and-set or eviction guarantees. This is **not** a production-ready multi-node/Redis/SQL Server/failover acceptance. See [security design and limitations](../../../docs/development/BFF-05-5-TICKET-REVOCATION-RACES.md); prove provider-level atomicity/consistency or use a dedicated atomic backend before enabling BFF.
- Baseline: merged [PR #26](https://github.com/sholehamed/foundation-backend/pull/26) real OIDC Web validator test, CI 82 BFF + 60 Identity. Backend BFF opt-in still disabled; Angular Password Grant/localStorage unchanged. Original F00 PR #2 remains open.


## BFF-05.3 real callback + Web adapter integration gate — 2026-11-06

- [Draft PR #26](https://github.com/sholehamed/foundation-backend/pull/26) runs actual Foundation OpenIddict Identity and BFF middleware on two independent TestServers **with the production Web-owned live Identity validator wired into the BFF host**. It shares an explicit EF InMemory root only for test DB access, not an issuer/token mock.
- [PR code-head CI #42207855770](https://github.com/sholehamed/foundation-backend/actions/runs/42207855770): build green, **82 BFF + 60 Identity** tests. New System and Tenant tests prove real code+PKCE/metadata/JWKS/UserInfo/callback produces a server-side ticket with captured DB baseline; a subsequent stamp change makes `GET /bff/session` return 401, and reverting the stamp cannot resurrect the removed ticket.
- This closes the **two-TestServer callback + Web live validator integration test**, not the deployed-browser/SQL Server integration or concurrency guarantee. A very narrow race between IdP exchange and BFF baseline capture, concurrent session renewal/logout, upstream token revocation/logout-all, absolute/idle expiry, real browser/proxy and multi-node shared cache/keyring remain release gates.
- `Identity:Bff:Enabled=false` remains default; Angular continues legacy Password Grant/localStorage without runtime changes. Merge only after final documentation-head CI.


## BFF-05.3 verified adapter checkpoint — 2026-11-06

- [Draft PR #24](https://github.com/sholehamed/foundation-backend/pull/24) includes a BFF-only abstract validation contract and a Web-host Identity DB adapter. The production Web composition registers OIDC ticket capture and cookie `OnValidatePrincipal` live user/Tenant/security-stamp/grant checks only when `Identity:Bff:Enabled=true`.
- [Code-head CI #42206181883](https://github.com/sholehamed/foundation-backend/actions/runs/42206181883): **82 BFF + 58 Identity tests**, full solution build passed. Added direct adapter DB tests and HTTP CookieAuthentication tests for System/Tenant, security-stamp rotation, tenant deactivation and removed-session replay.
- The HTTP tests issue a **test-only baseline ticket**, not an actual OIDC callback; the new OIDC `OnTicketReceived` adapter hook still needs true end-to-end verification with the live Foundation Identity server, as do stamp-exchange callback races.
- Do **not** assert production readiness: BFF flag stays disabled, Angular legacy Password Grant/localStorage unchanged. Logout-all, remote token revocation, renewal races, actual browser, SQL Server and multi-node infrastructure still pending.

> Tracking: [Issue #12](https://github.com/sholehamed/foundation-backend/issues/12). First security slice [PR #14](https://github.com/sholehamed/foundation-backend/pull/14) merged at `53732242450bd90c206e29738007372bbe2dde1b` (35 BFF + 41 Identity tests passed in CI 42131501508).  
> [Merged PR #16](https://github.com/sholehamed/foundation-backend/pull/16) introduces an opt-in browser challenge and callback cookie plumbing. It **does not yet prove successful cookie issuance in an actual OIDC callback E2E**; the feature is disabled by default.

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

## Earlier merged BFF checkpoint — BFF-04.2

- **Merged to `main`:** [PR #16](https://github.com/sholehamed/foundation-backend/pull/16) at `c4f6a81c302f3524bed3e58931221874be16928f`. Last verified PR-head [CI 42140557977](https://github.com/sholehamed/foundation-backend/actions/runs/42140557977): **46/46 BFF** and **42/42 Identity** tests plus full solution build passed. This supersedes historical draft statuses lower in this file.
- **Included:** opt-in BFF OIDC Authorization Code + PKCE challenge, configured callback middleware, protected server-side ticket store and isolated Secure/HttpOnly host-only cookie scheme; Identity code-grant token endpoint passthrough and current-user revalidation; strict host-derived System/Tenant login context and validated return paths. Broad CORS credentials were not enabled.
- **Still not accepted as complete BFF-04:** no real two-host OIDC callback E2E or actual cookie-issuance/browser verification. `Identity:Bff:Enabled` is **false by default**. Do not enable in production or migrate existing Angular token storage on this evidence alone.
- **Next task:** implement authentic Identity Server ↔ BFF callback integration tests covering protected browser cookie, invalid/missing nonce/state/correlation, code replay, account/tenant mismatch and logout/expiration. Then BFF-05 (`/bff/session`, CSRF, revocation, fresh host/context validation) and BFF-06 Angular migration after cross-repo E2E.
- **Frontend dependency:** [Frontend PR #8](https://github.com/sholehamed/foundation-frontend/pull/8) merged at `e94c144e4293faa297290aa608c5e7409aeaa1c2`, 22 unit tests + development build passed; it handles only same-origin Identity UI continuation and preserves legacy Password Grant.
- **Code map and test/deployment instructions:** [BFF-04.2 implementation handoff](development/BFF-04-IMPLEMENTATION.md).

## First security slice

- `Security/BffLocalReturnUrl.cs`: strict same-origin rooted path validation for future `/bff/auth/login?returnUrl=...` and post-login callback routing.
- Accepted: `/`, `/dashboard`, `/dashboard?tab=appointments`.
- Rejected: absolute URLs, protocol-relative paths, backslashes, path traversal, percent-encoded pathname, fragments, CR/LF, overlong input.
- The BFF challenge handler **must** carry the validated result as protected ASP.NET Core OIDC `AuthenticationProperties`; it must never simply reflect an arbitrary request return URL as a Redirect destination.
- This validator is not an authorization check; System/Tenant login and host membership must still be verified by the Identity/BFF middleware.
- Unit tests: `tests/Foundation.Bff.Tests/BffLocalReturnUrlTests.cs`.

## BFF-04 remaining

Confidential OIDC challenge/handshake, authorization-code callback, correlation/nonce, server-held tokens, ticket-backed `__Host-Foundation.Bff` cookie, safe Identity UI continuation and integration E2E. See [ROADMAP](../../../docs/PROJECT-ROADMAP.md) and [PROJECT-STATE](../../../docs/PROJECT-STATE.md).

Current legacy Angular login is untouched, with no automatic migration or token changes.

## BFF-04.2 current Draft PR (2026-11-06)

- [PR #16](https://github.com/sholehamed/foundation-backend/pull/16) introduces optional `AddBffAuthentication` and `MapBffEndpoints`, a confidential OIDC Code+PKCE challenge, callback `/signin-oidc`, separate `BffCookie` and opaque server-side ticket store. These are **NOT merged into main** and are not complete production BFF authentication.
- `BffHostContextResolver` determines System/Tenant from the validated request host. `BffSessionContextValidator` checks account type/tenant/callback correlation before a BFF ticket can be accepted.
- Unit tests in `tests/Foundation.Bff.Tests` cover return URL, context validation and ticket store. HTTP challenge tests use TestServer and static OIDC metadata instead of a live fake Identity Provider.
- At [CI 42135641269](https://github.com/sholehamed/foundation-backend/actions/runs/42135641269), 3 of 46 BFF tests failed. Root cause of two `500` challenges: isolated test tried external OIDC discovery; a third `500` came from default-scheme-free `Results.Forbid()`. Both have branch fixes validated: [CI #42138452399](https://github.com/sholehamed/foundation-backend/actions/runs/42138452399) **passed** (solution Build; 46/46 BFF tests and 42/42 Identity tests). This confirms the isolated HTTP challenge test fix, **not** full deployed Identity/BFF callback integration..
- Next: confirm CI, prove challenge redirect context/PKCE, then integrate full callback + exchange, persistent session and cookie security under real Identity TestServer. No Angular migration until cross-repository E2E.


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

## BFF-05.1 — read-only browser session slice (merged PR #19)

- [PR #18](https://github.com/sholehamed/foundation-backend/pull/18) is merged at `3f90363227ed5497c82a378c9f73093c46adcad4` (signed fake-issuer callback CI green). Real Identity Server integration remains pending.
- New `GET /bff/session` uses explicit BFF cookie authentication; bearer credentials never authenticate this endpoint. It validates current host/callback against registered redirect hosts, System/Tenant context, and tenant claims, and returns only a minimal no-store session snapshot. 401 = missing/invalid cookie; 403 = host/context mismatch.
- Tests: `tests/Foundation.Bff.Tests/BffSessionHttpTests.cs`. Issue remains open: real two-host code-flow E2E, active-user/tenant DB checks, revocation, CSRF/logout, distributed storage and Angular migration. `Identity:Bff:Enabled` stays false by default.

## BFF-04.4 real Foundation Identity ↔ BFF two-host checkpoint — 2026-11-06

- [Draft PR #22](https://github.com/sholehamed/foundation-backend/pull/22) implements an isolated two-TestServer HTTP gate using the actual OpenIddict Foundation Identity endpoints and the actual confidential BFF OIDC middleware. No fake issuer, token, key or UserInfo is used; only transport and test-only Identity cookie sign-in are provided by the fixture.
- [PR-head CI #42198434579](https://github.com/sholehamed/foundation-backend/actions/runs/42198434579) passed: full solution build (0 errors, 264 existing warnings), **58/58 BFF tests** and **48/48 Identity tests**. This includes six new cross-module positive/negative Identity+BFF cases; verify final latest-head CI after documentation changes before merge.
- Real discovery/JWKS, Authorization Code+PKCE, token exchange/UserInfo, secure opaque BFF cookie and /bff/session were exercised for System and Tenant accounts. Security-stamp change before exchange, account-context mismatch, reused code, missing correlation cookie and cross-host cookie replay are rejected.
- Root causes fixed **inside the test fixture**, not Production: stable EF InMemory database name across scopes; proper Delegate route mapping so the real UserInfo IResult is executed rather than silently discarded. Temporary OIDC diagnostics only exist in TestServer. Production authentication/CORS/cookies stay unchanged.
- **Not full-production acceptance:** Identity login UI + CAPTCHA/browser Playwright, SQL Server data behavior, trusted reverse proxy, shared Data Protection keyring, durable cache, session refresh, CSRF/logout/revocation/live tenant grant revalidation remain pending. BFF remains `Identity:Bff:Enabled=false` by default. Angular still uses legacy Password Grant and localStorage.
- Protocol report: [BFF-04 real Identity E2E](../../../docs/development/BFF-04-REAL-IDENTITY-E2E.md). BFF-05 tasks: [Issue #20](https://github.com/sholehamed/foundation-backend/issues/20); Angular BFF-06: [Issue #10](https://github.com/sholehamed/foundation-frontend/issues/10).
