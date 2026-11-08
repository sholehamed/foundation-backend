# FR-12E — Tenant grant write-boundary audit

2026-11-08. This hardens the existing FR-12B/C/D pathways; it does not add new user-visible routes or relax any permission.

## Inventory of authenticated and public grant writers

| Writer | Entrypoint | Scope | Intended controls |
|---|---|---|---|
| Tenant RolePermission | PUT `/api/tenant/workspace/roles/{id}/permissions` | Current local Tenant role, excluding Owner / actor-held | FR-12B: live Owner, roles rights, SQL RowVersion CAS, per-change tenant grant audit |
| Tenant UserRole | PUT `/api/tenant/workspace/members/{id}/roles` | Current local Tenant user, excluding Owner/self | FR-12D: registered ACTION, live Owner, active target, SQL RowVersion CAS, per-change member-role audit |
| Tenant Role lifecycle | POST/PUT/PATCH `/api/tenant/workspace/roles` | Current local Tenant role, Owner protected | FR-12C: action rights, SQL RowVersion CAS, append-only lifecycle audit |
| Public signup | POST `/api/public/signup/checkout` | Newly generated Tenant + Owner user + Owner role | Anonymous captcha/rate-limit, server-generated IDs, same atomic tenant+user+role+UserRole transaction |
| Platform UserRole | PUT/DELETE `/api/system/Users/{id}/roles/{roleId}` | Fixed Master Tenant; System account targets | SystemAdministrator policy plus live SuperAdmin gateway, exact mutation intent, serializable audit |
| Platform RolePermission | PUT `/api/system/Roles/{roleId}/permissions` | Fixed Master Tenant | SystemAdministrator + PlatformRoleWriteExecutor, mutation intent + audit |
| Platform UserPermission/GroupRole/UserGroup | `/api/system/Users` and `/api/system/Groups` | Fixed Master Tenant | SystemAdministrator + target-scoped live gateway and audit services |

### Gap closed
Previously the SaveChanges tenant interceptor checked only whether the user could write to the chosen tenant; it did not require a distinct grant-management authority. A generic handler with an ambient writable TenantId could create or remove `UserRole`, `RolePermission`, `UserPermission`, `GroupRole`, `UserGroup` links without going through the audited IAM services.

`TenantSaveChangesInterceptor` now rejects ordinary Tenant `UserRole`/`RolePermission` Added/Deleted unless a narrowly target-and-related-ID bounded `TrustedTenantWriteScope.BeginTenantGrantMutation` is active; Modified links are always rejected. Existing FR-12B and FR-12D *live-authorized SQL transactional writers* open and dispose that intent only around their actual SaveChanges. It is a defense-in-depth storage guard, **not** a substitute for the transaction's live Owner/permission/target checks and Audit. An intent alone does not prove audit completion; only those API writers complete audited transactions.

Tenant `UserPermission`, `GroupRole`, and `UserGroup` mutations fail closed until independent reviewed, audited Tenant endpoints exist. Existing Platform writes still use the separately checked Master+exact pair intent; the new Tenant intent does not authorize Master grants. The existing infrastructure startup Bootstrap remains privileged and must never be offered as an HTTP request action.

Anonymous public signup creates the initial Owner link only if a new Tenant, a new Tenant User **whose ID matches the current provisioning actor**, and a new active-scope Tenant Owner Role all participate in the same SaveChanges. It cannot be reused to add Owner to existing users or existing tenants. This exception does not apply to RolePermissions.

## Tests and deployment
- Guard tests cover Sync and Async SaveChanges, cross-target intent, post-disposal, system-context refusal, nested scope refusal, missing intent for five grant types, and fresh Owner provisioning.
- Real SQL FR-12B and FR-12D tests still cover CAS, live permission effectiveness, audit rollback and identity/tenant checks. Additional tests should run the complete audited writers with the configured interceptor on SQL Server before a real deployment.
- No migrations, grants, role assignments, operational permission registration, login flows, or front-end routes changed here. No production deployment or browser E2E was performed.

## Residual work
Role and User entities are **not** `IMustHaveTenant`; their mutations still rely on authenticated endpoint/handler authorization and verified scoped services. Do not assume this SaveChanges safeguard covers all possible future direct Role/User writes. Also pending: explicit Tenant group membership and per-user overrides, robust BFF authenticated browser E2E, signed/auditable release migration and IAM audit read UI.

Tracked in backend Issue #44.