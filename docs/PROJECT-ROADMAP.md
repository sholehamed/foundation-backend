# Foundation Backend — BFF implementation roadmap

> Last updated: 2026-11-06  
> Parent decision: [ADR-0001 — Full BFF](decisions/ADR-0001-browser-bff.md)  
> Current state: [PROJECT-STATE.md](PROJECT-STATE.md)

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
[Tracking issue #9](https://github.com/sholehamed/foundation-backend/issues/9) · [Draft PR #11](https://github.com/sholehamed/foundation-backend/pull/11) · **In progress — not merged; code and integration tests are not fully verified**

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
**Blocked on BFF-03**

Scope:
- Register OIDC client middleware in `Modules.System.Bff` using a confidential client and Authorization Code + PKCE.
- Challenge route with state/nonce and validated relative return URL.
- Callback validates correlation/state/nonce/issuer and performs server-to-server code exchange.
- Store protected ticket and tokens via `DistributedBffTicketStore`; issue only `__Host-Foundation.Bff` opaque cookie.
- Regenerate session identifier after login; enforce idle and absolute expiration.
- Do not wire the module into Web until this vertical flow works.

Acceptance:
- Browser never receives OAuth tokens in body, URL, JS storage or readable cookie.
- Invalid state/nonce, replayed callback, bad redirect and token exchange failures fail closed.
- Ticket store survives app restart when backed by shared durable cache and Data Protection key ring.

### BFF-05 — session API, CSRF, logout, revocation and tenant/host binding
**Blocked on BFF-04**

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
- Unit tests for identity session revalidation and scope filtering passed in CI before the protocol harness was added. A TestServer HTTP suite for authorization code, PKCE, replay, redirects and account-context isolation is now committed but not yet verified green.
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

The authorization endpoint currently handles GET; POST is explicitly refused to avoid losing form parameters on the login continuation path. Tests cover this behavior. Browser-based Identity login continuation and a full Angular BFF session are **not** part of BFF-03 and remain explicit blockers for subsequent slices.
