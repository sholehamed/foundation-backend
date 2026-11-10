# FR-16B — Replace unusable historical DefaultSeedData with authoritative startup seed

Date: 2026-11-10. No database reset, no migration, no tenant entitlement expansion, and no environment-wide feature flag change.

## Why the old file was unusable

The previous `Infrastructure/DefaultSeedData.cs` had ~16 KB of hard-coded demonstration tenants, roles, resources, pages and permissions. Most were not called: `DefaultIdentitySeedContributor.cs` contained 11 commented-out `Ensure*` calls. Its menu URLs used obsolete `/system/admin/...` routes, old permission IDs did not match the reviewed Feature Registry, and fake users/tenants were mixed with the real Master Tenant. Editing those arrays had **no actual effect** on the installed menu/role catalog.

## What changed

- Retain `DefaultSeedData` as a small, **real bootstrap identity definition**: stable historical Master Tenant ID/slug, System SuperAdmin role name, and factory for the Development-only System administrator. `MasterTenant` returns a new EF entity each access to avoid reuse of a tracked static object across startup/DbContexts.
- Central System pages/resources/permission/UI/Menu definitions have a **single code owner**, `FeatureRegistryDefinitions.Features` + `UiElements`; Tenant PAGE/ACTION permissions come solely from `TenantWorkspacePermissionCatalog.Definitions` and its six global menu paths solely from `TenantWorkspaceNavigationService.RoutePermissions`. `InitialAccessBootstrap` is the single clean-database execution path; existing data is never silently re-granted.
- Cut the 500-line `DefaultIdentitySeedContributor` down to the actual startup orchestration: OpenIddict clients, Master Tenant, first-install feature and Tenant registry bootstrap, legacy platform definition-only compatibility, and Development-only system account/E2E fixtures. Remove duplicate, unused EF CRUD helpers and commented-out seed calls.
- Development account initialization now consumes a fresh User instance. It preserves existing local `admin` / `admin@master.ir` login behavior, does not overwrite a previously configured password, and still permits the existing `SHINERA_DEVELOPMENT_ADMIN_PASSWORD` environment variable. The historic `123456` fallback is **only** for Development, is unsafe on shared hosts, and must never be used outside an isolated local environment.
- No fake demo Tenant/Owner account is created at startup. New Tenant owners obtain their centrally owned, scoped 17 permissions atomically via public signup, as in FR-16. The System role gets only code-defined System capabilities. Production initial SuperAdmin credentials remain a separate approved secure provisioning step.

## Compatibility and safety

- Existing Master Tenant ID **unchanged**: historical `DefaultSeedData.Entities.Tenant` enum ordinal was 13, sequence 1, so the same deterministic StructuredGuidV7 ID continues to be used by all production authorization services.
- The first-install code-owned catalog remains a **create-once** bootstrap; restarts never restore deleted RolePermissions, disabled registry records or legacy cross-tenant permissions. Existing legacy deployments need separately reviewed reconciliation, not a magic seed wipe.
- The Development E2E fixture IDs are unchanged. No change to remote Staging, production data, or default-OFF Angular dynamic menu flag.
- Regression tests check stable Master ID, fresh EF factories, unchanged Development admin identity, and exact System/Tenant registry routes. Existing real SQL first-install, HTTP/JWT and Chromium suites are required to pass before merge.

## Developer workflow

To get a clean development environment, start with an isolated SQL Server database, run normal migrations and start the API once. Startup creates the Master scope, code-owned System/Tenant PAGE/Permission/Menu rows, Master-scoped System role permissions, and the **Development** admin user/assignment. For shared Development hosts, explicitly configure `SHINERA_DEVELOPMENT_ADMIN_PASSWORD` with a strong unique value before starting.

Do not edit sample Menu or Permission arrays in `DefaultSeedData` (there are none). To add a System page, add reviewed metadata to `FeatureRegistryDefinitions` and corresponding Angular route/API policies. To add a Tenant feature, update `TenantWorkspacePermissionCatalog`, reviewed Tenant navigation definitions and tests. An existing deployed database requires operator-reviewed catalog Preview/Apply, not automatic startup re-grants.

**Evidence:** PR CI and post-merge full-stack Actions runs recorded in this change's PR.
