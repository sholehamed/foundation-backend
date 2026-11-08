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

## Next slice
GET tenant role-specific permission matrix (owned role, Tenant Roles.Read) will display registered/unregistered definitions; edits are intentionally excluded until a durable tenant grant audit + anti-escalation + concurrent-write contract is implemented and tested on disposable real SQL Server. No broad tenant CRUD, grant controls or BFF cutover yet. Track in backend issue #44.
