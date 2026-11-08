# FR-12D — Tenant member role assignments

2026-11-08. Continues FR-12C. Existing roles/role permissions are never silently created or assigned.

## API contract
- Catalog adds code-owned `TENANT.MEMBERS.ASSIGN-ROLES` ACTION (nine Tenant keys). This is not automatically seeded or granted. System Admin's reviewed, default-OFF Tenant catalog preview/apply registers the metadata.
- GET `/api/tenant/workspace/members/{id:guid}/roles` returns the local member's `{userId,userName,version,canEdit,roles:[{id,name,isActive,assigned}]}`. It requires live Members.Read + Roles.Read; foreign/missing users return 404. This is a direct UserRole matrix, not the effective access evaluator.
- PUT `/api/tenant/workspace/members/{id:guid}/roles` body `{expectedVersion,roleIds}` replaces only *ordinary local RoleScope.Tenant* direct UserRole associations. Server derives actor and TenantId. New grants require active roles; revocation of a previously assigned inactive ordinary role is allowed. Owner/System/foreign/special legacy roles are never mutable and are never accepted.
- Writes require live active local Owner, Members.Read + Roles.Read + Members.Assign-Roles, writable ActiveTenantId=HomeTenantId, active target Tenant user who is not the actor and not an OWNER role holder. Inactive users are viewable but not editable.
- Role/User ownership, type, active state, protected Owner, duplicates/unknown ids and stale versions are enforced server-side. No bulk arbitrary TenantId or user creation, permission grant or role change.

## Atomicity & audit
- SQL Server only; Serializable transaction, User.RowVersion CAS UPDATE, additions/deletions and one audit row per change commit atomically. No-op does not bump RowVersion or write audit. Unique `(TenantId,UserId,RoleId)` index protects duplicate links.
- Migration `20261108210000_tenantmemberroleaudit` creates `TenantMemberRoleAuditEntries` (tenant, actor, member, role/id/name, grant/revoke, UTC), plus UPDATE/DELETE-denying trigger. SQL audit insert failure rolls back both membership and User RowVersion.
- Error behavior: invalid code/list/version 400, revoked/foreign/Owner/self 403 or foreign member 404 for read, stale version/concurrent insert 409. 409 makes frontend discard drafts and reload.

## Deployment and remaining
Merge does not execute migrations or touch production grants. Reviewed SQL migrations must precede app deployment; operator explicitly previews/applies the ninth catalog key separately (config opt-in disabled by default). The historical Owner fallback is preserved. Full role policy and tenant menu dynamic registry, invited members, group-role UI and branch delegation still pending. See backend Issue #44.