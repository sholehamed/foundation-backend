# FR-14B-2B — System initial password and one-time forced change

Development checkpoint: 2026-11-09. This is a System-specific protocol, separate from Tenant staff onboarding; System identities have TenantId=null and no Tenant slug.

## Reviewed flow

1. An active, live Master SuperAdmin calls POST /api/System/Admins/{id}/temporary-password. Requires existing SystemAdministrator policy, code-owned ADMINS.ISSUE-TEMP-PASSWORD endpoint metadata, HTTPS, Identity:PlatformAccounts:FirstLoginEnabled (default OFF), live Master grant, inactive non-self System target and zero UserRole/UserGroup/UserPermission associations. Maximum five issuance requests per actor per minute. Generates one 4x6 random secret, reveals it once with no-store response and persists only its hashed form. Expiry 60 minutes; five failed password attempts maximum.
2. The ordinary System Login form can call POST /api/System/Auth/system-first-login/resolve with userName and temporaryPassword for *only* the generated credential format. Endpoint is rate-limited five per IP per minute and restricted to HTTPS on the exact configured root workspace domain, never Tenant or Identity hosts. Missing/invalid/expired input returns continue_login, without an account-existence oracle; normal passwords use the unchanged token login.
3. Valid one-time secret produces a 256-bit random challenge with 10-minute expiry, stored only as SHA-256 hash; previous challenges are revoked. It authorizes only POST /api/System/Auth/system-first-login/complete with challenge and newPassword, not OAuth token/cookie grants.
4. SQL SERIALIZABLE completion verifies unused challenge, unused/unexpired secret, inactive/no-grant System account and active Master. It rejects reuse of temporary password, username-containing password and common values. It hashes the permanent password, activates account, rotates SecurityStamp and ConcurrencyStamp, clears lockout, atomically consumes one-use rows and audits activation. Response is passwordChanged=true and requiresRelogin=true; **no Identity cookie, OAuth token or BFF session is issued**. Fresh ordinary login is required.
5. Issuance, proof verification and activation use append-only SystemAccountLifecycleAuditEntries with transaction-bound actor/target/action/time; if an audit insert fails all changes roll back. Verified proof-holder is recorded as actor of verification/activation without pretending to have an ordinary login session.

## Storage and rollout

- Migration 20261109060000_systemfirstlogin introduces SystemAccountTemporaryCredentials, SystemAccountFirstLoginChallenges and expands existing append-only audit CHECK; a guarded rollback prevents destroying tables if permanent first-login audit records exist.
- Strict separation from TenantMemberTemporaryCredentials. Random challenges and initial credentials are never persisted in plaintext. All mutating steps use SQL transactions, locks and conditional consume to make replay fail.
- The newly active System user has no automatic grants. SuperAdmin bootstrap/rotation is separate from regular platform role assignment; first-login is NOT a way to gain privileged admin access.
- System Users legacy create and other Users/Admins unsafe state/edit/delete/lock operations remain 403. Existing OAuth/BFF token flows are not widened. BFF Identity-host System first login is a separate future work item.

## Required acceptance

- Real SQL tests: actor and target scope, invalid/unknown secret, fresh challenge replacing old, tampered token, immutable grants, inactive -> active only on correct password, replay rejection, audit event count and injected audit failure atomic rollback.
- Identity unit: feature OFF, HTTPS and exact System host, strict proof-request JSON; CI static permission/route inventory; BFF unit and signed Tenant HTTP/SQL regression.
- Companion frontend: default-OFF System first-login flag, memory-only proof, same-origin fetch without cookies, password form in existing System Login, fresh normal login after success.

**Not activated or deployed:** feature flags, migration outside disposable CI, Registry Apply, catalog/grants, Staging, Production, actual deployed browser/proxy verification, identity-host BFF cutover, System profile/state/lock CRUD, recovery/reset, operator secret delivery policy. These remain mandatory independent gates.
