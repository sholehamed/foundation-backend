# FR-08A — System Administrator dashboard

2026-11-08. Query-backed operational overview, under the existing live SystemAdministrator policy. No grants, SQL migrations, BFF or tenant context changes.

## HTTP contract

GET /api/System/Dashboard/super-dashboard retains its original five fields and adds inactiveTenants, activeUsers, systemUsers, tenantUsers, totalGroups, totalModules and recentTenants (six newest by stored CreatedAt: id/name/slug/isActive/createdAt). Server response is Cache-Control: no-store.

Business tenant counts explicitly EXCLUDE the technical Master Tenant. Active and inactive are based on actual IsActive records. SystemUsers and TenantUsers are stored UserType counts, NOT tenant memberships, subscriptions, sales or billable seats. No invented revenue or activity time-series.

## Angular experience

The compatible frontend restores meaningful loading/error/retry/empty states, fixes the bug that showed totalUsers for totalRoles, renders useful KPI summaries and real newest tenant records, and provides existing role/tenant/group/module links under the optional existing UI permission controls. Does not create new privileged APIs, role grants or subscription features.

Existing login -> /system/dashboard remains unchanged. Backend independently requires live SystemAdministrator. Never let Tenant Admin use this endpoint.

## Tenant Admin handoff

Before this work, Angular /dashboard is one read-only workspace card and backend GET /api/tenant/workspace returns tenant/profile/mainBranch with a placeholder pending_activation subscriptionStatus; this is not a billing activation service. FR-08B will begin scoped Tenant Admin navigation and useful read-only screens separately. Tenant ownership is enforced on the backend, never by a client-provided tenantId.

Validation: System Dashboard unit tests for Master exclusion, current counts, sorted recent tenants and empty result; Angular UI states and API contract tests. Run latest-head CI prior to merge. Related tracker: https://github.com/sholehamed/foundation-backend/issues/44
