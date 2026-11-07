# FR-01B — Shared live permission evaluator and token claim compatibility

> Status: PR under CI review, 2026-11-07. This is a focused part of [FR-01 issue #39](https://github.com/sholehamed/foundation-backend/issues/39), **not** the completed dynamic navigation / Feature Registry system.

## Existing code / risk

- `SystemPermissionHandler` and `TenantPermissionHandler` duplicated live-DB checks for active scoped account/tenant, user direct deny, direct allow, scoped role grants and group-role grants. URL-derived `PermissionMiddleware` remains disabled.
- `Permission.Code` is the stable resource-action identifier the handlers check. `IdentityPrincipalFactory` previously emitted `Permission.Name` only. Direct `UserPermission.IsGranted=false` did not suppress the inherited **token claim**, even though live API handlers denied it.
- Current System users have `User.TenantId == null` and their grants are stored at the fixed Master Tenant ID; Tenant users must match their home tenant. Reusing a client-supplied roleId/tenantId would violate both rules.

## Implemented in this slice

1. Added `Web/Util/EffectivePermissionEvaluator` with a single live DB `HasPermissionAsync` decision shared by the two existing authorization requirement handlers. It requires authenticated account/scope validation by the caller, re-checks active user/tenant against DB, denies mismatched System/Tenant types, and honors direct user **deny before direct/role/group grant**. Every nested assignment is scoped to the same Master/Tenant ID. System/Tenant handlers retain their existing claim context checks and current stable key normalization.
2. Legacy `permission` access-token claims still contain `Permission.Name` for compatibility. Additional `permission_code` claims contain stable `Permission.Code`, and **both** claim projections now exclude direct user-denied codes. The destination policy includes `permission_code` in the access token only; it does not enter the ID token by default.
3. Claims of either kind are **informational snapshots**. They may be stale immediately after a role or account change. Do not use them as authorization. API policy handlers check the current database on every protected call.
4. Added isolated tests for direct deny precedence, direct grants, group grants, disabled entities, scope boundaries and direct System handler claim checks, as well as the token code/legacy claim and access-token destination contract.

## Not implemented / migration constraints

- No new EF migrations, production data mutations, auto-grants, new authorization feature flags, BFF enablement or Angular token storage changes.
- `TenantPermissionRestriction` semantics are not yet wired into this evaluator; investigate production data and restriction policy **before changing authorization behavior**. Branch/Own/Child authorization remains out of scope.
- This single-key evaluator will be reused as the decision semantic reference for a later **multi-key, current-user navigation** query; an efficient bulk permission projection is still required. Never call HasPermissionAsync in a loop across every menu without considering DB query cost.
- Existing `GetUserMenus?roleId=` and static Angular Sidebar fallback remain unchanged in this PR; deprecate only after an authorized current-user navigation endpoint and frontend route tests are green.
- Inspect downstream consumers that read legacy `permission` labels before removing that field in a versioned contract. `permission_code` is not a secure authorization source.
- The read-only database inventory from FR-01A has **not** been executed against a deployed production database. Authorization rollouts and data backfills require an approved, sanitized inventory and rollback plan.

## Verification / follow-up

- GitHub CI: build solution + Identity/BFF test suites + disposable SQL Server BFF regression suite. Mark results in the PR only after the latest-head run completes.
- Next FR-01C/FR-04: new current-user navigation from authenticated identity, all scoped active role/group memberships, explicit deny, proper category/ancestor filtering, recursive tree without cycles. No client-selected roleId and no privilege-bearing hardcoded menu fallback.
