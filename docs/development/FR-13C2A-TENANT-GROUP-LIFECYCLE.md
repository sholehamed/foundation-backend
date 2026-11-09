# FR-13C-2A — Tenant Group lifecycle, WITHOUT group grants

Date: 2026-11-09. Continues FR-13C-1 Owner member history.

## Contract / boundaries

- Existing Group records are tenant-owned and already have a SQL RowVersion,
  unique (TenantId, NormalizedName) index and active flag.
- New reviewed Tenant ACTIONS:
  TENANT.GROUPS.CREATE, TENANT.GROUPS.UPDATE,
  TENANT.GROUPS.CHANGE-STATE, all scoped to /dashboard/groups.
  Catalog size is now **15 definitions** (3 PAGE + 12 ACTION). Code registration
  never applies Registry/role permissions automatically.
- Owner write is allowed only when **all** live checks pass: active home Tenant,
  writable scope, Groups.Read PAGE, exact requested ACTION, active local Owner
  role, with direct UserPermission Deny precedence. Every check repeats within
  the serializable SQL transaction. System and non-Owner users cannot use it.
- POST /api/tenant/workspace/groups {name, description}: create an active Group
  scoped to the authenticated Tenant, with **no UserGroup / GroupRole grants**.
  Name trims to 2..100 characters; case-insensitive reserved names OWNER,
  SUPERADMIN, SYSTEMADMIN and control characters rejected. Description <=500.
  Duplicate names return 409; no actor/TenantId/roles/users in request.
- PUT /api/tenant/workspace/groups/{id} {name,description,expectedVersion}:
  change Group metadata using a strictly 8-byte base64 RowVersion CAS. No-op
  returns unchanged version and writes no audit event. Foreign Group is 403,
  invalid payload 400, version/name conflict 409.
- PATCH /api/tenant/workspace/groups/{id}/state
  {isActive,expectedVersion}: activate/deactivate with the same CAS. This is
  intentionally a high-impact action: existing GroupRole/UserGroup links
  (if any) are retained, but the existing permission evaluator ignores inactive
  Groups immediately. Reactivation can restore inherited access, hence the
  explicit Owner-only ACTION and confirmation UI.
- GET /api/tenant/workspace/groups?page=1 returns existing directory paging
  with one additional *opaque* version field. Does not reveal grants.
  Admin capabilities and /ui-access/me now independently expose these
  exact ACTIONs (subject to mutable scope and local Owner).
- Migration **20261109050000_tenantgrouplifecycleaudit** adds
  TenantGroupChangeAuditEntries with append-only UPDATE/DELETE-deny SQL trigger;
  it records actor, group, before/after name/status and UTC time, no secrets.
  CRUD writes and audit are in one SERIALIZABLE SQL transaction. Failed audit
  insertion rolls back the group mutation. Audit isn't cryptographically
  tamper-evident. Down would DROP that ledger (operator must preserve/backup it).

## Explicitly deferred

The existing TenantSaveChangesInterceptor **intentionally rejects** all
ordinary Tenant UserGroup and GroupRole mutations (and UserPermission).
Never bypass it via a generic DbContext.SaveChanges, direct SQL or unreviewed
feature-grant endpoint. FR-13C-2B will introduce separately reviewed,
narrow, target-bound intents, transaction CAS and append-only audit for group
memberships and GroupRole assignments. No unsupported UI buttons are shown.
No Staging or Production DB migration or automatic flag/grant enablement.

## Testing and acceptance

- Disposable SQL Server: create/update/state/no-op/stale-version/duplicate/
  reserved-name, foreign Group denial, non-Owner permission denial, audit
  append-only trigger and transaction rollback if audit unavailable.
- Real signed JWT HTTP + SQL: Owner 200, anonymous 401, staff/system 403,
  duplicate/version 409, bad input 400; no cross-Tenant mutation. Tenant
  Registry Preview/Apply continues to be explicit and local to CI.
- Angular: guarded directory route, Owner-specific create/edit/state controls,
  confirmation for deactivation/reactivation, version payload, stale 409
  refresh, 403 clear and no unsupported group-membership/role actions.
- Merge only after backend unit/SQL/HTTP and frontend unit/build/Chromium CI
  pass. GitHub disposable CI is NOT a hosted Staging acceptance.
