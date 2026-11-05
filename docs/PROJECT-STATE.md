# Foundation Backend — Project State & Handoff

> Last verified: 2026-11-06  
> Repo: `sholehamed/foundation-backend` · integration branch: `main`  
> **Read this first in a new chat.** Always verify current GitHub head/CI before writing.

## Approved architecture

- .NET 10 Modular Monolith. `src/Domain`, `src/Application`, `src/Infrastructure` and `src/Web` are for the consuming application; `src/modules` and shared projects are future reusable libraries.
- `Modules.System.Identity` owns users, tenants, roles, permissions, OpenIddict Authorization Server. `Modules.System.Bff` is a separate optional browser-facing confidential OIDC client and server-side browser session boundary; `src/Web` composes them.
- Target browser pattern: **Full BFF**, Authorization Code + PKCE, server-held access/refresh/ID tokens, host-only Secure HttpOnly cookie `__Host-Foundation.Bff`. Identity Provider's separate cookie is `__Host-Foundation.Identity`.
- System accounts and Tenant accounts require explicit identity context and database-based tenant/permission checks. No cross-tenant grants from any role, group or nested permission.
- Legacy Password Grant stays available until existing consumers and Angular migrate successfully. Angular must **not** switch until BFF callback/session/revocation and real E2E pass.
- Reusable Angular library extraction is **postponed** until app features are complete and stable.

## Git & test checkpoint

| Milestone | State | Evidence |
| --- | --- | --- |
| BFF-01: protected server-side ticket store | **Merged** | [PR #6](https://github.com/sholehamed/foundation-backend/pull/6), commit `80f15d6c5bf59e6c557561fe97a2efbf1b5ee74a` |
| BFF-02: Identity interactive login cookie | **Merged** | [PR #8](https://github.com/sholehamed/foundation-backend/pull/8), commit `4a798da96c735be0bc0823c82c02a495f4a9c4a8` |
| BFF-02 handoff docs | **Merged** | [PR #10](https://github.com/sholehamed/foundation-backend/pull/10), commit `02a29943c178d2e835577a329d01c69f68560cad` |
| BFF-03: OpenIddict Authorization Code + PKCE and scoped validation | **Merged** | [PR #11](https://github.com/sholehamed/foundation-backend/pull/11), commit `c201e4c67d18c9d7896887a688eb31d3340d7930` |
| BFF-03 final-head CI | **Green** | [GitHub Actions 42130167589](https://github.com/sholehamed/foundation-backend/actions/runs/42130167589): solution Restore+Build, **6 BFF tests** and **41 Identity tests** passed |
| BFF-04: confidential client callback and BFF session | **Not implemented** | [Issue #12](https://github.com/sholehamed/foundation-backend/issues/12) |
| BFF-05: session API/CSRF/revocation | **Not implemented** | [Roadmap](PROJECT-ROADMAP.md) |
| BFF-06: Angular migration | **Not started** | [Frontend status](https://github.com/sholehamed/foundation-frontend/blob/main/docs/PROJECT-STATE.md) |

## Implemented modules / code map

### BFF-01

- `src/modules/Modules.System.Bff/DistributedBffTicketStore.cs`: ASP.NET Core `ITicketStore` with `IDistributedCache`, protected ticket bytes via Data Protection, opaque randomly generated session IDs, expiry and server-side deletion.
- `BffServiceCollectionExtensions.cs`: opt-in registration of an independent BFF cookie scheme, antiforgery services.
- **Not composed into `src/Web` yet**. No application BFF browser cookie is emitted.

### BFF-02

- `Modules.System.Identity/Web/Util/IdentityServerCookieDefaults.cs`: dedicated Identity Provider cookie.
- `GET /api/system/Auth/csrf`: CSRF bootstrap; `POST /api/system/Auth/interactive-login`: CAPTCHA and anti-forgery verified sign-in; emits only Identity Server cookie, no OAuth token.
- Login rate limit. Shared credential authenticator for legacy Password Grant and interactive Identity session.

### BFF-03

- `Modules.System.Identity/Application/Services/IdentityPrincipalFactory.cs`: builds roles and permissions scoped to **master System tenant** or **owning Tenant**, including nested RolePermission/GroupRole.
- `IdentitySessionValidator.cs`: checks active user and tenant, cookie account context and current security stamp; rehydrates grants to avoid stale cookie permissions.
- `Infrastructure/Persistence/OpenIddictBffClientSeeder.cs`: optional confidential BFF client with exact registered callbacks, PKCE, external client secret; refuse silent changes to existing client registrations. `Identity:Bff:Enabled` remains false by default.
- `Web/Endpoints/Connect.cs`: OpenIddict GET `/api/connect/authorize`; explicit Identity cookie; restrict to enabled confidential BFF client, scoped identity context, validated redirect/scope/PKCE. OpenIddict issues one-time Authorization Code.
- `tests/Shinera.Identity.Tests/IdentitySessionAndScopeTests.cs` and `OpenIddictAuthorizationHttpTests.cs`: revoked user/stamp/tenant, cross-scope role isolation; TestServer Code+PKCE exchange, replay rejection, wrong verifier, unknown client, bad redirect, unknown scope, missing PKCE, account context and POST refusal.
- `prompt=login`, `max_age` currently fail closed rather than loop; GET-only authorization endpoint. See [BFF-03 implementation record](development/BFF-03-IMPLEMENTATION.md) for limitations.

## Outstanding risks / next actions

1. **Next: [BFF-04 Issue #12](https://github.com/sholehamed/foundation-backend/issues/12).** Implement opt-in BFF OIDC client challenge/callback and `ITicketStore` cookie; server-side token exchange only, no OAuth token browser exposure. Use a new feature branch and Draft PR.
2. **Safe interactive login continuation blocker:** Angular `SignInComponent.getRedirectUrl()` currently rejects an `/api/connect/authorize` return path. Provide dedicated validated Identity/OIDC return flow; do not relax open redirect checks.
3. BFF-03's code+PKCE tests use isolated ASP.NET Core TestServer. **Real deployed Identity UI/BFF integration, SQL Server, multi-instance storage, trusted forwarded host, third-party/custom-domain browser paths and full Angular login are not proven.**
4. BFF-04/05 must implement signed OIDC state/nonce verification through platform middleware, session/host binding, 401/403 JSON, CSRF for unsafe methods, logout/revocation, keyring and store lifecycle, refresh coordination.
5. Angular still uses Password Grant and localStorage tokens. Leave it unchanged until BFF end-to-end safety gate is fulfilled.
6. Known legacy repo security/debt: environment/domain couplings, forwarded-header proxy trust, signing credentials, broad NuGet warning suppression, dev-seeded credentials. Review separately; don't confuse BFF unit-test success with production-hardening.
7. Before merging each slice: update this file, [ROADMAP](PROJECT-ROADMAP.md), [architecture](architecture/browser-bff.md), [HTTP contract](reference/browser-auth-contract.md) and module README; link actual CI run and code.

## Resume protocol

1. Read this file, [ROADMAP](PROJECT-ROADMAP.md), [ADR-0001](decisions/ADR-0001-browser-bff.md), and [BFF-03 implementation record](development/BFF-03-IMPLEMENTATION.md).
2. Query actual GitHub `main`, open PRs and current CI — docs are a *snapshot*, not a substitute for live evidence.
3. Work on a new feature branch. Implement a small vertical slice, add tests, update docs in the same PR, and verify CI before merge.
4. Maintain existing browser Password Grant until new BFF consumer has working integration; never expose OAuth tokens to browser as a workaround.
