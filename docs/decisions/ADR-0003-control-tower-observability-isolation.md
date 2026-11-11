# ADR-0003 — Isolate Foundation observability from business persistence

- **Status:** Accepted as architectural direction; **implementation status: NOT STARTED**
- **Date:** 2026-11-11
- **Owners:** Foundation Platform / System Administration
- **Scope:** FR-18, Control Tower, all present and future Foundation modules and BFF/browser clients
- **Supersedes:** none
- **Related:** [Control Tower architecture](../architecture/control-tower-observability.md), [FR-18 delivery plan](../development/FR-18-CONTROL-TOWER-ROADMAP.md), [telemetry contracts](../reference/control-tower-telemetry-contracts.md), [ADR-0001 BFF](ADR-0001-browser-bff.md)

## Decision

1. **Business SQL Server is not an observability data lake.** No raw per-request trace/log/span/SQL-command/body history tables are permitted in `IdentityDbContext`, Tenant business DbContexts, their migrations or transactional save paths. Observability settings, reviewed policies, and audit *metadata* may live in a separately owned small control-plane store; even there, no raw diagnostic payloads.
2. **Telemetry ingestion is out of the request transaction path:** .NET/Angular instrumentation -> bounded asynchronous exporter -> OpenTelemetry Collector -> dedicated telemetry services/stores. All services have independently scalable capacity, retention and credentials. The business database instance must not share observability storage I/O or maintenance in Production. Separate SQL databases on the *same SQL instance* are not enough to isolate CPU, RAM, tempdb and disk contention.
3. **Standardize on OpenTelemetry and W3C Trace Context** at the instrumentation boundary. `ActivitySource` / `Meter` / `ILogger` in .NET, OTLP to a Collector, `trace_id` + `span_id` for correlation. Vendor-neutral internal contracts must not return Tempo/Loki/Prometheus-specific DTOs to Angular.
4. **Reference deployment for first isolated staging slice:** OpenTelemetry Collector, Tempo (traces), Loki (structured logs), Prometheus-compatible metrics, optional Grafana for internal operations. These are *candidate implementations*, not a commitment to an unreviewed version, storage backend, licensing, security hardening or infrastructure. A supported alternative backend may replace any without changing the API contract.
5. **Control Tower UI accesses observability through a privileged Foundation API facade**, never direct-to-Tempo/Loki/Prometheus from Angular. The API validates the current live System identity, central code-owned granular permissions, tenant visibility, filters and rate limits, returning normalized paginated/redacted responses. No browser-held telemetry datastore credentials.
6. **No default payload capture.** Request/response bodies, SQL literal values and parameters, DTO inputs/outputs, user-generated text, cookies and authorization artifacts are *not* stored by default. Restricted diagnostic policies can allow only explicit safe fields with field-level and media-type allowlists, budget limits, expiry and tamper-evident audit. Secrets never collected, even under a privileged session. `EnableSensitiveDataLogging` is not approved for Production.
7. **Observability is best effort and must fail open for the business path**, subject to explicit resource caps and backpressure. A telemetry outage cannot prevent an authenticated booking, financial transaction or authorization decision. **Security/financial audit is a separate, durable requirement** and must not silently rely on sampled, lossy telemetry.
8. **No implicit privilege expansion.** System Admin authors central menu/Resource/Permission; only authorized operators can inspect diagnostics. Tenant Admin cannot create core definitions or read other tenants' telemetry. Sensitive diagnostic exports require stronger authorization and auditing. All observability read/write endpoints are isolated from Tenant Core/Commercial subscription gates.
9. **TraceId is correlation, never authorization or an idempotency key.** Incoming `traceparent` and `baggage` are untrusted. Extract validated trace syntax, trim unsafe baggage, derive authenticated Tenant/User identity server-side, and never use caller-provided trace labels to decide row visibility.

## Rationale

Detailed per-request spans/SQL/exception records have high cardinality and write volume, potentially dwarfing the actual business transactions. Storing them in the same SQL instance creates coupled failure modes, makes transaction latency unpredictable and increases backup, index, retention and privacy burdens. The modular product needs operational isolation, safe aggregation, and independent scalability while keeping browser-level debugging useful.

## Consequences and rejected approaches

| Alternative | Decision | Reason |
| --- | --- | --- |
| Raw telemetry tables in Identity SQL | Rejected | Competes with core authentication, subscription and commerce transactions |
| Separate telemetry DB in same SQL instance for Production | Rejected as final state | Still contends for CPU, RAM, I/O, tempdb and maintenance |
| Dedicated telemetry SQL Server | Possible for *small control-plane state only*; not first choice for raw traces | SQL row-store indexing and retention of high-volume span events is not a natural fit |
| Application writes synchronously to logging database | Rejected | Ingestion failures/latency would affect customers |
| Grafana UI embedded with datasource token in Angular | Rejected | Leaks query capability, complicates System/Tenant authorization and redaction |
| Universal full DTO + SQL parameter capture in Production | Rejected | PII, credentials, customer data and performance exposure |
| Blanket automatic method instrumentation for every CLR method | Rejected by default | Too much overhead/volume; explicit instrumentation of meaningful boundaries is testable |
| One OpenTelemetry vendor throughout code | Rejected | Data collection and storage backends evolve independently |
| Metrics-only dashboards without linked traces | Rejected | Cannot answer “which method/query caused this request?” |
| Treat telemetry events as durable security audit | Rejected | Sampling and backpressure necessarily lose some telemetry |

## Outage and resilience requirements

- Exporters are bounded, non-blocking on hot business paths with short connection timeouts; limits are configured and tested.
- Collector ingest/export buffers have explicit memory limiter, batch, retry and optional bounded persistent queue; full queues may drop low-priority telemetry with a visible loss metric.
- No unbounded in-process queues, no recursive logger/exporter logging loops, no telemetry SQL writes inside business transactions.
- If observability storage is unavailable, the Control Tower displays **unavailable / delayed / incomplete**, never “0 errors” or “healthy”.
- Audit security-sensitive Capture Policy changes, diagnostic session access and privileged payload views in a separate durable audit store. Consider persistence failure behavior per sensitive administrative command; the loss-tolerant rule above **does not** waive durable audit requirements.
- Production rollout is gated on load/chaos testing, ingress allowlists/mTLS, encryption at rest, retention jobs, backups and least-privilege datastore credentials.

## Unresolved implementation choices / review gates

- Pin exact Collector/backend packages and versions by deployment environment; review supply-chain and license requirements.
- Choose Tempo/Loki/Prometheus single-node vs scalable storage based on actual ingestion rate and retention, not screenshots.
- Decide object-store provider and encryption/key rotation; do not assume local disks are durable.
- Quantify data-classification laws and retention with product/legal owner, and identify whether any allowed payload class needs explicit customer notice.
- Separate audit persistence boundary for security/financial events must be reviewed before activating deep diagnostic sessions.
- Evaluate OTLP endpoint egress isolation, proxy headers, high-cardinality limits, and read-query performance independently.

## Acceptance criteria for this ADR

The architecture is accepted as a **design**, not implementation. FR-18A is complete only when the contracts, risks, phase plan and UI specification are in the repos; no new API, data collection, backend storage, user permission or feature is represented as shipped by this documentation PR.
