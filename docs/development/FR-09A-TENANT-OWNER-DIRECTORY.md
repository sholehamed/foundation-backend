# FR-09A — Owner-only Tenant administration directories

Date: 2026-11-08. This slice adds read-only, **owner-gated** staff, role and group directory APIs to the existing tenant workspace. It is **not** tenant IAM CRUD or an API permission-enforcement rollout.

## Contract

- `GET /api/tenant/workspace/admin-capabilities` returns `{canViewAdministration:boolean}` for an authenticated owning tenant; revoked/non-owner -> false, invalid tenant/system -> 403/404. No cache.
- `GET /api/tenant/workspace/members?page=1` returns `{items:[{id,userName,email,firstName,lastName,isActive}],page,hasNext}`.
- `GET /api/tenant/workspace/roles?page=1` returns `{items:[{id,name,description,isActive}],page,hasNext}`.
- `GET /api/tenant/workspace/groups?page=1` returns `{items:[{id,name,description,isActive}],page,hasNext}`.
- Pages are 20 rows with deterministic order and a one-row lookahead. Only pages 1..1000 are accepted, no unbounded enumeration. No client-supplied TenantId or user ID.

Every directory checks the **live** authenticated `ITenantContext` (local home tenant, readable active tenant, active Tenant user), and a local **active Owner UserRole assignment** with an active tenant-owned Role. System-scoped roles never qualify. Legacy self-signup Owner RoleScope=0 is read-compatible; new public-signup Owner has explicit `RoleScope.Tenant`. Read queries filter `TenantId == ActiveTenantId` and never include foreign tenants or System accounts. No permission derives from a client claim or a UI-only guard.

Important: User.TenantId is the current *home membership* used here; cross-tenant delegated memberships are intentionally not included. A larger membership model and Tenant permission catalog must be reviewed before write operations.

## Not delivered / gates

No create/update/delete, role assignment, user invitations, permission grants, schema migration, billing switch, BFF cutover, production data apply or tenant menu registry. The Angular pages are owner-only read views; a 403 from the API is authoritative. Before tenant writes: define granular Tenant role/group/branch policies, enforce on server, test revoked grants and isolation on real SQL Server, then register tenant PAGE/ACTION resources and run full browser E2E.

Related: issue #44 and FR-08B.
