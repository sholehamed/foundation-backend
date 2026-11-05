# Modules.System.Identity

Identity, OpenIddict authorization server, user/role/permission and tenant context.

- Interactive Authorization Server cookie: `IdentityServerCookieDefaults` (`__Host-Foundation.Identity`).
- Browser BFF application cookie is owned by `Modules.System.Bff`; it is not the Identity Server cookie.
- Legacy Password Grant remains enabled only for staged migration and existing clients.
- `POST /api/system/Auth/interactive-login` creates an Identity Server cookie and returns no OAuth token. It requires a valid CAPTCHA and ASP.NET Core antiforgery token.
- `GET /api/system/Auth/csrf` issues the Angular-readable request token cookie; the actual ASP.NET Core antiforgery cookie remains HttpOnly.
- The interactive login endpoint is not yet connected to the OpenIddict authorization endpoint. Full Authorization Code + PKCE is a later slice; do not treat this endpoint as completed BFF authentication.
