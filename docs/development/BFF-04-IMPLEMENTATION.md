# BFF-04.2 — merged implementation map and acceptance gates

> Verified 2026-11-06. **PR #16 is merged**, not equivalent to an operational full-browser BFF. Canonical live status: [PROJECT-STATE](../PROJECT-STATE.md), [ROADMAP](../PROJECT-ROADMAP.md), [Issue #12](https://github.com/sholehamed/foundation-backend/issues/12).

## Merge/test evidence

- Backend PR #16 merged to `main`: `c4f6a81c302f3524bed3e58931221874be16928f`.
- PR-head GitHub Actions [42140557977](https://github.com/sholehamed/foundation-backend/actions/runs/42140557977): solution Restore and Build successful; Foundation.Bff.Tests **46/46** and Shinera.Identity.Tests **42/42**.
- Frontend identity continuation PR #8 merged to `main`: `e94c144e4293faa297290aa608c5e7409aeaa1c2`; Angular CI 22/22 tests and development build.
- No integration evidence yet that a browser receives a completed `/signin-oidc` callback or a BFF session cookie after exchanging a code against the actual configured Identity issuer. **Do not infer it from the unit or challenge tests.**

## Backend code map

| Component | Responsibility | Test coverage |
| --- | --- | --- |
| `src/modules/Modules.System.Bff/BffServiceCollectionExtensions.cs` | Optional confidential ASP.NET Core OIDC handler (code + PKCE), callback, UserInfo claim mapping, separate BFF cookie and protected ticket-store registration | Isolated challenge HTTP tests; not full callback E2E |
| `src/modules/Modules.System.Bff/BffEndpointRouteBuilderExtensions.cs` | `/bff/auth/login` challenge, return-path validation, host/tenant context and exact callback gate | `BffOidcChallengeHttpTests` |
| `src/modules/Modules.System.Bff/BffHostContextResolver.cs` | Distinguish System root host vs tenant subdomain | BFF challenge/context tests |
| `src/modules/Modules.System.Bff/BffSessionContextValidator.cs` | Validate callback principal/account/tenant, expected callback URI and safe return path | `BffSessionContextValidatorTests` |
| `src/modules/Modules.System.Bff/DistributedBffTicketStore.cs` | Protect session ticket using Data Protection, persist via `IDistributedCache`, issue only opaque key reference | `DistributedBffTicketStoreTests` |
| `src/modules/Modules.System.Bff/Security/BffLocalReturnUrl.cs` | Allowlisted same-origin relative navigation, reject open redirects/traversal | `BffLocalReturnUrlTests` |
| `src/modules/Modules.System.Identity/Web/Endpoints/Connect.cs` | Validate Identity session and client; issue OpenIddict Authorization Code only after matching account/tenant | `OpenIddictAuthorizationHttpTests` |
| `src/modules/Modules.System.Identity/Web/Endpoints/Auth.cs` | In the actual passthrough token endpoint, exchange Authorization Code after current principal/security-stamp/tenant revalidation; preserve legacy grants | `OpenIddictAuthorizationHttpTests` uses explicit passthrough |
| `src/modules/Modules.System.Identity/Infrastructure/Persistence/OpenIddictBffClientSeeder.cs` | Optional confidential first-party client, exact redirect URI and PKCE grants; do not overwrite previously registered secrets | Identity unit tests |
| `src/Web/DependencyInjections.cs` | Host composition via `AddBffAuthentication` and `MapBffEndpoints` | Solution build |

## Intended protocol and trust boundaries

1. A browser navigates on the **same public origin** to `GET /bff/auth/login?returnUrl=/dashboard`.
2. BFF validates local return path, current host, System/Tenant context and exact `https://host/signin-oidc` callback, and invokes ASP.NET Core OIDC challenge (state, nonce, PKCE).
3. Identity `/api/connect/authorize` validates client, callback URI, account context, actual tenant and interactive Identity cookie. When needed, Angular Identity UI performs **same-origin** `GET /api/system/Auth/csrf`, `POST /api/system/Auth/interactive-login`, then navigates back to the allowlisted AS authorization URI.
4. Identity grants a one-time code; confidential BFF middleware must exchange it server-to-server and retrieve UserInfo, checking issuer, audience, nonce, correlation and context.
5. BFF must store the OAuth tokens **server-side only** in a protected `ITicketStore` ticket; `__Host-Foundation.Bff` browser cookie must be host-only, Secure, HttpOnly, Path=/ and contain an opaque protected reference.
6. BFF-05 will add `/bff/session`, CSRF, logout, revocation, refresh coordination and per-request account/host revalidation before Angular BFF migration.

## Configuration and deployment gates

- `Identity:Bff:Enabled` defaults to **false**. Do not turn it on in production merely because CI is green.
- `Identity:Bff:Authority` is the stable HTTPS issuer; it must match AS issuer metadata. Development loopback HTTP is allowed by config only in Development, but real Secure cookies require HTTPS.
- Set `Identity:Bff:ClientSecret` only in secret manager/user-secrets or environment; 32+ high-entropy characters; never commit the secret.
- `Identity:Bff:RedirectUris` holds exact public HTTPS callbacks ending in `/signin-oidc`. `Identity:Bff:SystemHosts` names explicit System hosts; tenant subdomains derive from `Tenant:WorkspaceBaseDomain`.
- Production requires a shared distributed ticket store and persisted shared ASP.NET Core Data Protection key ring. Existing development signing certificates in Identity are a separate production-hardening blocker.
- Keep Angular, Identity login pages and `/api` routes on the expected same browser origin. CORS is **not** a substitute for CSRF/session design; broad legacy CORS origins are not granted cookie credentials.
- Only trust forwarded headers from explicit reverse proxies; validate actual deployment Host/Proto and registered callback scheme before enabling.
- Host/context checks currently run during login/callback; BFF-05 must also validate host and permissions on every protected browser session request.

## BFF-04 remaining acceptance tests (do next)

- [ ] Run one authentic code+PKCE flow across separate Identity TestServer and BFF TestServer (real discovery, signing keys, token and UserInfo), assert successful callback and only opaque browser cookie.
- [ ] Negative tests: mismatched/replayed code, incorrect PKCE verifier, tampered state/nonce, missing correlation cookies, failed userinfo and malicious callback/redirect.
- [ ] Tenant vs System sign-in and cross-subdomain cookie replay must fail closed.
- [ ] Verify expired/revoked Identity security stamp cannot exchange code; preserve bearer/native clients.
- [ ] Test server restart/multi-instance with shared cache and Data Protection keys and validate failure on store outage.
- [ ] Add CSRF/session/logout/revocation APIs and E2E in BFF-05; migrate Angular only after those gates pass.

## How to resume safely

Inspect current GitHub `main`, the open PRs and the latest CI first. Start a new focused branch from current `main` for BFF-04 callback integration E2E, keep `Identity:Bff:Enabled=false` by default, and update this file plus the project state/roadmap on each functional change. Do not merge failing CI, silently loosen cookie/CORS rules or remove legacy Password Grant without explicit migration evidence.

## Active BFF-04.3 callback HTTP integration tests — PR #18 (Draft, not merged)

- [PR #18](https://github.com/sholehamed/foundation-backend/pull/18), branch `test/bff-04-real-oidc-callback`, extends `tests/Foundation.Bff.Tests/BffOidcCallbackHttpTests.cs` with an in-process **fake OIDC issuer** that exposes discovery/JWKS, signs RS256 ID tokens and handles code+PKCE token exchange and UserInfo. The **actual ASP.NET Core BFF OIDC middleware** processes the callback; the fake issuer does not represent the real Foundation Identity module.
- Acceptance assertions: OIDC callback redirects to the validated return path, protected `__Host-Foundation.Bff` host-only Secure/HttpOnly cookie contains no OAuth credentials, server ticket retains access/ID tokens, invalid state/nonce, tenant mismatch and reused code fail closed.
- Tests are isolated from real DNS/production credentials. No production middleware options or cookie restrictions were relaxed. Latest-head CI must pass before any merge.
- **Still required after PR #18:** End-to-end tests with two real Foundation TestServer hosts (OpenIddict Identity + BFF), including actual token endpoint passthrough, current user/tenant revalidation, identity cookies, and final browser session flows; then BFF-05 APIs, CSRF, logout, revocation and Angular migration.
