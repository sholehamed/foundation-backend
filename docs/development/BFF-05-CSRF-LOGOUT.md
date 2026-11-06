# BFF-05.2 — CSRF bootstrap and current-session logout

> Implemented in [PR #23](https://github.com/sholehamed/foundation-backend/pull/23). Verified code-head CI is recorded below; check the PR for final documentation-head checks and merge state.

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

- [x] Anonymous and bearer-only CSRF bootstrap rejected; valid BFF cookie receives XSRF header token and HttpOnly antiforgery cookie.
- [x] System and Tenant valid POST logout returns 204; replaying the removed session cookie returns 401.
- [x] Missing/forged CSRF token rejects logout without revoking session.
- [x] Tenant A cookie/antiforgery on Tenant B host fails closed.
- [x] Cross-origin Origin and disallowed Fetch Metadata cannot log out user despite valid tokens.
- [x] Disabled BFF does not map new routes, existing BFF and Identity tests still pass.
- [x] Latest CI green, behavior+contract docs updated.

## Deferred gates for BFF-05 and BFF-06

- Current Session is not the same as logout-all or account-wide revocation; implement verified index-by-user and revocation separately.
- Remote Identity Server logout/revocation may be attempted as best-effort after local removal, with well-defined failure semantics.
- CSRF enforcement must extend to every future unsafe BFF/first-party cookie API, not just this logout route.
- Fresh user/tenant/security stamp/grants, absolute/idle expiry and distributed refresh locking need a separate DB/service layer plus tests.
- Multi-instance ticket/cache/Data Protection failures, SQL Server, trusted reverse proxy, browser E2E and migration of Angular storage/guards are outstanding.

## Resume

Check [PR #23](https://github.com/sholehamed/foundation-backend/pull/23) latest commit+CI, repair test failures without removing security checks, update this report with exact counts, then merge only on green CI. Track [BFF-05 issue #20](https://github.com/sholehamed/foundation-backend/issues/20) and [frontend BFF-06 issue #10](https://github.com/sholehamed/foundation-frontend/issues/10).

## Continuation review — 2026-11-06

- Reviewed PR #23 head `663ebd201527b89e2126d14f35dc328de240d9d9`: [CI 42200256418](https://github.com/sholehamed/foundation-backend/actions/runs/42200256418) passed full solution build, **68 BFF + 48 Identity tests**. No unresolved review threads at review time.
- Added regression coverage for logout replay, preserving an independent ticket, request token without antiforgery cookie, GET logout refusal, cross-host CSRF bootstrap, HTTPS enforcement, Referer fallback, opaque Origin, port mismatch and Fetch Metadata boundaries. These additions require a new green PR-head CI before merge.
- Next implementation slice: live user/tenant/security-stamp/grant revalidation behind the BFF cookie boundary, followed by account-wide revocation and refresh coordination. Angular migration remains blocked; frontend handoff must reflect PR #22 and this slice. Keep both F00 PRs open.

## Real Identity lifecycle acceptance extension

The two-real-TestServer suite now exercises System and Tenant CSRF bootstrap and logout after actual OpenIddict code exchange. It checks missing-token rejection, removed-ticket replay, an independent ticket for the same user, and a fresh login using the deliberately preserved Identity cookie. This closes the protocol-level lifecycle gap; CAPTCHA/UI, real browser cookies, SQL Server and deployed infrastructure remain separate gates. Verify latest-head CI before accepting the new tests.

## Current checkpoint — BFF-05.2 verified implementation (2026-11-06)

- [PR #22](https://github.com/sholehamed/foundation-backend/pull/22) merged the real Foundation OpenIddict Identity + BFF two-TestServer flow at `f53c5e04d899b09d33133f57beafe234b671501c`.
- [PR #23](https://github.com/sholehamed/foundation-backend/pull/23) implements cookie-only CSRF bootstrap and current-session logout. Code head `2abab6b3973580298db01005b97f617f1014ff5f` passed [CI 42202732554](https://github.com/sholehamed/foundation-backend/actions/runs/42202732554): full solution build, **82 BFF + 50 Identity tests**, zero failures. Final documentation-head checks and merge state are available on the PR.
- Verified System/Tenant lifecycle through real OpenIddict code exchange: CSRF bootstrap, rejected missing-token logout, successful local ticket removal, replay rejection, preservation of a second ticket for the same user, and fresh login via the separate Identity cookie. Boundary tests also cover GET logout refusal, missing antiforgery cookie, cross-host CSRF bootstrap, HTTPS, Referer fallback, Origin/port and Fetch Metadata.
- This is **current-session local logout only**. It does not revoke upstream tokens, other sessions or the Identity cookie. Two-TestServer tests exclude the interactive password/CAPTCHA UI, browser cookie enforcement, SQL Server and deployed infrastructure.
- Frontend handoff [PR #12](https://github.com/sholehamed/foundation-frontend/pull/12) merged at `73b5d9ba46425a8951d7eda81c0573559d4ffc34`, after [CI 42202665599](https://github.com/sholehamed/foundation-frontend/actions/runs/42202665599) passed tests and build. Angular runtime still uses legacy Password Grant/localStorage. BFF remains disabled by default.
- **Next:** live user/tenant/security-stamp/grant revalidation at the BFF cookie boundary; then account-wide/upstream revocation, bounded idle/absolute lifetime and refresh coordination. Test logout-versus-renewal concurrency: the current cache store's unconditional renewal must not be treated as proof against ticket resurrection during concurrent requests.
- **Remaining release gates:** durable shared cache/keyring, restart/outage behavior, trusted proxy and SQL Server, real System/Tenant browser E2E, then Angular BFF-06 migration and legacy-client closeout. Both F00 PRs remain open.
- The sections below preserve historical checkpoints. This current checkpoint supersedes their old pending/merged status descriptions.


The initial two-host test extension failed compilation due to the domain entity named HttpMethod. An explicit System.Net.Http alias fixed the ambiguity; CI 42202732554 verifies the corrected tests without weakening assertions.
