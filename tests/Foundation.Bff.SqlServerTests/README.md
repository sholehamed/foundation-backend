# BFF atomic SQL Server integration tests

This suite uses **real SQL Server**, never EF InMemory, SQLite or a fake atomic cache. GitHub Actions runs [bff-ci.yml](../../.github/workflows/bff-ci.yml) against a disposable SQL Server 2022 service with a CI-only password. Every test creates a unique `FoundationBff_<guid>` database, runs the **same embedded migration** shipped for operators, and drops that database in finally.

To run locally, set `SHINERA_SQLSERVER_TEST_CONNECTION` to a disposable test-only SQL Server `master` database connection with CREATE/DROP DATABASE rights and execute:

```sh
dotnet test tests/Foundation.Bff.SqlServerTests/Foundation.Bff.SqlServerTests.csproj -c Release
```

**Never supply a production/customer connection string.** The test fixture intentionally creates and drops databases and tests missing-table/tampered-constraint failures.

Verified scenarios: SQL UPDATE-only renewal and atomic revocation, cross-connection race under a held row lock, expiry/replay rejection, encrypted/corrupt ticket handling, schema preflight refusal, two independent TestServers with persisted Data Protection keyring and cross-host cookie logout/restart replay rejection.

Not proven here: multi-region SQL replication, SQL Server Always On failover, production keyring encryption/rotation and real HTTPS browser/reverse proxy integration. Keep BFF disabled by default until all release gates finish. See [technical SQL store handoff](../../docs/development/BFF-05-5B-SQL-ATOMIC-SESSION.md).
