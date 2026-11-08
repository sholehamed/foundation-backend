# FR-08B — Tenant workspace read-only foundation

2026-11-08. First Tenant Admin surface after the System dashboard milestone. This is a **tenant-scoped read-only workspace**, not a fully built Tenant RBAC or a billing/subscription system.

## Existing and new HTTP endpoints

GET /api/tenant/workspace remains authenticated and tenant-context restricted. It includes the existing tenant, onboarding profile, mainBranch and placeholder subscriptionStatus=pending_activation, and now returns branchesCount based on database rows belonging to the *active tenant*.

GET /api/tenant/workspace/branches is added under RequireAuthorization(). The endpoint accepts **no tenantId** and only reads Branch records whose TenantId equals current ITenantContext.ActiveTenantId. It requires tenant context, ActiveTenantId in ReadableTenantIds and an active target Tenant. System context, unreadable tenant and absent/inactive tenant fail closed. Response: list of {id,name,city,address,phone,isMain}, main branch first, followed by branch name; Cache-Control: no-store.

These endpoints are not SystemAdministrator functions. Do not reuse system /Dashboard/super-dashboard across tenant sessions. Tenant Admin vs tenant employee role distinctions have not yet been introduced; these are read-only views for authenticated owning-tenant sessions (future Tenant-specific permission policy required before write functionality).

## Angular companion

The Tenant shell keeps existing /dashboard after login, adding read-only /dashboard/business (onboarding/business information) and /dashboard/branches (owned branch list). Navigation is tenant-scoped, nested beneath tenantSessionGuard. No option allows arbitrary TenantId selection, editing another tenant, or enabling paid subscriptions. The UI must display requestedPlanKey as an *unconfirmed request*, never an active plan.

## Acceptance

- Existing tenant after-login entry /dashboard is preserved.
- Branches belong only to current tenant, order main first, no arbitrary tenant ID on client HTTP.
- System/empty/foreign contexts return forbidden or not found; no cross-tenant information.
- Empty business profile/branches, failed reads, logout and retry supported.
- No DDL, grants, BFF mode, Identity role/tenant permissions or financial contracts changed.

Further Tenant Admin: review scoped users, roles, groups, permission checks and branch operations before implementing mutating APIs. See issue #44.
