# FR-14C — Central Tenant navigation readiness (read-only)

Checkpoint: 2026-11-10. System Admin defines every Menu, Category, Page, Route, Resource, Action, Endpoint and Permission. Tenant Owner only assigns allowed, centrally defined capabilities to local roles and users; **no Tenant menu authoring** is introduced.

## What was missing

The existing global System Feature Registry has a safe Preview/Apply for the 13 System pages, and Tenant Permission Catalog Preview/Apply manages 17 registered PAGE/ACTION definitions. Neither automatically creates the six Tenant dashboard Menu links in the **single global Menu/MenuCategory catalog**. The Tenant dynamic navigation reader is read-only and only projects those centrally authored records. Switching it ON prematurely may produce no links or inconsistent visibility.

## Read-only operator endpoint

`GET /api/System/FeatureRegistry/tenant-navigation/readiness` (EndpointGroupBase prefix conventions apply) uses the same SystemAdministrator group policy as the Feature Registry **plus a live System SuperAdmin DB role check**, and returns `Cache-Control: no-store`. It calls `CentralTenantNavigationReadiness.ReadAsync` and has **no write or Apply companion**.

The report contains `readyForDynamicPilot`, required/ready counts, one `ready|missing|conflict` state and named issue codes per canonical route, extra unregistered `/dashboard/*` Menu routes and a count of ungated external links.

Canonical System Admin Menu routes are:

| Menu Route | Live Tenant PAGE policy |
|---|---|
| `/dashboard` | baseline, no explicit Menu.PermissionId |
| `/dashboard/business` | baseline, no explicit Menu.PermissionId |
| `/dashboard/branches` | baseline, no explicit Menu.PermissionId |
| `/dashboard/members` | `TENANT.MEMBERS.READ` |
| `/dashboard/roles` | `TENANT.ROLES.READ` |
| `/dashboard/groups` | `TENANT.GROUPS.READ` |

For privileged pages the report requires the exact Menu.PermissionId, one active Permission with that code, live ownership by the central Tenant Workspace Module/Resource, one active PAGE `UiResource` at the canonical route and a unique PermissionUiResource link. For every route it checks exactly one Menu entry with canonical case, active/unhidden status, a live global Category, safe acyclic same-category ancestor chain (max depth 32), and no unexpected external URL. Baseline routes must not silently inherit unrelated privileges. It fails readiness on any extra unrecognized live `/dashboard/*` route or on a visible ungated external URL in the shared catalog.

A non-ready result is an **operator diagnostic**, not an authorization decision or permission grant. The endpoint never claims coverage of every nested route, action, API endpoint or Subscription/Plan entitlement; System Feature Registry `preview` and Tenant Permission Catalog `tenant-permissions/preview` are still separate required reviews.

## Testing and activation

The SQL Server test is part of the existing required `FeatureRegistrySqlServerTests` CI filter: no seed on empty audit, missing menus before any explicit System Admin registration, clean six-route populated catalog after Tenant Catalog Apply, inactive PAGE, mismatched Menu permission, duplicate route, unregistered internal route, ungated external URL and malformed ancestor/category. A unit test locks the six exact route/PAGE pairs and confirms no System route. The new endpoint rejects a non-System user.

No SQL migration, automatic menu generation, catalog Apply, permission grant, feature flag changes, Staging/Production deployment or Tenant data changes in this PR. The existing `Identity:FeatureRegistry:EnableApply`, `Identity:TenantPermissions:EnableCatalogApply` and Angular `tenantDynamicNavigationPilotEnabled` remain default OFF.

**Operational gate:** System Admin reviews the two other Preview reports, explicitly approves catalog Apply in a controlled environment if required, manually registers any missing Tenant Menu records in the global System Menu editor, verifies readiness and live two-Tenant browser/BFF access, then separately approves the dynamic navigation pilot. Staging remains deferred until requested.
