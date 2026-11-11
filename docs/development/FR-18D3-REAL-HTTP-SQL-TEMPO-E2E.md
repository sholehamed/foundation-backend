# FR-18D-3 — Persisted *real* signed HTTP/CQRS/SQL TraceId proof

**Status:** Implementation PR; only accept if the required PR-head and post-merge CI succeed.  
**Date:** 2026-11-11  
**Scope:** Actual existing Foundation Kestrel + OpenIddict signed **JWT** System API, source-owned Dispatcher, SQL Server EF DbCommand execution, OTLP gRPC Collector, Tempo storage and readback by the original HTTP `X-Trace-Id`. No test-only endpoint or commercial domain code, no Identity SQL schema changes.

## Goal and non-goal

A synthetic OTLP JSON packet proving that Collector/Tempo work (FR-18C) is insufficient. This gate starts the **real Foundation application** against **disposable local SQL Server**, logs in using real `/api/system/Auth/login` OpenIddict signed credentials from CI, calls the **existing guarded** `GET /api/System/Dashboard/super-dashboard`, and must prove that the *same HTTP request* created genuine `Microsoft.AspNetCore` server, `foundation.http.pipeline`, `cqrs.query` (`SuperDashboardQuery`) and actual `db.ef.command` child spans that the Collector persisted in Tempo. No raw request/response body, SQL text/parameters or authentication artifacts are captured.

**Important boundary:** this is an actual System JWT request, NOT proof of the Angular BFF Cookie/OIDC-authorization-code browser flow or Tenant Cookie bridge. Those remain gated by `.github/workflows/tenant-bff-live-browser-e2e.yml` (trusted main only, with frontend connection), and their tracing will need a separate dedicated acceptance. No misleading “BFF end-to-end validated with TraceId” claim.

## End-to-end execution

1. Reuse `.github/workflows/tenant-iam-http-e2e.yml` disposable SQL service, explicit development fixture credentials and guarded `tests/Shinera.Identity.E2E.SqlMutator -- ensure-e2e-database/seed-tenant-iam`; no additional SQL databases or test-only HTTP endpoints.
2. **Explicitly enable** OTLP only inside this CI job via `Observability__Otlp__Enabled=true`, `Observability__Otlp__Endpoint=http://127.0.0.1:4317`, `Observability__TraceSampleRatio=1`. Development-only HTTP loopback is allowed by current source-owned configuration; production still requires approved HTTPS Collector.
3. Start the existing `observability/docker-compose.yml` Collector + Tempo + Prometheus in isolated bounded containers, and first assert actual OTLP ingest/store/scrape with the existing FR-18C synthetic smoke. That is a **stack readiness prerequisite**, not the proof.
4. Start the **real Web Host** on `127.0.0.1:5069`. Test `tests/http/fr18d3_real_trace_to_tempo_e2e.py` obtains captcha + signed System JWT with the real OpenIddict token endpoint and sends one authenticated `SuperDashboardQuery` request with a W3C `traceparent` and untrusted bogus `baggage`.
5. Compare HTTP `X-Trace-Id` to the **exact** request-sent W3C ID, then poll **Tempo /api/v2/traces/{traceId}** for the *real exported* data (bounded 100s; async exporter batch and collector delay expected).
6. Parse returned Tempo OTLP JSON `trace.resourceSpans[*].scopeSpans[*].spans`; validate an ASP.NET Core Server span, Foundation middleware Span, CQRS query Span with `foundation.cqrs.request_type=SuperDashboardQuery`, and multiple actual EF SQL spans. **Authorization/permission SQL that runs before Handler invocation is correctly parented to the Foundation HTTP pipeline; dashboard query SQL must be directly parented to CQRS**. Verify all SQL parentSpanIds resolve and true measured `startTimeUnixNano/endTimeUnixNano`. Each exported EF span must contain a bounded per-process HMAC fingerprint and `foundation.db.client=ef-core`.
7. Reject forbidden raw SQL attributes (`db.statement`, `db.query.text`) and a known unsafe query-string sentinel; reject malicious caller baggage and authorization/password leakage. The separate sampled traceparent on `/api/system/Auth/captcha/new` must not result in exported auth trace or `X-Trace-Id`.
8. After the trace proof, run the **unchanged existing** real Tenant IAM chain, public signup and audit tests, proving that enabling OTel does not bypass the existing signed Tenant JWT / SQL authorization workflow.
9. Always `docker compose ... down -v` to delete disposable trace/metric volume data. Never archive raw application logs as artifacts; only bounded Collector container diagnostics on failure.

## Validation and security properties

- No mock requests, in-memory database, bypass JWT or test-only application route. The existing System dashboard Handler actually executes IdentityDbContext SQL Count and Top-N queries. Existing SQL Server is created locally for disposable CI.
- Parent relations are evaluated by decoded real Span IDs, **not** merely asserting the presence of a list of names. A dashboard SQL span not parented to observed CQRS fails the test; pre-handler permission SQL may belong to the HTTP pipeline; an HTTP request with `X-Trace-Id` but no persisted SQL trace fails. Any 200 Tempo response with incomplete/partial spans keeps polling until complete or fails after deadline.
- SQL body, SQL parameters, response body, bearer JWT and OIDC credentials never enter the intended telemetry fields. A known query parameter sentinel is used solely for redaction validation.
- `TraceId` is correlation, **never** authority. The original SystemAdministrator policy and source-owned Feature Registry remain unchanged. Tracing does not create grants, subscription entitlements or external access to traces.
- Collector unavailable should not block business HTTP by FR-18B's bounded exporter and test; this suite intentionally *needs* a working Collector to prove persistence and will correctly fail if it is down.
- The Collector service and query API are development-local; the operator-only query facade, safer trace search, retention policy review, prod mTLS/object storage and audit are future tasks.
- No Dapper/SqlClient command span is asserted in this particular *System Dashboard* query because its actual SQL comes from EF Core; raw SqlClient/Dapper were tested with disposable real SQL in FR-18D-2.

## Running and evidence

Workflow: `.github/workflows/tenant-iam-http-e2e.yml` now runs both the FR-18C stack readiness test and `tests/http/fr18d3_real_trace_to_tempo_e2e.py` **inside the signed Tenant IAM real HTTP + SQL job**. Trigger on PR/source/observability changes and on main; no GitHub secret is copied into a new workflow. PR-head and post-merge **the same SHA** need successful `Tenant IAM real HTTP + SQL E2E` plus `BFF foundation CI` to call FR-18D-3 delivered.

Explicit CI config uses the existing disposable `Shinera_E2E_TenantIAM` database; no public persistent Staging URL or automatic DNS setup. Results are CI-local only. Existing `tenant-bff-live-browser-e2e.yml` remains the source of truth for real BFF Cookie/Angular, and is not automatically run on PR branches.

## Next phase

**FR-18E** — Add System-owned `control-tower` Resource under existing `system` PlatformCore Module, live server permissions and first-install seed, a bounded secret-safe read-only **authenticated query facade** for Tempo/metrics with no direct Angular/Tempo credentials, then responsive Angular System Admin Overview, Trace Explorer and real Waterfall/SQL views. Keep operator access audited and show `Unavailable`/`SampledOut` instead of fake empty telemetry.

Reference: [Control Tower architecture](../architecture/control-tower-observability.md), [FR-18D1 EF instrumentation](FR-18D1-CQRS-EF-SQL-SPANS.md), [FR-18D2 raw SQL](FR-18D2-RAW-SQLCLIENT-DAPPER-TRANSACTIONS.md), [Observability stack](FR-18C1-ISOLATED-COLLECTOR-STORES.md).
