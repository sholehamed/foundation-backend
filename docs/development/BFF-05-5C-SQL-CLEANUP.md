# BFF-05.5b.3 — bounded SQL session cleanup

> [PR #34](https://github.com/sholehamed/foundation-backend/pull/34). Maintenance slice only, **not** multi-node SQL HA/failover certification. Latest-head CI required before merge.

## Contract

`SqlServerBffSessionCleanup.DeleteExpiredBatchAsync(batchSize, ct)` is an explicit Web-owned SQL Server maintenance operation, **not** a hosted background loop. It deletes at most 1–1000 rows per call from `dbo.FoundationBffSessions` when both `ExpiresUtc < SYSUTCDATETIME()` and `AbsoluteExpiresUtc < DATEADD(HOUR,-48,SYSUTCDATETIME())`. Revoked and naturally expired rows are eligible only after this cutoff. Bounded `DELETE TOP` and row hints minimize long locks; a concurrent row write participates in normal SQL Server lock ordering. The operation returns the number of rows removed; 0 signals no eligible work in that batch (and is not a certificate of database-wide emptiness if competing writers hold locks).

SQL sessions use UPDATE-only renewal and INSERT-only new random session keys. Removing aged rows **cannot resurrect** them: renew never INSERTs, and old signed browser cookies remain bound to their now-absent random key.

## Operational deployment

1. Apply the existing reviewed `src/Web/Bff/SqlServerBffSessionStore.sql` migration and pass the existing startup schema check. Do not change app DDL automatically.
2. Create a dedicated maintenance SQL principal with **DELETE** on only `dbo.FoundationBffSessions`, plus the SELECT permissions required for your monitoring. **Do not grant DELETE/DDL to the browser API runtime principal** (which uses SELECT/INSERT/UPDATE).
3. Schedule a controlled operator job (SQL Agent or external worker approved by operations) invoking the .NET cleanup method with its **maintenance-only** connection string, e.g. batches of 100–1000 until a 0-row result, with a total run limit and telemetry on deleted-row counts/SQL errors. Do not run cleanup on every HTTP request or in every Web node.
4. Enforce transactionally consistent SQL primary access. Do not point cleanup at an asynchronous read replica or run with unreviewed failover topology. Retry transient failures by running another bounded batch, not by widening the retention threshold.
5. Audit/log **counts, duration and failures** without logging session lookup keys, ciphertext, user identifiers or connection strings. Monitor table growth and oldest `AbsoluteExpiresUtc`; do not delete before the 48-hour post-absolute cutoff.
6. Deleting only definitively expired sessions means deleted rows never become active again, even when existing cookies are replayed. This does **not** revoke still-active sessions, provide account-wide logout, or revoke upstream IdP tokens.

## Automated checks

- [ ] SQL Server disposable DB: two old rows (revoked + naturally expired) removed in 1-row batches, third run idempotent.
- [ ] Fresh active and fresh revoked rows retained; active cookie still authenticates and revoked cookie remains rejected.
- [ ] A revoked row 24 hours after absolute expiry (inside 48-hour retention) is not deleted.
- [ ] Invalid batch sizes fail without touching DB.
- [ ] Stale renewal of a deleted key cannot INSERT/recreate it.
- [ ] Latest PR-head CI: full .NET solution, BFF & Identity tests, **real SQL Server** integration job.

## Still open

Real SQL Server production HA/failover and multi-node deployment, production keyring encryption/rotation, upstream Identity token revocation/logout-all, coordinated refresh rotation, OIDC stamp timing, deployed-browser/proxy integration, Angular BFF-06 migration. Keep `Identity:Bff:Enabled=false` default and both F00 #2 PRs open.
