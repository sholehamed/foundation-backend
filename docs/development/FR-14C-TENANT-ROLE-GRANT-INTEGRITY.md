# FR-14C — Quarantine foreign grants in Tenant role editor

Decision confirmed: only **System Admin** owns the Menu/Page/Resource/Action/Endpoint and global Permission definitions. Tenant Owner assigns **existing, permitted, centrally registered** Tenant workspace rights to local roles; Tenant never creates definitions.

## Issue

The Owner's `TenantRolePermissionGrantWriter.ReplaceAsync` already validates every *requested* code against the 17 System-defined Tenant permission entries and requires an approved active Tenant catalog with matching UI links. However it previously queried only matching known Permission IDs when building the existing grant set. A historic or manually inserted `RolePermission` for an unrelated System or inactive/foreign-owned Permission was **invisible** to that replacement: a valid Owner could submit an apparently complete matrix while the foreign grant silently survived.

This is not authorization to grant a System role; such a link may not confer actual rights because endpoint policies independently verify scope. But the Owner interface must not assert that it controls the full role when extra unreviewed links exist.

## Change

`TenantRolePermissionGrantWriter.CanEditAsync` now checks *all* existing local `RolePermission` links for the target role against active, correctly System-registered Tenant codes under the `tenant-workspace` module and `tenant-administration` resource. If any link is foreign, unregistered, inactive or orphaned, `CanEditAsync` returns false. The scoped GET role matrix still lists only the known Tenant entries and now returns `canEdit: false`. The writer's existing preflight and repeated in-transaction `CanEditAsync` check deny the PUT entirely, including a no-op. No unknown links are deleted or adopted; this requires separate System Admin investigation/reconciliation.

The existing protection remains: active home-Tenant Owner, `TENANT.ROLES.READ` plus `TENANT.ROLES.ASSIGN-PERMISSIONS`, writable Tenant scope, ordinary non-Owner target, actor not assigned to target, strict allowlisted request codes, SQL rowversion CAS, transaction-bound append-only audit.

## Verification

The existing disposable SQL Server `TenantRoleGrantSqlServerTests` suite now seeds an explicit foreign `SYSTEM.USERS.READ` permission linked to an ordinary Tenant role. It verifies read-only matrix, owner PUT denied without mutation/audit, and that only explicit operator test reconciliation removes the bad record. After removal, a canonical `TENANT.MEMBERS.READ` grant succeeds with a new role version and immutable audit. Existing role/grant and live HTTP E2E tests must remain green.

## Deployment and next steps

No new tables, no migrations, no Feature Flag changes, no data/grant modifications, no tenant-owned menus, and no Staging/Production deployment. Code-only hardening can accompany a reviewed application release, but a role with preexisting foreign grants may become uneditable until investigated, **by design**. Before enablement, perform an operator-owned, read-only cross-Tenant role/grant inventory and approve remediation for flagged roles. Do not auto-delete legacy permissions.

Next: verify the full System Admin-owned Registry and canonical Tenant routes, review the Tenant grant/entitlement policy and cross-Tenant tests, then consider a controlled browser rollout of the existing read-only dynamic central Menu. FR-14C-3 branch membership and scoped authorization remain separate.
