# FR-15A — Two-Tenant Staging readiness gate (read-only)

Checkpoint: 2026-11-10. **Status: operator tooling prepared; actual Staging has NOT been provisioned or tested.** This step is deliberately separate from FR-14C's disposable GitHub Actions HTTP/SQL/Chromium suites. There is no automated deployment, SQL migration, Registry Apply, role grant, feature-flag activation or Production access in this PR.

## Why this phase

The prior FR-13A preflight accepted one Tenant origin and could not distinguish an isolated two-Tenant rollout from a single-host demo. The browser E2E already covers a disposable HTTPS/SQL/Kestrel system, but that does **not** certify an actual remote ingress, DNS, certificates, BFF session storage or deployed SQL.

This PR tightens the non-secret Staging manifest and adds a manually triggered, **main-only** GitHub Action that requires explicit consent and real Staging hosts before it will send anonymous HTTPS requests.

## Operator-owned environment requirements (not created automatically)

1. Choose and control an isolated Staging base domain (for example a delegated `staging.example.org`); ensure System, Identity, Tenant A and Tenant B are distinct subdomains with trusted certificates. Values in `scripts/staging/staging.example.json` are **examples, not evidence of deployed DNS**.
2. Provision isolated SQL Server/service identity, secure backups **and a tested restore**, signing credentials, shared encrypted Data Protection keyring and an approved durable BFF session store. Never reuse Production user data, secrets, keys or certificates.
3. Document the deployed Backend SHA and Frontend SHA and ensure a matching Angular/Identity deployment behind reviewed host and forwarded-header trust rules. BFF cookies must be Secure, HttpOnly and host-only; no production-origin credentialed wildcard CORS.
4. Review EF migration history before any migration, specifically the superseded FR-14C-2A/2B Tenant-owned navigation migrations. Do not automatically drop or rewrite existing deployed/audit data. Complete the System Feature Registry and Tenant Permission Catalog Preview and reconcile the six central Menu routes with operator authorization.
5. Only when the actual isolated Staging deployment has the reviewed BFF feature switches enabled should the six anonymous checks be run. No button here enables these switches.
6. Manual authenticated browser and two-Tenant role grant/revoke, fresh-cookies, wrong-Tenant denial, CSRF, restart/revocation, and SQL audit tests are required **after** this gate. This gate does not certify them.

## Manifest contract and how to configure a live probe

`scripts/staging/staging.example.json` now provides `second_tenant_origin`. For a live Staging pilot, copy the example **outside** the repository, replace example origins with actual HTTPS origins on the approved isolated zone, and set only the reviewed BFF pilot flags:

```json
{
  "environment": "Staging",
  "stage": "tenant-bff-pilot",
  "tenant_base_domain": "staging.example.org",
  "system_origin": "https://system.staging.example.org",
  "identity_origin": "https://identity.staging.example.org",
  "tenant_origin": "https://tenant-a.staging.example.org",
  "second_tenant_origin": "https://tenant-b.staging.example.org",
  "backend": {
    "bff_enabled": true,
    "tenant_cookie_bridge_enabled": true,
    "registry_apply_enabled": false
  },
  "frontend": {
    "tenant_bff_pilot_enabled": true,
    "system_ui_access_pilot_enabled": false
  }
}
```

These are illustrative only. The manifest must contain **no** credentials, tokens, emails, connection strings, client secrets or extra flags. The offline validator enforces an allowlist and rejects unexpected fields, unknown hosts, duplicate origins, production-zone origins and inconsistent pilot flags.

For a local review on an operator-controlled machine (Python standard library only):

```bash
python3 -m unittest discover -s scripts/staging -p 'test_*.py' -v
python3 scripts/staging/preflight.py --config scripts/staging/staging.example.json
python3 scripts/staging/preflight.py --config /secure/non-secret/staging-manifest.json --probe --evidence /secure/non-secret/staging-readiness.json
```

The `--probe` command is **only valid** for stage `tenant-bff-pilot`. It performs six anonymous, non-redirecting HTTPS GET requests using system CA verification and a bounded read:

- exact Identity OIDC discovery issuer (200 JSON);
- anonymous Tenant A, Tenant B and System `/bff/session` must be 401 with `Cache-Control: no-store`;
- anonymous Tenant A and Tenant B `/api/tenant/workspace` must be 401.

No credentials, cookies, bearer tokens, HEAD/POST/PUT/DELETE, TLS bypass, flag changes, migrations, database queries or grant operations are performed.

## Manual GitHub Actions flow

The workflow `.github/workflows/staging-readonly-gate.yml` runs **only** using `workflow_dispatch` on the trusted Backend `main`. It does not run from a PR, from a branch, or on push. Use:

1. Set Repository **Settings → Secrets and variables → Actions → Variables**: `FOUNDATION_STAGING_MANIFEST_JSON` to the approved **non-secret** JSON above, adapted to your real Staging. Do **not** configure a credential here; this variable is not protected as a Secret.
2. Open Backend **Actions → FR-15A Staging read-only two-Tenant readiness → Run workflow** on `main`.
3. Use `mode: offline` to run regression and offline sample validation without network. For a deployed pilot use `mode: probe` and type **`PROBE_ISOLATED_STAGING`** into confirmation.
4. Review CI outcome and the sanitized `fr15a-staging-readonly-evidence` artifact (SHA-256 fingerprint of the approved config, stage, six-probe result, explicit *not certified* flags). Actual hostnames and the original manifest are not archived.
5. Compare the fingerprint against the operator's original JSON and record the approved deployment SHAs, test report/run ID, TLS/backup/keyring evidence and any observed failures outside this repository. A green anonymous gate **does not** demonstrate deployed config parity or authenticated behavior.

`FOUNDATION_STAGING_MANIFEST_JSON` absent, malformed, incompatible, or still `baseline` → live probe fails closed. There is never an automatic fallback to the illustrative example origins.

## Remaining gates / handoff

**Blocked until actual Staging exists:** trusted live TLS/host verification, independent System SuperAdmin signed session, central Registry reconciliation, real Tenant A vs Tenant B browser isolation, BFF logout/revocation and CSRF, SQL audit integrity, multi-node cookie/keyring survival, failover/rollback and explicit pilot sign-off. Production remains OFF. Continue with FR-15B only when environment evidence is available, and track related FR-14C-3 Branch RBAC and FR-14D media tenancy separately.
