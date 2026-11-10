# FR-17B-2B — System-owned Plan authoring, reviewed publication and Angular admin

**Date:** 2026-11-10  
**Implementation PRs:** [Backend #131](https://github.com/sholehamed/foundation-backend/pull/131), [Angular #61](https://github.com/sholehamed/foundation-frontend/pull/61)  
**Parent task:** [FR-17B-2B Issue #130](https://github.com/sholehamed/foundation-backend/issues/130)  
**Status:** Implemented in PR; verify current-head SQL/HTTP/build/browser CI before merge. This document distinguishes ready functions from **unimplemented** hardening.

## Core ontology and non-negotiable boundaries

- **Resource** is a Controller/EndpointGroup-level grouping, **not** a page/business entity.
- The `System / plan-catalog` Resource is centrally code-owned. A **System Administrator** may propose, review and publish Plan catalog offerings. A Tenant Owner cannot define or edit Modules, Resources, Permissions, Plan content or prices.
- `ModuleResourceContracts.Current()` contains the 14 System Resource declarations, Tenant core and currently **no Commercial Module**. Code-owned Commercial Modules must be introduced after corresponding real endpoints and authorization policy exist, **never inferred from a DB Module row**.
- Buying/activating a Plan will later license a **Tenant** to use its commercial Modules. It does **not** create any `RolePermission` / `UserPermission` or choose which Tenant employee may use it. Effective access will require both purchased Tenant entitlement **AND** explicit Tenant-local permission.
- New Tenant's initial Owner core administration is separate from commercial entitlement. `PublicSignup.RequestedPlanKey` remains **intent only**. No `Subscription`, `Order`, `Coupon`, `Payment`, trial, usage ledger, paid activation, discount redemption or grant is created here.

## Central registration and security

In `FeatureRegistryDefinitions.Features`, the four permission codes are `PLAN-CATALOG.READ`, `PLAN-CATALOG.CREATE`, `PLAN-CATALOG.UPDATE` and `PLAN-CATALOG.PUBLISH`. Page `system.plan-catalog` and route `/system/plan-catalog` are centrally owned, with Menu permission `PLAN-CATALOG.READ`.

This makes **14 System root pages**, 14 corresponding System Resource definitions and a total of **20** initial System+Tenant root menus (14+6) on a truly fresh installation; first-install Master SuperAdmin is assigned **all** code-owned System permissions by `InitialAccessBootstrap`. Tenant Owner still receives only Tenant-core keys.

On an **existing populated database**, `FeatureRegistrySyncService` remains deliberately additive and does **not** recreate revoked grants or silently grant newly introduced permissions. A live System SuperAdmin must use the reviewed Registry preview/apply process and then the audited role-permission grant workflow to add new `PLAN-CATALOG.*` rights to the existing Master role. Until then the UI may not appear and corresponding API granular enforcement can fail. Do **not** bypass that with unreviewed seed. If the product decision is to automatically grant all future first-party admin features to an existing SuperAdmin, that requires a separately reviewed versioned policy, audit/idempotency and explicit revoked-vs-never-granted distinction.

Every mapped `/api/system/PlanCatalog/*` endpoint:
1. Inherits the existing `SystemAdministrator` ASP.NET policy.
2. Carries `WithFeatureEndpoint` exact API Key, Resource Code and Permission Code for live Registry reconciliation and optional reviewed granular enforcement.
3. Re-verifies the active **Master** System principal, `SUPERADMIN` role membership and user status against SQL for every read/write.
4. Returns no-store data, without any arbitrary TenantId/RoleId in the input.

## API contract

| Verb | Endpoint (below /api/system/PlanCatalog) | Permission | Result |
| --- | --- | --- | --- |
| GET | `/` | READ | System Plan list, latest published/draft version identifiers |
| GET | `/commercial-modules` | READ | Only **active** DB Modules also present in deployed commercial source contract |
| POST | `/` | CREATE | Creates immutable Plan identity + Draft version 1 (not active for sale) |
| POST | `/{planId}/drafts` | CREATE | Copies last Published/Retired version into a **new editable Draft revision** |
| GET | `/drafts/{versionId}` | READ | Current persisted Draft content, offers, module codes, typed capability values, card display rows |
| PUT | `/drafts/{versionId}` | UPDATE | Atomically replaces **Draft-only** contents (validated commercial modules, typed caps, IRR offers, display rows) |
| GET | `/drafts/{versionId}/preview` | READ | SHA-256 content fingerprint, issues and effective inherited commercial modules; **zero writes** |
| POST | `/drafts/{versionId}/publish` | PUBLISH | `{ "fingerprint": "<last-reviewed-hash>" }`. Serializable SQL transaction; re-preview exact snapshot before published status change |

The expected fingerprint is based on code-owned Resource manifest, selected Draft identity/fields, pinned parent version references/fingerprints, referenced Module ids, price offers, capability values and ordered display items. A changed price or display row produces a **different hash**; publishing an old review fails `stale_preview`. Concurrent publishing the same version is rejected, and a newer Published revision blocks publication of an older version. All writes use SQL Server transactional execution, not SQLite/in-memory transaction semantics.

Input limits are intentionally tight: Plan.Code max 20 until `BusinessProfile.RequestedPlanKey` is migrated; stable lower-kebab-case; Module Code references must be registered commercial/active; price currency must equal `IRR` with monthly/yearly cycle and nonnegative decimal amount; user-entered display strings reject markup characters. Capability values must have exactly one source-compatible typed value. Zero-price is a **published offer**, not a coupon redemption or payment confirmation.

## Hierarchy / versioning

`CatalogPlans.ParentPlanId` is an organizing relationship. **Effective inherited content** is pinned by `CatalogPlanVersions.ParentPlanVersionId`, and that pin must point to the published revision of the expected parent Plan. The editor cannot point at an unpublished or inactive parent. Preview validates parent chain/cycles/depth and rejects noncommercial modules. Module membership is inherited as the distinct union, typed capability merge follows the reviewed source policy, child price overrides parent price (each version has its own `PriceOffers`), and card descriptions inherit from the pinned ancestor with child override for the same Module/Capability key.

Example: Starter v1 contains a Bookings module; Pro v1 pins Starter **v1** and adds Personnel. Publishing Starter v2 does **not** change Pro v1. To take v2 features, a new Pro Draft revision must explicitly pin v2.

**Current production behavior:** no deployed Commercial Modules means `GET /commercial-modules` returns `[]`, Draft creation works, but Publish preview reports `no_commercial_module` and commit is rejected. Therefore `GET /api/public/plans` still safely displays no purchasable plans.

## Angular implementation

Companion route `/system/plan-catalog` is beneath existing `systemSessionGuard` and `systemAllUiAccessGuard`. Standalone Angular UI reads real plan catalog, creates drafts, selects a pinned published parent, manages recognized Commercial module checkboxes, IRR price options, ordered display items, loads saved drafts, requests read-only preview and only enables Publish if the backend returns `canPublish: true`. Unknown or noncommercial modules are never generated by the UI. It clearly distinguishes “پیش‌نویس ذخیره شد” from “نسخه منتشر شد” and **never claims a subscription was sold**.

## Test acceptance

- Central FeatureRegistry counts, UI route inventory and initial Master role/menu rights include the 14th root. No new Tenant permissions are granted.
- SQL Server isolated DB tests: Draft creation and persistence; no module/invalid price cannot publish; code-owned Commercial Modules can publish in an **explicit test fixture only**; price edit changes preview hash; outdated hash rejected; service rejects changes to published version; revision copies content without touching earlier version; public view sees published offer; **zero** RolePermission/UserPermission/TenantModules side effects.
- Existing signed JWT Tenant-role/permission/revoke, System feature discovery, SQL Server migration/build suites stay green.
- Angular HTTP contract tests and UI test cover no-saleable-module state and Publish disabled until server-reviewed eligibility; browser pilot remains unchanged.
- Check **newest PR head** and **post-merge main** workflows separately; do not infer Staging/production deployment from CI.

## Explicitly unfinished / next hardening slice

**H-01:** Direct database writes to published `CatalogPlanVersions` and its child tables are not yet prevented by SQL immutable-version triggers/permissions. This PR enforces immutability through its reviewed admin command handlers only. Do not treat this as a DB-sealed publication model or expose arbitrary Catalog table write APIs before that hardening is completed.

**H-02:** Full SQL publication CAS tests under multiple concurrent DbContexts, a verified operator audit trail, and dedicated live HTTP tests for every Plan endpoint should precede production catalog authoring.

**H-03:** Source-owned Commercial Module implementations, their EndpointGroups and operation capability enforcement are **not shipped**; no plan can become sellable by using generic Module CRUD alone. Typed CountLimit, PeriodQuota and workflows need bound operations and usage ledgers in later slices.

**H-04:** Existing databases need an explicit, reviewed System Registry Apply plus approved RBAC permissions for the new Plan Catalog menu. The system must never auto-heal previously revoked grants. Review a safe versioned first-party permission rollout procedure with operators.

**H-05:** Currency/tax/cycle unit, quoted price snapshots, dynamic capabilities in the admin editor, tenant subscription activation, coupon allocation/redemption (100% and fixed amount with expiry/usage limits), zero-total settlement and verified payment adapter come later. Marketing Benefit text is not a permission.

**H-06:** Plan Parent ID and ParentPlanVersion may form arbitrary longer cycles when modified by privileged SQL; the current create+preview checks must be supplemented by DB consistency protections/audit and reliable publish-time validation.

Keep these issues as acceptance requirements for the next PR, not silent implementation assumptions.
