# FR-10A — Tenant Owner branch create/update

Date: 2026-11-08. First **narrow** Tenant Admin write operation, built on FR-09A live owner checks.

## Server endpoints
- Existing GET /api/tenant/workspace/branches remains readable to an authenticated owning tenant, including employees.
- POST /api/tenant/workspace/branches with {name,city,address,phone?} returns 200 + WorkspaceBranchDto.
- PUT /api/tenant/workspace/branches/{id:guid} with the same body returns 200 + WorkspaceBranchDto.
- GET /api/tenant/workspace/admin-capabilities now also exposes `canManageBranches` (additive, backward-compatible); true only for an active local Tenant Owner with active tenant *and* WritableTenantIds.
- POST and PUT check live Owner UserRole + tenant/user/role active status and writable scope **inside each API**; a read-only tenant employee or System session cannot mutate.
- Every new or edited Branch is scoped to ITenantContext.ActiveTenantId. Body has NO TenantId, IsMain, billing or other system fields. Updates apply explicit tenant predicate and return 404 for unknown or foreign IDs.
- No-store on reads/writes. Reject blank/oversize name (120), city (80), address (400), or phone >20 with 400 ValidationProblem.
- New branches are always non-main; editing the existing main branch can only change its contact/location fields. Main flag, tenant ownership, deletions and reorder remain unsupported.

## Safe rollout boundary
This is owner-only interim policy, NOT the eventual granular Tenant feature registry or delegated TenantAdministrator policy. No broad role grants, branch membership schema, cross-tenant writes, public signup changes, migration, billing, BFF cutover or automatic production DB Apply. Explicit Tenant PAGE/ACTION catalog, branch-level authorization, concurrency controls and real SQL Server/browser staging E2E are future prerequisites for expanding writes.

Contract companion: Frontend FR-10A. Tracker: backend issue #44.
