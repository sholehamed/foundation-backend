# Browser authentication HTTP contract — v1 proposal

> Status: **Partially implemented in merged BFF-04.2 PR #16; disabled by default, not full-browser E2E verified**. Session/CSRF/logout APIs remain planned. API response schemas are provisional until integration tests and OpenAPI agree.

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

### Not yet available

The following remain Planned: `/bff/auth/login`, `/signin-oidc`, `/bff/session`, `/bff/auth/csrf`, `/bff/auth/logout`, and `/bff/auth/logout-all`. Do not build Angular against these routes until their implementation and integration tests are merged.


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

## BFF-05.1 read-only session contract — active branch, pending CI/merge

- With `Identity:Bff:Enabled=true` **in an isolated test environment only**, `GET /bff/session` authenticates the explicit `Foundation.Bff` cookie scheme; a bearer token alone is not a BFF session. When disabled, the endpoint is not mapped (404).
- `200` example: `{ "authenticated": true, "user": { "id": "<subject>", "username": "operator", "displayName": "Operator" }, "context": { "type": "tenant", "tenantId": "<uuid>", "tenantSlug": "tenant-a" } }`. System context returns `tenantId: null`, `tenantSlug: null`.
- `401` for no/invalid cookie, `403` for a cookie whose stored System/Tenant context does not match the current registered host and callback origin. All responses use `Cache-Control: no-store`; none include OAuth credentials. API auth never redirects to an HTML login page.
- This endpoint does **not** yet validate current Identity database security stamp or revoked sessions, or implement CSRF/Logout. Do not enable the BFF or switch Angular while those gates and real Identity↔BFF E2E are pending.
