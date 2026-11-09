> **SUPERSEDED — 2026-11-10.** The user confirmed that only **System Admin defines menus, routes and permission definitions**. This earlier Tenant-owned menu design is retired and **must not be deployed**. See [the authoritative decision](FR-14C-CENTRAL-MENU-AUTHORITY.md). Historical content below is retained solely to explain previous commits.

# FR-14C-2A — Tenant-owned navigation catalog (read-only, default OFF)

Date: 2026-11-09. Scope: additive **physical Tenant ownership** for a new Menu catalog and a safe read-only cutover path. This is not an editable Tenant Menu UI and does not imply Branch-based RBAC.

## Why a separate catalog

Legacy `Menus` and `MenuCategories` are global (no TenantId). Adding an optional TenantId to them would silently alter System Admin and existing Tenant menu behavior, or expose mixed global/owned ancestors. Instead, FR-14C-2A creates two **new isolated SQL tables**, with no migration/backfill from global Menu and no changes to existing assignments.

`20261109080000_tenantownednavigation` creates:

| Table | Scope/columns | Constraints |
| --- | --- | --- |
| `TenantNavigationCategories` | `TenantId`, `Id`, `Title`, `SortOrder`, `IsActive`, `RowVersion`, `CreatedAtUtc` | Composite PK (TenantId, Id), FK to Tenant, title bound and visible index |
| `TenantNavigationItems` | `TenantId`, `Id`, `CategoryId`, `ParentId`, `Title`, `Icon`, `Route`, `SortOrder`, `IsActive`, `RowVersion`, `CreatedAtUtc` | Composite PK, FK to same-Tenant category, composite FK to parent **of the same Tenant AND category**, self-parent CHECK, route-shape CHECK, unique filtered Tenant+Route |

SQL delete rules are NO ACTION (no accidental cascading loss). Down refuses to drop either table if any records exist. No initial menu rows or grants are auto-seeded. **Future operational writers** must use independently audited, target-bound SQL CAS/RowVersion transactions; no generic Tenant Menu CRUD is enabled now.

The new catalog intentionally has **no BranchId yet**. A BranchId on a menu row is *not* sufficient branch authorization without a reviewed user-to-branch membership, selected-branch context and live API enforcement. That remains FR-14C-3.

## Read protocol

The existing `GET /api/tenant/workspace/navigation/me` and authenticated Tenant caller of `GET /api/System/Menus/me` use `Identity:TenantNavigation:OwnedCatalogEnabled` (missing/default **false**).

- **OFF**: existing FR-14C-1 flow unchanged, projecting global Menu with the existing Tenant PAGE checks.
- **ON**: requires matching authenticated Tenant principal, active home Tenant, TenantContext and current user; then reads only new owned SQL tables, using **parameterized server-derived TenantId**. Never uses request TenantId, role, selected Branch or UserId; direct role/group/Deny/legacy Owner checks come from the existing `TenantWorkspacePermissionEvaluator`.
- Exact allowlist: `/dashboard`, `/dashboard/business`, `/dashboard/branches` and privileged `/dashboard/members`, `/dashboard/roles`, `/dashboard/groups`; privileged links still require live corresponding Tenant PAGE. No arbitrary custom routes, external URLs or privileged dynamic paths. Nested menus are pruned on denied ancestor/orphan/cycle and limited to 32 levels and 1024 items.
- **No fallback to global Menu** on an empty or missing owned catalog. Unsupported providers return empty. With missing SQL migration, the reader fails rather than showing unscoped global data; **do not enable** before migration approval.
- No-store and independent API/Angular PAGE guards remain unchanged. This is navigation, never an authorization substitute.

## Testing, review and operational safety

The PR adds pure C# tests for unknown/denied routes, malformed parents/cycles, direct-Deny projection and no global fallback. Disposable SQL Server tests verify composite FK tenant/category isolation, duplicate route rejection, active Tenant/role grants, live direct Deny, no global fallback after owned category deactivation and foreign-tenant JWT mismatch. The BFF PR CI filter explicitly includes the new SQL test class; merely compiling it is not accepted as a test pass.

No Staging/Production migration, catalog Apply, grants, feature-flag activation, tenant menu authoring or existing DB row modifications in this PR. Both `tenantDynamicNavigationPilotEnabled` (Angular) and `OwnedCatalogEnabled` (Backend) remain OFF. The existing global System Admin Menu editor remains as before; it cannot mutate the new owned tables.

## Next gates, before enabling

1. **FR-14C-2B:** reviewed read/write authoring flow for Tenant-owned categories/items, explicit Owner/delegated manager permission catalog (Preview/Apply separate approval), atomic RowVersion CAS + append-only audit, immutable Tenant/Category/Parent ownership, cross-Tenant SQL/HTTP adversarial tests.
2. **Legacy classification & migration:** inventory existing global Menu rows and resolve System vs Tenant labels, duplicate routes, inactive categories, and parent graphs. Produce an explicit operator-reviewed import plan and reversibility checks. No automatic adoption/backfill.
3. **FR-14C-3:** BranchMembership or equivalent per-user/per-branch relation, active branch selection, role delegability limits, strict branch-scoped API/data policies, last-Owner rules and tests. Do not infer privileges from current branch URL.
4. **Operational enablement:** review/apply SQL migration, seed authorized owned navigation via audited writer, verify non-empty and page compatibility, test Chromium/proxy and BFF sessions with two tenants, then independently approve Backend/Frontend flags. Staging remains deferred until explicitly requested.
