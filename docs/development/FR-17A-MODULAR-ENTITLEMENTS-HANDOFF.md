# FR-17A — Modular Resource/Permission Inventory and Entitlement Contract Handoff

**Status:** Design/documentation proposed in branch only; **zero runtime code, SQL migrations, flags, grants, deployment or payment integration changed**. Date: 2026-11-10. Primary architecture: [Modular Entitlements, Plans, Limits & Workflow Extensions](../architecture/modular-entitlements-plans-limits.md).

## 1. Why this is the first step

We now know the intended Resource grain: **approximately one Controller / Minimal API EndpointGroup**, whose operations have Permission definitions and individually registered API endpoints; UI elements are linked separately. Resources roll up into Modules, Modules and typed capabilities are licensed by published PlanVersions, and Tenant Owners only delegate allowed permissions. Until Resource grouping/identity and control-plane exceptions are reviewed, adding Billing tables risks licensing System Admin APIs or breaking first-login Tenant Owner.

**Goal of FR-17A:** produce a tested, source-and-SQL-grounded mapping and compile-time metadata contract without changing effective permissions. No checkout/entitlement gate in this first slice.

## 2. Exact inspected baseline

| Existing file/behavior | Observation | Gap/action |
| --- | --- | --- |
| `Domain/Entities/Module.cs` | Code, Title, Description, sort, active | Distinguish platform/core vs sellable product Module classification; preserve Code |
| `Domain/Entities/Resource.cs` | ModuleId, Code, Title, sort/active; uniqueness (ModuleId, Code) | Document one EndpointGroup grouping and exceptions; no page-based duplication |
| `Domain/Entities/Permission.cs` | Code, nullable ResourceId, links to API/UI | Code uniqueness and nullable legacy rows require reviewed inventory; Permission is under Resource |
| `Domain/Entities/ApiResource.cs` | ResourceId, stable Key, route, HTTP method, Source; unique key and method/route | Endpoint-to-Resource metadata parity, not route-key-derived identity |
| `Domain/Entities/UiResource.cs` | ResourceId, Key, Type PAGE/ACTION/FIELD/WIDGET | UI elements can relate to a group even across multiple pages, not security by themselves |
| `Domain/Entities/RolePermission.cs` / `UserPermission.cs` | Scope via TenantId, direct deny support | Existing grants remain; entitlement is an extra gate, not a replacement grant |
| `Domain/Entities/TenantModule.cs` | IsEnabled, IsTrial, CustomExpiryDate, TenantId, ModuleId | Historical storage, not purchasable subscription/entitlement source |
| `Domain/Entities/WorkspaceEntities.cs` | RequestedPlanKey/BillingCycle/PromoCode only | “Selected plan” is intent; there is no checkout activation |
| `Web/Endpoints/PublicSignup.cs` | Owner gets 17 central Tenant admin RolePermissions on new Tenant | Must preserve core administration without confusing it with paid licensing |
| `Web/Util/FeatureRegistryDefinitions.cs` | 13 System features with resource codes; metadata on endpoints | Map each to exact live EndpointGroup; preserve navigation and System privileges |
| `Web/Endpoints/TenantWorkspacePermissionCatalog.cs` | 17 Tenant PAGE/ACTION definitions | Single tenant-administration Resource currently spans several Tenant features |
| `Web/Util/EffectivePermissionEvaluator.cs` | Live role/group/direct grant + deny | No subscription, Module, typed Limit check yet |
| `Infrastructure/Persistence/InitialAccessBootstrap.cs` | Fresh-DB System/Tenant menu and permission seed | No automatic historical DB reconciliation or commercial entitlements |

Paths above are relative to `src/modules/Modules.System.Identity/` unless noted.

## 3. Mandatory signed-off inventory before touching schema

For every routed Minimal API EndpointGroup and any MVC Controller:
- Identify `EndpointGroup/Controller name`, current `Module.Code`, `Resource.Code`, actual `FeatureEndpointMetadata`, endpoint keys, method + route templates, required permission codes, auth policy, current code-owned/special/anonymous classification, test coverage.
- Enumerate matching DB rows in `Modules/Resources/Permissions/ApiResources/PermissionApiResources/UiResources/PermissionUiResources/Menus`; find null owners, duplicate keys, inactive/foreign owners, misrouted or orphan links. Reuse `docs/sql/fr-01-identity-catalog-inventory.sql` and `/api/System/FeatureRegistry/central-access/readiness` (read-only; System only) as inputs. A clean disposable DB does not establish existing-target readiness.
- Classify each Module: `PlatformCore`, `TenantCoreAdministration`, `Commercial` or `InternalOnly`. **These are proposed classifications; do not reinterpret existing Code or role grants as classifications without a reviewed migration.**
- Evaluate whether tenant-administration should split into Members/Roles/Groups/Branches resources, or whether it is a deliberate one-group façade. Present explicit old→new Module/Resource/Permission graph and affected handlers. Never automatically move `TENANT.*` permissions to new Resource owners while the active handlers validate the old owner.
- Inventory consumers of `TenantModules`, `RequestedPlanKey` and tenant-admin RolePermission editors; record whether any existing current behavior assumes entitlement from `IsEnabled`.

