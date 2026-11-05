# Modules.System.Identity

Identity, OpenIddict Authorization Server, user/role/permission and tenant context.

## Current BFF migration status (BFF-02)

- Interactive Authorization Server cookie: `IdentityServerCookieDefaults` (`__Host-Foundation.Identity`).
- Browser BFF application cookie is owned by `Modules.System.Bff` (`__Host-Foundation.Bff`); it is a separate cookie and is **not issued by this slice**.
- `IUserCredentialAuthenticator` centralizes password verification, lockout handling and principal construction for both the legacy token grant and interactive sign-in.
- `POST /api/system/Auth/interactive-login` validates CAPTCHA and antiforgery, then creates only the Identity Server cookie. It returns no OAuth token.
- `GET /api/system/Auth/csrf` bootstraps the antiforgery cookie and readable `XSRF-TOKEN`; clients send the request token in `X-XSRF-TOKEN`.
- Interactive login is IP rate limited (20 requests per minute per observed remote IP).
- Legacy Password Grant and Refresh Token grant remain enabled for staged migration; do not remove them until BFF and existing clients have passed end-to-end migration checks.

## Security and integration boundary

- Cookie policy is Secure + HttpOnly + host-only (`__Host-` prefix) + Path=/ + SameSite=Lax. HTTPS is required, including local browser testing.
- The Identity cookie is not yet wired into `/api/connect/authorize`; Authorization Code + PKCE issuance, cookie security-stamp/tenant/role revalidation, and the BFF callback are later slices.
- Do not treat the interactive endpoint as a completed BFF login or expose this cookie as the application session.
- Keep System and Tenant credential lookup separate. System role/permission claim scoping against the master system tenant must be explicitly verified before enabling cookie-backed authorization-code issuance.

## Validation

- `dotnet test tests/Shinera.Identity.Tests/Shinera.Identity.Tests.csproj -c Release`
- Current credential tests cover System/Tenant account selection, tenant binding, invalid passwords and five-attempt lockout.


## BFF-03 — implemented / merged (PR #11)

- [PR #11](https://github.com/sholehamed/foundation-backend/pull/11): `IdentityPrincipalFactory`, `IdentitySessionValidator`, `OpenIddictBffClientSeeder`, and `Connect` authorization endpoint.
- New claims are scoped by the master System tenant or the user's owning tenant, including nested role/group/permission assignments.
- Existing Identity Server cookie is revalidated against current user/tenant/security stamp state before a code can be issued.
- Confidential client registration uses exact callback URLs and external secrets; the feature is disabled by default.
- TestServer Authorization Code + PKCE integration tests (including replay and negative client/redirect/context cases) passed in [CI 42130167589](https://github.com/sholehamed/foundation-backend/actions/runs/42130167589). Real Angular/BFF browser migration and BFF cookie callback are **not** implemented yet.
- See [implementation notes](../../../docs/development/BFF-03-IMPLEMENTATION.md).
