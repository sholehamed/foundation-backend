# Foundation Identity UI ↔ BFF login contract v1

**Status: approved architecture and HTTP contract (2026-11-06); not a Production rollout.** Canonical backend contract for the Angular identity UI; mirrored in frontend docs.

## 1. Decision: same Angular, separate security origins

We **reuse the current Foundation Angular repository and SignInComponent**, including its existing routes `/system/auth/login` and `/auth/login`. No Razor login form or second Angular project is necessary. Publish the **same compiled Angular assets** at the Identity-only browser origin, but configure its ingress to serve ONLY required login routes and static assets, NOT app dashboards. This is a deployment decision, not a codebase split.

- **Identity origin** (illustrative `https://identity.shinera.ir`) serves Angular login UI plus same-origin ASP.NET Core OpenIddict and Identity APIs under `/api/*`. The Identity UI collects credentials, CAPTCHA and anti-forgery and obtains the **Identity host-only cookie**. It **does not receive access/refresh tokens**.
- **System application origin** (`https://shinera.ir`) serves Angular dashboard + `/bff/*`, `/signin-oidc` and `/api/*` through the application BFF/API ingress. Owns the **System BFF host-only cookie**.
- **Tenant application origin** (`https://tenant-a.shinera.ir`) serves tenant Angular dashboard plus same relative BFF/API paths; owns a **separate Tenant BFF host-only cookie**.
- Identity and application origins exchange **top-level browser navigations** only for OIDC, not cross-origin credentialed XHR. Do not configure wildcard credentialed CORS, a shared parent cookie domain or an OAuth ClientSecret in Angular.

The Identity-only DNS labels `identity` and `auth` are **never BFF application hosts**, even when mistakenly configured in SystemHosts. Reserved infrastructure slugs may not be interpreted as tenant names. Angular does not initialize TenantContext on these origins. Actual public hostname/DNS and TLS certificates must be approved at deployment.

## 2. Browser sign-in flow (Code + PKCE, not Password Grant)

1. From the **application host**, browser navigates to `GET /bff/auth/login?returnUrl=%2Fdashboard` (Tenant) or `?returnUrl=%2Fsystem%2Fdashboard` (System). The BFF derives login_context and tenant from its **verified Host**, validates the local return URL and an **exact allowlisted** `https://<app-host>/signin-oidc`, then sends an OIDC Code+S256 PKCE Challenge to the configured Identity Authority.
2. Identity's `GET /api/connect/authorize` validates the registered confidential first-party client, redirect URI, code challenge, requested context and existing Identity cookie. If not signed in, it issues a **same-origin relative 302** to Angular `/system/auth/login` or `/auth/login` on the Identity origin. Query carries `login_context`, `tenant` (tenant only), and an encoded **local** `/api/connect/authorize?...` continuation. It never forwards an arbitrary external URL from the browser.
3. Identity-origin Angular form loads CAPTCHA and performs `GET /api/system/Auth/csrf`. It then calls **same-origin** `POST /api/system/Auth/interactive-login` with JSON {username, password, captchaId, captchaCode, loginContext, tenant?} and header `X-XSRF-TOKEN` plus Identity anti-forgery cookie. Success is **204 + Identity Cookie only**; failures include **400** for invalid CSRF/context and **401** for bad CAPTCHA/credentials (also **429** for rate limit). It **does not call OAuth grant_type=password** in this continuation path.
4. Angular validates the **local** continuation (client/Code/S256/state/nonce/redirect shape/context/tenant) and performs **full-page navigation** back to Identity `/api/connect/authorize?...`. The server—not Angular—performs authoritative OpenIddict validation.
5. Identity confirms fresh user/Tenant/session/permissions and redirects the browser to the approved BFF callback with a **one-time authorization code**. The BFF middleware verifies correlation/state/nonce/issuer/PKCE, exchanges code server-to-server, revalidates context and Identity grants, persists protected tokens/ticket in SQL and sets only opaque `__Host-Foundation.Bff` on **that application host**. Then it redirects to the previously validated **local application returnUrl**.
6. Application Angular bootstraps state via `GET /bff/session`, **not** localStorage/JWT. The response contains minimal user/context, never OAuth tokens. A different application host cannot reuse the BFF cookie.

**Login with username/password remains possible; only the OAuth Password Grant goes away after migration.** System and Tenant can share the same Angular form component with different route data and context. MFA, password reset and prompt=login/max_age are separate UI/re-authentication features; do not claim they are complete.

## 3. Frozen HTTP and data contract (implemented now unless labelled future)

