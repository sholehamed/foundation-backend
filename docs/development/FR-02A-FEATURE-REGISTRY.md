# FR-02A — Code-owned Feature Registry: preview and approved additive apply

> Scope: first executable vertical for **System Roles and Permissions**, dated 2026-11-07. Not a global auto-registration engine, not a grant migration, and not a production release certification.

## Why this exists

Before FR-02A, `ApiResourceSyncService` generated unstable keys from URL+HTTP verb, registered one manually selected Resource at a time, and automatically disabled missing `Auto` endpoints. Angular routes, permission definitions, UI resources and menus were not linked. Code-owned definitions and reviewable reconciliation eliminate per-record manual entry for the first pair of features without changing current authorization.

## Code-owned catalog (source of truth)

`Web/Util/FeatureRegistryDefinitions.cs` declares the stable **System** features:
- `system.roles` → `/system/roles`, Resource `system/roles`, view permission `ROLES.READ` plus role CRUD/assignment permissions.
- `system.permissions` → `/system/permissions`, Resource `system/permissions`, view permission `PERMISSIONS.READ` plus permission CRUD permissions.

`Roles.cs` and `Permissions.cs` endpoints have explicit `FeatureEndpointMetadata(Key, ResourceCode, PermissionCode, Title)`. Discovery inspects **actual** EndpointDataSource metadata and declared method, not a derived URL identity; route template is mutable description. Duplicate stable keys, unmapped permission names or competing HTTP keys produce blocking Conflicts. Multi-verb endpoints use `Key.METHOD` disambiguation; existing single-verb keys never change when the route changes.

The Angular companion adds a typed route manifest and a guarded administration page, but does not ship arbitrary DB-supplied component code. Both repositories must be deployed as compatible versions.

## HTTP operations

Both are under **live `SystemAdministrator` authorization**, never exposed as tenant or anonymous APIs:

| HTTP | Relative path | Behavior |
| --- | --- | --- |
| GET | `/api/System/FeatureRegistry/preview` | Read-only `{canApply,hasPending,changes:[{type,key,state,detail}]}`; Cache-Control no-store |
| POST | `/api/System/FeatureRegistry/apply` | Admin-initiated sync; `Identity:FeatureRegistry:EnableApply=true` required, defaults OFF in every environment; 409 if disabled, conflicting or stale constraints |

States: `Create`, `Present`, `UpdateRoute`, `Conflict`. Do not infer that `Present` means Active or Granted. A 409 conflict requires manual DB or code catalog review. Do not programmatically force a conflict away.

## Apply semantics and boundaries

- Code-owned identities: `Module.Code`, scoped `Resource.Code`, `Permission.Code`, `UiResource.Key`, explicit `ApiResource.Key`. On approved apply, create missing modules/resources/permissions/UI pages/active menu category/permission-protected default menu entries/API endpoints and `PermissionUiResource` / `PermissionApiResource` metadata associations.
- **Never** write RolePermission, UserPermission, UserRole, GroupRole or any tenant grant. New menu entries point to `ROLES.READ` / `PERMISSIONS.READ`, so the current-user Sidebar remains empty for ungranted users. An administrator must explicitly assign reviewed grants for visibility. API checks are independently authoritative; the metadata links do not enable automatic enforcement.
- Preserve existing titles, menu order, enabled/hidden states, source and manually configured assignments. A known existing `Permission.Code` with `ResourceId=null` (legacy platform permission catalog) is reused, **not attached silently to a resource**. This is why some historical platform permissions can still be absent from the Module→Resource role permission tree.
- A disabled/deprecated code-owned ApiResource stays disabled/deprecated. An approved Apply may update its RouteTemplate by stable key only when `Source=Auto` and method/resource owner agree. `Manual` key collisions block apply. Do not delete or disable endpoints missing from a discovery run.
- The old resource-scoped `PATCH /api/System/ApiResources/{resourceId}/sync` remains supported for legacy clients but no longer automatically deprecates/reactivates unseen/disabled records. It still generates **route-derived** keys and is not the recommended feature registry workflow.
- One SaveChanges batch adds definitions/links atomically in EF providers with transactional writes. Duplicate keys and concurrent modifications are protected by existing unique DB indexes or return conflict for review, not by an automatic migration or silent retry.
- The `Menu` schema lacks stable Menu.Key/Scope: this first slice conservatively identifies default navigation by exact Route. Existing duplicate routes or incompatible existing PermissionId are reported as Conflicts; no auto-repair. The new system route namespace restriction in `Menus/me` remains unchanged.
- The platform Code normalization uniqueness migration may be deferred in Production. Check it and run the existing **read-only** `docs/sql/fr-01-identity-catalog-inventory.sql` before production apply; don't assume CI in-memory tests represent deployed data.

## Runbook

1. Review the code definition, Angular manifest, current permissions and real Identity database. Take appropriate backup/snapshot and record target DB/app version.
2. Confirm existing SystemAdministrator is permitted to use the administrative endpoints. Call `GET /api/System/FeatureRegistry/preview` with a valid System administrator session. Export/report only sanitized counts and conflicts.
3. If `canApply=false`, stop. Resolve duplicate keys/owner conflicts through a reviewed migration or manual admin operation. **Do not give broad permissions to make preview green**.
4. Enable `Identity:FeatureRegistry:EnableApply=true` **temporarily** for the reviewed deployment, and invoke `POST /api/System/FeatureRegistry/apply` once as SystemAdministrator. Then set the flag OFF.
5. Re-run preview. Expect `hasPending=false` if no new application definitions; review any operator-disabled records without reactivating. Verify the menu stays permission-filtered, grant only the required `*.READ` permissions through the existing admin UI and validate the correct account scope.
6. Rollback: restore the application version/flag; **do not blindly delete definitions/links that may be used by grants**. A data rollback requires a reviewed migration with FK/grant inventory and audit trail.

## Tests / what's next

Backend tests cover manifest uniqueness, no-write preview, idempotent apply, disabled legacy Permission preservation, no-grant guarantee, stable API key across route changes, inactive API flag retention, manual-key collisions and bad metadata. GitHub CI verifies build and BFF/Identity/SQL suites. The first Angular route manifest/preview UI lives in its own PR.

**Still missing:** a released cross-repo manifest exchange protocol/CI version pin; automatic source-level discovery of arbitrary Angular pages; stable Menu.Key + explicit System/Tenant Scope and route unique constraints; production SQL E2E; explicit named permission enforcement on all endpoints; TenantPermissionRestriction and Branch/Own/Child behavior; browser BFF cutover.

This is a deliberate small **real** registration vertical rather than pretending the full catalog is finished.
