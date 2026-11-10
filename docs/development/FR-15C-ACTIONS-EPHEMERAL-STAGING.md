# FR-15C — Bring up disposable full-stack Staging-like environment from GitHub Actions

Date: 2026-11-10. Scope: **GitHub-hosted temporary integration**, never a durable remote Staging deployment.

## Available workflows

| Workflow | Action |
| --- | --- |
| `FR-15C Launch Ephemeral Staging` | One-click main-only launcher; uses GitHub's scoped `GITHUB_TOKEN` with `actions:write` exclusively to dispatch the existing trusted, private, main-only browser E2E |
| `FR-12G-C live HTTPS BFF browser E2E` | Actually provisions and runs isolated SQL Server 2022, the ASP.NET Core API and OIDC, Angular built from matching frontend main, temporary local HTTPS reverse proxy, two Tenant hostnames and Playwright Chromium |

The launcher records the unique follow-on run URL in `GITHUB_STEP_SUMMARY`, explicitly distinguishing **dispatch accepted** from **tests passed**. If GitHub does not expose exactly one matching newer manual run (the same Backend SHA), the launcher fails closed instead of attributing unrelated success to itself.

**How to run:** open the [backend Actions tab](https://github.com/sholehamed/foundation-backend/actions), choose **FR-15C Launch Ephemeral Staging**, click **Run workflow** on **main**. The new launcher is automatically executed once when first merged to main because the workflow's own file is in its push path filter. Follow the linked **FR-12G-C** run to its end and require **success** (including the HTTP/SQL and Chromium tests). The runner executes the app locally; there is **no remote app URL**. All infrastructure is destroyed at job completion.

## What actually runs

- Disposable **SQL Server 2022** database, local **.NET 10/Kestrel**, real **OpenIddict OIDC and Code+PKCE**, compiled **Angular**, short-lived local HTTPS proxy with a throwaway CA, System/Tenant BFF context and two isolated Tenant hostnames.
- Real signed **HTTP/SQL** IAM and System Registry diagnostic checks, including forbidden System endpoints for Tenant actors, and two **Chromium** browser E2E tests (BFF permissions/revocation and first-login/rotation).
- The existing runner has its own explicitly synthetic **Development fixture** opt-ins; those are not Staging/Production settings. No production credentials, data, keys, or real Tenant URLs are used. No externally exposed tunnel is opened.
- Existing script cleans up local processes and GitHub tears down the SQL service when the job finishes.

## Security and cost

- The launcher runs only from Backend trusted `main`, never a PR head or untrusted branch. It never needs a deploy SSH key/cloud token. It uses `actions:write` only to trigger the reviewed workflow; secrets are still confined to the existing main-only browser job.
- The original browser workflow requires the configured `FOUNDATION_FRONTEND_READ_TOKEN` to check out the private frontend. If missing/expired, the browser workflow **fails**, rather than falling back to a different frontend.
- Launch runs consume GitHub Actions minutes, including the separate, longer full-stack acceptance run. Do not confuse a successful launch step with a successful acceptance suite. The disposable runner is not a 24/7 server.

## What this does NOT unblock

The **FR-15A remote Staging readiness gate** still requires real, stable HTTPS origins for System, Identity and two Tenants, protected SQL migrations/backup, keyring/session store, remote host deployment and explicit operator consent. There is currently no documented actual deployed remote Staging environment. In particular, the CI-only Tenant Menu fixture does not certify six real System-owned Tenant menu definitions, branch RBAC or entitlement boundaries.

After a real target is provisioned, run the separate main-only FR-15A read-only probe, authenticate as live persisted System SuperAdmin, review the four independent central registry diagnostics and consolidated gate, reconcile missing **System-owned** global Menus only under operator approval, and run two-Tenant deployed browser UAT. The Tenant dynamic navigation feature flag stays **OFF** until those independent checks pass.

## Acceptance of this launcher

1. A main-only Actions run displays a new link to **FR-12G-C live HTTPS BFF browser E2E** in its Job Summary.
2. The linked run completes `success`; look for real API checks and both Chromium results.
3. No DNS, remote deployment, registries on real environments, grants, or global feature flags are changed.
4. If dispatch is forbidden or the run cannot be uniquely identified, launcher fails and reports why; never claims deployed Staging.
