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

## How to resume

1. Read [ADR-0001](decisions/ADR-0001-browser-bff.md), [architecture](architecture/browser-bff.md), and [HTTP contract](reference/browser-auth-contract.md).
2. Read [roadmap](PROJECT-ROADMAP.md) and select the first uncompleted slice.
3. Check live GitHub PR/CI state before assuming the checkpoint above is still current.
4. Work as vertical slices: branch → implementation → automated tests → docs update → CI → review → merge.
5. Update this file and the roadmap in every PR that changes implementation status or the next planned work.
