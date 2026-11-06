# BFF-04 — browser authentication integration (in progress)

> Tracking: [Issue #12](https://github.com/sholehamed/foundation-backend/issues/12). First security slice [PR #14](https://github.com/sholehamed/foundation-backend/pull/14) merged at `53732242450bd90c206e29738007372bbe2dde1b` (35 BFF + 41 Identity tests passed in CI 42131501508).  
> [Merged PR #16](https://github.com/sholehamed/foundation-backend/pull/16) introduces an opt-in browser challenge and callback cookie plumbing. It **does not yet prove successful cookie issuance in an actual OIDC callback E2E**; the feature is disabled by default.

## Current merged BFF checkpoint — 2026-11-06

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
