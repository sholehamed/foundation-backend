# BFF-04.4 — Real Foundation Identity to BFF protocol integration

> Status: draft PR #22, pending CI and review. This is NOT a production or deployed-browser acceptance claim.

## Test topology

- Identity host: HTTPS identity.example.com, an ASP.NET Core TestServer running the **actual Foundation OpenIddict Identity module endpoints** (Connect authorization, Auth token exchange and UserInfo), scoped identity principals and the real confidential-client seeder.
- BFF host: HTTPS tenant-a.shinera.localhost and HTTPS localhost, a separate TestServer running Foundation BFF OIDC middleware and its ticket-backed cookie plus GET /bff/session.
- The test routes the BFF OIDC backchannel to the Identity TestServer HTTP handler. It does not mock metadata, signing keys, JWT issuance, userinfo or tokens.
- Identity test DB is EF Core InMemory. A test-only /__test/sign-in route directly signs in a real factory-built Identity principal; the interactive password/CAPTCHA frontend is NOT under test.
- No production auth, CORS, middleware or default BFF enablement switches change in this PR.

## Evidence-gated checks

- [ ] Actual OpenIddict discovery and JWKS accepted by the BFF handler.
- [ ] Real authorization-code + PKCE token exchange, signed ID-token validation, UserInfo and cookie issuance.
- [ ] System and Tenant A logins produce correctly bound BFF sessions with safe JSON.
- [ ] Browser-visible cookies remain opaque, Secure, HttpOnly, host-only; no OAuth tokens in response bodies.
- [ ] System Identity cookie cannot authorize for a Tenant challenge.
- [ ] Revoked security stamp before token exchange fails closed.
- [ ] Code replay, missing correlation cookie and cross-tenant-host cookie replay fail closed.
- [ ] Full .NET solution build and existing BFF / Identity test suites remain green.

## Remaining gates

1. Production Identity login UI and CAPTCHA, Angular Identity continuation, full Playwright browser redirect and cookie behavior.
2. SQL Server integration and real external reverse-proxy / trusted forwarded host checks.
3. BFF-05: CSRF bootstrap, invalid/valid unsafe requests, logout, ticket and refresh revocation, current user/tenant DB validation on authenticated APIs, shared durable cache and Data Protection key ring.
4. BFF-06 Angular session-store / guards migration and legacy Password Grant retirement only AFTER the above gates.

## Continuation

Track draft PR #22 at https://github.com/sholehamed/foundation-backend/pull/22. Check latest-head GitHub Actions, fix actual failures without weakening Identity/BFF checks, and record final result in docs/PROJECT-STATE.md and docs/PROJECT-ROADMAP.md before merge.