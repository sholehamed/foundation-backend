# FR-04A — Current-user navigation contract

> Status: Backend implementation / PR under CI, 2026-11-07. System and Tenant navigation are a **UX projection only** and do not replace live API permissions.

## HTTP contract

`GET /api/System/Menus/me` (assuming configured `BackendPrefix=api/`).

- Authorization: required, including for Tenant users. Registered in a separate route group from the SystemAdministrator-only Menu administration endpoints. Do **not** add `SystemAdministrator` to this GET.
- No query parameters, RoleId, tenantId or impersonation inputs are accepted.
- 200: `[{ "title": "Administration", "items": [{ "title": "Users", "icon": "group", "route": "/system/users", "externalUrl": null, "childs": [] }] }]`. Empty arrays are valid when catalog/grants are not configured.
- 401: unauthenticated by ASP.NET authorization middleware. 403: authenticated but invalid System/Tenant account/scope, inactive account/tenant, or forged tenant context.
- `Cache-Control: no-store`. Never share this payload across users or tenants.

## Data ownership and security

1. Derive user ID from validated `sub` and account type from authenticated principal. For System require **no** tenant claims, active global System account, and active Master Tenant grant-storage scope. For Tenant require a valid tenant_id and slug matching an active owning tenant, active Tenant account and same scope.
2. Compute all allowed **PermissionIds** from scoped active direct UserPermissions, all active user roles and active group→roles; remove explicit direct denies **after** union. Bounded group/direct/role queries instead of one query per menu. Do not evaluate token `permission` / `permission_code` snapshots.
3. Filter menus by active category, active/nonhidden rows, and allowed PermissionId (null means no additional permission required **inside an authenticated account scope**).
4. Enforce ancestry strictly: hidden, disabled, permission-denied or broken ancestors block descendants. Orphans and cyclic records are not promoted to root. Build a depth-limited (32) recursive tree; no infinite loops. Containers without a route/external URL require a child.
5. Only site-absolute router paths are accepted; reject query/fragment, `//`, backslash and `..` in menu route values. External links must be HTTPS without embedded credentials. For now, System routes must start `/system/` excluding `/system/auth/`, and Tenant routes must not start `/system/` or auth/API infrastructure. **This is a temporary boundary until Menu gets an explicit scope/feature key and Angular manifest validation.**
6. This endpoint **does not enforce access to Angular components**. Frontend route guards are UX; backend APIs must independently enforce live DB authorization. Do not use a visible link as evidence of permission to execute an endpoint.
7. Existing `GET /api/System/Menus/getUserMenus?roleId=` remains legacy **SystemAdministrator-only** and is not used by new Sidebar. No silent removal or change in permissions.

## Operational/deployment notes

- The startup seeder intentionally does not populate historical Menu/Resource lists in Production. An empty response may mean the database has no configured menu categories/menus or permission mappings. This is not an error. Do not fill it with hardcoded privileged links.
- Register active `MenuCategory` and `Menu` records via the existing System administrative UI, use registered Angular routes, associate appropriate PermissionId, then explicitly grant that PermissionId via permitted Role/User/group assignment. No auto grants and no production seed in this change.
- Current schema has no Menu.Scope. Route prefixes are a conservative interim guard, not a complete type-safe feature registry or arbitrary-URL routing. Support for TenantPermissionRestriction and future branch/child scope requires a separate reviewed policy.
- Existing System administration routes are still protected by their own API authorization; hiding a menu item does **not** lock down the Angular URL. A dedicated route-to-permission Angular manifest/guard is future work.
- The new endpoint relies on correct tenancy filter wiring and its own explicit scope checks. Do not turn on disabled URL-based `PermissionMiddleware`.
- BFF opt-in settings and legacy frontend token storage are unchanged. Do not infer this work completes BFF-06.

## Acceptance

- Backend tests: multi-role/group union, explicit deny precedence, System/Tenant isolation, inactive principals/category and malformed tree inputs, unsafe external URLs, empty-catalog behavior.
- Frontend tests: requested endpoint is `/system/Menus/me` without RoleId, empty and error states show no fallback, recursive submenu links, System branding click target.
- CI: full solution + Identity/BFF/SQL tests, Angular tests/build. Real target SQL Server catalog inventory and permission-data backfill have not run; no production release certification is claimed.
