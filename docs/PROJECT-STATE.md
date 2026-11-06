## Active BFF-05.3 Web-host live validation (draft PR #24, CI pending)

- [BFF-05.2 PR #23](https://github.com/sholehamed/foundation-backend/pull/23) merged at `88239b7d3454fd03b87aa7bc20e70d1a820b088a`; latest PR-head [CI #42203066912](https://github.com/sholehamed/foundation-backend/actions/runs/42203066912): build, **82 BFF + 50 Identity** tests successful.
- [Draft PR #24](https://github.com/sholehamed/foundation-backend/pull/24) adds a narrow BFF contract and Web-owned Identity DB adapter for live account, tenant, security-stamp and scoped-grant revalidation. Local session snapshots remain exclusively in the server-side protected ticket, never the browser.
- See [BFF-05.3 design](development/BFF-05-3-LIVE-IDENTITY.md). Do not merge before current-head CI and Web event integration tests. `Identity:Bff:Enabled` remains false, Angular legacy login unchanged; distributed races, upstream token revocation, logout-all, browser E2E and SQL Server remain gated.

# Foundation Backend — Project State & Handoff

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
