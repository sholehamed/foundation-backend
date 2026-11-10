# FR-14C — System feature Permission must be centrally owned

Checkpoint: 2026-11-10. This is a targeted authorization hardening step before any dynamic Tenant navigation or optional System granular-authorization rollout. It does **not** activate either flag, apply registries, grant users, or deploy Staging.

## Observed drift path

Previously the System UI projection allowed a live permission whose `ResourceId` was **null**, even if the associated UI was owned by a System feature. A historical unowned `ROLES.READ` permission plus a valid Master grant could therefore make `system.roles` appear available without a centrally owned Resource. Separately, the System feature authorization handler validated the **code-owned descriptor** and the user's live grant by Permission.Code but did not recheck the persisted Permission/Resource/Module ownership when `EnforcePermissionEndpoints=true`.

The central catalog is supposed to be the exclusive authority for a System feature. A matching string code is not itself proof of origin, especially on environments with legacy rows/migration history.

## Fix / invariant

- `CurrentSystemUiAccessService`: a PAGE/ACTION is visible only when the exact registered Permission belongs to the same ResourceId as that UI, with exactly one row of that code, an active System Resource and Module, one expected UI link and a live Master grant. Null, foreign or duplicate Permission rows now fail closed.
- `SystemFeatureEndpointPermissionHandler`: when the default-OFF granular overlay is explicitly enabled, it first checks a **single bounded SQL query** to assert exactly one active persisted Permission with the code-owned System Resource and Module before it evaluates the live User/Role/Group grant. Null/moved/duplicate/inactive owners are rejected. The separate SystemAdministrator group boundary remains and the disabled-flag behaviour is unchanged.
- No user-supplied TenantId, RoleId, ResourceId or permission string is introduced into the endpoint path. These feature requirements are installed from code-owned metadata.
- The operator-only central Registry Preview and runtime endpoint integrity diagnostics remain independent. This fix cannot repair legacy data, and deliberately refuses to auto-adopt it.

## Acceptance

Unit regression includes a granted System user, correct registered ownership, null owner, moved-to-another-System-Resource owner, disabled resource, and a conflicting duplicate Permission.Code under a foreign Resource. The existing direct-deny, inactive-account, nested PAGE/ACTION, in-memory Registry and real SQL Feature Registry tests must remain green. Any operator encountering drift must explicitly reconcile the central definitions and grants before enabling permission enforcement.

## Outstanding central-access gates before dynamic menu pilot

- Review System Feature Registry operator Preview against live System API and UI graph, and Tenant Permission Catalog Preview + central Tenant Menu route readiness on the target environment; resolve conflicts without hidden writes.
- Review/secure the existing System Feature Registry Apply path's human authorization, preview freshness and rollout operation; preserve the default-OFF gate.
- Use real two-Tenant BFF browser admission, permission and revoke tests, then explicit operator approval to turn on the independently default-OFF Angular Tenant navigation pilot.
- Branch RBAC and subscription/entitlement bounds are separately scoped tasks; no real deployed Staging evidence exists yet.
