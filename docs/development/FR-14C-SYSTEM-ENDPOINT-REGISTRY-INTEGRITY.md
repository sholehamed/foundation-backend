# FR-14C — System endpoint / API Resource / Permission integrity audit

Checkpoint: 2026-11-10. **System Admin alone defines** Menu, Page, Route, Resource, Action, Endpoint and global permission definitions. Tenant Owners only assign centrally permitted capabilities; no Tenant-authored menu or definition.

## Why another audit?

The existing Feature Registry Preview is an **additive synchronizer**, not proof that runtime System endpoints are consistent with the current SQL graph. It intentionally retains inactive/deprecated API resources and operator-owned records.

A new **read-only** SystemAdministrator + fresh persisted System SuperAdmin route, GET /api/System/FeatureRegistry/system-endpoints/integrity (configured API prefix may vary), inspects the live ASP.NET Core EndpointDataSource. It returns Cache-Control: no-store; there is **no Apply** companion.

Each FeatureEndpointMetadata descriptor is reconciled with code-owned FeatureRegistryDefinitions and persisted Module/Resource/Permission/ApiResource/PermissionApiResource:

- stable key and HTTP-method/route uniqueness;
- declared System resource + permission ownership and exact executable granular authorization requirement, plus SystemAdministrator boundary, no AllowAnonymous;
- active Module/Resource/Permission, correct Permission.ResourceId;
- exactly one active, non-deprecated, non-anonymous, Auto-owned API descriptor with identical key/method/route/resource;
- **exactly one** PermissionApiResource link to the expected permission, rejecting extra links that could broaden grants.

Unknown active persisted SYSTEM.* ApiResource keys and SystemAdministrator routes lacking feature metadata are reported as blocking conflicts. Three explicitly reviewed GET-only internal exceptions (UiAccess/me, UiAccess/me/{key}, anonymous Tenants/resolve) are counted separately and are never claimed to be Feature endpoints.

The report exposes discovered/ready counts, per-endpoint issue codes, unknown System API keys, uncovered admin routes, reviewed internal routes and out-of-scope route count. Readiness is **only for System feature endpoint graph integrity**. Authentication/OIDC, public, BFF, Tenant workspace and media are separate contracts; the static FR-14A inventory and live Tenant permission evaluator remain required. It does **not** prove that existing user grants, Tenant subscriptions or Branch scopes are correct.

## Test gates

- InMemory contract exercises empty registry, clean explicit System Registry Apply, inactive/stale API, foreign Permission.Resource ownership, extra API link, missing SystemAdministrator/granular requirement, AllowAnonymous, duplicate endpoint keys, untagged admin route, unknown stored System API, exact internal exceptions and denied diagnostic.
- The existing disposable SQL Server FeatureRegistrySqlServerTests group additionally verifies clean graph, permission-link injection, HTTP method mismatch, absence of user/role grants, no automatic writes and unauthorized access refusal.
- All existing BFF CI, Tenant IAM HTTP/SQL E2E, and FR-14A static inventory must remain green; merge only after latest-head checks.

## Operational activation and remaining work

This PR does NOT execute Registry Apply, add or remove grants, migrate data, modify System or Tenant menus, change Feature Flags or deploy Staging/Production. The System Permission enforcement flag remains OFF.

Before enabling granular permission enforcement: review the live Feature Registry Preview and this integrity report on the target host; reconcile missing/extra links via separately approved operator flow; inspect user/role grants; run Tenant Permission Catalog Preview and central Tenant Navigation readiness separately; verify BFF/Angular sessions and two-Tenant denial cases. Protocol and Branch authorization remain separate reviews, not certified by this report. Staging stays deferred.
