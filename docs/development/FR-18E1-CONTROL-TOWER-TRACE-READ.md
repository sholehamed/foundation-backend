# FR-18E-1 — Secure System Control Tower TraceId read facade and Angular Explorer

**Date:** 2026-11-11  
**Status:** implementation PR; acceptance requires both repos' PR-head + post-merge main CI.  
**Scope:** first real **read-only single TraceId** lookup, registered under source-owned `system/control-tower` Resource; no arbitrary Tempo query syntax, no raw OTLP pass-through, SQL text/values, request bodies, or guessed revenue. Angular viewer is in Foundation frontend repo FR-18E1 PR.

## What the first slice actually does

- `FeatureRegistryDefinitions` gains `system/control-tower` (PlatformCore) and fixed page `/system/control-tower`. `CONTROL-TOWER.TRACES.READ` belongs exclusively to this System Resource. The *existing* first-install central bootstrap automatically gives reviewed new SuperAdmin role this code-owned Permission; **existing production roles are never silently regranted**. Existing installations require a reviewed catalog preview/apply and explicit grant. Do not assume every historical SuperAdmin has this new permission on upgrade.
- `Web` maps **GET `/api/System/ControlTower/traces/{traceId}`**; `WithFeatureEndpoint` registers `SYSTEM.CONTROL-TOWER.TRACE-DETAIL` metadata. The endpoint has the live `SystemAdministrator` policy **and independent mandatory** runtime checks for System scope, current SuperAdmin identity, exact active `system/control-tower` permission owner and `EffectivePermissionEvaluator` (including deny precedence). It does not depend on the optional global granular-permission rollout flag.
- `ControlTowerTempoTraceReader` is source-owned in `Modules.System.Observability`. Server-only `Observability:Read:Enabled` defaults **OFF**; separate `Observability:Read:TempoEndpoint` points to operator-owned private Tempo (production: HTTPS, development: HTTP only for loopback). This config is not returned by the API. No visitor can select a collector host, arbitrary URL, query expression, service, tenant, SQL or search predicate.
- Outgoing HTTP uses 3s timeout, max 8 concurrent connections per host, no redirects, cookies, proxies or automatic decompression. Response is processed only for HTTP 200/404 and limited to **1 MiB** before JSON parsing (max depth 24). Any malformed, huge, mismatched trace or unknown upstream response is `503 Unavailable`, never reflected to the caller. The maximum returned spans is 256; when exceeded, `PartiallyCaptured` + `truncated=true` are explicit, not a falsely complete trace.
- Every returned Span is a strictly typed, allowlisted projection: normalized ID and parent ID, source-approved operation name, category, UTC start, observed milliseconds, error flag and **only** a short list of non-sensitive `foundation.*` / HTTP route-template fields. Any unknown attributes (headers, query strings, SQL literals/text/parameters, user/tenant names, exception messages) are **dropped**. Fingerprints remain HMAC digests. Server-supplied operation names outside the known code-owned set become `http.server` rather than echoed raw.
- Success returns `Captured` or `PartiallyCaptured` JSON; missing/never-retained/expired Trace `404 {state:NotRetained}`; invalid 32-char W3C id `400`; unauthorized `401`; revoked or unrelated role/permission `403`; disabled/unavailable store `503`; throttling `429`. `Cache-Control: private, no-store`; fixed-window 15 per minute per signed subject, queue disabled.
- Frontend root `/system/control-tower` is lazy, RTL responsive, light/dark aware and uses existing System session/UI permission guard; only a validated TraceId is saved to URL query parameter, not tokens/body. Shows response-derived stats, category filters, Waterfall and selected redacted Span metadata, with explicit partial/forbidden/missing/disabled/unavailable states. Browser never calls `/api/v2/traces` or Tempo directly, no fake data.
- Backend telemetry API currently runs over the existing **System API bearer/BFF-compatible host auth** boundary. Dedicated full browser Cookie/OIDC BFF-bridge telemetry E2E is still required before claiming that end-to-end scope; the existing real HTTP signed JWT + SQL + Collector + Tempo job is distinct.

## Operator-only Development activation

```sh
Observability__Read__Enabled=true
Observability__Read__TempoEndpoint=http://127.0.0.1:3200/
```

`Observability__Otlp__Enabled=true` and its separately validated Collector endpoint remain independent. For any non-development read endpoint, HTTPS with private operator-controlled service account, network egress allowlist and TLS validation is essential; do not publish Tempo query ports on public interfaces. The existing local Compose stack intentionally binds Tempo to `127.0.0.1` only. A remote Production deployment has **not** been certified.

The first-install SuperAdmin receives permission via existing `InitialAccessBootstrap` only if the entire database/catalog is truly fresh; manual grants are intentionally required for prior installations. The feature catalog will appear as pending in existing production until reviewed sync; the API will deny reads until it is present and actively granted. A UI navigation entry alone does not grant read capability.

## Required tests / security checks

