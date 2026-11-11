# FR-18B-1 — Host trace correlation and bounded opt-in OTLP

**Date:** 2026-11-11  
**Implementation status:** In progress in PR; verify PR-head / post-merge CI before treating as shipped.  
**Scope:** Host-owned OpenTelemetry .NET SDK, safe inbound ASP.NET Core tracing, one explicit nested pipeline Activity, safe request outcome metrics and logger correlation; OTLP trace+metric export opt-in. No new commercial module, EF interceptor, data warehouse, Controller, UI route or Identity migrations.

## What is actually implemented

- Added reusable `src/modules/Modules.System.Observability` project, with **no dependency on IdentityDbContext / Tenant entities** and no direct writes to business SQL.
- `src/Web/DependencyInjections.cs` calls `AddFoundationObservability` before other service registration. Host `RegisterApps` inserts `UseFoundationTelemetry()` **after** existing forwarded headers and **before** existing CORS/Identity/BFF middleware; original auth/permission/CSRF order is untouched.
- Pinned stable release `1.20.1` of OpenTelemetry Hosting, ASP.NET Core Instrumentation, Runtime Instrumentation and OTLP exporter in central `Directory.Packages.props`. SDK `ActivitySource("Foundation.Observability")` and `Meter("Foundation.Observability")` standardize future source-level modules without vendor-specific API.
- ASP.NET Core inbound spans and standard runtime/HTTP server metrics, plus explicit `foundation.http.pipeline` child span and two low-cardinality metrics `foundation.http.requests.completed` and `foundation.http.pipeline.duration`.
- `X-Trace-Id` is an opaque **correlation reference only**, emitted via `OnStarting` for allowed routes. W3C `traceparent` propagates through ASP.NET Core trace context, and all incoming `Activity.Baggage` is removed before application dispatch because it is not authenticated. Tenant or role is **not** taken from tracing headers.
- Safe request logger logs HTTP method normalized to bounded enum, **server-defined** route template, status and duration for failures; Debug for normal operations. Uses ActivityTrackingOptions TraceId/SpanId. No body, headers, SQL text, SQL params, username, full URL or query string. No OTLP logging export of existing unreviewed `ILogger` messages.
- `FoundationTelemetryPrivacy` removes known-dangerous URL/header/SQL/exception/identity Activity tags before export, and forces ASP.NET Core exported span names to normalized method + server route template. Auth/OIDC/login/first-password-related paths are excluded from FR-18B export; safe tracing for these flows requires separate FR-18D/FR-18F security approval.
- OTLP gRPC `traces+metrics` **disabled by default**, with explicit `Observability:Otlp:Enabled=true` and endpoint required. Endpoint rejects credentials/query/fragment; HTTPS required except Development HTTP. Traces exported through asynchronous bounded batch (max queue 512, batch 64, max export wait 1s), metrics periodic 15s with 1s timeout. `TraceSampleRatio` defaults to 0.1 and validates [0..1]; no untrusted upstream forced-sampling rule. HTTP calls do not synchronously wait for Collector.
- No localhost/production Collector or Tempo/Loki/Prometheus deployment yet. Without the Collector, the app still runs; no trace search UI or persisted telemetry is claimed.

## Configuration

Config (safe baseline):
```json
{
  "Observability": {
    "ServiceName": "foundation-web",
    "TraceSampleRatio": 0.1,
    "Otlp": { "Enabled": false }
  }
}
```

**Development only**, to connect an **operator-owned, reachable** gRPC Collector on loopback:
```sh
Observability__Otlp__Enabled=true
Observability__Otlp__Endpoint=http://127.0.0.1:4317
Observability__TraceSampleRatio=1
```
In Production, use HTTPS with an approved private/internal Collector, configured via environment secrets, network policies and egress allowlist. Exporter is **not** activated by `OTEL_EXPORTER_OTLP_ENDPOINT` alone. Do not put telemetry credentials in source, browser, or public config.

Sampled spans can still be lost when Collector is unavailable or exporter queue exceeds bounded capacity. `X-Trace-Id` only proves a request correlation identifier, **not that a complete retained trace exists**. No sampled-out or ingestion status API in this phase.

## Instrumentation and trace boundaries

- Root inbound `Microsoft.AspNetCore` Activity is created by standard ASP.NET Core instrumentation; `foundation.http.pipeline` is a child emitted by source-owned request middleware. Future CQRS/handlers and SQL commands must explicitly register their own child Activities through common `FoundationTelemetry.ActivitySource`.
- FR-18B does **not** claim visibility of all methods, DTO inputs/outputs, EF/Dapper/SqlClient commands, SQL transactions, Outbox/background jobs, browser spans, or exact Query Store plan.
- No raw body/SQL/DTO fields; even administrator cannot temporarily enable them before reviewed diagnostic policy and durable access-audit implementation.
- Auth routes are intentionally excluded from application spans and X-Trace-Id support references until safe auth-aware instrumentation is designed.
- OTel SDK/export in process is independent of primary business DB, but a full storage isolation guarantee requires Collector/Tempo/Loki/metrics deployment in FR-18C.
- Logging providers may independently log their own content; this change does not make third-party/provider logs automatically secret-free. Existing console logs remain existing behavior; FR-18F will require policy tests before any raw log streaming to telemetry storage.

## Acceptance evidence / code test boundaries

`tests/Shinera.Identity.Tests/FoundationObservabilityHostTests.cs` covers:
- Valid W3C traceparent returned as `X-Trace-Id` with matching in-request Activity, plus real TestServer 200/403/500 without external Collector.
- Untrusted `baggage` stripped before application dispatch; no header/body values intentionally read by the telemetry middleware.
- Redaction of danger-tag SQL/header/url/exception attributes and excluded authentication paths.
- Export opt-in and invalid/unsafe production endpoint and sampling-rate rejection.
- Unreachable local OTLP collector does not prevent a 403 business response; exporter remains asynchronous.

**CI:** solution Build and `Shinera.Identity.Tests` plus existing BFF/SQL tests, then PR-head/main. These tests are isolated HTTP TestServer, **not yet real production BFF+OpenIddict two-Tenant telemetry E2E**. FR-18C/18D adds actual end-to-end data flow.

## Next implementation slices

- **FR-18B-2** (if needed): widen to safely sanitized outbound HttpClient/BFF Activity boundaries and fail-open load tests under real SQL/BFF; no tokens.
- **FR-18C**: private independently stored Collector and backend trace/metric ingestion with retention and query authorization.
- **FR-18D**: safe CQRS, EF Core/Dapper/SqlClient query/transaction instrumentation, isolated SQL fingerprint+N+1 detection.
- **FR-18E**: central `system/control-tower` Feature Registry with System permissions, read-only trace API, Angular Request Explorer, honest missing/partial states.
- **FR-18F/H**: protected capture policies and audits, privacy, load and staging certification.

See [FR-18 roadmap](FR-18-CONTROL-TOWER-ROADMAP.md) and [ADR-0003](../decisions/ADR-0003-control-tower-observability-isolation.md).