Required output artifact (no assumptions): `docs/development/FR-17A-RESOURCE-INVENTORY.md` with a reviewable one-row-per-EndpointGroup/Controller mapping and a gap table. If the repo or database cannot be introspected, mark each missing source/evidence “not verified.”

**Initial source evidence:** [FR-17A Resource/EndpointGroup inventory](FR-17A-RESOURCE-INVENTORY.md) now documents the 13 current System resources, real endpoint file groupings, shared Menus/MenuCategories resource and Tenant/PublicSignup exceptions. This completes only the *first source mapping pass*; exhaustive routes and target SQL are still pending.

## 4. Metadata contract proposal and guardrails

Proposed typed C# concepts (example *not implemented*):

```csharp
public enum ModuleClass { PlatformCore, TenantCoreAdministration, Commercial, InternalOnly }
public sealed record ResourceDefinition(
    string ModuleCode,
    string ResourceCode,
    string EndpointGroupKey,
    ModuleClass Class,
    IReadOnlyList<PermissionDefinition> Permissions);
public sealed record PermissionDefinition(
    string Code, string Operation, string? Description);

public sealed record CapabilityDefinition(
    string Code, string ModuleCode, CapabilityKind Kind,
    string Unit, CapabilityMergePolicy MergePolicy);
```

- Module and Resource keys are globally stable (Resource scoped by Module); a RouteTemplate/Controller class rename must not rotate Permission.Code or Resource.Code. EndpointGroupKey must be stable despite C# class rename if linked to persistent DB definitions.
- A Resource may have multiple endpoint routes and HTTP verbs. Each endpoint declares an explicit stable API key and required Permission; some public/auth endpoints are separately classified rather than invented as purchase-controlled resources.
- UI metadata is explicitly linked through PermissionUiResources. Source catalogs are code-owned, System Admin is only author/editor of centralized definitions, and Tenant administrators cannot define arbitrary new Permission/Feature keys.
- Plan/capability definitions are **not** user grants. Never introduce implicit `RolePermission` writes in registry sync.
- CI fails closed on duplicate keys, missing required metadata, unknown Permission owner, cross-System/Tenant references, duplicate route registration and catalog drift; don't let default grants mask integrity gaps.

### Resource-operation limit mappings must be inventoried

Inventory the exact operation requirements alongside every EndpointGroup/Resource/Permission mapping: a code-owned contract `(ModuleCode, ResourceCode, PermissionCode, EndpointKey?) -> RequiredFeatures + CountLimit/PeriodQuota + AllowedModes`. A capability can be Module-wide or Resource/operation-specific. The shared engine coordinates an authoritative domain count adapter or consumption ledger; System Admin can edit approved typed values in Plans but cannot author arbitrary executable policies.

Acceptance: for two actions in the same Resource, an entitled/authorized READ never spends the quota of CREATE; CREATE fails at the limit under concurrent requests; failed operations do not consume quota; expired module entitlement denies both appropriately without deleting RBAC grants.

**FR-17A-2 typed code contract:** [implementation handoff and tests](FR-17A2-TYPED-RESOURCE-CONTRACTS.md). The current System 13 and Tenant administrative 17 definitions now produce a single typed, class-labelled Module/Resource/Permission projection, with prospective Capability and specific operation bindings. This is diagnostic-only, not a purchased-plan access gate.

## 5. Next phases and dependencies

| Milestone | Implementation slice | Gate / output |
| --- | --- | --- |
| **FR-17A-1** | EndpointGroup/Resource inventory, current SQL map, plan/entitlement exceptions, docs | Signed mapping; no change to SQL/API authorization |
| **FR-17A-2** | Typed Module/Resource/Permission metadata + read-only parity diagnostics | Unit and SQL tests for stable keys, collision/owner drift; no runtime cutover |
| **FR-17B** | Typed CapabilityDefinition catalog and entitlement schema with migration plan, versioned Plan/PlanVersion/display/pricing | Review ERD, constraint matrices, hierarchy cycles, trial/price rules; migrations additive only |
| **FR-17C** | Subscription source-of-truth and typed entitlement resolver, flag OFF/shadow validation | Current code unaffected while disabled; expired/no-plan/foreign-tenant deny in isolated tests |
| **FR-17D** | Combine entitlement with scoped Permission and dynamic menus/UiAccess; preserve core access | Auth tests positive & adversarial; no automatic Tenant role grants |
| **FR-17E** | Quota ledger and workflow extension contracts (synchronous policies, Outbox, required saga) | Concurrent quota and retry/idempotency tests; no duplicate workflow implementation |
| **FR-18** | System Admin plan/editor, parent/version/catalog display + Angular dynamic public PlanCards | Public cards only from published DB content; version-pinned inheritance |
| **FR-19** | Order and Checkout, discount/redemption, zero-cost and external settlement | Coupon race, free vs externally settled audit; no invoice tampering |
| **FR-20** | Payment gateway adapter + renewal/downgrade/upgrade accounting | Verified callbacks, replay handling, refund and expiration behavior |
| **FR-21** | Full UI/BFF/Staging rollout | Two Tenant browser E2E, checkout→entitlement→role→API→UI, operator sign-off |

