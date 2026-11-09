# FR-14C — Canonical menu authority decision (2026-11-10)

## Confirmed product rule

**System Admin defines the platform: Menu, MenuCategory, Page/Route, Resource, Action, Endpoint and the available Permission catalog.** No Tenant, Owner, Tenant Admin, staff member or Tenant role may create or mutate those definitions.

**Tenant Owner manages local usage of these centrally defined capabilities:** within the reviewed permission/entitlement boundary established by System Admin, the Owner creates local roles/groups, assigns permitted role permissions and assigns users/members to roles. Staff see and use only the Menu routes for which their *effective live* PAGE rights allow entry. A menu link is a projection of security, not a permission or entitlement in itself.

System Admin manages the canonical Menu tree; a Tenant does **not** have a separate Menu/MenuCategory tree. Tenant route and menu updates are derived from the central Menu catalog and per-user effective grants (including explicit deny and inherited role/group memberships), never from a Tenant-authored link.

## Correction of FR-14C-2A/2B

Earlier work incorrectly introduced parallel Tenant-owned navigation tables, an Owner menu-writing API and an Angular editor. They were merged with feature flags OFF but do not match the confirmed business rule.

This corrective PR:

- Removes the Tenant-owned Category/Item reader/writer, management HTTP routes, Angular editing route/form/client, and obsolete NAVIGATION.READ / NAVIGATION.MANAGE definitions.
- Deletes **unapplied** migrations `20261109080000_tenantownednavigation` and `20261109090000_tenantnavigationaudit`, their SQL tests/CI filter, and restores the Tenant permission catalog's original 17 code-owned definitions and associated test expectations.
- Preserves FR-14C-1's **central Menu projection** endpoints `/tenant/workspace/navigation/me` and `/System/Menus/me`, with live Tenant PAGE filtering and cross-Tenant checks. Existing System Admin menu management remains protected by `SystemAdministrator`.
- Preserves Angular's read-only, recursive centralized navigation pilot (`tenantDynamicNavigationPilotEnabled`, still OFF). The existing static header and live API guards remain in place until an approved cutover.
- Adds a CI regression to forbid reintroducing Tenant-controlled menu authoring endpoints and migrations.

Historical FR-14C-2A/2B documents and merged PRs remain accessible as a record but their design is **superseded**; they must not be used as rollout instructions.

## Database preflight — critical before deployment

Feature flags being OFF does not prove every developer/database has never executed these migrations. **Do not automatically drop tables or delete historical data.** For each real database, inspect:

```sql
SELECT [MigrationId]
FROM [dbo].[__EFMigrationsHistory]
WHERE [MigrationId] IN (
  '20261109080000_tenantownednavigation',
  '20261109090000_tenantnavigationaudit'
);
SELECT OBJECT_ID('dbo.TenantNavigationCategories') AS CategoriesObject,
       OBJECT_ID('dbo.TenantNavigationItems') AS ItemsObject,
       OBJECT_ID('dbo.TenantNavigationAuditEntries') AS AuditObject;
```

If any row/object exists, stop deployment; back up the database and review the schema, the actual Tenant records and audit-retention implications before a separate, explicitly approved reconciliation migration. Removing migration files from source does not undo a migration already applied to a database.

No real Staging/Production migration, permission catalog Apply, grant mutation or feature switch is made in this PR.

## Next development and operational gates

1. Verify the System Admin Registry contains and owns the complete Menu/Page/Resource/Action/Endpoint hierarchy and every canonical Tenant route. Add missing definitions centrally through an audited/approved Registry flow, not Tenant UI.
2. Inspect and restrict the Owner role/grant APIs so the Owner can assign **only** centrally registered Tenant-applicable permissions and cannot self-escalate or grant System privileges.
3. Confirm live central Menu lookup, valid PAGE rights/direct deny, effective Role/Group membership and cross-Tenant isolation in real HTTP+SQL and browser tests. Enable the read-only dynamic sidebar only after catalog parity.
4. FR-14C-3: model actual BranchMembership, delegated role scope and branch/data enforcement independently; selecting a branch in UI is never authorization.
5. Carry out Staging/Production rollout only after database preflight, authorization audit, CI and manual approval.

**Operational status:** source corrected but no live deployment or new feature enabled.