- `ControlTowerTempoReaderTests` (xUnit) — SSRF-ish URL rejections, fail closed disabled endpoint, strict trace ID shape, bounded body, Tempo 404/unavailable distinction, sanitization even when Tempo returns forbidden SQL/auth/user values, no direct untrusted redirects.
- `FeatureRegistrySyncTests`, `ModuleResourceContractsTests`, `FeatureRegistrySqlServerTests` updated for the 15th System Resource/Menu and 83rd root UiResource (exact first-install role ownership maintained).
- Existing `.github/workflows/tenant-iam-http-e2e.yml` explicitly opts the **disposable runner only** into Tempo reads. After real signed JWT HTTP/EF/SQL->Tempo lineage test, calls the actual **System API facade** and checks 200 sanitized Trace, 401 anonymous, 400 malformed and 403 Tenant; Tenant IAM permission/denial/first-signup tests still run.
- Angular build and Vitest service/route/component tests must pass on its own PR. No production fixture/pre-seeded trace bytes in Angular.

## Known gaps and explicit future work

1. Current endpoint is **single exact TraceId** only. No broad trace-search, Overview RED charts, SQL grouped aggregates, Tenant view, alert rules, arbitrary SQL execution, Query Store/exact plans, log ingest, detailed DTO or payload access.
2. **Durable access-to-trace audit is not implemented in this slice.** Only minimal structured result-state logging is emitted; these logs are not an immutable/audited historical record. Before exposing higher-risk DTO fields or enabling unrestricted operator search, FR-18E-2/F must implement an independently durable operator audit with reviewed retention, access policy, pagination and privacy controls.
3. No mTLS/least-privilege telemetry service credential wiring to hosted Tempo yet. The Dev-only no-auth localhost reader must not be mistaken for a secure production deployment. Configure private ingress/outbound TLS and least privilege separately.
4. The partial/capped span response reports deliberate truncation. A 404 cannot prove whether the trace was sampled out, expired or never emitted; the UI says “not retained”, not zero activity.
5. No signed browser full BFF-Cookie + Angular trace E2E certification on this PR. JWT real HTTP tests do not establish that.
6. Missing stored TenantId in standard spans is intentional. TraceId must **never** be treated as Tenant authorization. System SuperAdmin privileges are distinct from Tenant Owner. Restricted multi-tenant view requires separately approved Tenant-scoped projection.
7. No raw trace data is written to the business SQL database: Tempo persists it in its own dedicated volume; the existing central permission catalog remains in Identity SQL as ordinary metadata.

Next **FR-18E-2:** privileged trace search by bounded timeframe/source, durable view-audit store, explicit capture/permission detail tiers, cross-repo browser testing, and safe Overview/readiness metrics. Follow FR-18F/18G for controlled rich diagnostics, SQL analytics and alerting, FR-18H for production-ready storage and private authenticated access.

See [FR-18D3 real Tempo proof](FR-18D3-REAL-HTTP-SQL-TEMPO-E2E.md), [FR-18A architecture](../architecture/control-tower-observability.md), [central registry bootstrap](FR-16-FIRST-INSTALL-ADMIN-ACCESS-BOOTSTRAP.md).

## CI and privilege regression hardening — 2026-11-12

- Existing SQL clean-install test asserts both `CONTROL-TOWER.READ` (PAGE/menu) and `CONTROL-TOWER.TRACES.READ` (API) are seeded for the first Master `superadmin`, with no later auto-regrant. New real SQL test additionally exercises `EffectivePermissionEvaluator` as an actual first-login System account: direct deny overrides inherited role allow, revoking TRACE leaves PAGE, repeated bootstrap cannot restore rights, disabled account cannot read.
- Tempo reader tests also cover a 257-Span payload returning at most 256 with explicit `PartiallyCaptured`, plus unsafe producer names, SQL content, query-style route labels, invalid fingerprints and forbidden attributes being dropped.
- FR-14A static audit now requires the reviewed `control-tower-trace-read` limiter on the source-owned EndpointGroup, with a mutation-style negative test.
- Both expensive PR workflows use `concurrency.cancel-in-progress` keyed by workflow and PR, so future superseded executions can be canceled; the HTTP workflow watches first-login acceptance script changes. These changes **cannot** guarantee GitHub hosted runners or restore a blocked queue/billing limit.
- Latest-head acceptance still requires green BFF build + unit tests, SQL Server integration tests and real signed HTTP/Tempo tests. Queued workflows, absent Check Runs and local source inspections are **not** test passes. No local .NET/SQL test runtime was available for this change.

## FR-18E-1 post-run count regressions (2026-11-12)

- CI run [BFF #559](https://github.com/sholehamed/foundation-backend/actions/runs/43252077695) exposed two pre-existing fixture-count assumptions after introducing the 15th System root `system.control-tower`. In Identity snapshot tests: 83 total UI resources (47 PAGE / 36 ACTION), not 82 (46 PAGE / 36 ACTION); in combined System+Tenant SQL readiness: 21 active menu definitions (15 System / 6 Tenant), not 20.
- Fixed those test assertions and added an explicit fail-closed snapshot assertion that the Control Tower PAGE is not accessible without its grant. This is **test expectation maintenance**, not a production permission or seed change.
- Previous commit's real JWT/SQL/Collector/Tempo CI [#283](https://github.com/sholehamed/foundation-backend/actions/runs/43252077684) passed. Latest-head BFF/Identity/SQL and HTTP E2E CI remain the merge gate; do not infer green from an earlier commit.
