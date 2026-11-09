> **SUPERSEDED — 2026-11-10.** The user confirmed that only **System Admin defines menus, routes and permission definitions**. This earlier Tenant-owned menu design is retired and **must not be deployed**. See [the authoritative decision](FR-14C-CENTRAL-MENU-AUTHORITY.md). Historical content below is retained solely to explain previous commits.

# FR-14C-2B — Tenant-owned menu authoring (Owner-only)

Checkpoint: 2026-11-09. This release creates a restricted editor API and audit. It does NOT enable delegation, branch RBAC or any Staging/Production deployment.

## API

Authenticated HTTPS routes under /api/tenant/workspace/navigation/catalog:

| Route | Request | Permission |
|---|---|---|
| GET / | no body | TENANT.NAVIGATION.READ (PAGE) |
| POST /categories | title, sortOrder | TENANT.NAVIGATION.MANAGE (ACTION) + live Owner |
| PUT /categories/{id} | title, sortOrder, isActive, expectedVersion | same |
| POST /items | categoryId, parentId, title, icon, route, sortOrder | same |
| PUT /items/{id} | parentId, title, icon, route, sortOrder, isActive, expectedVersion | same |

All use independently default-OFF Identity:TenantNavigation:OwnerEditorEnabled. Unknown body fields, TenantId, UserId, RoleId, grants and password updates are rejected. No hard deletion route. Get returns narrow metadata and Base64 RowVersion values, including canManage. The new PAGE/ACTION definitions enter the existing Tenant Permission Catalog Preview/Apply and create NO grants on their own.

## Security and storage

Every write executes inside SQL SERIALIZABLE with live actor subject, Tenant slug, home/read/write scope, active Tenant/User (including lock state), active registered permission, direct-deny evaluation and local Owner role rechecked. A MANAGE grant without live local Owner does NOT allow delegation in this slice. Route values are limited to the six code-owned dashboard roots; routes requiring PAGE permission are re-evaluated live. Parent changes validate category/active ancestors and reject cycles/depth over 32; immutable TenantId/CategoryId and composite SQL FKs prevent reassignment. Category and item caps bound tree growth.

Updates use conditional SQL Server RowVersion CAS; on stale versions HTTP 409, invalid body 400, unauthorized/foreign 403. Sort changes and soft-disable are updates, not deletes. The existing global Menu and MenuCategory tables and System routes are untouched.

New migration 20261109090000_tenantnavigationaudit adds an append-only TenantNavigationAuditEntries ledger: Tenant, actor, entity, action, UTC timestamp and previous/current versions (no personal data or grants). A trigger prohibits UPDATE/DELETE and rollback refuses to drop non-empty audit. SQL mutation and audit commit atomically, including failure rollback.

## Acceptance and rollout gates

The new Owner-only SQL integration tests must pass: create/update/reorder/disable, CAS conflicts, cross-Tenant denial, role grant without Owner rejection, direct Deny, audit failure rollback and immutable history; unit contract and native/BFF Tenant HTTP regression must remain green. Frontend pilot also requires build, unit and Chromium.

Operational order:
1. Explicitly review and back up databases, then apply both pending migrations 20261109080000 (owned catalog) and 20261109090000 (audit) only in an approved isolated environment.
2. Run Tenant Permission Catalog Preview and explicitly approve Apply of READ/MANAGE definitions; inspect grants and ownership (Owner fallback is not an implicit missing-permission grant).
3. Enable OwnerEditor in a controlled HTTPS test environment, verify cross-Tenant/CSRF/BFF/session behavior, and populate each Tenant's owned menu before cutting over display.
4. Verify Angular editor and browser, then enable OwnedCatalogEnabled and tenantDynamicNavigationPilotEnabled only after route parity and non-empty tree checks.
5. Finish reviewed legacy global Menu import, non-Owner delegation controls, and FR-14C-3 BranchMembership / scoped role boundaries separately.

No Staging/Production deployment, migration, grant Apply, feature flag activation, or actual tenant data change is authorized by this PR. CI success is necessary but not sufficient for operational release.