**Numbering is a proposed working sequence**: coordinate with existing FR roadmap and open issues before assigning parallel implementation tickets. Never treat a document-only FR-17A PR as completed FR-17A-2 or Billing.

## 6. Definition of Done per implementation PR

1. Updated central architecture/this handoff + local module README with explicit as-built behavior and outstanding gaps.
2. Reviewed ERD or code catalog definitions and stable keys; for DB changes include additive EF migration, reversible/restore plan and read-only preflight for existing data.
3. Unit + disposable SQL Server integration + signed HTTP security tests for relevant scope; frontend route/card tests where applicable; cross-Tenant negative tests and role+module conjunction.
4. No silent elevated grants, default commercial subscriptions, seed resets, no auto-opening of FeatureFlags, no startup auto-backfill on existing DB.
5. PR CI green; check actual head/main workflow run conclusions. Real staging remains a separate operator gate, not inferred from GitHub Actions ephemeral Chromium.
6. Any nontrivial permission or billing behavior changed only after review of the System/Tenant admin distinction and production migration plan.

## 7. Test scenarios to keep as permanent acceptance fixtures

**A — new first login:** clean DB System Admin sees full System menu; signup Owner gets six core admin routes and 17 core tenant-admin rights; Tenant has no paid subscription and cannot call a commercial endpoint.

**B — commercial permission:** Tenant A owns Appointment module through valid subscription, Tenant B does not; Tenant A Owner grants Appointments.Create to Employee. Allowed only for that employee and that Tenant while module entitlement and all resource/permission active. Tenant B's copied role/user/URL is denied. An owner with no explicit commercial resource user grant remains denied.

**C — downgrade:** Tenant A loses Appointments module and gets endpoint 403 + menu hidden; existing role links/data remain unchanged. Restore policy reviewed before reactivating them. Billing management remains reachable.

**D — count limit:** staff.active.max = 1; two concurrent requests to activate two employees: exactly one succeeds. A deleted/deactivated employee frees capacity only if the declared count semantics permit.

**E — periodic quota:** sms.sent.monthly = 1; repeat same delivery id causes one usage, not two; concurrent different delivery ids cannot overconsume; midnight/period boundary consistent and retry-safe.

**F — optional workflow:** no SMS module → appointment still confirmed, no notification. SMS enabled → Outbox delivers at least once with idempotent handler. Deposit required → PendingPayment until independently verified payment, no early confirmed state.

**G — plan inheritance:** Plan Y pins published Plan X v1 and adds Analytics module plus overrides active-staff limit; publishing X v2 must not alter Y v1 or existing subscriptions. Cycles and invalid capability ownership fail publication.

**H — checkout:** one-use 100%-discount code applied twice concurrently can settle exactly one zero-amount Order; no gateway required, one Subscription fulfillment. Other request gets deterministic conflict. Externally settled sale records actual confirmed settlement independently of coupon amount.

**I — read-only safety:** no-plan Tenant can still read their subscription state/start checkout; inactive catalog or feature never auto-reactivated; old `TenantModules` row cannot grant access on its own.

## 8. Proposed API surface (planning only)

- System Admin: `GET /api/system/modules/registry/preview`; `GET/POST /api/system/plans`; draft version and immutable Publish; typed capability catalog; `POST .../discount-codes`; audited manual-settlement review.
- Public: `GET /api/public/plans`, `GET /api/public/plans/{code}` returns only published price/display/feature data; no admin metadata or unlisted inactive offers.
- Tenant: `GET /api/tenant/billing/subscription/me`, `POST /api/tenant/billing/orders`, `POST /api/tenant/billing/orders/{id}/apply-discount`, checkout/confirm, `GET /api/tenant/entitlements/me`; all tenant-bound via trusted context and CSRF-safe BFF.
- Internal: `IEffectiveEntitlementService`, `IQuotaService`, `IWorkflowPolicyResolver`, event Outbox + verified billing fulfillment handler.
- Contracts/method names and API routes are **not implemented**, subject to namespace and existing endpoint inspection.

## 9. Immediate handoff and first PR after this documentation PR

**Do first:** FR-17A-1 code/SQL inventory and resource-key mapping. The next contributor must not start by adding `Plans`, `Subscription` or `CapabilityDefinitions` migrations, and must not split the Tenant `tenant-administration` Resource until all references/tests are documented.

**Owner review needed:** D-01 to D-10 in the [architecture decisions table](../architecture/modular-entitlements-plans-limits.md#11-explicit-decisions-to-settle-before-codemigrations). Prioritize which Module classes are saleable and what Owner can do while unpaid. Assumptions remain visibly provisional; do not treat them as an implemented specification.

**Cross-repo follow-up:** frontend must eventually adopt server-published plan display DTOs (not duplicate price/module arrays) and use effective entitlement/UI-access projection for visibility; System Admin remains the only writer of definition/catalog records.
