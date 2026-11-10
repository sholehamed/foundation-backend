# FR-16 — First-install System Admin and Tenant Owner access bootstrap

Status: Implemented in source; validate PR SQL and signed HTTP CI before merging.
Date: 2026-11-10.

## Problem solved

An administrator must be able to see and manage the centrally defined access registry on **first login**; they must not need a page they cannot access in order to grant that same page to themselves. The production/tenant privilege boundary must still hold: System Admin defines all Menu/Page/Action/Resource/API/Permission contracts; Tenant Owner can only **assign allowed centrally defined Tenant role permissions**, never create global Menu definitions.

## Brand-new Identity database only

After migrations and Master Tenant creation, and **before** the legacy nine-key Platform Permission seeder, `InitialAccessBootstrap.SeedFreshInstallAsync` accepts only a truly empty catalog: no Modules, Resources, Permissions, UI, API, Menu Categories, Menus, roles or grants; the sole Tenant must be active Master. Requires the reviewed global Permission.Code UNIQUE migration. Production with the migration rollout gate still OFF refuses to bootstrap a partial schema; the operator must explicitly approve the migration first. The code does not change production migration approval flags.

For a clean database, it:
- Creates reviewed **13 System PAGE/menu roots** and nested PAGE/ACTION resources through `FeatureRegistrySyncService` (at pre-mapping startup, not guessing HTTP endpoint descriptors). Creates all code-defined System Permission rows and their UI links.
- Creates the reviewed global **Tenant module/resource + 17 PAGE/ACTION permissions and links**, then exactly **six** System-owned Tenant dashboard menu roots (three public workspace links; Members/Roles/Groups linked to their exact PAGE permissions). Runs `CentralTenantNavigationReadiness` and rejects an incomplete result.
- Creates exactly one Master-scoped `SUPERADMIN` role with System scope and **all code-owned System permissions only**; never receives Tenant permissions. Development's existing account bootstrap links its `admin` account to this role. The separate Development legacy role-grant flag remains additive and cannot add unexpected foreign grants.
- Does **not** create an account with a predictable password in Production. **Production System Admin credentials/UserRole association still require secure independent provisioning** before login. A production first-install seed must never invent a password or grant any non-System user privileges.

No dynamic-navigation or Registry Apply feature flag is enabled, and no System HTTP API catalog is fabricated before endpoints exist; the separate runtime API registry check remains a distinct verified operator step.

## New Tenant signup (Owner = Tenant Admin)

A new Tenant signup is a one-shot server-owned transaction: Tenant, Owner User, Tenant-scoped Owner role and UserRole all appear together. After checking that the centrally owned Tenant catalog is complete/active, it also writes the **17 preapproved local RolePermissions** for that exact new Tenant Owner role in the same transaction.

The existing `TenantSaveChangesInterceptor` normally requires a target-bound audited grant mutation; it now recognizes this **one and only** additional bootstrap exception: RolePermission to an Owner role that is being created alongside its brand-new Tenant, User and UserRole, with an active globally owned catalog Permission. No existing Tenant, foreign Tenant role, System Permission, inactive Permission, arbitrary Staff role, later restart, or regular endpoint can invoke this first-signup exception. Direct user Deny continues to override role allowances.

If the global Tenant catalog is incomplete, a new signup returns a retryable **503** instead of creating an Owner whose security metadata and page grants are inconsistent; an operator must reconcile the central System catalog. Preexisting Tenant owners preserve their legacy Owner fallback; no automatic role-grant restoration is done on restart or import.

## No re-grant and rollout rules

Every subsequent startup sees nonempty catalog/role/grants and skips the entire first-install privilege seeder; no disabled System page or revoked System RolePermission is revived. Existing/partial catalog installations are **never silently adopted or repaired**. Those require System Admin review, fresh four-way read-only previews, backed-up database and dedicated approved reconciliation; never auto-backfill stale or foreign permissions.

Test coverage:
- Disposable actual SQL Server, real migrations: 13 System and six Tenant menus, 17 tenant definitions, exact System role grants, no cross-type grants, repeated seed is no-op and a manually removed privilege is not restored.
- Real SQL TenantWriteIsolation: new Tenant Owner may receive only 17 globally owned permissions in first signup; subsequent unaudited Staff grant denied.
- Existing signed HTTP + SQL and Chromium checks must remain green.

This is a **first-install seed**, not a production rollout approval, not a blanket System account creator, and not a replacement for the runtime Endpoint Registry read-only integrity report.
