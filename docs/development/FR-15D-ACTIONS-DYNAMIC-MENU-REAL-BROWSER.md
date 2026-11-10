# FR-15D — Enable and exercise the dynamic Tenant menu in Actions-only full-stack acceptance

Date: 2026-11-10. This is a deliberately scoped, **real Angular + Kestrel/BFF + OpenIddict + SQL Server + Chromium** dynamic-navigation pilot. It does **not** enable the feature in any deployed environment.

## Why this test is needed

The existing FR-15C Actions launcher successfully created a temporary SQL/Angular/OIDC environment, but the frontend test build kept `tenantDynamicNavigationPilotEnabled: false`, and its CI SQL database did not contain System Admin-owned Tenant menu rows. Accordingly the existing two-Tenant BFF browser E2E could not prove the real dynamic Tenant sidebar. This phase explicitly exercises that previously untested path on the temporary Actions runner.

## Runner-only activation

- Existing trusted Backend `main` workflow `FR-12G-C live HTTPS BFF browser E2E` invokes `tests/browser/run_live_https_bff.sh`.
- The shell runner refuses execution unless GitHub Actions reports exactly this repository, `refs/heads/main`, `Development`, `DBNAME=Shinera_E2E_RealBffBrowser` and the reviewed synthetic E2E DB guard.
- Only inside this ephemeral checkout it changes the **test-specific** Angular environment `environment.bff-pilot.e2e.ts` from `tenantDynamicNavigationPilotEnabled: false` to `true`, then recompiles Angular. The file change is **not committed**. The runtime uses the new built browser output. All committed frontend/default/production flags stay OFF.
- After the existing signed HTTP Tenant IAM suite registers 17 central Tenant Permissions in its disposable SQL database, the CI-only standalone SqlMutator command `seed-central-tenant-navigation` creates **exactly six** centrally owned Tenant Menu roots with the approved PAGE PermissionIds, an active global MenuCategory, and no Tenant-authored Menu. The command additionally rejects any invocation without an explicit `FR15D_E2E_DYNAMIC_NAVIGATION=true` plus trusted Actions main and the exact disposable database. Existing Menu keys cause a fail-closed exception. It verifies `CentralTenantNavigationReadiness.ReadAsync` and ensures **no RolePermission/UserPermission count changes**. This command does not exist in any production API.
- Actual Chromium browser test checks returned live `GET /api/tenant/workspace/navigation/me`, Angular nav DOM, six-route allowlist behavior, a staff `TENANT.MEMBERS.READ` grant making its Members menu visible, no unauthorized Roles/Groups link, immediate stale BFF cookie rejection on revocation, and after fresh staff login, **no Members menu link** and a direct members API 403. The System-only consolidated central-access report must remain 401/403 to Tenant BFF principals.

## Test separation

The dynamic menu pilot is enabled for the disposable **FR-12G-C** browser runtime only, including runs launched via **FR-15C Launch Ephemeral Staging**. It is *not* silently enabled in `.github/workflows/tenant-iam-http-e2e.yml`, and these are not production/staging hosts. No public URL, persistent SQL or remotely deployed service is provisioned.

This can demonstrate actual UI behavior and server-enforced revocation, but cannot certify a remote staging database, production Menu/Permission ownership data, Branch RBAC or capacity. Existing FR-14C four-way central consistency and FR-15A approved external Staging checks remain separate release gates. Do not change the default-OFF production dynamic navigation flag based on this disposable result alone.

## Acceptance evidence

1. Backend BFF solution/unit/SQL and offline scoped-activation guard green on PR head.
2. Merge to trusted main only after checks pass, triggering existing FR-12G-C workflow by changing files under `tests/browser/**`.
3. Require real signed HTTP/SQL suite and 2/2 Chromium tests green. Browser logs should show the `FR15D` Angular real-navigation/grant/revocation assertions (do not upload browser traces containing one-time credentials).
4. Record exact main SHAs, browser workflow run ID, number of HTTP assertions and confirmation that committed Angular flags remain OFF.

**No external Staging or global authorization catalog mutation is requested by this phase.**
