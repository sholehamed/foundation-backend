# BFF-04 — browser authentication integration (in progress)

> Tracking: [Issue #12](https://github.com/sholehamed/foundation-backend/issues/12). First security slice [PR #14](https://github.com/sholehamed/foundation-backend/pull/14) merged at `53732242450bd90c206e29738007372bbe2dde1b` (35 BFF + 41 Identity tests passed in CI 42131501508).  
> This module **does not yet issue a BFF browser cookie** or expose a login endpoint.

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
