# FR-17A-2 — Typed Module/Resource/Permission and Operation Capability Contracts

**Status:** Implemented on PR #127; activation is **diagnostic-only**, not commercial entitlement enforcement. **Date:** 2026-11-10.

## 1. Purpose and decisions

The owner-defined Resource is the logical grouping of a Controller / Minimal API EndpointGroup, not a business entity or page. Each Resource belongs to one Module and owns **Permission.Code** operations, while ApiResource represents one method/route and UiResource represents PAGE/ACTION/FIELD/WIDGET. Plan licensing will decide tenant-wide eligibility to use a Module; tenant RBAC will independently decide which employee may use each of its permissions.

The next implementation cannot simply store a PlanId in a user role: buying a Module **must not automatically assign user permissions**, and a role grant must not make a non-purchased Module usable. System core and Tenant signup/administration/purchase/recovery must remain independently accessible.

## 2. Added production source contract (no DB schema)

Code file: `src/modules/Modules.System.Identity/Web/Util/ModuleResourceContracts.cs`.

- `ModuleProductClass` = `PlatformCore` / `TenantCoreAdministration` / `Commercial` / `InternalOnly`.
- `ResourceContract(ModuleCode, ResourceCode, ProductClass, PermissionCodes)` is the explicit source declaration for an owner/EndpointGroup-level Resource. ResourceCode is scoped to a Module; PermissionCode remains globally stable/unique under one Resource.
- `ModuleResourceContracts.Current()` derives **13 existing System code-owned Resource declarations** from `FeatureRegistryDefinitions.Features` and one existing `tenant-workspace/tenant-administration` Tenant core Resource from the 17 registered `TenantWorkspacePermissionCatalog` codes. This is **not** a new parallel hand-maintained list.
- `ModuleResourceContracts.Validate()` refuses duplicate Resource keys, duplicate or cross-Resource Permission codes, invalid code format, empty permission list, mixing classifications under one Module, or classifying the existing `system`/`tenant-workspace` owner as commercial. It does not change any existing assignments.
- `CapabilityContract(Code, ModuleCode, ResourceCode?, Kind, ValueKind, MergePolicy)` provides code-validated types for future plan-granted features/limits/quotas/modes. Allowed kinds: `Feature` / `CountLimit` / `PeriodQuota` / `AllowedModes` with Bool/Integer/ModeSet value contract. No database-defined code, SQL or policies.
- `ResourceOperationContract(ModuleCode, ResourceCode, PermissionCode, EndpointKey, RequiredCapabilities)` associates a **specific operation** with a set of typed capabilities, validated for correct Module/Resource/Permission owner. A Resource may have READ and CREATE operations but only CREATE consumes a quota when explicitly bound. These typed declarations do **not** yet add authorization middleware, a usage ledger or plan fulfillment.

The **existing System Endpoint Registry** read-only report `GET /api/System/FeatureRegistry/system-endpoints/integrity` now includes `resourceContractIssues` (additional response property) and refuses a “ready” result if the typed current source contract is malformed. It still validates runtime `EndpointDataSource` metadata against persisted `Module/Resource/Permission/ApiResource/PermissionApiResource` state, SystemAdministrator policy and granular requirement, as before. The source/classification check is **additional**, not replacement.

## 3. Concrete valid commercial scenario (contract only)

```csharp
var resource = new ResourceContract(
    "appointments", "bookings", ModuleProductClass.Commercial,
    ["APPOINTMENTS.READ", "APPOINTMENTS.CREATE"]);

var limit = new CapabilityContract(
    "appointments.monthly", "appointments", "bookings",
    CapabilityKind.PeriodQuota, CapabilityValueKind.Integer,
    CapabilityMergePolicy.Additive);

var read = new ResourceOperationContract(
    "appointments", "bookings", "APPOINTMENTS.READ",
    "APPOINTMENTS.BOOKINGS.READ", []);

var create = new ResourceOperationContract(
    "appointments", "bookings", "APPOINTMENTS.CREATE",
    "APPOINTMENTS.BOOKINGS.CREATE", ["appointments.monthly"]);

var issues = ModuleResourceContracts.Validate([resource], [limit], [read, create]);
// issues.Count == 0. This is validation, not a license check.
```

