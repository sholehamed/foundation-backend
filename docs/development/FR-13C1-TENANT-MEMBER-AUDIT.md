# FR-13C-1 — Owner-only Tenant member security audit viewer

Date: 2026-11-09. Follows FR-13B onboarding and successful disposable
[FR-13C live browser acceptance](FR-13C-DISPOSABLE-LIVE-FIRST-LOGIN-E2E.md).

## Contract

- GET `/api/tenant/workspace/members/audit?page=1&memberId=<optional-guid>`.
  The HTTP handler is authenticated and checks the current active Tenant,
  *live* `TENANT.MEMBERS.READ` and a live, local **Owner** assignment;
  non-Owner staff cannot view the ledger even when granted Members.Read.
  System users, anonymous users, wrong-Tenant sessions and direct-deny
  overrides fail closed. Invalid pages and empty target GUID return 400.
- The caller **cannot** supply TenantId or actor ID. A parameterized SQL
  `UNION ALL` reads exactly two existing append-only ledgers:
  `TenantMemberLifecycleAuditEntries` (create, invitation, temporary
  credential issuance, password activation) and
  `TenantMemberRoleAuditEntries` (direct staff Role grants/revokes).
  Both source tables and both optional username joins filter on the server's
  current TenantId, not on browser-selected data.
- The response consists of `{items,pageNumber,hasNext}`; page size is
  **20**, bounded to pages 1..1000, fetched as 21 to calculate hasNext.
  Ordered by `occurredAtUtc DESC, id DESC` (deterministic for equal dates).
  Event DTO: `id,category,action,occurredAtUtc,actorUserId,actorUserName,
  targetUserId,targetUserName,roleName`. Name fields can be null after
  user deletion, while historical records remain queryable.
- `Cache-Control: no-store`. No password/secret, email, token, hash,
  CAPTCHA, request body, IP or session ID is returned. The Audit reader
  performs **no SQL mutation**. Existing immutable triggers stay intact.
  No new indexes, migrations, catalog entries, grants or feature flags.
- Tenant Members existing UI shows an Owner-only history link via
  `canViewMemberAudit` and per-member filtered links. The new Angular
  `/dashboard/member-audit` route checks the existing registered Members
  PAGE *and* Owner capability before activation. Server enforces both again.
  Dates display in Persian calendar. The UI supports paging, empty/retry
  states and clears stale data on 401/403/404.
- No static navigation item is inserted into the permission-bound Tenant
  menu; audit is reachable from Members only. This slice deliberately
  avoids extending the 12-key Tenant Feature Registry/catalog merely to
  display a new Owner-specific read-only subsection.

## Acceptance

1. SQL Server integration: owner reads mixed lifecycle + role audit in
   deterministic order; page 1 returns 20/hasNext, page 2 remainder;
   member filter and foreign-Tenant malicious same-user-id row are isolated.
2. Auth: ordinary member granted Members.Read still gets 403 for audit;
   Owner direct deny of Members.Read also remains effective through the
   existing live permission evaluator; invalid page returns 400.
3. Angular: Owner link hidden without server capability, GET contract contains
   only page and optional memberId, Persian rendering, paging and revocation
   clears stale entries. Existing member creation, role and menu tests remain.
4. Independent Backend + Frontend CI successful before merge.
   This is **code acceptance**, not Staging/Production deployment.

## Deliberate next steps

Continue Tenant Admin group CRUD and group membership with separately
registered ACTIONs, permission checks and audit, then a broader role/grant
audit screen. These two ledgers are append-only **but not tamper-evident**;
current usernames are live labels, not historical snapshots. No Staging or
Production rollout is authorized by merging this slice.
