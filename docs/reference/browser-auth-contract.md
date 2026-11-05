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