This example is NOT a shipped Appointments Module. No quota counting, ledger, entitlement, plan, order, payment, API gate, business workflow or token claim changed here.

## 4. Security and compatibility

- No EF migration, catalog Apply, Seed, grants, tenant saleability switch, billing access, or feature flag changes.
- No old Permission or Module identifiers were rehomed. `Menus.cs` + `MenuCategories.cs` still intentionally share the `menu-management` Resource. Tenant permissions still share `tenant-administration` and require separately reviewed split based on actual endpoint handlers.
- Scope remains enforced by existing principal-scoped System/Tenant authorization. Future commercial auth must AND subscription entitlement with effective Permission, with hard-deny precedence and no client-supplied TenantId.
- Current namespace policy is *conservative*: only `system` may be `PlatformCore` and only `tenant-workspace` may be `TenantCoreAdministration`. Adding other control-plane modules requires explicit, reviewed change to allowed ModuleClass boundaries.
- Enabling any Commercial Module is impossible through this diagnostic class: it has no entitlement evaluator or database write API.
- Public OIDC/BFF/Auth signup and FileStorage endpoint classifications remain in `fr17a_resource_inventory.py`; they are not silently converted to paid Resources.

## 5. Tests

`tests/Shinera.Identity.Tests/ModuleResourceContractsTests.cs` covers:
- 13 System resource + 17 Tenant admin code declarations and zero commercial assignments;
- duplicate Resource / cross-Resource Permission ownership rejection;
- prevention of classifying System/Tenant core as Commercial;
- one Resource with READ and CREATE, valid typed limit + quota + mode;
- wrong capability owner/Resource, wrong value kind, forbidden merge and duplicate endpoint key;
- `SystemEndpointRegistryIntegrityTests` asserts no typed issues for a healthy read-only runtime report.

Run:
```bash
dotnet test tests/Shinera.Identity.Tests/Shinera.Identity.Tests.csproj -c Release
dotnet test tests/Shinera.Identity.SqlServerTests/Shinera.Identity.SqlServerTests.csproj -c Release
python3 -m unittest discover -s scripts/security -p 'test_*.py' -v
```

The SQL integration tests require the already-documented **disposable** `SHINERA_SQLSERVER_TEST_CONNECTION` environment and must not point to production. Existing CI handles this. CI passing does not establish any deployed staging/production authorization.

## 6. Next slice: FR-17B

Implement an **additive typed persistence model**, preceded by ERD/constraint/migration review:
1. Code-owned capability definitions with Type/Unit/Scope/Period/MergeMode and optional Module/Resource owner, plus descriptive System-admin-controlled copy.
2. Immutable published PlanVersion, parent PlanVersion pinning, PlanVersionModules and typed PlanVersionCapabilityValues; db-owned dynamic PlanCard descriptions linked to vetted Module/Capability definitions; independent price offers.
3. Source-of-truth TenantSubscription with [StartUtc, EndUtc) validity, Add-on/approved override rules. Existing TenantModules cannot become an independent entitlement bypass.
4. A typed `EffectiveEntitlementService` in SHADOW/OFF mode with reproducible SQL tests for no-plan, expired, different Tenant, module missing, parent inheritance and malformed values. Preserve system control-plane and first-login Tenant Owner core access.
5. Only after tests and Staging review: add commercial entitlement checks to API/menus and reliable quota ledger enforcement. No automatic grants on checkout.

**Owner decisions still open:** D-01..D-10 in [architecture](../architecture/modular-entitlements-plans-limits.md#11-explicit-decisions-to-settle-before-codemigrations), especially per-Module vs per-feature purchase, downgrade dormant RolePermissions reactivation, quota period/rollover and offline settlement representation.
