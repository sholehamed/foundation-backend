# FR-18 — Control Tower delivery roadmap, work packages and gates

**Date:** 2026-11-11  
**State:** FR-18A design documents are being added. **FR-18B–18H: PLANNED, no production instrumentation/storage/API/UI has been implemented.**  
**Depends on:** Foundation current System Feature Registry/RBAC, OpenIddict/BFF, SQL integration and Angular.  
**Related:** [ADR](../decisions/ADR-0003-control-tower-observability-isolation.md) · [Architecture](../architecture/control-tower-observability.md) · [Contracts](../reference/control-tower-telemetry-contracts.md).

## Product acceptance north star

Authorized System Admin opens `/system/control-tower/traces/{traceId}`, sees the source-owned route, authenticated scope, timeline with correctly timed parent/child spans, relevant CQRS/domain stages, EF Core/SqlClient command fingerprints and durations, safe input/output **only if explicitly collected**, redacted error + rollback/retry details, and honest incomplete/not-retained indicators. All backed by **separately stored** telemetry, neither raw log/query/body writes nor Collector dependency in business SQL transaction path. A second Tenant cannot view it. Losing telemetry never blocks a business request. Anything not captured must clearly say why.

## First-launch dependencies and priorities

1. **FR-18A — Architecture/acceptance baseline** (this documentation PR)
2. **FR-18B — Source-level trace context & host instrumentation** (smallest running vertical slice)
3. **FR-18C — Independent Collector/storage POC + health/retention**
4. **FR-18D — Methods, CQRS, EF/SqlClient/Dapper/transaction spans**
5. **FR-18E — Privileged read facade and first Angular Control Tower**
6. **FR-18F — Capture Policies, audited diagnostic sessions and safe DTO fields**
7. **FR-18G — SQL performance/Query Store, N+1, alerting and runtime dashboards**
8. **FR-18H — Production hardening, two-Tenant load/chaos/staging and handoff**

Keep FR-17 real Commercial Module integration independent, but use its actual Command/DbContext as an integration proof later; never insert a fake paid module or grants just to show telemetry.

## FR-18A — Documentation and authoritative decisions

**Files:** ADR isolation, complete reference architecture, telemetry/read-policy contracts, this roadmap; frontend experience blueprint and documentation state/index.

**Done when:**
- Separate business SQL and observability data planes and failure semantics are explicit.
- Secure redaction/capture defaults + protection for auth/password/PII defined.
- Trace/Span/SQL event relationships and method limitations articulated.
- Query facade, permission boundaries, UI pages and API status semantics specified.
- CI/staging plan with negative tests, performance budgets and rollout/rollback documented.
- Documents link from `docs/README.md`, `PROJECT-STATE.md` and `PROJECT-ROADMAP.md` in BOTH repos; docs-only PRs pass their repo CI.
- No claim that instrumentation, Collector, domain SQL Trace or Control Tower pages exist.

## FR-18B — Observability core and first trace-through-host

**Backend** (first implementation PR):
- Create reusable host/module-neutral `Modules.System.Observability` (naming/package boundaries reviewed) or shared Infrastructure extension. Avoid putting telemetry domain behavior in `Modules.System.Identity`.
- Pin compatible .NET 10 OpenTelemetry SDK/exporter packages via `Directory.Packages.props`. Source-owned `ActivitySource` and `Meter` conventions, standard `service.name/version/environment`, W3C trace context, bounded processor/exporter, `OTEL_EXPORTER_OTLP_ENDPOINT` from secrets/config. **Disabled-by-default export** until endpoint verified, with safe metadata instrumentation enabled as approved.
- Add ASP.NET Core Request instrumentation to host `RegisterServices/RegisterApps` without changing OpenIddict/BFF/CORS/CSRF/Tenant middleware ordering. Instrument BFF spans safely; never log auth headers/cookies.
- Correlate `ILogger` with trace/span IDs; exception classification, HTTP Duration, status and basic runtime metrics; no body/SQL parameter values.
- Expose safe support trace reference only where approved.
- Tests: no Collector/Collector outage, 401/403/200/500 semantics preserved, no tokens in logs, W3C propagation, two-Tenant behavior preserved, exporter no unbounded queues.
- Migrate no Identity business tables; no speculative Commercial module.

