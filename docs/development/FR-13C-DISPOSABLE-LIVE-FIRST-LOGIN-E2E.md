# FR-13C — live disposable HTTPS browser acceptance for email-less first login

Date: 2026-11-09. Parent [FR-13 #70](https://github.com/sholehamed/foundation-backend/issues/70).
Workflow: [tenant-bff-live-browser-e2e.yml](../../.github/workflows/tenant-bff-live-browser-e2e.yml).
Browser specs: existing [tenant-bff-real.spec.ts](../../tests/browser/tenant-bff-real.spec.ts) and sensitive [tenant-first-login-real.spec.ts](../../tests/browser/tenant-first-login-real.spec.ts).

## Purpose

Extend the ALREADY EXISTING FR-12G-C live HTTPS/Chromium/OpenIddict/SQL test rather
than replacing its independently proven BFF/permission-revocation scenario.

- An isolated GitHub-hosted Ubuntu runner starts disposable SQL Server 2022, .NET 10,
  Angular, a local CA/TLS proxy and separate Identity/Tenant hostnames.
- Trusted Backend **main** checks out private Frontend **main** using the scoped,
  existing read-only GitHub Actions secret. Never run this secret-bearing job
  on pull requests, forks or unchecked branches.
- The runner sets backend Identity:TenantFirstLogin:Enabled=true. After checkout,
  it patches ONLY the local, never-shipped
  frontend/src/environments/environment.bff-pilot.e2e.ts in memory/on disk to set
  tenantFirstLoginEnabled=true for the disposable build. If the expected literal
  is missing or repeated, this job fails rather than silently altering other flags.
  Frontend repository and all normal/Production builds remain default-OFF.
- The existing FR-12G-C Owner/Staff real Code+PKCE, CSRF, host-only BFF cookie,
  tenant permissions, stale-ticket invalidation, re-login 403 and logout assertions
  continue unchanged.

## New real HTTP + browser scenario

1. A real Tenant Owner signs into the real BFF through the existing Identity Angular UI.
   The browser verifies separate code-owned **MEMBERS.CREATE** and
   **MEMBERS.ISSUE-TEMP-PASSWORD** effective capabilities after the existing
   disposable HTTP/SQL catalog Preview/Apply (12 catalog entries).
2. Owner uses the Tenant Members **Angular form** to atomically create an inactive
   employee with email omitted (real SQL NULL) and issue an individual initial
   password. Verify 200 response, one-time Owner dialog, inactive user, NULL email
   and zero default UserRole links. Close the one-time secret view.
3. The employee starts from the **ordinary Tenant BFF login button**. On the real
   Identity HTTPS host, real CAPTCHA + Identity interactive-login verifies the
   initial password and shows "تغییر اجباری رمز عبور" on the same sign-in
   component. Identity and Tenant BFF session cookies must be absent.
4. A completion request missing XSRF is rejected 400. The existing Angular form
   supplies a strong permanent password through the real antiforgery-protected
   completion; no auth session is created by the change alone.
5. The consumed initial credential cannot start a new challenge (401).
   Employee enters permanent password and fresh CAPTCHA on the **normal** form;
   actual Code+PKCE/OIDC callback issues a Secure, HttpOnly, tenant host-only
   BFF session cookie. SQL user is now active and has NO email/no inherited grants.
6. Before grant, API Members returns 403. Owner writes an audited local Role
   permission and staff UserRole; old staff BFF ticket immediately returns 401.
   After a fresh login, Members API returns 200 and live UI access allows it.
   Owner then revokes the permission: existing staff ticket returns 401, and a
   fresh login retains 200 BFF session but gets 403 API and denied UI permission.

## Confidentiality and boundaries

- No stored test password, email placeholder, SMTP account, Production DB,
  Production DNS, externally issued CA certificate or developer auth bypass.
- The one-time password is ONLY in the authenticated Owner response and in the
  employee's temporary in-memory test variable. It is never printed.
- First-login Playwright spec file sets **trace, video, and screenshot OFF** so a
  failure does not upload browser artifacts containing a one-time secret.
  This separate file is necessary because Playwright worker-scoped use options
  cannot be applied inside test.describe; keeping the existing E2E file separate
  preserves its normal trace-on-failure diagnostics.
- The workflow **uploads only** `tenant-bff-real-*/` failure artifacts. It never
  uploads `tenant-first-login-real-*/` error-context snapshots, even if
  Playwright creates such files locally on the disposable runner.
- The secret used by the employee is read from the **actual Owner one-time
  dialog**; do not parse Playwright response bodies that may disappear when
  Angular refreshes the directory. Member identity/NULL email are verified
  independently with the authenticated directory GET.

  Assertions avoid interpolating plaintext in error messages.
- The prior BFF browser test retains its existing trace-on-failure diagnostics.
- The test uses a disposable CI-only account, SQL database and domain ending
  in ".localhost". It exercises real HTTP/browser flows but **does not**
  provision, inspect or certify any remotely deployed Staging/Production server.
- This does not enable BFF, FirstLogin, Registry Apply, SMTP, SQL migration,
  granular System UI permissions, rate-limits or credentials outside CI.

## Recorded success (2026-11-09)

- **Approved code SHA:** `919f636b97c34b7886bda87964fe523758654b90` (Backend main, after PRs #84, #85 and #86).
- **Successful main-only job:** [FR-12G-C GitHub Actions run #42601232203](https://github.com/sholehamed/foundation-backend/actions/runs/42601232203), `angular-identity-bff-live` success.
- **Independent run log evidence:** disposable SQL initialized; 51 signed real-HTTP Tenant IAM permission/revocation checks passed; Playwright **2 of 2 passed** (original OIDC/Code+PKCE/BFF Cookie/role revoke, 13.6s; new email-less Owner atomic create / in-place mandatory first-password change / fresh BFF authentication / grant/revoke, 13.1s).
- Earlier first-live runs #42598865380 and #42600063128 failed on test harness issues (Playwright worker-scoped `test.use`, then response body lost on UI navigation). These were fixed in PRs #85 and #86, respectively, without weakening the security assertions. PR #86 also prevents sensitive first-login failure snapshots from being uploaded.
- **Boundary:** This proves the disposable GitHub runner integration only. No remotely hosted Staging or Production deployment or acceptance is certified by this run.

## Execution and acceptance

The workflow is main-only by design and supports manual workflow_dispatch.
Push to Backend main changing either this workflow or tests/browser/** triggers
the live job; it must also check out a matching Frontend main.

Required independent evidence: a successful **post-merge** workflow run on the
current Backend main SHA with all Playwright tests passing. Backend/Frontend
generic CI alone is NOT this evidence. If the live run fails, file issue(s) and
repair the underlying code/test, rather than reducing assertions.

For a permanent hosted environment, the separate [FR-13A staging runbook](FR-13A-STAGING-READINESS.md)
is still mandatory: operator-owned DNS/TLS, deploy identity, session persistence,
SQL migration backups/restores, Registry grants and independently recorded
configuration/rollout SHA.
