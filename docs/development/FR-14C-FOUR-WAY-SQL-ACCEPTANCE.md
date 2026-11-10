# FR-14C — Four-way central access positive/negative SQL acceptance

Checkpoint: 2026-11-10.

## Results that the test MUST prove

In `FeatureRegistrySqlServerTests.Central_access_four_way_gate_goes_green_only_for_complete_disposable_SQL_catalog`, a real isolated SQL Server (not EF InMemory) receives fixture-only, explicitly reviewed additive System and Tenant permission definitions. The test constructs a policy-tagged System runtime route to cover the central Endpoint Integrity diagnostic, then asserts:

1. System Registry and Tenant Permission Catalog and runtime API are clean, but the six System-owned Tenant dashboard Menu rows are missing → four-way gate **red**, with `tenant_navigation_not_ready`.
2. Only in disposable SQL fixture, create the six globally owned Tenant Menus with the reviewed PAGE Permission IDs → `centralCatalogConsistent: true`, **zero blockers** and six ready Tenant routes. System and Tenant catalog fingerprints are recorded.
3. Insert an unregistered `/dashboard/unreviewed` route → navigation block.
4. Insert an unexpected Roles-read Permission link to the Members PAGE → Tenant Catalog **and** Tenant Navigation blocks.
5. Disable the single code-owned runtime `SYSTEM.ROLES.CREATE` API row → runtime Endpoint Integrity block.
6. Explicit fixture cleanup restores all four gates to green; System and Tenant fingerprints are unchanged. There are **no UserPermission/RolePermission grants** in the fixture.

All SQL fixture changes are restricted to an ephemeral per-test database. The test invokes read-only `CentralAccessReadiness.ReadAsync`; it **never** calls an HTTP Apply in a deployed environment, never seeds any real Tenant, and does not switch feature flags.

## Actual Staging still needs operator evidence

The manual `FR-15A Staging read-only two-Tenant readiness` workflow is available but its **probe mode needs real independently provisioned** HTTPS origins stored as non-secret repo variable `FOUNDATION_STAGING_MANIFEST_JSON`. The `offline` mode merely validates an example. No real Staging DNS/TLS/session/keyring/SQL deployment or operator System Admin credentials have been evidenced by Actions.

Once an approved Staging exists, the operator must use the **real System Admin Feature Registry UI** to review `GET /api/system/FeatureRegistry/central-access/readiness` along with the four original diagnostics; approve any separate central reconciliation against a backed-up database; re-run the read-only reports and real two-Tenant browser/BFF tests; then independently authorize a default-OFF Tenant dynamic menu pilot. This SQL test result is not that external Staging certification.

If **any** gate is red, stop. Never auto-create Tenant-owned menus. Global Menu/Page/Route/Resource/Permission definitions remain System Admin-owned; Tenant owners only assign allowed centrally defined permissions.
