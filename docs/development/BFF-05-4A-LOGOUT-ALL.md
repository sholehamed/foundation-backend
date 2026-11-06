# BFF-05.4a — SQL-backed, cookie-authenticated logout-all of existing BFF sessions

## Verified merged BFF-05.4a local logout-all and BFF-05.5b.3 SQL cleanup — 2026-11-06

- **Merged SQL cleanup:** [PR #34](https://github.com/sholehamed/foundation-backend/pull/34) at `576b7941fada322fc32def221836ff211f469574`, [final CI #42218028623](https://github.com/sholehamed/foundation-backend/actions/runs/42218028623) full build, **94 BFF + 60 Identity + 12 real SQL Server** tests. Bounded maintenance-only DELETE retains every ticket row for 48 hours after original absolute expiry and preserves fresh active/revoked rows. Must schedule externally with separate maintenance principal.
- **Merged account-local logout-all:** [PR #35](https://github.com/sholehamed/foundation-backend/pull/35) at `18775eeb80ea6740d37943b516060172290e882e`, [final PR-head CI #42219967286](https://github.com/sholehamed/foundation-backend/actions/runs/42219967286) full build, **96 BFF + 60 Identity + 17 real SQL Server** tests (173 total, zero failures). `POST /bff/auth/logout-all` uses host-bound cookie, Origin/CSRF and verified `sub` to SQL-revoke all **existing** sessions for the subject. SQL v1→v2 migration revokes legacy active tickets missing ownership, adds checked subject binding/index and passes real SQL upgrade/security tests.
- **Not a full signout guarantee:** upstream Identity SSO cookie, access/refresh token families and future new OIDC authorizations are NOT revoked by this local endpoint; refresh token concurrency, live OIDC callback races, production Data Protection keyring encryption/rotation, SQL HA/failover, real browser/proxy and Angular BFF-06 plus BFF-07 remain. Browser opt-in default `Identity:Bff:Enabled=false`; Angular Password Grant/localStorage unchanged.
- Handoff: [logout-all and schema migration](../development/BFF-05-4A-LOGOUT-ALL.md), [SQL cleanup operations](../development/BFF-05-5C-SQL-CLEANUP.md), [BFF-05 master #20](https://github.com/sholehamed/foundation-backend/issues/20), [frontend BFF-06 #10](https://github.com/sholehamed/foundation-frontend/issues/10). Both initial F00 #2 PRs remain open.


> [Backend PR #35](https://github.com/sholehamed/foundation-backend/pull/35) merged; CI #42219967286: 96 BFF + 60 Identity + 17 SQL, all green. This does **not** revoke upstream OpenIddict tokens, rotate a refresh-token family, end the Identity Provider's browser cookie, or prevent a later new authorization.

## Protocol

`POST /bff/auth/logout-all` is registered only when `Identity:Bff:Enabled=true`. The handler requires:

- A valid CookieAuthentication `Foundation.Bff` principal (never a legacy Bearer token alone), current Identity/Tenant/stamp/grants validation via the existing Web adapter and host-bound System/Tenant callback context.
- Strict same-origin Origin/Referer/Fetch Metadata and the account-bound antiforgery header/cookie issued by `GET /bff/auth/csrf`, consistent with existing current-session logout.
- A valid GUID `sub` claim **from the verified server-side principal**. No body, query or route-supplied account/tenant/session identifier is used.
- A registered `IBffAccountSessionRevoker`, implemented by the SQL Server adapter. If missing, return 503 **without** expiring the current session or pretending account-wide revocation succeeded. Database faults fail closed and cannot return 204.

One committed SQL `UPDATE ... WHERE SubjectId=@subject AND RevokedUtc IS NULL` sets `RevokedUtc`, clears `TicketCiphertext` and increments `Revision` on **all existing rows owned by that subject**. A concurrent per-session `RenewAsync` is an UPDATE on the same SQL row with `RevokedUtc IS NULL`, serialized by SQL Server; it never reactivates a revoked key. Only after successful SQL operation, the handler signs out the current cookie and removes the readable XSRF token.

## Deployment / schema migration

The reviewed `src/Web/Bff/SqlServerBffSessionStore.sql` migration adds `SubjectId UNIQUEIDENTIFIER` and an index for account-scoped writes. A trusted `CK_FoundationBffSessions_Subject` CHECK enforces: active rows must have a subject. A SQL schema preflight refuses missing subject column, disabled/untrusted CHECK or missing index.

**Existing SQL tickets created before this migration do NOT have trustworthy indexed ownership.** The migration explicitly revokes all *active* legacy rows when adding the missing `SubjectId` column (and clears ciphertext), causing all pre-migration BFF cookies to require reauthentication. This avoids silently leaving orphan active tickets that logout-all could never reach. Coordinate a controlled deploy and communicate forced reauthentication; use separately privileged migration credentials, not the browser API runtime SQL user. Reapply script safely; it does not re-revoke rows after the column is present.

`StoreAsync` requires a GUID OIDC `sub` and writes it with the ticket. `RenewAsync` accepts only the **same** subject and immutable absolute expiry; `RetrieveAsync` cross-checks SQL `SubjectId` against the Data Protection protected `sub` inside the ticket and denies mismatches.

## Tests / acceptance

- [ ] Real disposable SQL Server tests for two sessions of Subject A versus Subject B: revoke A only; A's old cookies and renewal fail; B remains valid. A fresh post-revocation OIDC sign-in is still possible.
- [ ] Real SQL test for changed/missing `sub`, no 0 GUID and row ownership mismatch, and forced renewal/revoke row-lock race.
- [ ] HTTP BFF route tests: no cookie = 401, no account revoker = 503, wrong Origin = 403, invalid/cross-user antiforgery = 400, valid cookie+CSRF calls revoker with verified `sub` only, client-supplied body cannot choose victim.
- [ ] Actual OIDC+real SQL integration under two Web/Identity hosts, deployment migration from previous schema, SQL HA/failover and concurrent *new sign-in* behavior require separate release acceptance before production.
- [ ] Latest PR-head build, BFF unit/integration, Identity and real SQL Server CI green.

## Explicitly not finished — BFF-05.4b

This API revokes the *currently existing BFF browser sessions*, but it does not prevent new BFF sessions authorized after the SQL statement. The upstream Identity SSO cookie remains, existing access/refresh tokens may survive and the original OAuth Authorization Code may be used only according to its own issuer replay constraints. Implement OpenIddict refresh-token family revocation, end-session policy, coordinated server-side refresh/rotation and browser acceptance before presenting this as full logout-all. Prefer a new independent PR and separate tests.

Feature flag remains **OFF by default**. Angular legacy Password Grant/localStorage remains active. Original F00 PR #2 untouched. Track [backend BFF-05 #20](https://github.com/sholehamed/foundation-backend/issues/20) and [frontend BFF-06 #10](https://github.com/sholehamed/foundation-frontend/issues/10).
