# FR-14B-1 — fail-closed System account lifecycle containment

**2026-11-09 — Backend safety slice, not completion of FR-14B.**

## Finding

The shared legacy `UserCreateCommand` assigns a hard-coded TenantId instead
of representing a TenantId-null System identity; `UserUpdateCommand`
re-hashes a client-supplied password and maps an unrestricted update to the
tracked User. Both `/api/System/Users` and `/api/System/Admins` reached
these commands, alongside unscoped delete/state/lock toggles.

A coarse `SystemAdministrator` check and FR-14A's opt-in feature
permission requirement do **not** bind the target to the Master/System scope
or guard the SuperAdmin identity. Making those endpoints operational now would
risk cross-tenant edits and unreviewed credential/privilege changes.

## Immediate change

Both existing route families retain their SystemAdministrator group policy and
FR-14A feature descriptors. Their **five** legacy lifecycle write operations
(create, update, delete, change-state, change-lock) explicitly return HTTP 403
without accepting request-body commands or calling the legacy dispatcher.
Ten total mappings are locked. This is intentional fail-closed behavior, **not**
positive account CRUD.

The dedicated audited System UserRole/UserPermission grant APIs remain intact
and continue to perform live Master/actor/target, reserved SuperAdmin, self,
tenant ownership and transaction checks. System account read routes remain
as-is; read visibility is not equivalent to edit authority.

## Regression and acceptance

- `Fr14bLegacyAccountWriteLockdownTests`: real C# endpoint-method results
  assert fail-closed outcomes independently for Admins and Users.
- `scripts/security/test_fr14b_legacy_account_lockdown.py`: CI source
  invariant keeps each of the ten mapped handlers returning `Forbid` and
  rejects reintroducing any legacy command references. Mutation probes cover
  a re-enabled profile update and a reintroduced create dispatcher.
- Existing FR-14A audit remains responsible for SystemAdministrator/metadata,
  Identity public and Tenant authorization registration.
- All latest-head backend build, unit, disposable SQL and signed real HTTP
  integration jobs must pass **before** squash merge. This containment
  does not claim new positive lifecycle HTTP/browser coverage.

## Remaining FR-14B work — deliberately NOT enabled

1. Design a new System-only account create contract with server-forced
   `UserType.System`, `TenantId=null`, initial-credential rotation on first
   login, uniqueness enforcement, no client-assigned role/grant or active flag.
   A Tenant user must never be created by these endpoints.
2. Design an explicit profile PATCH/PUT that cannot accept or overwrite a
   PasswordHash/Password/SecurityStamp/TenantId/UserType/grants; separate
   credential reset with strong proof, sessions revoked via stamp rotation.
3. Add target-scoped, audited, transactional state/lock flows with clear
   desired state (not toggle); disallow self-edit and SuperAdmin bootstrap
   changes, require exact version/CAS, and preserve last-admin availability.
4. Implement live SQL and signed-HTTP positive/negative cross-tenant, self,
   SuperAdmin, stale version, concurrency, active-session and audit tests,
   followed by corresponding Angular editors and browser acceptance.
5. Only then consider the independent reviewed production Master catalog
   Preview/Apply/grant and granular permission cutover, still default OFF.

**Operational boundary:** no Staging/Production deployment, no SQL migration,
no registry Apply, no grants, no feature toggles, and no Angular write UI
activation in this slice.
