> **SUPERSEDED — 2026-11-10.** The user confirmed that only **System Admin defines menus, routes and permission definitions**. This earlier Tenant-owned menu design is retired and **must not be deployed**. See [the authoritative decision](FR-14C-CENTRAL-MENU-AUTHORITY.md). Historical content below is retained solely to explain previous commits.

# FR-14C-1 — Live Tenant navigation and page ownership boundary

2026-11-09. This slice aligns the existing global Menu catalog navigation with the **live, Tenant-scoped PAGE permission evaluator**. It does **not** implement branch membership, per-Tenant menu ownership rows, or new grants.

## Findings

- The existing `/System/Menus/me` builds a dynamic navigation tree using live direct UserPermission/RolePermission/GroupRole assignments, but `Menu.PermissionId=null` historically allowed every such route to appear.
- The Tenant workspace UI was still rendering its six static header links, while its three directory pages already had independent live PAGE guards.
- Global Menu/MenuCategory entities currently do **not** store TenantId/branch ownership. A Menu can therefore be shared across Tenants. We must not portray the global catalog as Tenant-owned content or silently add cross-tenant menu mutations.
- The old navigation role projection allowed a Tenant UserRole pointing to a System-scoped Role under the same Tenant. The actual Tenant PAGE evaluator rejects that role scope; navigation needed to match it.

## Backend contract

`GET /api/tenant/workspace/navigation/me` is authenticated, returns `Cache-Control: no-store`, and has no user/role/tenant/branch selector. A tenant principal and the live `ITenantContext` must agree on subject, home Tenant and active readable Tenant, after which `CurrentUserNavigationService` validates active Tenant, Tenant slug and User, and resolves the live Menu tree. Foreign Tenant, System, anonymous or stale principal/context yields 403.

`TenantWorkspaceNavigationService` filters every returned route through an **exact, code-owned Tenant route map**:
- `/dashboard`, `/dashboard/business`, `/dashboard/branches`: authenticated workspace paths; branch *writes* are still separately protected by `TENANT.BRANCHES.MANAGE`.
- `/dashboard/members`, `/dashboard/roles`, `/dashboard/groups`: require `TENANT.MEMBERS.READ`, `TENANT.ROLES.READ` or `TENANT.GROUPS.READ`, respectively, as decided by the **existing** `TenantWorkspacePermissionEvaluator.AllowsAsync`. That evaluator enforces live active Tenant/user, tenant-local Tenant-scoped Role and GroupRole, direct user override/deny and the existing reviewed legacy Owner fallback.
- Any unknown internal path, System route, unsafe external URL (filtered by original navigation) or unauthorized ancestor/child is omitted; containers with no visible descendants disappear. The client never receives role, permission, Tenant or User IDs.

The existing `GET /api/System/Menus/me` now uses that same filtered projection for Tenant callers, while its System caller behavior remains unchanged. There is no silent fallback from denied Tenant routes to raw Menu data.

The Menu catalog continues to be **global**. A data row with a spoofed title pointing to a safe route might still be rendered to multiple Tenants; this endpoint does not establish Tenant-owned labels or an editable tenant hierarchy. The next FR-14C slice must explicitly design Tenant menu ownership, migration of legacy catalog rows, approval and scoped writes before tenant-level customization.

## Acceptance

- In-memory security tests: no-permission Menu rows do not expose privileged Tenant PAGE, live grant/deny, local group-role, inactive group revocation, wrong RoleScope, spoofed Tenant principal/context, inaccessible ancestors, unknown paths and explicit no-store/403.
- Disposable SQL Server integration: role-scope mismatch, direct Deny, revoked assignment and persisted menu with no PermissionId all obey the same policy.
- Existing System navigation, Tenant IAM HTTP/SQL and BFF tests remain green; no migration or flags are activated.

## Remaining FR-14C

- FR-14C-2: tenant/branch-owned navigation schema, legacy migration classification, exact Menu to PAGE registrations, publisher/editor permissions, and zero cross-tenant leaks.
- FR-14C-3: explicit role delegation scope and branch-specific membership/RBAC. **Do not infer branch assignment from a readable TenantId or allow an Owner to silently expand another operator's branches.**
- Browser pilot, catalog/grants and Staging/Production operator approval. This phase does no deployment, registry Apply, grants, or DB migration.
