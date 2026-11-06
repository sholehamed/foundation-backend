# FR-01A — Identity catalog inventory and Menu read contract

> Status: Code patch / awaiting PR CI and review, 2026-11-07. This is a **non-destructive** slice of [FR-01 issue #39](https://github.com/sholehamed/foundation-backend/issues/39).

## What source inspection actually established

- `DefaultIdentitySeedContributor.SeedAsync` currently calls `EnsureTenantAsync` for Master and `PlatformPermissionCatalogSeeder.SeedDefinitionsAsync`. Development seeds its test/admin accounts; optional grant bootstrap is gated. The historical `DefaultSeedData` lists are **not automatically seeded**: `EnsureModulesAsync`, `EnsureResourcesAsync` and similar calls are commented out. Historic duplicate UI keys and stale `/system/admin/...` routes in that class are therefore **not evidence of persisted rows**.
- `DbSeeder.MigrateForStartupAsync` deliberately defers the `20261024200000_permissioncodeuniqueness` migration in Production until explicit `Identity:PlatformPermissions:AllowCodeUniquenessMigration` approval. Any inventory **must check** the normalized-code column/index status and must not apply this migration indirectly.
- The new granular `SystemPermissionRequirement` and `TenantPermissionRequirement` use `Permission.Code`; `IdentityPrincipalFactory` currently emits `Permission.Name` to `permission` claims. **No claim/policy behavior is changed in this slice.** This inconsistency requires dedicated callers-and-tests audit.
- The system navigation query still takes `roleId` from the client and only considers that one role plus user overrides. Angular has an additional hardcoded System fallback. Neither is changed by this slice.

## What's in FR-01A

1. Canonical Menu read DTOs use `parentId`, `route`, `externalUrl` (and `parentTitle` in paged list) as the existing write and Angular editor contract does.
2. The existing read-only response aliases `pMenuId`, `url`, and paged `pMenu` remain temporarily serialized. They are **not** accepted as aliases for UPDATE payloads, and no alias is stored as a separate database column.
3. `MenuCategoryGetMenusDto` adds missing `externalUrl` for editing external links. It remains a flat array; parent/children tree construction and safe navigation are separate frontend/backend tasks.
4. `docs/sql/fr-01-identity-catalog-inventory.sql` is a manually operated SELECT-only inventory: schema/unique-index flag, row counts, duplicate keys, menu route drift, permission links, cross-scope grants, deprecated APIs and `Permission.ResourceId == null` items.

## Procedure for a real environment

1. Select the correct **Identity** SQL Server database; verify the connection target and an approved read-only login. Use a replica or approved maintenance window if large.
2. **Review the script**, then execute `docs/sql/fr-01-identity-catalog-inventory.sql` in SSMS/sqlcmd. It has no writes, migrations, auto-fixes, user names or personal data in its output; it exposes keys, routes, role grant **counts**, and menu text. Treat results as operationally sensitive.
3. Capture the seven result sets in a restricted deployment record; distinguish the applied vs pending permission normalization migration. Check duplicate normalized codes, historical routes and key conflicts before planning FK/migration changes.
4. Do not paste sensitive environment output into public issues or PRs. Record anonymized counts, remediation plan, explicit backup/rollback and signoff before any production write.
5. If the target schema differs (non-dbo or incomplete migrations), stop and adapt with schema review; do not bypass the guard or modify the DB.

## Remaining FR-01 / later slices

- Read-only report **has not been run on an actual user database** in this PR; only CI can validate code/test changes. A passing .NET build does not validate SQL script against target DB. Run approved SQL Server validation separately.
- Add compatibility tests for old UPDATE request payloads if any external consumer still sends `pMenuId`/`url`. Canonical Angular editor uses `parentId`/`route`/`externalUrl`.
- Check menu tree invariants (parent exists, same category, no cycles); Menu update in FR-00 only guards immediate self-parent in validation; create/reorder need more defense. Existing `MenuCategoryGetMenus` returns flat rows; frontend currently interprets `children` recursively without building that tree.
- Decide stable Permission.Code/Name claim contract after inventory of consumers. Do not silently grant role permissions or turn on legacy `PermissionMiddleware`.
- Later user-scoped navigation must compute effective grants from the authenticated System/Tenant principal rather than a supplied RoleId.
