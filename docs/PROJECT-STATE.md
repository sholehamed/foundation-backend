# Foundation Backend — project state and handoff

> Last updated: 2026-11-06  
> Repository: `sholehamed/foundation-backend`  
> Current integration branch: `main`

This is the continuity checkpoint. Read this file first when resuming work in a new chat or environment. The roadmap describes future work; the architecture/ADR describe why and how.

## Product and architecture decisions

- Modular monolith on .NET 10; `src/Web` is the composition root.
- `Modules.System.Identity` owns users, tenant/system account context, OpenIddict Authorization Server, roles and permissions.
- `Modules.System.Bff` is a separate optional confidential OIDC client and browser session boundary. It must not become a second identity store.
- Browser target is **Full BFF**: Angular receives only a host-only HttpOnly Secure opaque session cookie; OAuth access/refresh/ID tokens stay server-side.
- OpenIddict Authorization Code + mandatory PKCE; BFF is a confidential client. Do not use Password Grant for the target browser flow.
- Identity Server interactive cookie and BFF application cookie are distinct:
  - `__Host-Foundation.Identity` — Authorization Server login session.
  - `__Host-Foundation.Bff` — browser application session backed by server-side `ITicketStore`.
- System and Tenant accounts have separate credential lookup and session context. Tenant context must be bound to an active tenant; never trust only a request header/slug.
- Keep the existing Password Grant temporarily for compatibility. Disable it only after all registered legacy clients and Angular pass migration E2E.
- Do not wire the BFF module into `src/Web` until the authorization-code and callback path is complete enough to work end-to-end.

## Current Git checkpoint

| Item | State |
| --- | --- |
| PR #6 — BFF-01 protected ticket store | Merged to `main` |
| Merge commit | `80f15d6c5bf59e6c557561fe97a2efbf1b5ee74a` |
| PR #8 — BFF-02 Identity cookie + credential authenticator | Merged to `main` at `4a798da96c735be0bc0823c82c02a495f4a9c4a8` |
| PR #8 CI | Run [42119993097](https://github.com/sholehamed/foundation-backend/actions/runs/42119993097): Restore, Build, 6 BFF tests and 25 Identity tests all passed |
| Angular | Not modified; existing token-based flow remains active |

PR #8 is merged; active implementation is now tracked by [BFF-03 issue #9](https://github.com/sholehamed/foundation-backend/issues/9).

## Next tracked slice