| Method / origin | Route | Browser contract |
| --- | --- | --- |
| GET app | `/bff/auth/login?returnUrl=/dashboard` | Anonymous, 302 OIDC challenge; 400 bad returnUrl/unrecognized host/callback; 403 context/tenant override. Relative local return only |
| GET Identity | `/api/connect/authorize` | OIDC validated Code+PKCE, 302 local Angular login when no Identity cookie; 302 allowlisted BFF callback with code after successful login |
| GET Identity | `/api/system/Auth/captcha/new` and `/api/system/Auth/captcha/image?token=...` | CAPTCHA challenge/image |
| GET Identity | `/api/system/Auth/csrf` | 204, readable `XSRF-TOKEN`; protected host-only antiforgery cookie; no-store |
| POST Identity | `/api/system/Auth/interactive-login` | JSON username/password/captchaId/captchaCode/loginContext/tenant; `X-XSRF-TOKEN`; 204 + Identity cookie, NEVER OAuth tokens |
| GET app | `/signin-oidc` | OIDC callback middleware, server token exchange, host-only opaque BFF cookie and local redirect |
| GET app | `/bff/session` | Auth by BFF Cookie (Bearer alone = 401). 200 `{authenticated:true,user:{id,username,displayName},context:{type,tenantId,tenantSlug}}`; 401 missing/expired, 403 host/context mismatch; no-store |
| GET app | `/bff/auth/csrf` | Authenticated cookie; 204 readable XSRF token; no-store |
| POST app | `/bff/auth/logout` | Cookie, same Origin/Fetch Metadata and XSRF; 204 after current server session revoked |
| POST app | `/bff/auth/logout-all` | Same safeguards; validated OIDC `sub` only; SQL revokes all **currently existing** account BFF sessions; **does not revoke upstream SSO** |
| Any app | `/api/*` | **Current** Angular uses legacy bearer. **Future BFF-06** Angular sends same-origin Cookie and BFF forwards server-side authorization; this bridge is NOT yet complete |

The Identity Cookie name is `__Host-Foundation.Identity`; the BFF Cookie is `__Host-Foundation.Bff`. Both are HttpOnly, Secure, Path=/, Domain omitted; do not share Identity Cookie across origins. BFF XSRF token is browser-readable but **not** an OAuth token. On a failed BFF session, Angular should redirect to the **host-local BFF challenge** only once BFF-06 is enabled, rather than silently fallback to Password Grant.

## 4. Deployment and configuration contract

- `Identity:Bff:Enabled=false` remains the default. Enabling requires current SQL atomic session store, validated keys and BFF-05 release gates.
- `Identity:Bff:Authority` must be the issuer's public HTTPS origin with no path/query; `Identity:Bff:ClientId` must match confidential OpenIddict client; `ClientSecret` is a **server-only** secret. Identity `LoginUrls:System` and `LoginUrls:Tenant` are **same-origin relative paths** (defaults as above).
- `Identity:Bff:RedirectUris` must contain **exact absolute HTTPS callback URI(s)** for each approved application host (including tenant workspace hosts via controlled provisioning). NO wildcard callback, unreviewed custom-domain callback or trust in an arbitrary Host header.
- Identity ingress: serve only Angular identity-login routes + assets; proxy `/api/*` to Identity ASP.NET Core; **do not** expose BFF callback or application dashboard on Identity origin. App ingress: Angular app + `/bff/*`, `/signin-oidc` and `/api/*` routed to the app BFF/API. Trusted reverse proxy must enforce Host and forwarded scheme/host using known proxies/networks, with HTTPS end-to-end.
- Both Angular Identity login XHR and Angular app BFF/API XHR are **same-origin**; Development uses a trusted HTTPS local reverse proxy because standalone `http://localhost:4200` + external API cannot issue the Identity anti-forgery cookie safely. Do not “fix” by adding cross-origin credentials.
- Keep client registration redirect URI inventory, TLS, CORS, CSP, no-store responses, access logging without tokens and secret/keyring deployment under operator review.
- Separate the **Identity** 8-hour login cookie/IdP session from the BFF SQL ticket (**8h sliding idle, max 24h absolute**) and from legacy access/refresh tokens. Logout-all currently revokes existing BFF tickets but NOT IdP cookies, upstream refresh families or newly issued sessions.

## 5. Migration boundaries and acceptance

**This completed phase freezes architecture/HTTP contracts and applies host-boundary code/tests. It does NOT activate BFF in Production and does NOT migrate Angular auth.** The existing AuthService legacy Password Grant/TokenStorageService/bearer interceptor stay functional until the BFF-06 feature-switched cutover.

Verified by existing suites: `OpenIddictAuthorizationHttpTests`, `FoundationIdentityBffTwoHostTests`, `BffOidcChallengeHttpTests`, `BffCsrfLogoutHttpTests`, `BffLiveCookieHttpTests` and Angular `bff-login-continuation.spec.ts`. Contract-specific checks now reject reserved issuer/infrastructure hosts in Backend BFF challenge and Angular TenantContext initialization.

**Hard remaining gates:** Production-quality Identity-only ingress and actual HTTPS browser/CAPTCHA route, upstream OpenIddict SSO/refresh revocation, concurrent callback/refresh semantics, encrypted rotated shared DP keyring, SQL HA/failover, proxy trusted-host validation, full Angular `/bff/session`/guard/interceptor migration and final legacy Password Grant deprecation. Track backend [BFF-05 #20](https://github.com/sholehamed/foundation-backend/issues/20), [SQL #30](https://github.com/sholehamed/foundation-backend/issues/30), frontend [BFF-06 #10](https://github.com/sholehamed/foundation-frontend/issues/10). Original baseline F00 PR #2 in each repo remains untouched.
