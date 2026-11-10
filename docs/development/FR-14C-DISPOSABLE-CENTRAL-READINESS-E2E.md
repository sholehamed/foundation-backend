# FR-14C — Disposable real HTTP / SQL central operator acceptance

Checkpoint: 2026-11-10. This extends the **existing** real HTTP Tenant IAM integration suite. It is not an authenticated test of a deployed Staging server, and does not turn flags ON in Production.

## Real components exercised

The `Tenant IAM real HTTP + SQL E2E` workflow provisions disposable SQL Server 2022, builds and starts real ASP.NET Core Kestrel, seeds explicitly isolated Development fixtures, and obtains actual signed OpenIddict JWTs for System Admin, Tenant Owner and staff.

After the reviewed fingerprinted Tenant Permission Catalog Apply (already performed by this disposable suite, and NEVER automatically on a deployed host), a new verification phase calls these three **real SQL-backed** endpoints with the signed System SuperAdmin JWT:

- `GET /api/system/FeatureRegistry/tenant-permissions/preview`: requires no pending/conflicts and a 64-character fingerprint.
- `GET /api/system/FeatureRegistry/tenant-navigation/readiness`: verifies the six canonical System-defined route diagnostics, counts/status/issue shapes and fail-closed ready invariant (does not seed or repair Menu rows).
- `GET /api/system/FeatureRegistry/system-endpoints/integrity`: proves actual Kestrel endpoint discovery, typed issue results, and that claimed ready implies no unknown/untagged System APIs.
- Existing `GET /api/system/FeatureRegistry/preview` is also inspected as an independent System registry gate, not silently applied.

For each privileged diagnostic, anonymous requests must get 401, Tenant Owner and Tenant staff must get 403. The tests never log bearer tokens and never send a TenantId/BranchId or grant-request payload to diagnostic endpoints.

The checks are deliberately compatible with a **not-ready** catalog. Missing System menus and pending System API records are real operator work, not an excuse to auto-create records or claim a green Staging release. An overall green CI result means only real disposable HTTP/SQL regression coverage passed.

## Executing and extending

- PR CI: `Tenant IAM real HTTP + SQL E2E` is triggered by `tests/http/tenant_iam_chain_e2e.py`. The script refuses non-disposable environments (Development, local SQL, E2E name prefix and explicit opt-in are mandatory).
- Main CI: the same script is run from `tenant-bff-live-browser-e2e.yml` ahead of the HTTPS/Chromium BFF tests. Those browser tests prove Tenant Owner, staff, OIDC, session/cookie, CSRF and role revoke. The new phase proves central diagnostics using real signed HTTP. They are complementary, not equivalent to a System Admin browser run.
- For **actual Staging**: provision isolated hosts, SQL, reliable session/keyring, trusted ingress and backups, apply reviewed migrations by an operator, and inspect all three reports through a live System SuperAdmin session. Record sanitized issue codes and checks, execute cross-Tenant/branch access denial, then separately approve any default-OFF navigation/permission flags. See `FR-13A-STAGING-READINESS.md`.
- Never use this workflow against Production, disclose JWTs/passwords in CI artifacts, or replace a read-only diagnosis with hidden automatic Registry Apply.

## Remaining release gates

System Admin browser login/registry UI acceptance on an approved deployed host, explicit central Menu registration and cross-Tenant dynamic sidebar pilot, entitlement and branch-level authorization, production-quality BFF session/keyring failover and formal deployment approval.
