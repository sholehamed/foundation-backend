# BFF-05.2 — CSRF bootstrap and current-session logout

> Draft PR [#23](https://github.com/sholehamed/foundation-backend/pull/23). This is **not** logout-all, remote IdP logout or full browser BFF acceptance. CI status must be checked on its latest head.

## Implemented endpoints (opt-in only)

| Method | Path | Protection | Response |
| --- | --- | --- | --- |
| GET | `/bff/auth/csrf` | Explicit BFF cookie, validated current registered System/Tenant Host | `204`, safe request-token cookie `XSRF-TOKEN` (Secure, host-only, JavaScript-readable); HttpOnly `__Host-Foundation.Antiforgery` cookie; `401` missing/invalid session; `403` context mismatch |
| POST | `/bff/auth/logout` | Explicit BFF cookie, registered host/account context, same-origin Origin/Referer and Fetch Metadata, valid `X-XSRF-TOKEN` plus antiforgery cookie | `204`, current `ITicketStore` ticket removed via `SignOutAsync`; invalid antiforgery `400`, cross-origin/host `403`, anonymous `401` |

All responses are `Cache-Control: no-store`. `GET` must never log the user out. Identity provider cookie remains logically separate from BFF cookie.

## Security decisions

1. `HttpContext.User` is populated with the **verified BFF cookie principal** for antiforgery validation. The default bearer/legacy scheme is not accepted as BFF auth.
2. `Sec-Fetch-Site: same-site` is deliberately rejected for unsafe browser requests: another tenant subdomain is not the same origin. If present, `Origin` or `Referer` must match scheme+host exactly. Token validation is **mandatory even if both headers are missing**.
3. Antiforgery `XSRF-TOKEN` is not an OAuth credential, while access/refresh/ID tokens remain exclusively in the protected server-side ticket.
4. Current-session logout removes the session server-side; simply expiring the browser cookie is not enough. This does not revoke the upstream OIDC refresh token or other BFF sessions.
5. Browser BFF requires HTTPS and registered Host; never widen CORS credentials or disable cookie security to make a local test pass.
6. Existing Angular Password Grant and localStorage are unchanged; `Identity:Bff:Enabled` remains false by default.

## Automated acceptance (verify CI before checking complete)

- [ ] Anonymous and bearer-only CSRF bootstrap rejected; valid BFF cookie receives XSRF header token and HttpOnly antiforgery cookie.
- [ ] System and Tenant valid POST logout returns 204; replaying the removed session cookie returns 401.
- [ ] Missing/forged CSRF token rejects logout without revoking session.
- [ ] Tenant A cookie/antiforgery on Tenant B host fails closed.
- [ ] Cross-origin Origin and disallowed Fetch Metadata cannot log out user despite valid tokens.
- [ ] Disabled BFF does not map new routes, existing BFF and Identity tests still pass.
- [ ] Latest CI green, behavior+contract docs updated.

## Deferred gates for BFF-05 and BFF-06

- Current Session is not the same as logout-all or account-wide revocation; implement verified index-by-user and revocation separately.
- Remote Identity Server logout/revocation may be attempted as best-effort after local removal, with well-defined failure semantics.
- CSRF enforcement must extend to every future unsafe BFF/first-party cookie API, not just this logout route.
- Fresh user/tenant/security stamp/grants, absolute/idle expiry and distributed refresh locking need a separate DB/service layer plus tests.
- Multi-instance ticket/cache/Data Protection failures, SQL Server, trusted reverse proxy, browser E2E and migration of Angular storage/guards are outstanding.

## Resume

Check [PR #23](https://github.com/sholehamed/foundation-backend/pull/23) latest commit+CI, repair test failures without removing security checks, update this report with exact counts, then merge only on green CI. Track [BFF-05 issue #20](https://github.com/sholehamed/foundation-backend/issues/20) and [frontend BFF-06 issue #10](https://github.com/sholehamed/foundation-frontend/issues/10).
