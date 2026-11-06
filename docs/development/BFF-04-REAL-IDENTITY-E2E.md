# BFF-04.4 — Real Foundation Identity to BFF protocol integration

> Status: merged PR #22 at f53c5e04d899b09d33133f57beafe234b671501c, final PR-head CI #42198862063 green (58 BFF + 48 Identity). This is NOT deployed-browser or production acceptance.

## Test topology

- Identity host: HTTPS identity.example.com, an ASP.NET Core TestServer running the **actual Foundation OpenIddict Identity module endpoints** (Connect authorization, Auth token exchange and UserInfo), scoped identity principals and the real confidential-client seeder.
- BFF host: HTTPS tenant-a.shinera.localhost and HTTPS localhost, a separate TestServer running Foundation BFF OIDC middleware and its ticket-backed cookie plus GET /bff/session.
- The test routes the BFF OIDC backchannel to the Identity TestServer HTTP handler. It does not mock metadata, signing keys, JWT issuance, userinfo or tokens.
- Identity test DB is EF Core InMemory. A test-only /__test/sign-in route directly signs in a real factory-built Identity principal; the interactive password/CAPTCHA frontend is NOT under test.
- No production auth, CORS, middleware or default BFF enablement switches change in this PR.

## Evidence-gated checks

- [x] Actual OpenIddict discovery and JWKS accepted by the BFF handler.
- [x] Real authorization-code + PKCE token exchange, signed ID-token validation, UserInfo and cookie issuance.
- [x] System and Tenant A logins produce correctly bound BFF sessions with safe JSON.
- [x] Browser-visible cookies remain opaque, Secure, HttpOnly, host-only; no OAuth tokens in response bodies.
- [x] System Identity cookie cannot authorize for a Tenant challenge.
- [x] Revoked security stamp before token exchange fails closed.
- [x] Code replay, missing correlation cookie and cross-tenant-host cookie replay fail closed.
- [x] Full .NET solution build and existing BFF / Identity test suites remain green.

## Remaining gates

1. Production Identity login UI and CAPTCHA, Angular Identity continuation, full Playwright browser redirect and cookie behavior.
2. SQL Server integration and real external reverse-proxy / trusted forwarded host checks.
3. BFF-05: CSRF bootstrap, invalid/valid unsafe requests, logout, ticket and refresh revocation, current user/tenant DB validation on authenticated APIs, shared durable cache and Data Protection key ring.
4. BFF-06 Angular session-store / guards migration and legacy Password Grant retirement only AFTER the above gates.

## Continuation

PR #22 merged. Continue with BFF-05.2 PR #23 (CSRF/bootstrap/current-session logout), then live DB revalidation and remaining session lifecycle. Do not migrate Angular yet.
## Verified test-head CI

- [GitHub Actions #42198434579](https://github.com/sholehamed/foundation-backend/actions/runs/42198434579): 58 BFF + 48 Identity tests all green; full solution build passed.
- Real Foundation OpenIddict test server had valid discovery/JWKS, Authorization Code+PKCE, signed ID token and UserInfo. The BFF stored an opaque server-held ticket and produced no OAuth tokens in session responses.
- Test harness bugs uncovered and repaired: ephemeral EF InMemory database names per scope, and an incorrect method-group MapGet overload that returned 200 with no UserInfo JSON. Neither bug required Production runtime changes.
- Still unverified: browser UI/HTTPS deployment, SQL Server, revoked BFF cookie after Identity changes, CSRF/logout, long-lived refresh and multi-node cache/keyring.
