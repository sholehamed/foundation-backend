# FR-12C — Tenant role lifecycle (create / edit / activation)

2026-11-08. FR-12B audited Tenant grant replacement prerequisite.

Adds three code-owned Tenant ACTION keys: TENANT.ROLES.CREATE, TENANT.ROLES.UPDATE, TENANT.ROLES.CHANGE-STATE. Catalog now contains eight keys. No automatic DB Apply, grants or seed. System Admin FR-12A preview/apply must be explicitly reviewed (flag OFF by default).

GET /api/tenant/workspace/roles keeps the authenticated 20-row paging and adds version (SQL RowVersion base64) and isOwner. POST /roles accepts name/description only, creates an active Tenant-scope role with zero grants. PUT /roles/{id} accepts name/description/expectedVersion. PATCH /roles/{id}/state accepts isActive/expectedVersion. DELETE is not provided.

All writes require live local Tenant Owner plus Roles.Read, corresponding ACTION, active tenant/user, home=active and writable scope. Cannot touch system/foreign/Owner or actor-assigned direct/group role, cannot use reserved OWNER/SUPERADMIN/SYSTEMADMIN names. Server derives TenantId, actor, Scope and IsActive defaults. SQL transaction, SQL RowVersion compare-and-swap and SQL audit insert are atomic. No-op does not write audit. Disabled role stops granting access immediately in the existing live permission evaluator, assignments remain stored.

Migration 20261108190000_tenantrolechangeaudit adds append-only TenantRoleChangeAuditEntries with an UPDATE/DELETE-denying trigger. Records tenant, actor, role, action, old/new name and active state; survives deletion but is not tamper-evident.

Deployment is separate: apply migration in reviewed release, use explicitly enabled catalog preview/apply for the new keys, then disable Apply flag. Code merge does not modify live DB, seed permissions or grant actors. Future: audited user-role membership, audit history UI, delegation scope, BFF cutover and browser staging E2E.