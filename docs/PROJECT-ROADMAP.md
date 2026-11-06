# Foundation Backend — BFF implementation roadmap

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