- [Issue #9 — BFF-03 OpenIddict authorization endpoint and identity-context validation](https://github.com/sholehamed/foundation-backend/issues/9).
- Its acceptance gate explicitly includes master-system-scope role/permission isolation, tenant-scoped assignments, security-stamp revalidation and PKCE/client/redirect validation.

## Implemented

### BFF-01 — protected distributed ticket-store primitive

- `Modules.System.Bff/DistributedBffTicketStore.cs` implements `ITicketStore`.
- Uses `IDistributedCache` and Data Protection; random opaque 256-bit lookup keys, protected ticket serialization, expiration and removal.
- Dedicated BFF cookie scheme and antiforgery registration primitives exist.
- Six tests cover ticket-store behavior.
- Not yet registered by the Web host; no browser session is issued yet.

### BFF-02 — Identity Server interactive session primitive

- Dedicated cookie scheme `Foundation.IdentityServer`, cookie name `__Host-Foundation.Identity`.
- `IUserCredentialAuthenticator` centralizes credential verification, lockout, and principal construction. Legacy Password Grant now calls this service too.
- New `GET /api/system/Auth/csrf` issues the ASP.NET Core antiforgery cookie and readable `XSRF-TOKEN`; clients return the request token in `X-XSRF-TOKEN`.
- New `POST /api/system/Auth/interactive-login` validates antiforgery + CAPTCHA, selects explicit `system` or `tenant` account context, and issues the Identity Server cookie only (204; no OAuth token).
- New login endpoint has a fixed-window IP rate limit of 20 requests/minute.
- Identity cookie: Secure, HttpOnly, host-only `__Host-` name, Path=/, SameSite=Lax, 8-hour ticket expiry with sliding expiration.
- Tests cover system account selection, tenant binding, invalid password, and lockout. Existing Identity suite remains green.
- This is **not** yet an OAuth login flow: the cookie is not yet consumed by `/api/connect/authorize`; no authorization code is issued and no BFF session is created.

## Known gaps and security gates

- Before authorization-code issuance, explicitly revalidate user active/locked state, security stamp, current tenant activity/membership and current role/permission state. A cookie's claims are not permanent authority.
- Audit System account role/permission claim scoping against the master system tenant. The legacy System Password Grant query currently reads all assignments for that System user; this behavior was preserved by the shared authenticator and must be narrowed/tested before those claims authorize a browser session.
- No integration test yet exercises real cookie issuance, antiforgery round-trip or the complete OpenIddict authorization-code flow.
- `__Host-` cookies require HTTPS. Do not relax Secure policy for local HTTP.
- BFF session storage requires shared durable cache and shared Data Protection keys in multi-instance production. Store outage must fail closed.
- Do not trust forwarded host/IP unless proxy networks are explicitly configured.


## In-flight work — BFF-03

- Branch: `feat/bff-03-authorization-endpoint` (based on merged BFF-02).
- Draft PR: [#11](https://github.com/sholehamed/foundation-backend/pull/11); [tracking issue #9](https://github.com/sholehamed/foundation-backend/issues/9).
- **Status: in development, not merged, not verified end-to-end.** Do not treat an opened PR or compiled code as a usable BFF.

### Changes implemented on the branch

- `Application/Services/IdentityPrincipalFactory.cs`: reconstructs principal claims using master System scope or the **owning** tenant; constrains role/group/role-permission relationships to that scope.
- `Application/Services/IdentitySessionValidator.cs`: verifies signed-in principal, account type, active user/tenant, matching security stamp and cookie identity; rebuilds claims from the database to avoid stale permissions.
- `Application/Services/UserCredentialAuthenticator.cs`: shares the scoped principal factory between password-grant compatibility and interactive login; keeps password hash verification and lockout.
- `Infrastructure/Persistence/OpenIddictBffClientSeeder.cs`: optional confidential first-party BFF client, strict exact redirects, PKCE, secrets from private configuration. Do not silently mutate an existing client's secret/redirects on application restart.
- `Web/Endpoints/Connect.cs`: handles `/api/connect/authorize` with explicit `Foundation.IdentityServer` cookie authentication; only configured first-party BFF client with implicit consent may proceed; validates account context and tenant slug; delegates code issuance to OpenIddict. Errors use the OpenIddict authentication scheme.
- `tests/Shinera.Identity.Tests/IdentitySessionAndScopeTests.cs`: adds tests for cross-tenant claims, inactive user/tenant, security stamp, role revocation and context mismatch.
- `tests/Shinera.Identity.Tests/OpenIddictAuthorizationHttpTests.cs`: new TestServer harness to cover actual OpenIddict authorization-code/PKCE round-trip and negative protocol behavior; latest CI on the new harness is not yet confirmed.
- `tests/Shinera.Identity.Tests/UserCredentialAuthenticatorTests.cs` adapted to the new principal factory constructor.
- `src/Web/appsettings.json`: BFF client feature disabled by default; callback arrays empty; never commit `ClientSecret`.

### Validation gates (not yet satisfied)

- Latest PR CI run must have Restore, Build, 6 BFF store tests, all Identity tests and all HTTP OpenIddict TestServer tests green. The pre-harness run [42127975232](https://github.com/sholehamed/foundation-backend/actions/runs/42127975232) was green (6+32 tests); it does not prove the newly added tests. The new BFF-03 tests may require corrections before this condition is met.
- An HTTP/OIDC integration test must demonstrate authorization requests with proper client/redirect, code + PKCE and rejection of code replay, bad PKCE, disallowed scopes, and cross-context auth.
- The current Angular sign-in component filters a returnUrl to app routes and **does not** continue `/api/connect/authorize` after cookie login. An integration login UI / post-login continuation must be implemented before end-to-end BFF is declared usable; do not alter existing browser grant flow prematurely.
- `Modules.System.Bff` is still not wired into the Web host; no BFF session cookie or BFF session endpoints are emitted.
- Consider sign-in endpoint integration: a browser-initiated OIDC login must be compatible with CSRF bootstrap and validated return URL on a trusted identity origin.
- Review all OpenIddict client registration settings and consent rules against the exact deployment and production secret provisioning.
- Reassess System-master privilege semantics and authorization endpoint interaction with current tenant resolution middleware in a real SQL Server/integration run.

### After successful PR merge

Update this section with actual PR number, head/merge SHA and CI run; move BFF-03 to merged in the roadmap. Start BFF-04 (BFF OIDC callback + server-side BFF cookie) only after the HTTP protocol tests prove the Authorization Code handler works.

## How to resume

1. Read [ADR-0001](decisions/ADR-0001-browser-bff.md), [architecture](architecture/browser-bff.md), and [HTTP contract](reference/browser-auth-contract.md).
2. Read [roadmap](PROJECT-ROADMAP.md) and select the first uncompleted slice.
3. Check live GitHub PR/CI state before assuming the checkpoint above is still current.
4. Work as vertical slices: branch → implementation → automated tests → docs update → CI → review → merge.
5. Update this file and the roadmap in every PR that changes implementation status or the next planned work.
