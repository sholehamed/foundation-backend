# FR-02B-2 — Full System root-page Feature Registry coverage

> 2026-11-07. Backend code-owned catalog and descriptive endpoint metadata; companion Angular routes PR required. **No tenant/user/role grant, enforcement flag, BFF mode, SQL migration or production Apply changes.**

## Coverage: all 13 current top-level Angular System pages

Registry now declares the exact currently implemented, executable Angular System root pages:

| Page route | Code-owned resource | Read permission |
| --- | --- | --- |
| `/system/dashboard` | `dashboard` | `DASHBOARD.READ` |
| `/system/admins` | `admins` | `ADMINS.READ` |
| `/system/users` | `users` | `USERS.READ` |
| `/system/roles` | `roles` | `ROLES.READ` |
| `/system/groups` | `groups` | `GROUPS.READ` |
| `/system/tenants` | `tenants` | `TENANTS.READ` |
| `/system/modules` | `modules` | `MODULES.READ` |
| `/system/permissions` | `permissions` | `PERMISSIONS.READ` |
| `/system/resources` | `resources` | `RESOURCES.READ` |
| `/system/api-resources` | `api-resources` | `API-RESOURCES.READ` |
| `/system/ui-resources` | `ui-resources` | `UI-RESOURCES.READ` |
| `/system/menu-management` | `menu-management` | `MENU-MANAGEMENT.READ` |
| `/system/feature-registry` | `feature-registry` | `FEATURE-REGISTRY.READ` |

The `GET /api/System/FeatureRegistry/manifest` schema remains v1; it now exports **13 rows**, including those with no current API operations other than reads. Stable PageKey is `system.<resource-code>`. Angular's route-only index is expanded to match.

## API endpoint registration and intentional exceptions

- **82 new explicit `FeatureEndpointMetadata` assignments** span Admins (13), Groups (13), Modules (7), Tenants (6), Resources (8), ApiResources (9), UiResources (8), MenuCategories (7), Menus (7), Dashboard (1) and FeatureRegistry (3). Including previous Roles/Permissions/Users mappings yields broad **System administration endpoint description**, not authorization enforcement.
- `Menus` and `MenuCategories` share the `menu-management` resource with distinct stable API keys such as `SYSTEM.MENU-MANAGEMENT.MENU-REORDER` and `SYSTEM.MENU-MANAGEMENT.CATEGORY-CREATE`. `ApiResources` legacy sync has its own `API-RESOURCES.SYNC` descriptive permission.
- `GET /api/System/Menus/me` is **authenticated current-user System/Tenant navigation**, so it is deliberately NOT assigned a System-only admin feature permission; its scope is separately protected and tested.
- `GET /api/System/Tenants/resolve` has an `AllowAnonymous` override; it is deliberately NOT classified as a protected Tenant administration operation.
- Auth/Connect/PublicSignup/TenantWorkspace and background BFF routes are outside the protected System root-page catalog and require separate explicit classification. None are silently reclassified or restricted by this PR.
- Existing `RequireAuthorization("SystemAdministrator")` and any existing granular enforcement opt-in and live write services remain unchanged. A `WithFeatureEndpoint` metadata annotation is descriptive; it does NOT give or remove access at runtime.

## Preview/Apply safety and existing-data limitations

- **Preview** on `GET /api/System/FeatureRegistry/preview` remains 100% read-only. **Apply** on POST requires live SystemAdministrator and `Identity:FeatureRegistry:EnableApply=true` (OFF by default).
- Sync adds missing Module/Resource/Permission/UiResource/ApiResource/link records and permission-bound default menus. No user/role/group grants, no deletion, no automatic reactivation, no identity/tenant scoping changes. Existing human-edited menus and manual resource ownership cannot be silently overwritten.
- For installations where the old menu rows have `PermissionId=null` or where a PlatformPermission.Code belongs to another Resource, this expansion can produce more **Conflicts**. This is intentional: do not delete/reparent records merely to make Preview green; review the existing [read-only inventory](../sql/fr-01-identity-catalog-inventory.sql) against the target SQL Server database and plan migration/rollback before enabling Apply.
- A prior URL-derived `ApiResource.Key` with the same `ResourceId + HttpMethod + RouteTemplate` as a new stable code-owned key now appears as a **blocking Preview Conflict**, rather than failing late on the existing unique SQL index. Review/rename legacy records through a separately audited migration; this Apply will not silently steal ownership or duplicate records.
- **Admins is not declared production-ready by registration**: its existing create/update screen and backend user contracts have known issues; descriptions and menu metadata do not repair those workflows. Review it before granting production access.
- No new Menu.Key or Scope columns and no full SQL Server FeatureRegistry reconciliation/race tests in this slice; System path prefix restriction remains temporary until a reviewed schema migration.
- Tenant features, branch-level assignments and TenantPermissionRestriction are not part of this System-only rollout.

## Verification

- Run full .NET solution build, Identity/BFF + SQL Server BFF CI.
- Test all 13 declared page routes, unique stable identities and Permission.Code names; no-write preview, idempotent Apply, permission-bound menus and no role/user/group grants; refuse incompatible existing menus and permission ownership.
- Paired Angular tests must confirm **every current top-level System route** has a route-only manifest entry and that Manifest v1 from Backend compares exactly with executable Angular routes. If Backend/Angular deployment versions differ, the admin UI must block Apply.
- This is **not** cross-repo build-time version certification or a client-side permission guard. API access is determined by existing server policies.

Next development gates are listed in [#44](https://github.com/sholehamed/foundation-backend/issues/44): real SQL Server Feature Registry reconciliation, version-pinned cross-repo CI and stable Menu.Key/Scope.
