# FR-17A — Source Resource/EndpointGroup inventory (first pass)

**Date:** 2026-11-10. **Status:** **Source mapping verified on Foundation Backend main 5f88e137; persisted target SQL NOT inspected, proposed ownership changes NOT implemented.** Use with [FR-17A handoff](FR-17A-MODULAR-ENTITLEMENTS-HANDOFF.md) and [architecture](../architecture/modular-entitlements-plans-limits.md).

## 1. What is known from checked source

Inspected `src/modules/Modules.System.Identity/Web/Util/FeatureRegistryDefinitions.cs` and these Endpoint source files under `src/modules/Modules.System.Identity/Web/Endpoints/`. Most System EndpointGroups already carry explicit `WithFeatureEndpoint(Key, ResourceCode, PermissionCode, Title)` metadata. This gives a stable mapping of individual endpoints to a persisted Resource, independent of HTTP RouteTemplate.

| Code-owned Module | Resource.Code | EndpointGroup/Controller source(s) observed | Example permission family | Classification proposed |
| --- | --- | --- | --- | --- |
| `system` | `dashboard` | `Dashboard.cs` | `DASHBOARD.*` | PlatformCore |
| `system` | `admins` | `Admins.cs` | `ADMINS.*` | PlatformCore |
| `system` | `users` | `Users.cs` | `USERS.*` | PlatformCore |
| `system` | `roles` | `Roles.cs` | `ROLES.*` | PlatformCore |
| `system` | `permissions` | `Permissions.cs` | `PERMISSIONS.*` | PlatformCore |
| `system` | `groups` | `Groups.cs` | `GROUPS.*` | PlatformCore |
| `system` | `modules` | `Modules.cs` | `MODULES.*` | PlatformCore |
| `system` | `tenants` | `Tenants.cs` | `TENANTS.*` | PlatformCore |
| `system` | `resources` | `Resources.cs` | `RESOURCES.*` | PlatformCore |
| `system` | `api-resources` | `ApiResources.cs` | `API-RESOURCES.*` | PlatformCore |
| `system` | `ui-resources` | `UiResources.cs` | `UI-RESOURCES.*` | PlatformCore |
| `system` | `menu-management` | **`Menus.cs` + `MenuCategories.cs`** | `MENU-MANAGEMENT.*` | PlatformCore |
| `system` | `feature-registry` | `FeatureRegistry.cs` | `FEATURE-REGISTRY.*` | PlatformCore |

All 13 Source Resource definitions above correspond to the 13 `FeatureRegistryDefinitions.Features` System root features, but **do not treat each as exactly one EndpointGroup**: `Menus.cs` and `MenuCategories.cs` both bind to `menu-management`. This is an observed shared Resource, not a claim that the source has a one-to-one Resource-to-class invariant. Product owner described the intended mapping as **approximately** Controller/EndpointGroup; this exception should be reviewed rather than forcibly split.

## 2. Tenant and cross-cutting exceptions

| Endpoint source / catalog | Evidence | Design handling |
| --- | --- | --- |
| `TenantWorkspace.cs` | Large Tenant workspace endpoint surface; 17 code-owned Tenant PAGE/ACTION definitions live in `TenantWorkspacePermissionCatalog` and are owned by current `tenant-workspace/tenant-administration` Resource | **Exception:** several business sub-domains (members/roles/groups/branches) are cataloged together; decide vetted EndpointGroup-level split after full authorization-handler and existing RolePermission/UiResource migration impact |
| `PublicSignup.cs` | Anonymous `/public/signup/checkout` and slug-available; stores `RequestedPlanKey`; explicitly no paid entitlement at signup | Core onboarding, not a purchased product Resource |
| `Menus.cs` / `GetMyNavigation` | Current-user scoped menu projection is authenticated separately from System Administrator menu CRUD | Navigation read should remain available to authenticated eligible users; no accidental commercial gate |
| BFF/Auth/OIDC endpoints (not yet exhaustively inventoried) | Separate security infrastructure and policy, not the commercial catalog | **Unverified here:** inventory them as System/PlatformCore/InternalOnly in follow-up; never bind them to saleable Module blindly |
| `TenantModules` and `BusinessProfile.RequestedPlanKey` | Existing TenantModule flags and onboarding intent; no actual subscription or checkout | Legacy inventory needed; never treat these as effective entitlement without reviewed migration |

## 3. Missing evidence: must not guess

This is a **source-only initial mapping**, not full release readiness. No access to a user's live/development SQL database was used. Before creating/modifying database schema:

- Run the existing read-only `docs/sql/fr-01-identity-catalog-inventory.sql` against approved target Identity DB, record DB/schema migration versions and sanitized counts of Modules/Resources/Permissions/PermissionApiResources/PermissionUiResources/Menus/RolePermissions/TenantModules.
- Read `GET /api/System/FeatureRegistry/central-access/readiness` with a proper System Admin in an approved environment. Preview is evidence about current catalogs but **not permission to apply writes**.
- For all routes: compare live EndpointDataSource metadata, explicit System/Tenant/custom policies, anonymous/special cases, registered EndpointKeys and Resource owners. The 13 System rows above are not exhaustive of all platform endpoints.
- Detect `ResourceId=null` legacy Permissions, duplicate/foreign code ownership, hardcoded `tenant-administration` checks, Menu/UiResource relationships and inactive inherited rows; propose **old/new ID/code mapping** and reversible manual migration rather than direct rename.
- Inspect any non-Identity app Modules/Controllers. This first pass covers the Identity source; no claim about future Sales/Appointments/CRM EndpointGroups.
- Map each operation to applicable typed entitlement/Limit/Mode requirements; READ and CREATE may have different limit enforcement even in one Resource. No unrestricted dynamic C#/SQL from the plan database.

## 4. Non-destructive mapping/migration approach

1. Freeze current stable `Module.Code`, `Resource.Code`, `Permission.Code`, `ApiResource.Key` and `UiResource.Key` in a test fixture.
2. Draft ownership additions and exceptions in a versioned manifest; compare actual rows without mutation. If a split is necessary, create new stable owners only after reviewing old Permission/RolePermission/Menu/API/UI links and authorization handlers.
3. Preserve all current System and Tenant bootstrap administrative permissions and Master Tenant ID; do not remove/regrant existing rights during source reclassification.
4. Add shadow-only typed entitlement evaluation and capability metadata before enforcing licensing, which needs separate signed SQL/HTTP and multi-Tenant browser acceptance.
5. Require explicit migration/scope classification review and operator restore plan before any production data reconciliation.

## 5. Completion state

- [x] Read actual System FeatureRegistry definitions.
- [x] Read the 13 System EndpointGroup sources (including shared Menus/MenuCategories mapping) and confirm their resource metadata.
- [x] Read TenantWorkspace, PublicSignup and Tenant catalog source and record noncommercial bootstrap exceptions.
- [ ] Full API EndpointDataSource enumeration, including Auth/OIDC/BFF and any non-Identity modules.
- [ ] Actual persisted SQL inventory, `TenantModules` usage and null/foreign/duplicate Resource ownership.
- [ ] Approved conversion plan for `tenant-administration` and control-plane classification.
- [ ] Runtime metadata validator and tests (FR-17A-2).
- [ ] User/business owner acceptance of ADR-0002 and D-01..D-10.

**No SQL records, API permissions, runtime gates or code definitions were changed by this inventory.**
