# FR-17B-2C — Database-enforced immutable Published PlanVersions

**Date:** 2026-11-11  
**Backend PR:** [#132](https://github.com/sholehamed/foundation-backend/pull/132)  
**Angular companion PR:** [#62](https://github.com/sholehamed/foundation-frontend/pull/62)  
**Prerequisites:** FR-17B-1 versioned Plan catalog and FR-17B-2B System Admin draft/publish API, both merged.

## Problem

Before this slice, application commands rejected edits to a Published PlanVersion but an ad-hoc SQL UPDATE (or a second repository bypassing the command) could silently change its **published price**, **included commercial Modules**, **typed Capability values** or **customer-facing display details**. This is unacceptable as future Orders, Subscription renewals and audit records will reference a pinned published PlanVersion.

The existing `ParentPlanVersionId` pin is only useful when that referenced version and its immediate content remain stable. The new migration enforces **DML-level** immutability using SQL Server triggers without changing or backfilling any existing rows.

## Exact protected tables and trigger behavior

| Table | Trigger | SQL error | Behavior |
|---|---|---|---|
| `CatalogPlanVersions` | `TR_CatalogPlanVersions_Immutable` | 51021–51023 | New versions must be inserted Draft. Existing Published/Retired version UPDATE/DELETE is denied. Draft→Published requires a timestamp and 64-character fingerprint; invalid transitions denied. |
| `CatalogPlans` | `TR_CatalogPlans_ImmutablePublishedIdentity` | 51024 | Once any version is Published/Retired, prevents in-place modifications or deletion of stable plan identity, parent, availability, labels. |
| `CatalogPlanVersionModules` | `TR_CatalogPlanVersionModules_Immutable` | 51025 | No INSERT, UPDATE, DELETE of associations owned by a Published/Retired version. |
| `CatalogPlanVersionCapabilityValues` | `TR_CatalogPlanVersionCapabilityValues_Immutable` | 51026 | No alteration to pinned Feature/Limit/Quota/Mode values. |
| `CatalogPlanPriceOffers` | `TR_CatalogPlanPriceOffers_Immutable` | 51027 | No price, cycle, period or offer activation changes after publication. |
| `CatalogPlanDisplayItems` | `TR_CatalogPlanDisplayItems_Immutable` | 51028 | No direct manipulation of the published dynamic marketing-card content. |

Every child trigger considers **both** `inserted.PlanVersionId` and `deleted.PlanVersionId`; moving a Published row into a Draft or deleting/reinserting price rows cannot bypass it. The parent Version row is read with `UPDLOCK, HOLDLOCK`, coordinating the child DML with a concurrent `SERIALIZABLE` publish. A multi-row SQL UPDATE touching a Published **and** a Draft row is rejected as a complete statement; it must not partially change the Draft row.

Creating a new Draft and editing its rows is allowed. `SystemPlanCatalogAdminService.PublishAsync` still verifies the *reviewed SHA-256 preview fingerprint* under one SQL transaction before the Draft→Published update; the DB trigger is a complementary guard, not a replacement for application authorization.

## Critical EF Core compatibility

On SQL Server, EF Core can use a direct `OUTPUT` clause during INSERT/UPDATE. SQL Server rejects certain such statements against target tables with enabled DML triggers.

The **same six tables** now declare their exact trigger names in `PlanCatalogConfigurations.cs` via `ToTable(... t => t.HasTrigger(...))`. The retained `IdentityDbContextModelSnapshot.cs` records the same metadata, preventing `Database.HasPendingModelChanges()` discrepancies. The new manual SQL-only migration does **not** change columns, indexes, keys, relationships, permissions or seed data.

## Migration / deployment

Migration: `src/modules/Modules.System.Identity/Migrations/20261111090000_plancatalogimmutability.cs`

- `Up`: uses standalone `EXEC(N'CREATE OR ALTER TRIGGER ...')` statements for idempotent EF-generated scripts; existing published catalog rows remain untouched.
- `Down`: explicitly drops six owned triggers. **Rollback removes SQL protection**, though app-side restrictions remain. Consider this a security-relevant rollback and require reviewed operator approval.
- If deployed as an EF migration, verify the normal deployment migration approval and do not force or bypass the existing startup migration gate. No migration is auto-applied by a GitHub merge.
- Verify in disposable SQL Server with `Database.MigrateAsync()` and `Database.HasPendingModelChanges()`. Do not run tests against production.
- The database application account must be denied unreviewed DDL/ALTER/CONTROL; SQL administrators with the ability to **disable/drop triggers** can bypass these guards. These are DML-integrity controls, **not protection against a malicious database owner**.

## Lifecycle limitation, by design

A Published version is entirely frozen, **including Status**. Today, a direct Published→Retired update or disabling an already Published `CatalogPlans.IsActive` is rejected. This prevents mixing immutable historical price/content snapshots with mutable *sale availability*. Before commercial launch, model a **separate audited visibility/availability overlay** or trusted retirement protocol, retaining historical Published version snapshots for existing subscribers. Do not drop/disable the trigger simply to unpublish a Plan. No commerce or entitlement activation is included here.

**Important security boundary:** The database verifies the Draft→Published status transition shape, but **cannot independently verify that the hash corresponds to a real reviewed operator preview**; the System Admin service must remain the only application path and enforce live authorization + fingerprint. A privileged SQL DML writer capable of changing a Draft to Published directly could still fabricate publication metadata. That is an outstanding production hardening item for restricted roles or trusted SQL publication procedure.

Other source-owned business objects (Module/Resource/capability definition) have separate lifecycle rules. The existing published reader verifies deployed Commercial code ownership and fails closed if references drift; this migration only freezes Catalog Plan/Version rows and four owned child tables.

## SQL regression cases

`PlanCatalogSqlServerTests.Database_triggers_reject_direct_mutation_of_published_plan_and_every_child_table` uses a disposable loopback SQL Server database and migrations, then asserts:

1. Draft + owned children can be created; Draft→Published remains possible.
2. Direct SQL UPDATE to Published version fields or Status fails with error 51022.
3. Plan identity UPDATE after publication fails (51024).
4. Module row DELETE (51025), typed value UPDATE (51026), price UPDATE (51027), display text UPDATE (51028) all fail.
5. Reassignment of Published price to Draft is blocked (51027).
6. **Mixed Published+Draft** multi-row UPDATE fails atomically; Draft price unchanged.
7. Draft-only price update still works.
8. Inserting an additional Published offer through EF fails with SQL 51027; forged direct Published version INSERT fails with SQL 51021.
9. No `TenantModules` or RolePermission grant occurs.

Earlier PlanCatalog fixtures now insert **Drafts first**, populate Modules/Capabilities/Prices/DisplayItems and transition those rows to Published, matching this stricter lifecycle.

## Angular companion

`/system/plan-catalog` shows an explicit lock message on every Plan with a `latestPublishedVersionId`: **نسخه منتشرشده قفل است؛ تغییرات فقط با نسخه جدید اعمال می‌شوند**. The action remains **نسخه جدید**, never an editor targeting an immutable revision. Angular unit test covers Published row behavior. No new client-owned permission keys or APIs.

## Exit criteria and next tasks

- SQL/EF migrations model snapshot, Build, Unit, real SQL Server Feature Registry & PlanCatalog, Tenant IAM HTTP E2E, Angular Unit/Build/Chromium **all green on each PR head and post-merge main**.
- **No sale is active**: no real code-owned Commercial Module registered yet. Published plan cards may correctly return `[]`. No Subscription, coupon, order, gateway or Tenant commercial grant.
- Existing populated databases require reviewed Registry Apply and Master grant assignment for `PLAN-CATALOG.*`; first-install roles only cover fresh databases.
- Next: restricted DB publication permissions/approved retirement overlay, genuine commercial module registrations with EndpointGroups and module resource scopes, FR-17C Subscription/Entitlement read-side shadow evaluation, then quota/discount/verified checkout.

Do not mark commercial launch or live staging verified based solely on CI.
