# FR-12B — Audited Tenant Role Permission Replacement

Date: 2026-11-08. This is the first restricted Tenant IAM write action; it does **not** authorize employees to administer IAM.

## Contract
- Code-owned ACTION `TENANT.ROLES.ASSIGN-PERMISSIONS` registered only through the existing opt-in, reviewed FR-12A System Administrator catalog preview/apply flow. Never auto-granted.
- `GET /api/tenant/workspace/roles/{id}/permissions` gains `version` (base64 SQL Server Role.RowVersion) and `canEdit`. List entries now include the new ACTION key.
- `PUT /api/tenant/workspace/roles/{id}/permissions` body `{expectedVersion:"<base64 rowversion>", codes:[...code-owned Tenant permission keys]}`; returns a fresh read-only matrix on success, HTTP 409 if version/catalog is stale, HTTP 400 for unknown/duplicate/invalid keys, 403 for unauthorized caller/immutable Owner/self-assignment and 404 for a foreign/missing target.
- The Actor and Tenant ID come from live server `ITenantContext`, never request body. Role must be active, `RoleScope.Tenant`, and must not be `OWNER`; actor must be active, local, hold the local Owner role and be writable, and have effective `Roles.Read` and `Roles.Assign-Permissions` decisions (direct user deny wins). Actor cannot modify a role held directly or through one of their groups.
- All defined catalog Permission rows must be active and belong to the reviewed Tenant Workspace Resource/Module, with active matching UI Resource and PermissionUiResource link. Otherwise grant writes are refused until explicit catalog review/apply.
- Only those five code-owned Tenant permission IDs may change. Other RolePermissions are preserved, including previously installed non-catalog privileges. No UserRole, UserPermission, UserGroup or GroupRole updates.

## Atomicity
- Writer accepts SQL Server only, runs one Serializable transaction, checks live scope/actor/target/catalog, verifies expected SQL rowversion, and executes an atomic compare-and-swap UPDATE on the **Role** row to serialize all grant replacements for the target.
- The matching RolePermission adds/removes and one audit row *per change* are saved/inserted **inside that same transaction**; audit insertion failure cancels all changes. No-op replacements do not bump version or write audit.
- Migration `20261108163000_tenantrolegrantaudit` creates `TenantRoleGrantAuditEntries` with an append-only trigger. Audit holds TenantId, actor, role, permission ID+code, grant/revoke and UTC time; no FK so historical evidence survives deletions. This ledger is durable but not cryptographically tamper-evident.
- SQL deadlocks/concurrency violations must produce conflict responses rather than overriding concurrent actor changes; UI reloads its matrix on a conflict.

## Deployment boundary
**No migration was executed on production** and no grants were assigned as part of this code merge. Apply DB migration through the standard reviewed deployment channel first, followed by an operator-reviewed FR-12A catalog Preview/Apply (flag OFF by default). Existing Owner fallback remains in place. Real per-branch scopes, new Tenant Admin invitations/roles, dynamic DB menus, audit listing UI and BFF cutover are future slices.
