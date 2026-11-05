# SQL Server Identity Security Regression Suite

The tests run only against a **disposable SQL Server**. Set `SHINERA_SQLSERVER_TEST_CONNECTION` to the SQL Server **master** database on an isolated test instance; the suite creates and drops a unique temporary database for each test, applies real EF Core migrations, and checks role revocation, tenant suspension and global TenantId filtering.

Run via [Backend SQL Server authorization tests](../../.github/workflows/backend-sqlserver-ci.yml) on every PR. The workflow uses a SQL Server 2022 service container. Never point this suite at shared or production SQL Server. This suite does **not** claim to test the entire OAuth token endpoint, which is exercised by frontend full-stack E2E.

The test explicitly verifies that normal tenant query filters hide another tenant's roles. `IgnoreQueryFilters(["tenant"])` is an internal privileged API, not a permission granted to a tenant user.

The next stage is a test-driven explicit Resource + Action + Scope permission handler for System and Tenant accounts (ADR-003), which must not be approximated by enabling the old implicit Route Permission Middleware.
