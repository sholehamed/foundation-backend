# FR-18C-1 — Isolated, real Collector + Tempo + Prometheus telemetry stack

**Date:** 2026-11-11  
**Status:** implementation branch; **do not mark shipped until PR-head and post-merge main smoke both succeed**.  
**Scope:** actual disposable/development telemetry ingestion and persistence, never raw data in Foundation Identity/Tenant SQL; no Angular UI/query facade or SQL-method instrumentation yet.

## 1. What is deployed by this slice

The repo contains an operator-run **development-only** Compose stack at `observability/docker-compose.yml`:

| Container | Image tag (fixed) | Purpose | Host exposure |
| --- | --- | --- | --- |
| `otel-collector` | `otel/opentelemetry-collector-contrib:0.164.0` | Receive OTLP gRPC/HTTP, bounded in-memory processing, redact known sensitive span attributes, batch+retry+export | `127.0.0.1:4317` gRPC, `127.0.0.1:4318` HTTP, `127.0.0.1:13133` health |
| `tempo` | `grafana/tempo:3.1.1` | Persist and query sampled traces in a **separate Docker volume**, 7-day block retention | `127.0.0.1:3200` query/readiness; OTLP receiver only on private Docker network |
| `prometheus` | `prom/prometheus:v3.16.0` | Scrape Collector's in-memory app metric exporter on internal port 8889, keep metrics in separate volume | `127.0.0.1:9090`; configured 7-day/512 MB TSDB retention |

- Fixed Docker images; no `:latest` tags. For production, approve image digest pinning and scan images.
- `mem_limit`, `cpus`, `pids_limit`, non-root where supported by vendor image, `no-new-privileges`, private bridge network and read-only configuration bind mounts. These limits are development budgets, **not an audited production capacity guarantee**.
- Raw trace/metric persistence uses Docker named volumes `tempo_data`, `prometheus_data`. **Zero** raw telemetry tables/migrations are added to the primary SQL Server.
- The Collector has `memory_limiter` (192 MiB plus 48 MiB spike), `batch`, extra `attributes/drop_secrets` processor, and a bounded `otlp_grpc/tempo` sending queue (256 queued batches; 2 consumers; retry max elapsed 15s). Full queues/retries can **drop telemetry**. No promise of lossless storage and no disk-backed queue yet.
- The Collector has no `logs` pipeline: *intentionally*. Existing `ILogger` messages are not reviewed for PII and must not be sent to a generic log store without a separate policy/capture review. Loki integration and log storage remain later FR-18F/G.
- All OTLP ingest/query/health ports bind to IPv4 `127.0.0.1` on the developer machine; **the compose file is NOT suitable for public or multi-host production deployment**. Docker bridge network allows container-to-container HTTP/gRPC without TLS **only in this isolated dev environment**.
- The Foundation App still defaults `Observability:Otlp:Enabled=false`; installing this Compose stack does **not** silently switch on app telemetry. No production OpenIddict changes, new permissions or new System Admin route.

## 2. Local start and safe connection

Prerequisites: Docker Engine + Compose v2, local ports 3200/9090/4317/4318/13133 free, at least ~2 GiB available for containers and disk volumes.

```sh
docker compose -f observability/docker-compose.yml config -q
docker compose -f observability/docker-compose.yml up -d
docker compose -f observability/docker-compose.yml ps
python3 observability/smoke.py
```

**Development only** when running the Foundation backend on the host (not inside Docker), configure:

```sh
Observability__Otlp__Enabled=true
Observability__Otlp__Endpoint=http://127.0.0.1:4317
Observability__TraceSampleRatio=1
```

Set these through your terminal environment, local launch profile or user secrets. If you start Foundation in a separate container, `127.0.0.1` refers to that *container*, not the Collector; explicitly attach it to the Compose `telemetry` network and use the internal `otel-collector:4317` endpoint under a reviewed development-only configuration. Do **not** publish the OTLP receiver through the Internet.

Default Developer endpoints:
- `http://127.0.0.1:13133/` -> Collector readiness/health (response 200).
- `http://127.0.0.1:3200/ready` -> Tempo readiness.
- `http://127.0.0.1:3200/api/traces/{traceId}` -> Tempo internal trace lookup (developer-only, no authenticated System facade).
- `http://127.0.0.1:9090/-/ready` -> Prometheus readiness; `/api/v1/query` -> queries (developer-only).
- `http://127.0.0.1:4318/v1/traces` and `/v1/metrics` -> test OTLP HTTP ingress.

