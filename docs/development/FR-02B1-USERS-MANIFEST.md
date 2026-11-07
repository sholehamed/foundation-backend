# FR-02B-1 — System Users registration and backend-owned feature manifest

> 2026-11-07. Focused, backward-compatible, review-only catalog expansion. Linked to [FR-02B/03B issue #44](https://github.com/sholehamed/foundation-backend/issues/44).

## Motivation and source ownership

A full-stack feature must have **one authoritative definition** of its code-owned identifier and access requirement. The backend `FeatureRegistryDefinitions.Features` is that source for `Feature.Key`, `PageKey`, `Route`, `ReadPermission`, module/resource and APIs. Angular separately owns *executable* routes (it cannot load components from JSON). It fetches the backend manifest at runtime on the privileged catalog review page, validates that its known routes agree, and refuses to issue Apply when the deployments disagree. No Angular copy of the permission code or feature key is required.

This is a runtime compatibility check, **not** a build-time guarantee across two separately deployed repositories and **not** a security boundary for data writes. The backend apply endpoint remains a SystemAdministrator-only, disabled-by-default administrative operation; direct API callers are not checked against the browser's Angular route catalog. A versioned artifact and release-pipeline verification remain open.

## Live HTTP contract

`GET /api/System/FeatureRegistry/manifest` inherits the same live SystemAdministrator policy as Preview/Apply; unauthenticated requests receive 401, unauthorized receive 403. This is DB-independent, read-only, and returns `Cache-Control: no-store`.

Example schema (from code, **not** DB-driven):

```json
{
  "schemaVersion": 1,
  "features": [
    {
      "key": "system.users",
      "moduleCode": "system",
      "resourceCode": "users",
      "route": "/system/users",
      "pageKey": "system.users",
      "readPermission": "USERS.READ"
    }
  ]
}
```

All enabled definitions are returned sorted by key. Manifest consumers must reject unsupported schema versions, unexpected keys/routes, duplicate route or stable keys, unknown Permission.Code shapes, or missing known Angular routes. They must not interpret the presence of a feature as permission for a caller to see a menu or call an API.

## Third vertical — Users

A source-owned `system.users` feature is introduced with `/system/users`, UI page `system.users`, default menu visibility based on `USERS.READ`. The code catalog defines `USERS.READ`, `CREATE`, `UPDATE`, `DELETE`, `CHANGE-STATE`, `CHANGE-LOCK`, `ASSIGN-ROLE`, and `ASSIGN-PERMISSION`. All 13 existing `Users` Minimal API endpoints now carry stable explicit `FeatureEndpointMetadata` under the same Resource.Code, with read/write/revoke methods distinguished by stable API key.

Importantly these metadata values **describe** permission intent. They do **not** replace the existing `RequireAuthorization("SystemAdministrator")` or grant access on their own; current write routes retain their previously reviewed live actor validation. Future granular authorization requires a separate permission grant and policy-gate migration.

## Apply / data safety

- Existing Preview and Apply endpoints and their configuration flag are unchanged. Preview writes zero rows. Apply is allowed only with `Identity:FeatureRegistry:EnableApply=true`, default OFF, and only by SystemAdministrator.
- Approved Apply inserts missing modules/resources/permissions/UI pages/permission metadata links/menu and API resource descriptors. It **never** creates `RolePermission`, `UserPermission`, `UserRole`, `GroupRole` or tenant grants.
- An existing unrelated `USERS.READ` permission associated with a different Resource is a blocking **Conflict**. Resolve only after inspecting real SQL Server data. The code does not automatically change ownership, delete, enable, or relabel an existing permission.
- Platform `Permission.Code` uniqueness migration may be deferred on real DB. SQL Server catalog diff/transaction/race/rollback tests and a production database inventory are pending. `Menu` still lacks immutable Key/Scope; no migration or automatic orphan repair here.
- When the backend manifest advertises a route unknown to the deployed Angular build, the Angular admin UI blocks Apply and shows the mismatch; API Apply still requires the independent server-side security and deployment switch, so operators must enforce matching deployments and reviewed Preview.

## Test and handoff

New Identity tests assert sorted stable manifest, uniqueness, third-feature idempotent Apply with *no grants*, and fail-closed collisions for a preexisting differently owned `USERS.READ`. Angular adds runtime contract validation, route manifest checks and UI suppression on drift or manifest API failure.

Next FR-02B/03B slices: Groups/Modules/Tenants/Resources/Menu/API/UI records, reliable versioned cross-repo build artifact, stable Menu.Key/Scope with real SQL migration and full SQL/browser integration; [issue #44](https://github.com/sholehamed/foundation-backend/issues/44).
