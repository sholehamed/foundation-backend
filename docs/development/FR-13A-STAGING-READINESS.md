# FR-13A — Staging readiness, operational gates and read-only preflight

**Status (2026-11-09):** Stage-1 implementation: manifest validator, optional anonymous HTTPS probes and offline unit/CI tests. **Actual Staging environment is NOT provisioned; no Production enablement.** Backend tracker [#70](https://github.com/sholehamed/foundation-backend/issues/70).

This is not a substitute for FR-12F real signed-bearer/SQL or FR-12G-C disposable real HTTPS/Chromium/OpenIddict/SQL acceptance.

## Goal and hard boundaries

Create a reviewer-approved non-Production deployment boundary before the FR-13B user journey: System Admin -> Tenant -> Owner -> employee creation -> role+permission -> limited session/menu -> revoke. A passing manifest preflight does **not** prove the deployment actually uses its declared settings; the running configuration, TLS, database, keyring and browser flows need independent verification.

- Keep `Identity:Bff:Enabled`, `Identity:Bff:TenantWorkspaceCookieBridge:Enabled`, Angular `bffTenantPilotEnabled`, and `systemUiAccessPilotEnabled` OFF in normal/Production deployment. Never bypass the explicit Production bridge safeguard.
- No production SQL/catalog apply, migrations, grants or settings changes are performed. No automatic catalog Apply or role/user creation from startup or CI.
- Do not reuse production credentials, SQL databases, user identities, encryption keys, cookies or signing material in Staging.
- BFF cookies must stay Secure/HttpOnly/host-only and JavaScript must not receive OAuth access/refresh tokens. Keep native Bearer flows functional; UI hiding is not server authorization.

## Versioned, NON-SECRET staging manifest

Copy `scripts/staging/staging.example.json` for operator review and fill in actual *approved* Staging hosts. The example domains are illustrative, not deployed endpoints. Use dedicated `staging.*`, `.test` or `.localhost` base domain distinct from the bare production domain, with one different HTTPS subdomain each for System, Identity and Tenant. No origin path/query/fragment/userinfo, no secrets in JSON.

| Manifest property | Real Backend or Angular deployment setting |
|---|---|
| `backend.bff_enabled` | `Identity:Bff:Enabled` |
| `backend.tenant_cookie_bridge_enabled` | `Identity:Bff:TenantWorkspaceCookieBridge:Enabled` |
| `backend.registry_apply_enabled` | `Identity:FeatureRegistry:EnableApply` |
| `frontend.tenant_bff_pilot_enabled` | Angular `bffTenantPilotEnabled` |
| `frontend.system_ui_access_pilot_enabled` | Angular `systemUiAccessPilotEnabled` |
| `tenant_base_domain` | Backend `Tenant:WorkspaceBaseDomain` / Angular `workspaceBaseDomain` |

Two allowed stages:
1. `baseline`: all BFF pilot switches, Registry Apply and System UI pilot explicitly **false**. Pure offline host/config review, not deployed BFF validation.
2. `tenant-bff-pilot`: all three Backend/Angular BFF pilot switches **true**, only after authorized isolated Staging deployment; Registry Apply and System UI pilot still **false**. This validator never changes any feature flags.

From the Backend repo root:

```sh
python3 -m unittest discover -s scripts/staging -p 'test_*.py' -v
python3 scripts/staging/preflight.py --config scripts/staging/staging.example.json
```

For an approved **already deployed** Tenant BFF Staging pilot, optionally run with an operator-owned non-secret manifest:

```sh
python3 scripts/staging/preflight.py --config /secure/non-secret/staging-manifest.json --probe
```

`--probe` performs only anonymous HTTPS **GET** requests, using normal CA verification, no credentials, cookies, redirects, CORS bypass, TLS bypass, migrations or HTTP writes:
- Identity `/.well-known/openid-configuration` must return bounded 200 JSON with the exact expected `issuer`.
- Tenant and System `/bff/session` must return 401 with `Cache-Control: no-store` when unauthenticated.
- Tenant `/api/tenant/workspace` must return 401 when unauthenticated.

Exit 0 means those narrowly defined checks passed; exit 2 means failure. Probes are disallowed in `baseline`. They are **not** an authenticated access, full browser flow, deployment integrity or HA certification.

## Human-controlled rollout checklist — NOT performed by this PR

- [ ] Provision a separate Staging DNS zone / three hosts / trusted TLS reverse proxy; validate actual `X-Forwarded-Proto`/host trust boundary and exact same-origin routing. Never enable wildcard credentialed CORS.
- [ ] Provision isolated SQL Server/service accounts. Record encrypted backup and **restored backup evidence** and signed migration plan. Apply reviewed migrations manually in the authorized Staging environment, not automatically on startup.
- [ ] Provision durable BFF session persistence, securely shared Data Protection keys and test restart/failover. No Production key material. Review token refresh coordination, upstream IdP logout and revocation behavior before multi-node pilot.
- [ ] Run `docs/sql/fr-01-identity-catalog-inventory.sql` READ-ONLY on approved Staging SQL, store sanitized conflict counts, then manually review Feature Registry Preview fingerprint, resolve legacy data collisions and approve scoped grants. Enable Apply only in a separately authorized operator window, then turn it OFF again. **The preflight explicitly rejects Registry Apply enabled**.
- [ ] Deploy matched Backend and Angular Staging builds and three BFF flags; check actual deployed config against the manifest and run `--probe`.
- [ ] Run true browser System -> Tenant -> Owner/Staff E2E; verify cross-tenant access denial, PKCE, CSRF rejection, 401 on stale cookie, 403 on fresh session after permission revoke, audit integrity and logout. [FR-12G-C CI acceptance](https://github.com/sholehamed/foundation-backend/actions/runs/42520612905) was a disposable environment, not this Staging deployment.
- [ ] Demonstrate rollback: disable experimental flags and return to known-good build; record deployment SHAs, approval evidence and sanitized run IDs. Do not enable Production without separate signoff.

## Next implementation: FR-13B Tenant Admin journey

Introduce tenant-scoped member **creation/invitation/activation** with explicit Owner-reviewed authorization, server-derived TenantId, audit and bounded abuse protection. Build on existing Tenant Members read directory and FR-12D member role assignment; do **not** reuse privileged System Admin Users writes in the tenant context. After the reviewed Tenant registry exists, complete current-user menu/feature filtering with live permissions and independent API guards. Track Backend/Frontend CI and merge SHAs under [#70](https://github.com/sholehamed/foundation-backend/issues/70).
