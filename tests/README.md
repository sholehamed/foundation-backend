# Shinera backend tests

## Current scope

- `Shinera.Identity.Tests`: isolated xUnit tests for system vs tenant context, scoped filter override, and tenant-resolution middleware.
- The backend CI builds the complete .NET solution and executes these tests.

## Next required tests

- Integration tests via `WebApplicationFactory` and an isolated SQL Server container: authentication and OpenIddict protocol lifecycle, tenant query isolation, RBAC and permissions.
- Database migrations and seed behavior.
- Concurrency and appointment booking once the business modules have been implemented.

The frontend repository owns the cross-repository Playwright E2E workflow (real ASP.NET Core API, ephemeral SQL Server and Angular browser). This project intentionally avoids treating a browser test as a replacement for backend integration tests.
