# FR-12A — Reviewed Tenant PAGE/ACTION catalog and role matrix groundwork

Date: 2026-11-08. Built atop FR-11A without automatic production or tenant-grant changes.

## Code-owned catalog
The four FR-11A keys are now backed by stable code-owned UI identities: `tenant.workspace.members`, `tenant.workspace.roles`, `tenant.workspace.groups`, and `tenant.workspace.branches.manage`. All share module `tenant-workspace`, resource `tenant-administration`, with appropriate PAGE/ACTION type, route, permission and `PermissionUiResource` mapping. The ACTION has no page URL or menu entry.

## Operator contract
The existing **SystemAdministrator** FeatureRegistry endpoint group adds:
- `GET /api/FeatureRegistry/tenant-permissions/preview` (exact route prefix depends on configured backendPrefix / EndpointGroupBase). Read-only: returns `{fingerprint,canApply,hasPending,changes[]}`, classifies Create/Present/Conflict; disabled or foreign-owned records are conflicts, not silently overwritten.
- `POST /api/FeatureRegistry/tenant-permissions/apply` body `{fingerprint: "<preview hex>"}`; requires live persisted System SuperAdmin, **separate opt-in** `Identity:TenantPermissions:EnableCatalogApply=true` (default OFF) and unchanged reviewed fingerprint. Uses serializable SQL transaction and SQL Permission.Code uniqueness; rejects any collision or stale plan, returns 409.
- Apply inserts **only** missing Module, Resource, Permission, UiResource and PermissionUiResource metadata. Never grants UserPermission/RolePermission, writes user/role/group/menus, activates disabled records, changes existing titles, or deploys features. It is idempotent and previewable again after apply.
- No background task, startup seed, arbitrary tenant ID, API-based role grant, runtime self-elevation or migration of existing data.

Security gates: SystemAdministrator endpoint policy **plus live System SuperAdmin role + active System user**, fail closed on inactive/missing/foreign owned or duplicate metadata. Apply must be approved by an operator against a reviewed SQL snapshot. Deployment flag should be disabled immediately after an approved sync.

## Read-only role permission matrix
- `GET /api/tenant/workspace/roles/{id:guid}/permissions` requires a live `TENANT.ROLES.READ` decision. Uses a strict `Role.Id + active TenantId` lookup; foreign or System role IDs return 404.
- Returns `{roleId,roleName,permissions:[{code,name,kind,route,registered,granted}]}` for the four catalog entries. `registered` means the global Permission row exists once and is active; `granted` means this Tenant Role has a matching direct RolePermission record. This is **not** the user's effective permission and does not account for UserPermission deny or group inheritance.
- The API is read-only with no-store; it never assigns/revokes permissions. Edit/Grant endpoints need durable tenant grant audit, anti-escalation, and concurrency controls before activation.

## Next slice
Audited, anti-escalation-controlled role grants, additional resource scopes and real browser E2E. No broad tenant CRUD, grant controls or BFF cutover yet. Track in backend issue #44.
