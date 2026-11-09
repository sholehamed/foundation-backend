# FR-13C-2B — Owner-controlled group membership (UserGroup)

**2026-11-09**. Companion to FR-13C-2A Tenant Group lifecycle. Staging deliberately deferred.

## Security contract

- **Read:** `GET /api/tenant/workspace/groups/{groupId}/members?page=1`. Tenant `GROUPS.READ` AND `MEMBERS.READ` required; 20 eligible active ordinary employees per page, deterministic ordering. No email, password, user token, Owner membership or foreign tenant rows in response.
- **Write:** `PUT /api/tenant/workspace/groups/{groupId}/members/{userId}` with `{assigned,expectedVersion}`, owner-only, authenticated, writable home Tenant, exact `TENANT.GROUPS.ASSIGN-MEMBERS` ACTION registered under the reviewed tenant workspace catalog and bound to its active UiResource. An Owner fallback **without registration** must not open the write.
- The caller supplies neither actor ID nor TenantId. Self assignment, Owner user, inactive/foreign/system user and foreign Group are forbidden. Newly assigning to an inactive Group is also forbidden; existing inactive memberships can be revoked.
- **Single-member mutation** rather than a full group-membership replacement: a page of 20 must never accidentally remove members from later pages.
- All writes use SQL Server serializable transaction and Group `RowVersion` CAS (covers Group state, metadata and all its member changes); even a different member's write invalidates stale versions. No-op is idempotent when the provided version is still current. The storage layer accepts `UserGroup` only inside the *exact group + user pair* temporary grant intent. Existing `GroupRole` and `UserPermission` remain denied to generic Tenant writes.
- Audit writes to `TenantGroupMembershipAuditEntries` in the same transaction, with actor, group, target user, grant/revoke, timestamp. Append-only SQL trigger prevents updates/deletes. Failed audit rolls back the relationship and Group version.
- Existing live Tenant permission evaluator already computes `UserGroup -> GroupRole -> RolePermission` (active, same-tenant links only), so adding/removing a member to a group with existing roles can change effective permissions immediately. No group role is created or updated by this feature; the separate FR-13C-2C role-link writer requires its own permission, target policy and independent audit.
- No SMTP, auth token storage changes, automatic seed grants, navigation bypass, shared Tenant reassignment, Staging/Production flags or Production migration rollout. Deployment requires separate operator-managed catalog Preview/Apply and schema migrations.

## Acceptance

Disposable SQL tests prove generic interceptor denial, scoped Owner write, inherited live read permission, stale CAS, Owner/foreign exclusion, immutable grant/revoke audit and rollback if audit unavailable. Real HTTP fixture asserts signed Owner/Staff/System/anonymous paths and member-by-member write/revoke with 403/400/409 behavior. Angular page must show paged members and be fail-closed on missing Owner capability; 403/409 must clear stale data and require reload. Existing BFF/role/first-login scenarios remain mandatory CI gates.

Next: **FR-13C-2C GroupRole assignment** with separate ACTION, audit, no Owner/System/actor roles and fresh effective-permission revocation tests.