**Acceptance demo:** real signed authenticated HTTP request, trace identifier in server logs and root ASP.NET Activity; no direct browser telemetry datastore access; CI green on disposable SQL/BFF test workflows.

## FR-18C — Separate ingestion/storage POC

**Infrastructure**:
- Docker Compose *dev-only* profile for Collector + trace/log/metrics stores, version pinned, private default network and capped disk/CPU/memory/retention.
- Collector policies: memory limiter, batch/retry, secrets removal, trace tail sampling considered only after validated memory/capacity; sample errors/slow endpoints and bounded normal load. Configure queue persistence if needed with explicit disk budget.
- Production choice reviewed separately (Tempo/Loki/Prometheus or equivalent). OTLP endpoint not public, mTLS/auth/scrape hardening required.
- Storage retention/deletion/test of cold start and restart.
- Testing: 100% telemetry-storage downtime with continued application write, visible exporter drop/queue metrics, Trace search after ingest, independent load testing from SQL primary.

**Acceptance demo:** one real request through host -> Collector -> stored root+child spans readable with read-only service credential, while disabling Collector leaves business operation unaffected.

## FR-18D — Deep command, SQL and service tracing

**Backend**:
- CQRS behavior around handler boundary using stable code-owned operation keys, explicit ActivitySource for business sub-steps.
- EF Core SqlClient and Dapper coverage with **one physical-command canonical span**; capture command start/duration/error without secrets; transaction begin/commit/rollback, SaveChanges/SQL batch boundaries, retries and cancellation.
- SqlCommand canonical template/fingerprint normalization, max text/event budget, no `EnableSensitiveDataLogging` or raw values.
- Optional allowlisted module-specific diagnostic providers; no blanket AspectProxy/all-CLR-method profiler.
- Context correlation across outgoing HttpClient and jobs with traceparent or Span Links, not arbitrary baggage claims.
- Tests: EF, raw SqlClient, Dapper, no SQL duplication, 2 Tenants, rollback, command retry, 1000 duplicate query safety, performance baseline vs instrumented.
- Never log query-derived user text or return affected rows for SELECT unless actually observed.

**Acceptance demo:** one request has parent Handler + DB children and safe SQL fingerprints, enough to identify slow/N+1 candidate without claiming exact Query Store plan.

## FR-18E — Privileged query facade and Angular vertical slice

**Backend**:
- Source-owned `control-tower` Resource under the existing `system` Module (`PlatformCore`) and System Feature Registry/menu with reviewed granular permissions (proposed `CONTROL-TOWER.OVERVIEW.READ`, `TRACES.READ`, `SQL.READ` etc; final keys registered code-owned). First-install System Admin bootstrap grants explicitly reviewed, production existing grants never silently re-enabled.
- `IControlTowerQueryService` with vendor-neutral DTO and strict actor/scope/time-range/rate limit/pagination checks. Backend proxies bounded queries to telemetry datastores via least-privilege credentials. No raw query string passthrough or direct Tempo/Loki credentials in browser.
- Endpoints for Overview, Trace search/detail/span events, SQL span details, service health. Responses report `Captured/Partial/SampledOut/Unavailable`, no-store, 401/403/404/429/503.
- Every access to sensitive trace/SQL detail writes audited metadata, separate from lossy telemetry.

**Frontend**:
- New lazy `/system/control-tower` child routes and backend-authoritative System Feature Registry route-segment integration. System Admin authors menus; Tenant never edits central catalog.
- Clean RTL/light/dark responsive Overview, Request Explorer, Trace Inspector/Waterfall, SQL Analyzer, Errors, Tenant scope, and ingestion health.
- Genuine loading/empty/stale/unavailable/error/forbidden states, Jalali display with server UTC, line-breaking and safe code display, copy TraceId, stable filters stored in URL *without personal/secret payloads*.
- Fast virtualization/pagination of large traces, keyboard and mobile accessibility.
- Angular permission guard plus backend authority; mock fixtures permitted **only in tests**.

**Acceptance demo:** real System operator can follow a TraceId to its spans/SQL through authenticated BFF; limited user cannot; tenant does not access another tenant.

