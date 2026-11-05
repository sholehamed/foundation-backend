# ADR-0001 — Full Backend-for-Frontend for browser clients

- **Status:** Accepted (architecture), Implementation in progress
- **Date:** 2026-11-06
- **Owners:** Foundation Backend + Frontend
- **Related:** [Design](../architecture/browser-bff.md) | [HTTP contract](../reference/browser-auth-contract.md)
- **Tracking:** [BFF design #3](https://github.com/sholehamed/foundation-backend/issues/3) | [Implementation #4](https://github.com/sholehamed/foundation-backend/issues/4)

## Context

Foundation serves multi-tenant Angular browser applications and potentially native clients. The current Angular code stores OAuth access and refresh tokens in localStorage and uses the resource-owner password grant. That creates script-accessible tokens and couples browser code to OpenIddict.

The backend is a modular monolith with an existing OpenIddict server and bearer-protected APIs. Library modules should remain distributable when mature; the application Web host is the composition root.

## Decision

1. **Browser uses Full BFF**: Browser does not receive OAuth access, refresh, or ID tokens. The browser receives a host-only HttpOnly Secure session cookie. The BFF owns OAuth, tokens, session and all browser API access.
2. **Identity remains independent**: OpenIddict is the authorization server, while a new optional `Modules.System.Bff` is the confidential OIDC client and session boundary. `Web` composes them; no second independent identity database is introduced.
3. **OIDC code + PKCE**: Confidential client, exact registered callback URLs, state/nonce, short-lived one-time codes, server-held client secret or asymmetric authentication. The Authorization Server uses a separate interactive login cookie; the BFF uses its own cookie.
4. **Same-origin**: Angular assets and BFF endpoints share the browser origin through ASP.NET host or trusted reverse proxy. Browser requests are cookie-authenticated. BFF/host enforces tenant-aware authorization; downstream APIs receive server-to-server bearer tokens only when actually separate.
5. **Session store**: `ITicketStore` with protected tickets and server-side token storage; browser cookie contains only an opaque session reference. A multi-node production deployment must share durable session storage and Data Protection keys; single-node development can use a non-distributed provider.
6. **Schemes are explicit**: BFF cookie applies only to browser-facing endpoints; bearer applies to native/third-party endpoints. Do not globally accept either without route-level policy and do not send HTML redirects from JSON APIs.
7. **CSRF and tenancy**: All cookie-authenticated state-changing endpoints require server-validated anti-forgery and origin checks. Auth cookies are host-only. Session tenant and verified request host must match; a header or URL slug is never sufficient authority.
8. **Revocation**: Server-side session removal invalidates the browser session; logout revokes tokens when present; live security stamp/role/membership must be revalidated at a bounded interval and on high-risk actions. Do not treat stale claims as permanent authority.
9. **Migration is staged**: Existing password-grant consumers continue to work until code-flow and session E2E are proven. Then disable password grant for browser clients and remove JS token storage. Native clients use Authorization Code + PKCE or another appropriate registered flow.
10. **Minimal overhead**: Within the same process, dispatch authorized API requests directly (no HTTP loopback). No mandatory YARP/Redis/microservice for F01; choose distributed session provider by deployment requirements.

## Alternatives considered

- LocalStorage/sessionStorage tokens: simple but script-readable and prone to token exfiltration by XSS.
- Access token in JS memory and refresh cookie: fewer persistent tokens, but JS still handles bearer credentials.
- Browser-managed OAuth + PKCE: standard for pure SPA, but higher token-exposure risk.
- Cookies containing full AuthenticationTicket: encrypted and supported, but poor immediate revocation and potentially excessive cookie payload; explicitly rejected for BFF sessions.

## Consequences

- More server state, deployment/session lifecycle and OIDC integration complexity.
- Strong browser token containment but **not immunity to XSS**: a compromised page can still act as the user. CSP, output encoding and CSRF are mandatory.
- Browser and native authentication protocols coexist, with explicit policies.
- Each tenant host has its own session; optional cross-tenant SSO uses a dedicated trusted identity domain, never shared BFF cookies.

## Non-goals

A generic OAuth framework, dynamic identity provider federation, arbitrary wildcard redirect URLs, browser-usable token endpoint, universal API gateway, or mandatory distributed cache.

## Evidence & status

This is an accepted target architecture, **not a completed BFF implementation**.

Implemented slices:
- **BFF-01 (PR #6, merged):** protected server-side ticket storage through `ITicketStore`, opaque random ticket keys, expiration/removal and an isolated BFF cookie registration primitive. The BFF module is not yet composed into the Web host.
- **BFF-02 (PR #8):** separate host-only Identity Server cookie, reusable credential authenticator shared by legacy token grant and interactive sign-in, CAPTCHA + antiforgery bootstrap/validation, and IP-based login rate limiting. The interactive endpoint creates no OAuth token and is not yet connected to authorization-code issuance.

Not implemented:
- A tested OpenIddict authorization endpoint that authenticates the Identity cookie, validates account/tenant/security state and issues Authorization Codes.
- A registered confidential BFF client, OIDC challenge/callback, server-side token exchange and BFF ticket creation.
- Browser session/CSRF/logout/revocation endpoints, tenant-host binding and Angular migration.

The current Angular frontend still persists OAuth tokens in localStorage and the legacy Password Grant remains enabled for staged migration. See [project state](../PROJECT-STATE.md) and [roadmap](../PROJECT-ROADMAP.md) for the current checkpoint and remaining acceptance gates.

## References

- [RFC 10017 — OAuth 2.0 for Browser-Based Applications](https://www.rfc-editor.org/rfc/rfc10017.html)
- [RFC 9700 — OAuth 2.0 Security Best Current Practice](https://www.rfc-editor.org/rfc/rfc9700.html)
- [ASP.NET Core confidential OIDC web authentication](https://learn.microsoft.com/aspnet/core/security/authentication/configure-oidc-web-authentication)
- [OpenIddict ASP.NET Core integration](https://documentation.openiddict.com/integrations/aspnet-core)
