# FR-05A — Nested PAGE and ACTION UI Registry (System)

**Implementation scope (2026-11-07).** Code-owned metadata for the actual System Angular child routes and selected named actions. Does NOT add an authorization middleware, grant any role/user, change SystemAdministrator, migrate the schema, or enable Registry Apply.

## Registered UI entries

The existing 13 root System PAGE resources remain unchanged. `FeatureRegistryDefinitions.UiElements` now additionally declares **41** subordinate elements: **32 PAGE** entries with route templates and **9 ACTION** descriptors with no URL. Each entry specifies `FeatureKey`, stable `Key`, `Title`, optional `Route`, `UiResourceType`, required `Permission.Code`, and `DefaultActive`.

Nested PAGE patterns cover:
- System Admin and User create/edit routes (4 entries, inactive by default);
- Role create/edit/permissions, Group create/edit/members, Module create/edit;
- Tenant create/edit/overview;
- Resource create/edit, and nested resource-scoped Permission, API Resource and UI Resource list/create/edit pages;
- Top-level Permission, API Resource and UI Resource create/edit.

ACTION descriptors name role permissions assignment, group/user/admin role assignment, menu create/update/delete/reorder, and Registry Apply. They are **descriptive metadata**; the Angular templates' button click bindings are not automatically hidden or guarded. Current REST endpoints remain separately protected.

**Important:** legacy System Admin/User create/edit flows have known unsafe or unsupported backend write behavior. Their nested PAGE entries and admin/user role-action entries are **created inactive** by default, and approved Apply never re-enables a preexisting inactive record. They should not be switched on until those workflows are separately corrected and integration-tested. Existing Angular routes are not disabled by this metadata; don't confuse inactive metadata with an actual route guard.

## Persistence and conflicts

- Existing approved `GET /api/System/FeatureRegistry/preview` remains read-only. `POST /api/System/FeatureRegistry/apply` requires a live SystemAdministrator and the independent `Identity:FeatureRegistry:EnableApply=true` flag (OFF by default).
- Each new subordinate UI record is added under its owning `ResourceId` and linked through `PermissionUiResource` to its exact code-owned PermissionId; **no menu row** is created for a nested PAGE or ACTION.
- A resource-scoped URL such as `/system/resources/:resourceId/apiResources/create` belongs to the **api-resources** permission resource, not to the parent **resources** CRUD permission. This avoids mistakenly classifying a write operation as `RESOURCES.READ`.
- Existing UI entries are neither reactivated nor relabeled. Conflicts on wrong `ResourceId`, `Type` or PAGE route description, duplicate key/URL, unknown parent/permission, or unrelated permission links abort the entire planned Apply. The code never removes/mutates unrelated historical permission links.
- All existing root pages, role/user/group grants, Auth/BFF policy gates, stored menu category/menus, API resource assignments and DB migrations are unchanged. Empty grants remain empty. Existing manual data may cause a blocking Preview requiring human review.

## Manifest v2 and Angular compatibility

`GET /api/System/FeatureRegistry/manifest` now returns `schemaVersion: 2`, existing `features` and a new `uiElements` collection. Each UI element exports `{featureKey,key,route,type,permissionCode,defaultActive}`, with enum `type` represented as `"PAGE"` or `"ACTION"`. Angular continues to own executable components and route-only patterns, and its registry page rejects stale schema, duplicate paths, unauthorized keys, missing or extra child pages, and any type/permission/owner mismatch before offering Apply. This is a deployment compatibility check, not client-side API authorization.

The frontend UI currently checks the catalog only on the privileged Registry page. Arbitrary direct browser navigation to a known path is not governed by `PermissionUiResource`; real API endpoints must enforce their own live policies. Adding a real route-to-permission guard requires a separate fail-closed, current-user endpoint and end-to-end tests.

## Acceptance tests and follow-up gates

- Manifest exports 13 root features, 41 subordinate UI records (32 pages/9 actions), unique keys and page routes, and correct owner/Permission.Code.
- Preview zero writes; approved Apply idempotent; no user/role/group grants or additional menu items.
- Disabled-by-default Admin/User forms remain inactive; existing disabled rows and edited titles remain unchanged.
- Conflict when an existing key is wrong resource/type/route or has unrelated permission links, without any write.
- Angular route-only inventory must exactly enumerate real nested routes (including resource-scoped pages and tenant overview), not forms or action URLs invented from a REST endpoint.
- A new disposable SQL Server 2022 integration gate (`tests/Shinera.Identity.SqlServerTests/FeatureRegistrySqlServerTests.cs`) is wired into CI. It uses **real Identity migrations**, read-only Preview, idempotent Apply with 13 root/41 child descriptors, no role/user grants, disabled-row preservation and outer-transaction rollback. It is not a concurrent Apply certification.
- **No production SQL Server Apply has been executed.** Concurrent Apply races, historical duplicate UI keys / missing unique constraints, target DB inventory and a documented production migration plan are still required before enabling production Apply. See [issue #44](https://github.com/sholehamed/foundation-backend/issues/44).

Source files: `Web/Util/FeatureRegistryDefinitions.cs` and `Web/Util/FeatureRegistrySyncService.cs`. Tests: `tests/Shinera.Identity.Tests/FeatureRegistrySyncTests.cs`.
