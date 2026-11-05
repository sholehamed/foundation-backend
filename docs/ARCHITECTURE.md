# Foundation Backend architecture — initial record

**Status:** F00 bootstrap (provisional architecture, not complete implementation).

## Why small first?
The first deployable slice is a .NET 10 API with an actual HTTP contract and integration tests. Empty projects, prematurely generic repositories, in-house policy languages, and mandatory broker infrastructure are intentionally avoided.

## Planned module boundaries (incremental)
- **Host:** ASP.NET Core API and composition root.
- **Identity & Access:** users, sessions, credentials, membership, role/permission policies.
- **Tenancy:** tenant context and isolation rules for reads, writes, cache, jobs, and files.
- **Application modules:** business use cases and their data contracts.
- **Observability:** structured logs, traces, metrics, audit (separate security and technical logs).
- **Caching:** HybridCache with tenant-safe keys; distributed provider optional.
- **Messaging:** in-process events; durable outbox/jobs and RabbitMQ only when needed.

## Constraints
- Start with a modular monolith and one deployment.
- Keep authorization authoritative on the server.
- Do not rely on EF global query filters as the only tenant defense.
- Prefer real features and tests over abstract framework scaffolding.
- Use an ADR when reversing a consequential architectural decision.

For next work, see [ROADMAP](ROADMAP.md).
