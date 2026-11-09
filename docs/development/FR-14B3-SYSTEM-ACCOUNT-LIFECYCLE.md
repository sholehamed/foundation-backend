# FR-14B-3 — Versioned, audited System account lifecycle (backend)

Date: 2026-11-09. This PR implements new System-only account lifecycle APIs, not unsafe generic User CQRS writes.

## Routes and contracts

All routes belong to the existing SystemAdministrator Admins group, retain registered FR-14A feature-permission metadata, require HTTPS and the default-OFF Identity:PlatformAccounts:LifecycleEnabled flag.

| Method + Admins path | Permission | Request |
| --- | --- | --- |
| GET {id:guid}/lifecycle | ADMINS.READ | no body; narrow profile, status and current CAS version |
| PUT {id:guid}/profile | ADMINS.UPDATE | firstName, lastName, email, expectedVersion |
| PUT {id:guid}/state | ADMINS.CHANGE-STATE | isActive (explicit desired boolean), expectedVersion |
| PUT {id:guid}/lock | ADMINS.CHANGE-LOCK | isLockedOut (explicit desired boolean), expectedVersion |

Existing PUT /{id}, PATCH /{id}, PATCH /{id}/changeLock and DELETE /{id} and all System Users shared lifecycle operations remain 403. No old UserUpdateCommand or unscoped password/tenant overwrite is dispatched.

Responses: 200 with AccountView and next version; 400 bad profile/version; 403 disabled, HTTP, self, Tenant or reserved SuperAdmin target; 409 stale version or duplicate email. Responses are no-store. Strict JSON DTOs reject unknown fields, including password, username, TenantId, UserType, security stamp, role/grant data. UserName is intentionally immutable.

## Authority and CAS

- Every write rechecks live Master SuperAdmin actor, active Master Tenant, authorized write context and target inside the same SERIALIZABLE SQL transaction as the User update and audit. The client never provides TenantId.
- The target must have UserType.System and TenantId=null. Self-edits and direct or inherited (via GroupRole) SuperAdmin targets are always forbidden, even when their role is disabled. Reserved bootstrapping requires independent review.
- An opaque ConcurrencyStamp is required as expectedVersion. SQL UPDATE predicates on that exact version and assigns a new GUID version. Stale clients receive 409. Existing legacy System users with absent stamps require independently reviewed remediation before they are editable.
- Profile is only first/last/email; changed email resets EmailConfirmed. Rotating SecurityStamp invalidates old sessions; editing a pending user revokes its outstanding System temporary credentials/challenges.
- Suspension rotates SecurityStamp, revokes initial proofs and blocks normal login. Direct activation of a new never-proven pending identity is forbidden; reactivation requires an earlier temp_activated audit event from the reviewed System first-login protocol. Legacy deactivated accounts without that evidence need reviewed recovery/repair.
- Lock sets IsLockedOut=true **and future LockoutEndUtc**, because UserCredentialAuthenticator checks both. Unlock clears lock end and failed attempts. Both rotate SecurityStamp. Inactive accounts cannot use lock API to bypass first-login.
- Identical desired state and unchanged version are no-ops. No roles/groups/permissions/password hashes or UserType/TenantId are changed.

## SQL audit / migration

Migration 20261109070000_systemaccountlifecycle adds nullable changed-field names, prior/current active/locked flags, prior/current **concurrency versions** to SystemAccountLifecycleAuditEntries. It expands the existing audit Action CHECK for profile_updated, suspended, reactivated, locked, unlocked; prior create/first-login events remain valid. No PII, passwords or security stamps go in the audit. Existing append-only UPDATE/DELETE trigger remains; Down refuses data loss if any mutation events exist.

Every mutation and audit INSERT commit/rollback atomically. Real SQL tests cover CAS/stale requests, live SuperAdmin revocation, Tenant/privileged/self-target rejection, no password/role edits, stamp rotation, future lock expiry, suspend/recovery, immutable audit and injected audit failure rollback.

## Deferred deployment and UI

No Staging/Production migration, flag enablement, production role/permission Apply/grants or live account modification. Angular editors/browser acceptance, verified BFF System session invalidation under a real proxy, legacy System reactivation bootstrap, recovery, role management and preflight remain separately gated. This backend slice does not claim operational CRUD is enabled.
