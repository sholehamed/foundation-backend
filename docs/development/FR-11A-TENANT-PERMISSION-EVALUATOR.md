# FR-11A — Tenant permission evaluator + PAGE/ACTION access snapshot

2026-11-08 — additive, opt-in-on-grants permission foundation. Builds on FR-09A/FR-10A without schema migration, automatic grants, admin menu DB writes or BFF cutover.

## Registry and contract

`TenantWorkspacePermissionCatalog` defines **four exact, tenant-only** keys:
- `TENANT.MEMBERS.READ` PAGE, /dashboard/members
- `TENANT.ROLES.READ` PAGE, /dashboard/roles
- `TENANT.GROUPS.READ` PAGE, /dashboard/groups
- `TENANT.BRANCHES.MANAGE` ACTION, /dashboard/branches

GET `/api/tenant/workspace/ui-access/me` returns `{entries:[{key,kind,route,allowed}]}` for the authenticated local active Tenant user, with `Cache-Control: no-store`. Unreadable/system context -> 403, inactive tenant -> 404. This is a *capability snapshot for UI*, never authorization by itself.

Existing GET `admin-capabilities` remains backward-compatible and adds canViewMembers, canViewRoles, canViewGroups; canViewAdministration is the union. canManageBranches requires effective Branches.Manage **and** server-side write scope. Existing /members, /roles, /groups use separate matching keys, and POST/PUT /branches enforce their matching ACTION key on the server, on every request.

## Effective grants

All decisions validate current ActiveTenantId = HomeTenantId, readable scope, active Tenant and active local Tenant User. Roles and groups must be active, `Role.Scope = Tenant`, every link and assignment must match the **same** active tenant ID. Direct user `UserPermission.IsGranted=false` is absolute deny, overriding positive role/group grants and legacy Owner. Direct positive override wins when exactly one exists. Duplicate or inactive matching catalog permission definitions fail closed. An absent definition means *no delegated grants*; only a live local Owner may use the limited historical fallback.

Legacy Owner fallback preserves the existing post-login Owner pages and branch writes; the fallthrough accepts old Owner roles seeded with Scope=0, but never System roles. A non-owner can only gain a permission by an explicitly existing active `Permission` row plus local UserPermission/RolePermission and UserRole or active GroupRole/UserGroup links. **No corresponding grant-writing API, seed or migration is shipped here**; operator-reviewed catalog synchronization and revocation tests precede production delegation.

## Security and rollout

This catalog is not a new set of paid entitlements, nor a replacement for System RBAC. No arbitrary tenant ID or resource key may enter the evaluator. The current root Tenant-scoped reads (/dashboard, /business, /branches) stay available under the existing authenticated readable scope; only new admin directories and branch writes move to granular evaluation.

The UI should read the snapshot to filter per-page nav and protect child routes; the server independently authorizes every directory or branch mutation. Remaining: reviewed DB catalog Apply, delegated Tenant grant CRUD with audit and concurrency, branch/own/descendant policy, full dynamic tenant menu registry, SQL Server permission graph integration and authenticated E2E. Tracker issue #44.
