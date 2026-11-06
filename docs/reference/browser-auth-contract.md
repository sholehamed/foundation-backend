# Browser authentication HTTP contract — v1 proposal

> Status: **Planned / not yet implemented**. Security requirements are mandatory when endpoints are introduced. API response schemas are provisional until integration tests and OpenAPI agree.

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

## BFF-04.2 login challenge draft contract (PR #16; not shipped)

- `GET /bff/auth/login?returnUrl=<localPath>` is an anonymous navigation endpoint, opt-in by `Identity:Bff:Enabled`.
- Invalid return URL or unknown browser host: `400`. Tenant or context supplied by query that conflicts with host-derived identity: `403`.
- Valid request: `302` OIDC Authorization Code redirect with PKCE S256, state, nonce, confidential registered client and exact callback `/signin-oidc`; client and server must carry System vs Tenant identity context.
- Session cookie and OAuth tokens are not provided by the challenge response. Callback/CSRF/revocation remain outside this tested contract until integration passes.
- This version is **Draft**, with failing challenge tests and diagnostic investigation tracked in [PR #16](https://github.com/sholehamed/foundation-backend/pull/16).


### 2026-11-06 — BFF-04.2 challenge test recovery

- The three unexpected HTTP 500 failures were traced to isolated OIDC TestServer metadata lookup against a fake Identity hostname and an unqualified Forbid response on a tenant mismatch. Production discovery has **not** been replaced with static metadata.
- Fixes: explicit HTTP 403 for host/tenant mismatch, `StaticConfigurationManager<OpenIdConnectConfiguration>` in the test harness only, diagnostics on unexpected TestServer 500.
- [CI #42138452399](https://github.com/sholehamed/foundation-backend/actions/runs/42138452399) **passed** (solution Build; 46/46 BFF tests and 42/42 Identity tests). This confirms the isolated HTTP challenge test fix, **not** full deployed Identity/BFF callback integration.
- BFF-04 remains **Draft PR #16**, not merged. Callback/token-exchange E2E, browser cookie issuance, live session lifecycle and multi-host verification are still required before acceptance.
