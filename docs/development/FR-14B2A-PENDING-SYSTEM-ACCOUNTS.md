# FR-14B-2A — Pending System account creation (security-controlled slice)

Date: 2026-11-09.

## Motivation and scope

FR-14B-1 closed ten legacy account lifecycle writes because shared `UserCreateCommand` hard-codes a TenantId and unrestricted `UserUpdateCommand` rewrites PasswordHash. We cannot safely reopen those commands.

This slice introduces **one new endpoint implementation** for `POST /api/System/Admins`. It creates a **non-login, inactive** System record. It does **not** issue a temporary password, activate a user, grant a SuperAdmin role, or enable an Angular account-create UI. `/api/System/Users` create and both families' old update/delete/state/lock operations continue returning 403.

## Endpoint contract and defenses

- Existing live `SystemAdministrator` route authorization and FR-14A `WithFeatureEndpoint("SYSTEM.ADMINS.CREATE","admins","ADMINS.CREATE")` remain required. Writer separately validates the live active System actor, enabled Master Tenant and active Master SuperAdmin grant within the same SQL SERIALIZABLE transaction as creation.
- Opt-in gate: `Identity:PlatformAccounts:PendingCreationEnabled` (missing/default is **false**); requires HTTPS, SQL Server and per-actor `platform-account-create` rate limiting (5/min). Do not toggle until dedicated audit migration and operational preflight are explicitly approved.
- Allowed request JSON **only**: `{userName,email,firstName,lastName}`; strict unmapped-field rejection prevents silent `tenantId`, `password`, `isActive`, `userType`, `roleId`, or grants. Username 3–100 safe ASCII; valid nonempty email; first and last 1–80 characters.
- Server exclusively sets `UserType.System`, `TenantId=null`, `IsActive=false`, unconfirmed email, fresh security/concurrency stamps, and an **unrevealed** cryptographically random 48-byte secret hashed via Identity `IPasswordHasher<User>`. This is **not** a usable initial credential. No UserRoles/Permissions/Groups are created.
- 201 returns `{id,userName,email,firstName,lastName,isActive:false}` with `Cache-Control:no-store`; 400 validation, 403 missing HTTPS/feature/live authority and 409 duplicate System username/email. No credential, hash or stamp is returned.
- System username index is already unique for `TenantId=null`; the new opt-in migration adds a filtered **unique System normalized-email** index. It fails if existing duplicate System emails exist: operator-led cleanup is mandatory (no automatic migration/repair).
- SQL-only `SystemAccountLifecycleAuditEntries` stores `create_inactive`, actor, target and UTC time with an append-only UPDATE/DELETE trigger. It shares the account creation transaction: audit failure rolls back the User insert. This is append-only, **not tamper evident**; DB administrators can disable triggers.

## Acceptance

- Mutation-tested Python source security gate retains nine remaining legacy 403 operations and narrowly recognizes only the new Admins create writer.
- .NET tests enforce strict JSON unmapped-field rejection and preserve the existing Users/other Admins lockdown.
- Disposable SQL Server integration verifies System/null tenancy, inactive state, no grants, no leaked secret, one audit event, duplicate rejection, append-only trigger, audit-insertion rollback, Tenant principal rejection and live SuperAdmin revocation.
- CI must pass latest HEAD BFF solution/Identity/unit, SQL Integration and signed HTTP Tenant IAM before squash merge. No frontend changes needed: editor remains disabled.

## Next part: FR-14B-2B initial credentials

Implement separate reviewed, expiring System initial-secret issuance and single-use password-change challenge. It must never deliver an unrestricted JWT/Identity cookie or BFF session before password rotation, must throttle invalid attempts, revoke prior proofs, update SecurityStamp, activate only after success, and require subsequent normal login. Add HTTPS + anti-enumeration, SQL concurrency/replay/rollback and real browser acceptance. Do not adapt Tenant's slug-scoped credential table to System accounts without a separate model and audit.

After that: FR-14B-3 target-scoped, audited, version/CAS System profile, state and lock operations with self/SuperAdmin safeguards; positive Angular editor flows.

**Not done here:** Staging/Production deployment; migration execution outside disposable CI; Registry Preview/Apply or role grants; System granular permission flag enablement; activation, first-login, password reset or useful login for these pending accounts. All remain explicit future gates.
