# FR-13C-2C — Tenant GroupRole administration (2026-11-09)

**Environment:** code-only change. Staging intentionally deferred. Companion to FR-13C-2A Group lifecycle and FR-13C-2B Group member assignment.

## Security contract

- API: `GET /api/tenant/workspace/groups/{groupId}/roles?page=1` requires **GROUPS.READ + ROLES.READ**, returning 20 ordinary Tenant roles per page, including current group state, role activation, role assignment, Group `RowVersion`, and server-computed `canEdit`. Owner/System/foreign roles are excluded.
- Mutation: `PUT /api/tenant/workspace/groups/{groupId}/roles/{roleId}` with **only** `{assigned,expectedVersion}`. Requires authenticated local home-Tenant Owner, both PAGE permissions, and **new separately registered** `TENANT.GROUPS.ASSIGN-ROLES` ACTION under the reviewed active Tenant Permission/UiResource catalog. Not authorized by implicit Owner fallback when the ACTION is missing.
- The writer prohibits an actor whose UserGroup membership points to the target Group (including inactive Group), and prohibits any group containing an Owner member even if that Owner's role is inactive or inherited from legacy data. No self-elevation through GroupRole.
- Only ordinary, local Tenant `RoleScope.Tenant` roles with normalized name != OWNER are writable. New assignment of inactive role or to inactive group is forbidden; existing links can be revoked. The client cannot provide TenantId, ActorUserId or bulk role lists.
- Both authorization and target are re-checked inside an **IsolationLevel.Serializable** transaction. All group metadata, membership and role-link writes coordinate using the Group SQL `RowVersion` CAS. Stale write produces 409; stale no-op never bypasses CAS.
- TenantSaveChangesInterceptor rejects arbitrary GroupRole adds/deletes/updates. Only an exact `GroupId+RoleId` trusted Tenant intent permits add/revoke; mutation of a link's foreign keys is always denied. Direct UserPermission writes remain categorically denied.
- Every grant/revoke records actor, Tenant, GroupId, RoleId, RoleName and action in `TenantGroupRoleAuditEntries` **inside the same transaction**; append-only SQL trigger blocks update/delete. A failed audit insert rolls back both GroupRole and Group version.
- Existing live Tenant effective permission evaluator sees GroupRole changes immediately through UserGroup->GroupRole->RolePermission, even for an unchanged signed JWT; no separate token refresh is required for endpoint authorization.

## Acceptance

Disposable SQL: raw generic GroupRole writes denied, Owner+foreign/self/legacy Owner role checks, optimistic CAS/idempotence, inherited permission activation/revocation without new JWT, append-only audit and failed-audit rollback. Signed real HTTP: Owner 200, anonymous 401, System/ordinary staff/foreign 403, invalid version 400, stale version 409, Group+Role read PAGE gates and inheritance/revocation. Frontend: guarded `/dashboard/groups/:id/roles` route, 20-row page, single pair confirmation and 409 reload without automatic replay. No new sidebar menu key or implicit privileges.

## Release

Schema migration and explicit system catalog Preview/Apply require separate operator review. Neither merging nor CI authorization implies Staging/Production deployment. Next: consolidate read-only security audit across group members/roles and reconcile any legacy GroupRole assignments.
