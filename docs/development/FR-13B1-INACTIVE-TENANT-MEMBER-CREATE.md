# FR-13B-1 — Owner-created, inactive Tenant member (NO invitation)

**Date:** 2026-11-09. Tracks [FR-13 #70](https://github.com/sholehamed/foundation-backend/issues/70) and [FR-13B-1 #72](https://github.com/sholehamed/foundation-backend/issues/72).

## Deliberately limited deliverable

This slice lets an authorized active **local Tenant Owner** record an employee who **cannot yet log in**. It does **not** issue an invitation, email, temporary password, activation link, auto role, group or other grant. A separate reviewed activation/invitation protocol is required before the end-to-end staff login journey is complete. Never mislabel a created inactive user as an invitation sent.

### HTTP + access contract

- New code-owned Tenant ACTION `TENANT.MEMBERS.CREATE` / UI key `tenant.workspace.members.create`, route ownership `/dashboard/members`. The existing SystemAdministrator-only Tenant catalog Preview and fingerprint-bound Apply must explicitly register it; the default catalog Apply flag remains OFF. Prior nine definitions become ten.
- `POST /api/tenant/workspace/members` body `{userName,email,firstName,lastName}`. Ignores no client-selected TenantId or UserId, IsActive, Owner flag, password, role IDs or grants: **none of those are present in the endpoint request model**. Responses include only `{id,userName,email,firstName,lastName,isActive:false}`, never a password, token or hash. `Cache-Control: no-store`.
- Requires `RequireAuthorization`, policy `tenant-member-create` rate limiter (10 requests/min per authenticated subject), valid active Tenant actor, writable *home* tenant, active local Owner, live `TENANT.MEMBERS.READ` and `TENANT.MEMBERS.CREATE` grants; explicitly active module, Resource, Permission and exactly one matching ACTION link. Direct deny overrides Owner. No new create capability if the catalog is absent/disabled.
- Invalid fields are 400; a duplicate per-Tenant normalized username/email or a 10,000-member cap returns 409; revoked/non-owner/System/foreign/no-write returns 403 (unauthenticated 401 at `RequireAuthorization`). New accounts cannot log in because `IsActive=false` is independently required by the existing `UserCredentialAuthenticator` and live principal/session validators.
- The new User gets a secure random, **unrevealed** password hash, a fresh SecurityStamp/ConcurrencyStamp, `EmailConfirmed=false`; no UserRole, group or direct permission is generated. Username accepts 3–100 ASCII letters/digits/._+-; FirstName/LastName 1–80 chars each, email 5–256 chars after validation. These rules are deliberately conservative for v1; the later invitation flow can review international usernames.

### SQL atomicity / audit

- Relational provider must be **SQL Server**, using SERIALIZABLE transaction. Re-evaluate live privilege and target inside the write transaction. Existing DB unique indexes `(TenantId,NormalizedUserName)` and `(TenantId,NormalizedEmail)` remain final duplicate protection.
- Migration `20261109010000_tenantmemberlifecycleaudit` adds `TenantMemberLifecycleAuditEntries` (TenantId, ActorUserId, TargetUserId, `create_inactive`, UTC). No credential or email in the append-only audit. SQL UPDATE/DELETE trigger rejects history mutation. Failure inserting audit rolls back the User creation.
- These are new approval-controlled migrations: commit or merge **does not run target SQL migrations**; production databases remain untouched. No automatic Tenant catalog Apply or grants.

## Verification scope

- Real disposable SQL Server tests exercise an Owner creating an inactive, unassigned member, strict password non-disclosure, duplicate/invalid denial, non-Owner & disabled ACTION refusal, immutable audit and User rollback on audit failure.
- Existing Identity/TestServer and FR-12F real HTTP tests continue; FR-12F expected manifest count rises from 9 to 10, with no new catalog grants.
- Angular companion FR-13B-1 must display **create inactive member** only when the server reports `canCreateMembers`, and never label it an actual invitation.
- Final real HTTPS owner/member-role/browser acceptance is **not** claimed here: inactive members intentionally cannot complete login.

## Known remaining and next stages

1. **FR-13B-2 invitation & activation:** cryptographically random single-use, short-expiry token stored/verified securely, delivery with confirmed email/channel, throttled anonymous redemption with antiforgery/origin rules, owner resend/revoke, password setup/credential lifecycle and SQL audit. No temporary password, shared token or invitation link in Angular storage/logs.
2. After activation, perform the actual Owner -> Staff -> role+permission -> filtered menus -> revoke -> 401 stale Cookie / 403 fresh session without permission, including SQL and real Chromium.
3. Remaining Tenant menu Registry and partial Group/Branch/Own/Child delegated policies; actual staging rollout steps from [FR-13A](FR-13A-STAGING-READINESS.md).
4. The existing User model lacks `IMustHaveTenant`; authorization and SQL audit in this writer must not be mistaken for generic EF write protection on every future User mutation. Review a comprehensive Tenant User mutation boundary before activation/account edits.

The FR-13A staging preflight is merged, but no real Staging hosts, SQL migrations, Registry Apply, production BFF or Angular flag changes are authorized by this development PR.
