# FR-14C — Consolidated System-owned central access read-only gate

Checkpoint: 2026-11-10. **This is a code/data consistency diagnostic**, not a production or Staging authorization, a grant, a System Apply operation or a command to switch Tenant dynamic navigation on.

## Contract

New GET `/api/System/FeatureRegistry/central-access/readiness` (as installed under the app's API prefix) is SystemAdministrator policy-gated **and independently checks a live persisted System SuperAdmin** before reading. `Cache-Control: no-store`. No Tenant Owner or staff user can call it. The endpoint accepts no Tenant/user IDs, URLs, feature flags, SQL repair instructions or Apply token.

Its read-only response aggregates **four existing, separately authorized diagnostics**:

| Check | Considered ready only when |
| --- | --- |
| `systemRegistry` | System Registry Preview is not in Conflict, has **zero pending Create/UpdateRoute** entries, and is nonempty |
| `tenantPermissionCatalog` | The central Tenant Permission Catalog Preview is not in Conflict, has **zero pending Create** entries, and is nonempty |
| `tenantNavigation` | Central Tenant Navigation Readiness reports **exactly six ready canonical paths**, no unresolved route/menu/UI conflicts |
| `systemEndpoints` | Runtime System Endpoint Registry Integrity reports **at least one discovered feature endpoint**, all ready and no uncataloged/unreviewed System API routes |

The top-level `centralCatalogConsistent` is true **only if all four** pass. Each nested `Check` includes `ready`, `conflicts`, `pending`, `observed`; the two Preview checks also return their **non-secret SHA-256 preview fingerprints**. The top-level `blockingChecks` includes one or more of:
- `system_registry_not_consistent`
- `tenant_permission_catalog_not_consistent`
- `tenant_navigation_not_ready`
- `system_endpoints_not_ready`

A missing/empty report cannot be mistaken for success. This is an advisory read-only snapshot: the sequential checks are not a locking/serializable deployment transaction and may become stale when other operators edit the global catalog.

## Security and acceptance

- The new route is a **code-owned System Feature Registry READ endpoint** with the standard `SystemAdministrator` policy and optional strict `FEATURE-REGISTRY.READ` granular guard, rather than a separate authorization exemption. The existing System Endpoint Integrity auditor sees it as a runtime feature endpoint.
- Unit tests exercise each independently failing gate, empty reports and fingerprints; disposable real SQL Server tests confirm deny for a non-System actor, no-store, initially unready catalogs, readiness still false after registered System/Tenant permissions but before Tenant Menu authoring, and **no mutations or grants by diagnostic reads**.
- Existing signed OpenIddict HTTP+SQL E2E tests exercise 401 anonymous / 403 Tenant Owner and staff / 200 System SuperAdmin, compare aggregate results against the four individual reports and ensure missing central Tenant menus do not become ready.
- CI checks the new gate; no migration, central Apply, grant, automatic Menu creation, manual prod flag, or Staging deployment is performed.

## Required human-controlled cutover

**Do not switch on** `tenantDynamicNavigationPilotEnabled` merely because disposable CI succeeds or `centralCatalogConsistent` is true on a local fixture. On a deployed, approved staging environment, an operator should:

1. Confirm trusted DNS/TLS, isolated SQL database, migrations/rollback, matched Backend/Frontend builds, keyring/BFF session store and explicitly approved Staging hostnames.
2. Authenticate as the **live System SuperAdmin** and capture the four original reports plus the new consolidated read-only report. Confirm every check is ready, no `blockingChecks` and the two fingerprints match the individually reviewed previews. Inspect every conflict before any manual remediation.
3. Apply only explicitly approved System-owned catalog changes through independent guarded Apply flows and matching fresh fingerprints. Register any missing **System-owned** global Tenant Menus manually; Tenant users must never define or edit Menu/Page/Route/Permission definitions.
4. Re-run diagnostics and real two-Tenant browser/BFF isolation and revocation tests. Separately evaluate Tenant entitlement/branch scope and intended PAGE/Endpoint parity. Any mismatch **blocks activation**.
5. Approve a distinct default-OFF dynamic navigation pilot for only the reviewed environment, preserve a rollback path, and observe authorization logs. Do **not** enable this pilot in this PR.

This closes the absence of a consolidated **operator-visible** central consistency decision. It does **not** certify remotely deployed Staging, branch-scoped RBAC, subscription entitlements or authorize Production.
