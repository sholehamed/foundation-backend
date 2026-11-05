# Milestones

| Milestone | Deliverable | Acceptance |
| --- | --- | --- |
| F00 | Runnable .NET API, health/ping, error contract, CI tests | CI green and documented local run |
| F01 | Identity, tenants, memberships, authentication sessions | tested end-to-end auth and cross-tenant isolation |
| F02 | Roles, permissions, resource checks, cache invalidation | 401/403/cross-tenant and revocation tests |
| F03 | Audit, OpenTelemetry, logs/metrics/traces | queryable diagnostics with sensitive-field policy |
| F04 | HybridCache and optional Redis | hit/miss, tenant-key and invalidation tests |
| F05 | Durable jobs/outbox and optional broker | crash/retry/idempotency tests |
| F06 | Reference feature with Angular client | full HTTP security and E2E workflow |

Do not mark a milestone done based solely on source presence. Every milestone is delivered via an isolated PR with test evidence.