## FR-18F — Safe capture and audited diagnostic sessions

- Register code-owned `IDiagnosticFieldsProvider<T>` allowlists; versioned capture policies with metadata-only default, no generic recursive JSON serialization.
- System review/apply/disable for limited future capture, timed Diagnostic Session with TTL/request/byte caps, server-derived Tenant scope, reasoning/approval and durable access audit.
- Encrypted storage for approved diagnostic payloads if required; exact retention/erase policy validated. Secrets permanently forbidden.
- UI policy preview/diff/approval and session controls with “collected/redacted/truncated/not collected” semantics.
- Chaos/load, attacker-controlled DTO, auth token, signed cookie, SQL parameter redaction and session revocation matrix.

## FR-18G — Advanced SQL Analyzer and alerts

- SQL grouped fingerprint summary: N+1 candidates, top latency/count/error/timeout, regression comparisons; sanitized query templates when allowed.
- Query Store aggregated historical plan + wait data via **dedicated read-only DBA-approved connection**; never arbitrary SQL execution from UI, never confuse plan candidates with exact individual request. Advanced XEvents only reviewed time-bound sessions.
- Runtime health: CPU/GC/ThreadPool, HTTP RED, DB connection pools, Collector ingestion/dropped samples. Avoid labels containing Tenant or SQL text.
- Alert rules source-owned evaluated centrally; incident state/dedupe/severity, links to safe Trace details. Prepare event contract to Notification Center; do not embed emails/SMS in request path.

## FR-18H — Release hardening

- Production topology, independent compute and storage from business SQL, encryption, object-store backups, access policies, retention, GDPR/PII compliance, rate/cost limits, secure OTLP transport.
- Both System/Tenant two-tenant real HTTPS OpenIddict/BFF end-to-end; real EF/Dapper SQL tracing; browser multi-viewport; permission revocation; third-party outage chaos; load/footprint comparison at target expected throughput.
- Candidate baseline budget: p95 request overhead <=5%, CPU <=5% for Standard, bounded exporter buffers, no drop without visible rate; validate and revise with measured test evidence. Detailed diagnostics separately bounded and opt-in.
- Explicit Production rollout: canary standard metadata, isolated collector rollout, trace facade read only, diagnostics approval, load monitor, staged operator grant, rollback to disabled exporter. Disable observability by config without requiring a production SQL migration rollback.
- Certify hosted Staging and Production **separately** from GitHub CI. Capture Staging source manifest and redacted test evidence, no production secrets in artifacts.

## Definition of Done for every implementation slice

- Include source, tests, docs/PROJECT-STATE + docs/PROJECT-ROADMAP, any migrations with repeatable rollback plan.
- CI: .NET solution build/unit, disposable SQL, signed HTTP+BFF, Angular unit/build/Chromium when changed; verify PR-head before merge and `main` after merge.
- No unreviewed schema/role grant/seed; System central authority remains unchanged.
- No false “fully monitored” claims if some spans/SQL/actions remain uninstrumented; UI distinguishes missing/captured/redacted.
- New features are opt-in with retention/overhead measured, no sensitive data captured.
- Track implementation matrix per source module and dependency (ASP.NET, CQRS, EF, SqlClient, BFF, Angular, Background).

## Handoff checklist (for next chat)

- Current design PR documents live in backend `docs/decisions/ADR-0003-control-tower-observability-isolation.md`, `docs/architecture/control-tower-observability.md`, `docs/reference/control-tower-telemetry-contracts.md`, `docs/development/FR-18-CONTROL-TOWER-ROADMAP.md`; frontend `docs/development/FR-18-CONTROL-TOWER-UX.md`.
- First actual code action: implement **FR-18B**, not FR-18E fake dashboard. Start with host composition, OpenTelemetry SDK and W3C Trace/Activity/Logger correlation; preserve BFF middleware and source-owned central auth semantics.
- Verify no collector required for application startup, no raw payload capture, trace extraction does not set Tenant identity, token/password leakage tests, and build compatibility in CI before adding deep SQL hook.
- Keep the Notification Center as a separate later module consuming events; avoid mixing alert notification side effects into request pipeline.
