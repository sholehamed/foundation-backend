# FR-15B — Live HTTPS/SQL/Chromium acceptance inside GitHub Actions

Checkpoint: 2026-11-10. There are **two distinct kinds of Staging testing**.

1. **Disposable Staging-like integration environment on an Actions runner**: `FR-12G-C live HTTPS BFF browser E2E` provisions an isolated SQL Server 2022 service, Kestrel/.NET 10, compiled Angular, an HTTPS reverse proxy with a temporary test certificate, real OpenIddict Code + PKCE, isolated BFF sessions, Owner/staff browser sessions and independent Tenant hostnames. It runs only on trusted Backend `main`, manually or automatically after changes under `tests/browser/**` or the workflow YAML. The test can run without a separately provisioned remote Staging server. It does not reuse any operational SQL data, signing keys or secrets.
2. **Real, separately deployed Staging**: `FR-15A Staging read-only two-Tenant readiness` is manually invoked on `main` with an operator-owned, non-secret `FOUNDATION_STAGING_MANIFEST_JSON`, `mode=probe` and explicit `PROBE_ISOLATED_STAGING` confirmation. It probes an actual trusted HTTPS origin set, but neither deploys those hosts nor conducts an authenticated browser test. This requires real DNS/TLS/ingress/SQL/keyring/session store and an approved deployed build.

## Added acceptance to disposable CI

The real Chromium test now enforces that an authenticated Tenant Owner, despite holding a real host-only BFF cookie, receives HTTP **401 (BFF context rejected) or 403 (SystemAdministrator denied)** from all three centrally defined System Admin Registry diagnostics:

- `GET /api/system/FeatureRegistry/tenant-permissions/preview`
- `GET /api/system/FeatureRegistry/tenant-navigation/readiness`
- `GET /api/system/FeatureRegistry/system-endpoints/integrity`

It also asserts 401/403 for a protected Tenant Members read through the **second** Tenant hostname, in addition to existing cookie-domain isolation. The first complete live run (Actions #42780940636) proved an actual **401** from Tenant-context BFF to a System-only endpoint; the first Playwright assertion had incorrectly required 403 only. This is a test expectation correction, **not** a server authorization change. Both responses deny access; 200 remains a test failure. The same suite already verifies TLS OIDC redirects, PKCE, secure HTTP-only host-only cookies, no JavaScript bearer token, CSRF rejection, role grant/revocation, stale-cookie invalidation and fresh-session 403. The independent real HTTP+SQL suite verifies System SuperAdmin's signed JWT can read central diagnostics while anonymous/Tenant actors cannot.

## How to run and interpret results

- After this test PR has a green Backend BFF solution + SQL suite, merge it. The `tests/browser/**` path change **automatically triggers** the trusted Backend `main` workflow `FR-12G-C live HTTPS BFF browser E2E` and builds both Backend and the current Frontend main. Review its jobs and sanitised logs. This is a **real integration test on an ephemeral Actions runner**, not a remote Staging deployment.
- Or manually open Backend **Actions → FR-12G-C live HTTPS BFF browser E2E → Run workflow** on the trusted `main` branch.
- Only use the FR-15A manual probe against an approved **already deployed** Staging. Never set live Staging URLs in the disposable workflow, or upload initial passwords, cookies, JWTs or test trace files containing one-time credentials.
- An E2E CI result does not demonstrate an approved System Admin browser flow, deployed configuration parity, multi-node persistence, Tenant branch RBAC, entitlement restrictions or production readiness. Those are remaining operational gates.

This change performs no remote deployment, database migration, Registry Apply to any operator database, user grants or production feature-flag activation.

## Verified result — 2026-11-10

- Initial trusted-main browser run [#42780940636](https://github.com/sholehamed/foundation-backend/actions/runs/42780940636) exposed a **test-only** expectation mismatch: the System-only Catalog returned 401 for a Tenant Owner BFF principal rather than the test's 403-only expectation. This was a valid denial, not unauthorized access.
- [PR #109](https://github.com/sholehamed/foundation-backend/pull/109) corrected the Chromium expectation to accept precisely **401 or 403**; a successful (200) central Registry response still fails the test. The API's live role checks and policies were not modified.
- **PASS:** trusted Backend main workflow [#42781976862](https://github.com/sholehamed/foundation-backend/actions/runs/42781976862), real SQL Server / Kestrel / HTTPS reverse proxy / compiled Angular / OIDC + PKCE / Chromium. Logs explicitly confirm **135 real HTTP checks succeeded** (including central System Registry and Tenant denials), and **2/2 real browser E2E tests passed** (Owner/staff BFF permissions/revoke and first-login password rotation).
- **PASS:** matched Backend main [BFF foundation CI #42781977240](https://github.com/sholehamed/foundation-backend/actions/runs/42781977240), including solution, Identity and SQL tests.
- Backend main SHA tested: `21e4496dd4c3fb840757ae0e5debca2b5fac6561` (merge of PR #109). This is **disposable real integration acceptance**, not an externally hosted Staging deployment or a Production readiness certification.

