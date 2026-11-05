# Foundation Backend

A reusable .NET 10 backend starter. This repository starts small and grows by tested vertical slices. **This is not yet a production identity or multi-tenant system.**

## Requirements
- .NET 10 SDK
- No external database or service required for F00

## Run

```bash
dotnet restore Foundation.slnx
dotnet build Foundation.slnx --no-restore
dotnet test Foundation.slnx
dotnet run --project src/Foundation.Api --launch-profile http
```

- HTTP local default: `http://localhost:5080`
- `GET /health/live`: process liveness
- `GET /health/ready`: readiness shell (no dependency checks configured yet)
- `GET /api/v1/system/ping`: minimal JSON contract used by the frontend
- `GET /openapi/v1.json`: OpenAPI in Development only

Unhandled errors return Problem Details. Response headers carry a server-generated `X-Trace-Id` for correlation. Health routes should not be mistaken for database readiness until dependency checks are introduced.

## Principles
- Modular monolith by default; module boundaries follow actual use cases rather than placeholder projects.
- Tenant isolation and permission evaluation are enforced by the backend when implemented.
- Identity and authentication **are not implemented in F00**. Do not put business data or protected endpoints here before the security milestone.
- Structured observability, persistence, cache, and messaging are separate milestones.
- Redis, RabbitMQ and SQL Server are not prerequisites for starting a fresh project.
- CI must validate every change before merging.

See [architecture](docs/ARCHITECTURE.md) and [roadmap](docs/ROADMAP.md). Frontend: [foundation-frontend](https://github.com/sholehamed/foundation-frontend). Tracking: [F00 issue](https://github.com/sholehamed/foundation-backend/issues/1).
