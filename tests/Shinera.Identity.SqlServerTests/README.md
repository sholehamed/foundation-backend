# SQL Server Identity Security Regression Suite

The tests run only against a **disposable SQL Server**. Set `SHINERA_SQLSERVER_TEST_CONNECTION` to the SQL Server **master** database on an isolated test instance; the suite creates and drops a unique temporary database for each test, applies real EF Core migrations, and checks role revocation, tenant suspension and global TenantId filtering.

Run via [Backend SQL Server authorization tests](../../.github/workflows/backend-sqlserver-ci.yml) on every PR. The workflow uses a SQL Server 2022 service container. Never point this suite at shared or production SQL Server. This suite does **not** claim to test the entire OAuth token endpoint, which is exercised by frontend full-stack E2E.

The test explicitly verifies that normal tenant query filters hide another tenant's roles. `IgnoreQueryFilters(["tenant"])` is an internal privileged API, not a permission granted to a tenant user.

The next stage is a test-driven explicit Resource + Action + Scope permission handler for System and Tenant accounts (ADR-003), which must not be approximated by enabling the old implicit Route Permission Middleware.


## FR-05A Feature Registry SQL Server gate

`FeatureRegistrySqlServerTests` runs on the BFF CI SQL Server 2022 service container using the same disposable `SHINERA_SQLSERVER_TEST_CONNECTION` test-only setting. The tests require a loopback connection to the `master` database, create unique `FoundationRegistryTest_*` databases, apply **real EF Identity migrations**, and always drop the databases. No shared/production connection is accepted.

The gate checks read-only Preview, idempotent approved Apply of 13 root menu resources + 41 subordinate PAGE/ACTION descriptors, no user/role grants, preservation of disabled manually named UI rows, and rollback of all catalog rows inside an outer real SQL transaction. It does **not** yet certify simultaneous `ApplyAsync` calls, the lack of a unique UI key index on historical databases, or a production rollout with legacy data. Until those are reviewed, the `Identity:FeatureRegistry:EnableApply` setting remains OFF by default.

Run locally against a disposable loopback SQL Server:

```bash
dotnet test tests/Shinera.Identity.SqlServerTests/Shinera.Identity.SqlServerTests.csproj -c Release --filter "FullyQualifiedName~FeatureRegistrySqlServerTests"
```
