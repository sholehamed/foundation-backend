# FR-12G-C — Live HTTPS Chromium + Angular + Identity/OpenIddict + BFF + SQL

2026-11-08. **Acceptance workflow; remains disabled in Production.**

Workflow `.github/workflows/tenant-bff-live-browser-e2e.yml` runs only by a reviewed main push changing the workflow, browser test or source, or by manual workflow_dispatch on main. No PR or fork is given the private Frontend token. Both repos remain private. PAT comes from Backend Actions Secret `FOUNDATION_FRONTEND_READ_TOKEN`, scoped to read-only Frontend Contents; `persist-credentials:false`.

An isolated Ubuntu Actions runner starts SQL Server 2022 and a .NET 10 Kestrel API, compiles the real Angular BFF pilot with `apiUrl=/api`, generates a short-lived local CA and trusted HTTPS server certificate, and serves two real virtual hosts (`identity.shinera.localhost` and `e2e-http-ci.shinera.localhost`) over 8443. A tiny CI-only reverse proxy forwards `/api`, `/bff`, `/signin-oidc`, discovery to Kestrel and serves compiled Angular assets; `X-Forwarded-Proto=https` carries the original browser TLS scheme. These are disposable *.localhost test domains, not public production domains or a hardened ingress.

The API uses real OpenIddict Authorization Code+PKCE, the registered confidential first-party client, interactive Identity password/CAPTCHA/CSRF login, host-only secure HttpOnly BFF cookie, Cookie-backed Tenant Workspace bridge, actual tenant+user+role SQL data, CSRF-protected audited grants and live permission revocation. BFF and bridge are explicitly enabled ONLY in this isolated Development job. No Production config changed.

First, existing real bearer/SQL FR-12F 51-check script applies the reviewed catalog in the disposable database and proves its SQL audit. Next, new Chromium test performs an actual Tenant login redirect through the Identity host, real credential+CAPTCHA form and callback, session validation, cross-tenant cookie isolation, rejects an unsafe request with no CSRF, creates Role, assigns Permission/UserRole, signs in a staff browser, revokes Permission and verifies an existing staff BFF session immediately loses its `TENANT.MEMBERS.READ` authorization. Logout revokes the opaque ticket. There are NO mock HTTP responses in this new test.

This workflow is a gate, not a declaration of success until a trusted main run is confirmed GREEN. Browser traces/screenshots can contain sensitive local disposable authentication context; artifacts are uploaded only on CI failure, access-controlled within the private repository. The runner and CI credentials are disposable and cannot be used against non-loopback database connections.

Out of scope: production BFF enablement, multi-node session store/keyring/revocation hardening, deployment-grade TLS proxy, cross-node logout, rate/load tests and release approval. Legacy bearer and frontend BFF pilot remain off by default.

Master tracker: Backend Issue #44.