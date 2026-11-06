# Browser authentication HTTP contract — v1 proposal

> Status: **Partially implemented through merged BFF-05.1 PR #19**. GET /bff/session exists behind a disabled-by-default BFF flag; CSRF/logout/revocation remain planned. Actual Foundation Identity↔BFF E2E is unverified. API response schemas beyond the tested session endpoint remain provisional.

## Latest verified merged checkpoint — BFF-05.1 (2026-11-06)

- **Merged:** [BFF-04.3 PR #18](https://github.com/sholehamed/foundation-backend/pull/18) at `3f90363227ed5497c82a378c9f73093c46adcad4`; [CI 42143363623](https://github.com/sholehamed/foundation-backend/actions/runs/42143363623) passed signed fake-issuer OIDC callback tests.
- **Merged:** [BFF-05.1 PR #19](https://github.com/sholehamed/foundation-backend/pull/19) at `bcfc109488be75981dee7f3aead91e1bca38d976`; [latest PR-head CI 42194597675](https://github.com/sholehamed/foundation-backend/actions/runs/42194597675): full solution Build (0 errors; 263 pre-existing warnings), **58/58 BFF** and **42/42 Identity** tests passed.
- **What now exists (opt-in):** GET `/bff/session`, explicitly cookie-scheme authenticated (bearer alone is 401); safe no-store System/Tenant JSON; current-host/callback/tenant binding checked on every session read. 401 anonymous/invalid, 403 host mismatch. Fake test cookie issuance is not a real Identity↔BFF E2E.
- **Still blocked:** real two-host OpenIddict Identity↔BFF E2E ([Issue #12](https://github.com/sholehamed/foundation-backend/issues/12)); CSRF/logout/revocation/live user and tenant DB checks and durable infrastructure ([Issue #20](https://github.com/sholehamed/foundation-backend/issues/20)). `Identity:Bff:Enabled=false` by default. Frontend BFF-06 ([Issue #10](https://github.com/sholehamed/foundation-frontend/issues/10)) is not started; Angular Password Grant/localStorage are unchanged.
- **Two F00 baseline PRs remain open by explicit decision.** All later slices require independent PR, tests and documentation. This section supersedes older snapshots that call PR #16 or #18 Draft.

## BFF-04.4 real Foundation Identity ↔ BFF two-host checkpoint — 2026-11-06

- [Draft PR #22](https://github.com/sholehamed/foundation-backend/pull/22) implements an isolated two-TestServer HTTP gate using the actual OpenIddict Foundation Identity endpoints and the actual confidential BFF OIDC middleware. No fake issuer, token, key or UserInfo is used; only transport and test-only Identity cookie sign-in are provided by the fixture.
- [PR-head CI #42198434579](https://github.com/sholehamed/foundation-backend/actions/runs/42198434579) passed: full solution build (0 errors, 264 existing warnings), **58/58 BFF tests** and **48/48 Identity tests**. This includes six new cross-module positive/negative Identity+BFF cases; verify final latest-head CI after documentation changes before merge.
- Real discovery/JWKS, Authorization Code+PKCE, token exchange/UserInfo, secure opaque BFF cookie and /bff/session were exercised for System and Tenant accounts. Security-stamp change before exchange, account-context mismatch, reused code, missing correlation cookie and cross-host cookie replay are rejected.
- Root causes fixed **inside the test fixture**, not Production: stable EF InMemory database name across scopes; proper Delegate route mapping so the real UserInfo IResult is executed rather than silently discarded. Temporary OIDC diagnostics only exist in TestServer. Production authentication/CORS/cookies stay unchanged.
- **Not full-production acceptance:** Identity login UI + CAPTCHA/browser Playwright, SQL Server data behavior, trusted reverse proxy, shared Data Protection keyring, durable cache, session refresh, CSRF/logout/revocation/live tenant grant revalidation remain pending. BFF remains `Identity:Bff:Enabled=false` by default. Angular still uses legacy Password Grant and localStorage.
- Protocol report: [BFF-04 real Identity E2E](development/BFF-04-REAL-IDENTITY-E2E.md). BFF-05 tasks: [Issue #20](https://github.com/sholehamed/foundation-backend/issues/20); Angular BFF-06: [Issue #10](https://github.com/sholehamed/foundation-frontend/issues/10).

## Latest BFF checkpoint — 2026-11-06 (BFF-04.4 merged; BFF-05.2 active)

- **BFF-04.4 merged:** [Backend PR #22](https://github.com/sholehamed/foundation-backend/pull/22), commit `f53c5e04d899b09d33133f57beafe234b671501c`. Actual *Foundation OpenIddict Identity TestServer + separate BFF TestServer*, real discovery/JWKS, Code+PKCE, UserInfo, secure cookie and host isolation. Final PR-head [CI #42198862063](https://github.com/sholehamed/foundation-backend/actions/runs/42198862063): **58 BFF and 48 Identity tests passed**, solution build green. This is not a deployed-browser, SQL Server or multi-node acceptance result.
- **BFF-05.1 merged:** [PR #19](https://github.com/sholehamed/foundation-backend/pull/19), GET `/bff/session` explicitly authenticates BFF cookie (no bearer fallback), revalidates registered Host and Account/Tenant context; 401/403, safe no-store JSON.
- **BFF-05.2 draft:** [PR #23](https://github.com/sholehamed/foundation-backend/pull/23) implements opt-in GET `/bff/auth/csrf` and POST `/bff/auth/logout` for the **current server-side session** only. Requires BFF cookie/host validation, same-origin Origin/Fetch-Metadata policy and a validated ASP.NET Core antiforgery request header+cookie. Antiforgery is bound to the validated BFF principal. Logout calls cookie scheme `SignOutAsync` to remove its `ITicketStore` ticket, not merely the browser cookie. System/Tenant, failure, replay, and 401/403/400 cases have HTTP tests; await latest PR-head CI before claiming complete.
- **Still not implemented/proven:** cross-session logout-all, Identity Provider logout/token revocation, fresh database user/tenant/stamp/permission checks for every protected request, CSRF enforcement across future *all* unsafe BFF APIs, refresh coordination, restart and distributed keyring/cache failures, browser Angular Playwright and SQL Server/deployment validation.
- `Identity:Bff:Enabled=false` by default. Angular Password Grant/localStorage unchanged; **do not migrate browser auth until end-to-end lifecycle and deployment gates**. The initial F00 PR #2 remains untouched.


| Method | Path | Auth | Browser-visible result |
| --- | --- | --- | --- |
| GET | `/bff/auth/login?returnUrl=/app` | Anonymous | 302 to OIDC authorize; validates local return URL |
| GET | `/signin-oidc` | OIDC protocol | Callback middleware, never echoes tokens |
| GET | `/bff/session` | BffCookie | 200 minimal safe user/context JSON, 401 if no valid session |
| GET | `/bff/auth/csrf` | BffCookie (or explicitly bootstrap-safe) | 204, sets readable `XSRF-TOKEN` cookie |
| POST | `/bff/auth/logout` | BffCookie + CSRF | 204, revoke ticket, expire cookie; optionally AS logout |
| POST | `/bff/auth/logout-all` | BffCookie + CSRF + verified user | 204, revoke all sessions; not a browser-only clear |

```json
{
  "authenticated": true,
  "user": {
    "id": "user-guid",
    "username": "operator",
    "displayName": "Operator"
  },
  "context": {
    "type": "tenant",
    "tenantId": "tenant-guid",
    "tenantSlug": "salon-a"
  }
}
```

## Current merged BFF checkpoint — 2026-11-06

- **Merged to `main`:** [PR #16](https://github.com/sholehamed/foundation-backend/pull/16) at `c4f6a81c302f3524bed3e58931221874be16928f`. Last verified PR-head [CI 42140557977](https://github.com/sholehamed/foundation-backend/actions/runs/42140557977): **46/46 BFF** and **42/42 Identity** tests plus full solution build passed. This supersedes historical draft statuses lower in this file.
- **Included:** opt-in BFF OIDC Authorization Code + PKCE challenge, configured callback middleware, protected server-side ticket store and isolated Secure/HttpOnly host-only cookie scheme; Identity code-grant token endpoint passthrough and current-user revalidation; strict host-derived System/Tenant login context and validated return paths. Broad CORS credentials were not enabled.
- **Still not accepted as complete BFF-04:** no real two-host OIDC callback E2E or actual cookie-issuance/browser verification. `Identity:Bff:Enabled` is **false by default**. Do not enable in production or migrate existing Angular token storage on this evidence alone.
- **Next task:** implement authentic Identity Server ↔ BFF callback integration tests covering protected browser cookie, invalid/missing nonce/state/correlation, code replay, account/tenant mismatch and logout/expiration. Then BFF-05 (`/bff/session`, CSRF, revocation, fresh host/context validation) and BFF-06 Angular migration after cross-repo E2E.
- **Frontend dependency:** [Frontend PR #8](https://github.com/sholehamed/foundation-frontend/pull/8) merged at `e94c144e4293faa297290aa608c5e7409aeaa1c2`, 22 unit tests + development build passed; it handles only same-origin Identity UI continuation and preserves legacy Password Grant.
- **Code map and test/deployment instructions:** [BFF-04.2 implementation handoff](development/BFF-04-IMPLEMENTATION.md).

### Mandatory semantics

- Do not return `access_token`, `refresh_token`, `id_token`, `token_type` or Authorization header in browser JSON, redirects or localStorage.
- `Cache-Control: no-store` on session, login callback and sensitive responses.
- `401` for missing/expired session, `403` for authenticated but unauthorized; neither emits HTML redirect for API callers.
- `POST/PUT/PATCH/DELETE` with BFF cookie require CSRF validation; reject missing/invalid tokens.
- Sessions are host-scoped; tenant/domain mismatch must fail closed.
- Validate and normalize `returnUrl` as a local, same-host path only; never reflect external redirects.
- Angular must not use `grant_type=password` or a token-bearing HTTP interceptor after migration.
- **Do not expose new endpoints or mark them Done before tests demonstrate their behavior.**

### Acceptance tests (cross-repository)

1. Authorization Code + PKCE and callback work for System and Tenant; replayed code fails.
2. Browser sees only host-only HttpOnly Secure cookie, never OAuth tokens.
3. `/bff/session`: anonymous 401 / authenticated 200 / revoked or expired 401; no caching.
4. Cross-tenant cookie replay fails; Host spoofing and untrusted forwarded headers fail.
5. Unsafe request without anti-forgery fails; valid request succeeds.
6. Session invalidates on logout, revoke-all and active-role/tenant removal.
7. Concurrent refresh and multi-instance/restart tests succeed with shared session store.
8. Existing bearer/native consumers retain explicitly declared access during migration.
9. Angular guards use `/bff/session`, not localStorage token presence.


---

## Existing Identity Server endpoints — BFF-02 (implemented, not the BFF contract)

These endpoints belong to `Modules.System.Identity`. They are an intermediate Identity Server session primitive, **not** the future `/bff/*` browser API.

| Method | Path | Auth / protection | Behavior |
| --- | --- | --- | --- |
| GET | `/api/system/Auth/csrf` | Anonymous bootstrap | Sets HttpOnly antiforgery cookie plus readable `XSRF-TOKEN`; response is no-store. |
| POST | `/api/system/Auth/interactive-login` | Anonymous + IP rate limit + CAPTCHA + antiforgery | Accepts `username`, `password`, `captchaId`, `captchaCode`, `loginContext` (`system` or `tenant`) and optional `tenant`; sets Identity Server cookie only; returns 204 on success and no OAuth token. |

For `interactive-login`, send the antiforgery request token in `X-XSRF-TOKEN`. The server cookie is `__Host-Foundation.Antiforgery`; the Identity Server session cookie is `__Host-Foundation.Identity`. Both are Secure, HttpOnly (except the request-token cookie), host-only and Path=/; HTTPS is mandatory.

The existing `POST /api/system/Auth/login` token endpoint continues to accept the legacy Password and Refresh Token grants. Its credential verification now shares the same authenticator as interactive sign-in. This is temporary migration compatibility, not the target browser flow.

### Implementation status (updated 2026-11-06)

The opt-in `/bff/auth/login`, `/signin-oidc` callback middleware and `GET /bff/session` are merged but disabled by default. `/bff/auth/csrf`, `/bff/auth/logout`, and `/bff/auth/logout-all` are not implemented. The current Angular client must not use the new BFF session until Identity↔BFF E2E and all lifecycle/CSRF/revocation checks pass.


## Authorization Server endpoint — BFF-03 (in-progress)

| Method | Path | Requirement |
| --- | --- | --- |
| GET/POST | `/api/connect/authorize` | OpenIddict must validate client, exact redirect URI, response type, scopes and PKCE. Handler explicitly checks Identity Server cookie and a live database-scoped principal, then signs into the OpenIddict server scheme. |
| GET | `/api/system/Auth/csrf` | Existing CSRF bootstrap before interactive Identity login; no token issued. |
| POST | `/api/system/Auth/interactive-login` | Existing anti-forgery- and CAPTCHA-guarded Identity cookie issuance; frontend OAuth continuation not implemented yet. |

The following protocol parameters are needed on authorization: `client_id`, `response_type=code`, `scope` including `openid`, `redirect_uri` (exact), `state`, `nonce`, `code_challenge`, `code_challenge_method=S256`, and `login_context=system|tenant`; Tenant flows also carry the exact `tenant` slug. The client and URI must have been explicitly registered and PKCE enforced by OpenIddict. Never accept `returnUrl` as an absolute arbitrary redirect; only allow safe local paths and review login-UI continuation.

**Current status:** only the handler and registration scaffold exist on PR #11. Protocol E2E must verify code issuance, token exchange, invalid client/redirect, replay and consent behavior before the endpoint can be called production-ready. No OAuth tokens are intended for browser JSON responses.

## BFF-04.2 login challenge contract (PR #16 merged; disabled by default)

- `GET /bff/auth/login?returnUrl=<localPath>` is an anonymous navigation endpoint, opt-in by `Identity:Bff:Enabled`.
- Invalid return URL or unknown browser host: `400`. Tenant or context supplied by query that conflicts with host-derived identity: `403`.
- Valid request: `302` OIDC Authorization Code redirect with PKCE S256, state, nonce, confidential registered client and exact callback `/signin-oidc`; client and server must carry System vs Tenant identity context.
- Session cookie and OAuth tokens are not provided by the challenge response. Callback/CSRF/revocation remain outside this tested contract until integration passes.
- This version is a **Draft partial implementation**. Initial challenge tests failed during fake-IdP metadata discovery and were fixed in TestServer; latest pre-doc CI [42140046994](https://github.com/sholehamed/foundation-backend/actions/runs/42140046994) passed 46 BFF and 42 Identity tests. Full callback/cookie E2E remains unverified.


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

## BFF-05.1 read-only session contract — merged PR #19, tests green

- With `Identity:Bff:Enabled=true` **in an isolated test environment only**, `GET /bff/session` authenticates the explicit `Foundation.Bff` cookie scheme; a bearer token alone is not a BFF session. When disabled, the endpoint is not mapped (404).
- `200` example: `{ "authenticated": true, "user": { "id": "<subject>", "username": "operator", "displayName": "Operator" }, "context": { "type": "tenant", "tenantId": "<uuid>", "tenantSlug": "tenant-a" } }`. System context returns `tenantId: null`, `tenantSlug: null`.
- `401` for no/invalid cookie, `403` for a cookie whose stored System/Tenant context does not match the current registered host and callback origin. All responses use `Cache-Control: no-store`; none include OAuth credentials. API auth never redirects to an HTML login page.
- This endpoint does **not** yet validate current Identity database security stamp or revoked sessions, or implement CSRF/Logout. Do not enable the BFF or switch Angular while those gates and real Identity↔BFF E2E are pending.
