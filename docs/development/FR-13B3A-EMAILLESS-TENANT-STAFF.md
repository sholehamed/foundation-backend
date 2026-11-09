# FR-13B-3A — Tenant staff with no email (2026-11-09)

Tracking: [Issue #78](https://github.com/sholehamed/foundation-backend/issues/78), parent [FR-13 #70](https://github.com/sholehamed/foundation-backend/issues/70). Complements [FR-13B-3B](FR-13B3B-UNIFIED-TENANT-LOGIN.md) and [FR-13B-3C](FR-13B3C-BFF-IDENTITY-FORCED-CHANGE.md).

## Model and SQL

- The existing `User.Email` and `User.NormalizedEmail` properties are now nullable at the EF/database level. **Only local Tenant staff creation** uses this optionality. Other API contracts and validators for System users, public Tenant Owner signup and existing account management **still require real email**.
- Migration `20261109040000_tenantstaffoptionalemail` alters the two SQL Server columns to NULL and replaces `IX_Users_TenantId_NormalizedEmail` with a **unique filtered index** on `TenantId,NormalizedEmail` applying only when both `TenantId IS NOT NULL` **and** `NormalizedEmail IS NOT NULL`. Thus two or more members without emails are legal while case-normalized real emails remain unique within each Tenant. The existing normalized UserName index remains untouched.
- The EF model snapshot is updated alongside UserConfigurations, so drift tests will detect later accidental changes.
- Migration Down explicitly **THROWs** if even one email-less user exists. It will not generate fictitious values or silently delete accounts; to restore NOT NULL the operator must first establish genuine email addresses. Never run an unreviewed reverse migration on Production.
- `User.EmailConfirmed` remains **false** for staff created without an email. Successfully changing a first-login password must not change it to true. No fake/placeholder domains or inferred recipient addresses.

## Endpoints

- The previously reviewed Owner-only `POST /api/tenant/workspace/members` and `POST /api/tenant/workspace/members/with-initial-password` both accept `email:null`, omitted, or whitespace-only. The server trims/validates any **nonempty** supplied email and saves `null` + `NormalizedEmail=null` for missing. The response DTO and `GET .../members` directory return `email:null` honestly.
- Permission gates **unchanged**: local writable home Tenant, live Owner + Members.Read/Create and, for one-step onboarding, separate registered/granted `TENANT.MEMBERS.ISSUE-TEMP-PASSWORD`; all rechecked inside serializable audited SQL transaction. No default roles, invitation, BFF session or ordinary login for inactive staff.
- When email is missing, `POST .../members/{id}/invitations` **rejects target (404)** before any token is created or email sent. Revalidation occurs both before and **inside** the serializable SQL write. The TLS sender is never called. Existing active/confirmed/foreign/system users remain ineligible.
- `POST .../members/{id}/temporary-password` and the atomic create-with-initial-password path continue to allow valid email-less staff. They issue a 60-minute unique initial secret, shown once only to the local Owner. The employee uses the normal Login and must set a permanent password via the restricted 10-minute challenge, then **log in again**. Both direct Tenant and Identity-host BFF login protections are unchanged.
- If an employee later needs the email-invitation/recovery route, they must first provide an actual email through a separately authorized, validated profile-management process; this PR **does not silently create** an email update endpoint.

## Verification

- SQL Server disposable database tests: two employees without email, 1-step temporary credential activation and no automatic role/EmailConfirmed, duplicate real email rejected case-insensitively, index filter checked against SQL Server metadata, email invitation fails with zero token/audit/send, malformed nonempty email rejected. Existing rollback/anti-enumeration tests continue to run.
- Angular tests: optional form validity, `email:null` DTO in pending/atomic creation, one-time secret cleared, hide and refuse email invite without recipient, existing email invite preserved.
- CI validates DB migration snapshot equivalence to the EF runtime model; release requires an isolated SQL Server migration on Staging and a browser-level multi-user flow.
- Default-OFF `Identity:TenantFirstLogin:Enabled` and `tenantFirstLoginEnabled` remain unchanged. The optional pending account without issuance stays inactive. No Staging or Production migrated and no permission catalog Apply/grant or BFF rollout occurs solely by merging PRs.

## Remaining Staging gate

The real HTTPS Identity origin, Tenant host proxy, SQL connection, owner permissions and real browser accounts are not provided by the repository. Operator must provision these independently and run a complete scenario: Owner creates email-less staff with temporary password -> staff enters normal login -> password change required (without Identity/BFF session) -> complete once -> new login succeeds -> assigned role controls member/menu permissions -> revoke -> fresh session returns 403. This is **not yet claimed completed** without deployment evidence.

Rollback caveat: Down refuses non-NULL conversion with live email-less rows. Back up the DB and reconcile real contact information before attempting rollback.
