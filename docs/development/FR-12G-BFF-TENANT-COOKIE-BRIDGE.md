# FR-12G-A — Opt-in Tenant BFF cookie API bridge

2026-11-08. Implements an isolated acceptance slice for an eventual browser BFF migration. No default or Production cutover.

## Previously missing API bridge
Backend already supported BFF `/bff/session`, Authorization Code+PKCE, HttpOnly host-only session cookie and CSRF-protected BFF logout, **but authenticated Tenant `/api/tenant/workspace/**` endpoints use the host's default OpenIddict bearer scheme**. A valid BFF cookie alone could not authorize existing Angular Tenant APIs. Replacing the default scheme would silently affect native clients and System routes.

`src/Web/Bff/BffTenantWorkspaceCookieBridgeMiddleware.cs` is a deliberately narrow bridge for Tenant workspace endpoints only, enabled by BOTH `Identity:Bff:Enabled=true` and `Identity:Bff:TenantWorkspaceCookieBridge:Enabled=true`, and blocked in Production until browser acceptance. All deployment defaults remain OFF; it is not installed at all by default. The existing native bearer scheme remains the host default.

- The exact configured `/api/tenant/workspace` segment (and its children) is in scope; System API, other Tenant API families and near-prefix aliases are untouched.
- Any supplied `Authorization` header bypasses the cookie bridge, including invalid bearer; a cookie cannot rescue an invalid Authorization header. Bearer clients retain existing authentication and their existing non-CSRF API contract.
- For requests with a BFF cookie and no Authorization header, the middleware explicitly authenticates the BFF Cookie scheme. It rejects invalid/expired/revoked cookies (401), host/callback/login-context mismatches and System sessions (403) and requires Tenant account type.
- It relies on the existing Web BFF `OnValidatePrincipal` database revalidation (user activity, security stamp, live scoped grants). It does not trust claims or client TenantId as sufficient authority.
- For **unsafe cookie-authenticated verbs** the bridge requires secure HTTPS, exact Origin or safe Fetch Metadata + a valid ASP.NET Core antiforgery header/cookie bound to the authenticated BFF principal; invalid CSRF is 400, foreign origin 403. GET/HEAD remain read-only.
- The existing `TenantResolutionMiddleware`, live permission evaluator and endpoint read/write authorization still run normally; no role or grant bypass is introduced.

## Acceptance
`tests/Shinera.Identity.Tests/BffTenantWorkspaceCookieBridgeTests.cs` validates authenticated cookie requests on tenant workspace, rejected cookie on System/unrelated paths, cross-tenant callback replay, forged bearer with cookie, valid bearer priority, CSRF absent/forged, cross-origin/same-site rejection and successful same-origin cookie POST. Actual OIDC grant/session/SQL revocation integration is tested separately (BFF-04/05 and FR-12F). The new tests are genuine ASP.NET Core TestServer HTTP tests, **not a Chrome/Playwright browser or real HTTPS reverse-proxy test**.

## Remaining FR-12G phases
Add a separately opt-in Angular BFF Tenant session facade, route guard, same-origin CSRF integration and login navigation. Then run Chromium over a real HTTPS proxy with Identity and app origins, validate OpenIddict callback cookies, zero JWT in browser storage, role revocation, login/logout and cross-tenant isolation. No production activation before the browser, shared session store/keyring, upstream logout and refresh-token acceptance gates are complete.

Master tracker: backend Issue #44.