Run `python3 observability/smoke.py` after startup. The test generates a random valid W3C trace ID and a synthetic span+metric, **never customer data**. It exercises:
1. Collector health, Tempo readiness, Prometheus readiness with bounded wait.
2. Actual JSON OTLP/HTTP trace ingest into Collector.
3. Actual asynchronous export + Tempo trace retrieval by TraceId.
4. Defense-in-depth Collector removal of a known synthetic `url.full`/Authorization secret marker.
5. Actual OTLP metric ingest + Prometheus scrape detection.

All smoke assertions are executable; **healthy containers alone are not acceptance evidence**. This test is separate from and does not replace FR-18B Web TestServer tests or future real signed BFF+SQL instrumentation tests.

## 3. Retention, loss, backpressure and storage

- Tempo `compactor.compaction.block_retention: 168h` (7 days) for local block storage; recently ingested spans may not appear immediately, so smoke polls query availability. Retention is per block; do not imply exact individual deletion at seven days.
- Prometheus `--storage.tsdb.retention.time=7d` and `--storage.tsdb.retention.size=512MB` are storage limits, **not** a guarantee against disk-full during head/WAL activity. Monitor disk consumption separately.
- Docker volumes survive `docker compose down` and restarts. `docker compose ... down -v` irreversibly destroys all dev traces and metrics; CI uses `-v` deliberately.
- Collector queue and batch in-memory buffering are bounded; during Tempo outage, retry expires and data may be dropped. FR-18H must add and test drop metrics, backpressure/chaos dashboards and operational paging. Collector outage and downstream outages must not block the app; FR-18B tests local unreachable Collector, but **end-to-end production** outage tests remain FR-18H.
- Known-dangerous span attributes are removed by app **before** export and again in the Collector, but there is not a universal guarantee against arbitrary PII in custom OTLP payloads; only reviewed source-owned fields can be emitted. Collector dev OTLP accepts local producers without authentication.

## 4. Operator controls and rollback

See current state:

```sh
docker compose -f observability/docker-compose.yml ps
docker compose -f observability/docker-compose.yml logs --tail 100 otel-collector tempo prometheus
```

Stop without data loss: `docker compose -f observability/docker-compose.yml down`.

Discard dev telemetry intentionally: `docker compose -f observability/docker-compose.yml down -v`.

To disable Foundation outbound telemetry without altering any SQL schema or app authorization: set `Observability__Otlp__Enabled=false` and restart only the API process. Trace correlation and safe local metrics remain configured by the FR-18B Host independently of the Collector.

Failure meanings:
- Collector unavailable: ingestion unavailable; app keeps working. Don't claim zero errors or full observability.
- Tempo unavailable: Collector may accept OTLP temporarily but cannot confirm durable trace storage; `queue` may drop after timeout.
- Prometheus unavailable: the Collector's in-memory metric exporter can still accept data but scraping/persistence stops; missing data is **not zero**.
- Sampling off: TraceId remains a correlation reference but there may be no retained trace. Never infer security or business correctness from telemetry.

## 5. CI gate and evidence

New workflow `.github/workflows/fr18c-observability-smoke.yml` runs on changes to `observability/**` or itself and manually, both PR and post-merge main:
- `docker compose config -q`
- Start real pinned Collector, Tempo and Prometheus containers in isolated GitHub runner.
- Run `python3 observability/smoke.py` to validate trace and metrics ingestion/redaction/query.
- On failure, output bounded logs from all services and fail the workflow.
- Always `docker compose down -v --remove-orphans` to prevent storage from leaking between runs.

**Note:** this workflow does not attest to a permanently hosted Staging endpoint. FR-18C POC is an ephemeral disposable Docker environment, not full Production. Existing `BFF foundation CI` and `Tenant IAM real HTTP + SQL E2E` should remain green on repo main.

## 6. Remaining FR-18C / FR-18D / FR-18E tasks

1. Link a running Development Foundation API request to an actual persisted Tempo Trace via approved OTLP config, with signed BFF+Tenant SQL E2E correlation; the smoke currently proves real telemetry **stack** ingestion with synthetic OTLP only.
2. Implement independently secured production deployment, mTLS/collector auth/ingress, external object storage and backups, quotas and privacy/compliance policy. This Compose stack deliberately lacks them.
3. Logging pipeline + Loki after explicit safe `ILogger` capture and redaction audit; no raw logs currently flow.
4. FR-18D: CQRS and Domain method Activities, EF+Dapper/SqlClient SQL command and transaction spans, no credentials or values.
5. FR-18E: System permission-checked read facade and Angular Control Tower. Developer Tempo/Prometheus endpoints are **never** browser-accessible in production.

For source architecture: [FR-18A](../architecture/control-tower-observability.md), [ADR-0003](../decisions/ADR-0003-control-tower-observability-isolation.md), [FR-18 Roadmap](FR-18-CONTROL-TOWER-ROADMAP.md).
