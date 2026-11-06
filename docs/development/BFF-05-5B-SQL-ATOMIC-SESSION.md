# BFF-05.5b.2 — SQL Server atomic session ticket provider

> [Backend draft PR #32](https://github.com/sholehamed/foundation-backend/pull/32), **CI pending at this checkpoint**. This is the *first actual provider* for the prior `IBffAtomicTicketStore` startup contract. BFF remains opt-in OFF; browser/Angular migration is blocked by other BFF-05 gates.

## Ownership and architecture

- Reusable `Modules.System.Bff` defines the `IBffAtomicTicketStore : ITicketStore` contract, opaque ticket key format and BFF cookie scheme; it does **not** reference SQL Server, Redis or Identity persistence.
- `src/Web/Bff/SqlServerBffAtomicTicketStore.cs` is a Web composition adapter, depends on Microsoft.Data.SqlClient (through the existing centrally versioned EF Core SQL package), and stores ASP.NET Core Data-Protection-protected AuthenticationTicket bytes **only on the server**. The `__Host-Foundation.Bff` browser cookie carries only the existing protected opaque key.
- `src/Web/Bff/SqlServerBffSessionStore.sql` is the **manual, idempotent DDL deployment migration**. The source schema is also embedded as `Foundation.Bff.SqlServerSchema.sql` for the disposable integration tests; **the application never automatically creates a production database or table**.

## Database contract

`dbo.FoundationBffSessions`: random `SessionKey` binary-collated PK, nullable protected `TicketCiphertext` (cleared on revoke), `CreatedUtc`, sliding `ExpiresUtc`, original immutable `AbsoluteExpiresUtc`, nullable `RevokedUtc` and monotonic `Revision`. A CHECK constraint enforces ticket-null iff revoked and expiry bounded by 24 hours from initial issuance; a cleanup index supports later maintenance. Keep revoked rows until the absolute lifetime has expired; deleting already revoked/expired rows cannot reinsert a ticket, since renewal is **UPDATE only**. Do not add INSERT-on-missing fallback.

### Atomic ordering

1. Initial `StoreAsync` inserts a random 256-bit opaque key and a protected ticket, clamping `ExpiresUtc` to **24h from original `IssuedUtc`**.
2. `RenewAsync` requires the original protected `.bff.absolute_expires_utc` property; one SQL `UPDATE` succeeds *only* for a present, unrevoked row with a non-null ticket, active sliding and absolute expiry, and exactly matching original absolute deadline. All competing SQL row writes serialize. No upsert, resurrection or renewed deadline beyond the original ceiling.
3. `RemoveAsync` uses one SQL `UPDATE` of the same row, sets `RevokedUtc=SYSUTCDATETIME()`, sets `TicketCiphertext=NULL`, and increments revision. A successful command commits server-side revocation; any SQL error throws rather than reporting successful logout.
4. `RetrieveAsync` reads from SQL under `READCOMMITTEDLOCK`, requiring active deadlines and no revocation. It unprotects bytes and cross-checks persisted DB expiry against protected ticket properties. Corruption or foreign Data Protection keys fail closed. A request that completed authentication before logout's SQL commit may still execute: this is distinct from **subsequent requests authenticating after the commit**.
5. SQL UTC time controls active/deadline checks, with original issuance and absolute deadline bound during Store and checked on read. Operators must synchronize clocks across hosts; this doesn't replace end-to-end clock-skew tests.

## Configuration / deployment

1. Apply the reviewed DDL from `src/Web/Bff/SqlServerBffSessionStore.sql` to the target SQL Server DB using your existing approved migrations/deployment workflow; verify `dbo.FoundationBffSessions`, PK, CHECK constraints and SQL Server SQL logins/least-privilege DML grants. Do **not** use `EnsureCreated` on an existing Identity database.
2. Provide **secret-managed** `ConnectionStrings:BffSessions` (independent SQL connection string, never log it). Register a **shared persistent ASP.NET Core Data Protection keyring** across every BFF node before enabling the feature; local development keys are not a valid multi-node setup.
3. Set `Identity:Bff:SqlServer:Enabled=true` AND `Identity:Bff:Enabled=true` only after the remaining BFF release gates are proven. Web composition calls `AddSqlServerBffAtomicTicketStore` *before* BFF authentication registration; the non-Development singleton guard from PR #29 verifies the adapter is chosen as `ITicketStore`. The main appsettings value remains `Identity:Bff:Enabled=false` by default.
4. Database privilege: grant INSERT, SELECT and UPDATE on the session table to BFF service identity, and no runtime CREATE/DROP/ALTER permissions. Keep admin migration credentials separate from app credentials.
5. Do not use read replicas, cross-region async replication or an eventually-consistent secondary as session read authority. All instances must use the **same writable primary/transactional failover topology**. In failover, block serving BFF requests until fully consistent; SQL outages fail closed rather than reading a stale local fallback.
6. Run `dotnet test tests/Foundation.Bff.SqlServerTests/Foundation.Bff.SqlServerTests.csproj` against a disposable SQL Server, with `SHINERA_SQLSERVER_TEST_CONNECTION` set to a **master database on a test-only instance** (CREATE/DROP DATABASE permission). GitHub Actions `bff-atomic-sqlserver` provisions a SQL Server 2022 service with CI-only credentials, runs tests and deletes per-test databases. Never use this test account or suite against production.

## Automated checks (PR #32)

- [ ] Full solution build, existing BFF/Identity TestServer suites remain green.
- [ ] Disposable real SQL Server create/schema rerun + isolated per-test database.
- [ ] Cross-object, cross-connection round-trip, renewal, logout, durable tombstone and revision.
- [ ] SQL row-lock forced overlapping concurrent renew+logout, replay after logout.
- [ ] Expired/missing/old keys cannot be inserted or renewed; absolute deadline cannot move.
- [ ] Corrupted protected ticket rejects, missing-table SQL outage throws (no local fallback).
- [ ] SQL schema is idempotent; cleanup preserves isolation.

## Remaining release gates: NOT complete yet

- **BFF-05.3:** OIDC issuance stamp-change race, Web DB outage under actual callback, grant-change and single/multi-host replay.
- **BFF-05.4:** account-wide/logout-all across session index, upstream OpenIddict refresh-token revocation and end-session policy, replay and refresh rotation.
- **BFF-05.5:** shared persistent Data Protection keys, multi-instance restart/failover, schema drift startup check, bounded idle/absolute expiry and distributed refresh coordination. Real SQL tests prove row serialization but are **not** a full deployed multi-node/failover acceptance.
- **BFF-05.6/BFF-06:** HTTPS browser E2E, SQL Server production-like integration, trusted reverse proxy/forwarded host, server-side Angular migration from Password Grant/localStorage to same-origin BFF, route guard and CSRF tests and rollback.

The original F00 PR #2 must remain open and `Identity:Bff:Enabled=false` stays the default. Track [backend master #20](https://github.com/sholehamed/foundation-backend/issues/20), [SQL provider #30](https://github.com/sholehamed/foundation-backend/issues/30) and [frontend BFF-06 #10](https://github.com/sholehamed/foundation-frontend/issues/10).
