# FR-18 — Control Tower telemetry, API and diagnostic policy contracts

**Date:** 2026-11-11  
**Status:** PROPOSED CONTRACTS ONLY — no implementation, endpoint, migration, active permission or enabled collection.  
**Owner:** Foundation backend, source-owned contract (not database-defined executable definitions).  
**Related:** [Architecture](../architecture/control-tower-observability.md) · [ADR](../decisions/ADR-0003-control-tower-observability-isolation.md).

## 1. Canonical names, identities and timestamps

| Attribute | Type | Semantics / constraints | Placement |
| --- | --- | --- | --- |
| `traceId` / `trace_id` | 32 lowercase hex characters | W3C identifier; validated, never an authorization bearer | trace/span/log; read API |
| `spanId` / `span_id` | 16 lowercase hex | Exact span identity, not request identity | trace/span/log |
| `parentSpanId` | nullable 16 lowercase hex | Only direct parent; use Span Links for asynchronous causality | span |
| `startUtc`, `endUtc` | ISO-8601 UTC with offset | Backend stores UTC; Angular shows Jalali where relevant | span/read API |
| `durationMs` | non-negative decimal | Monotonic elapsed, not endUtc-startUtc if clocks drift | span/read API |
| `serviceName`, `serviceVersion`, `environment` | low-cardinality strings | Source-generated from process/service metadata | resource |
| `operationKey` | bounded source-owned key | Stable route/Handler/operation name, not user URL or SQL text | span |
| `httpRoute` | sanitized route template | e.g. `/api/tenant/workspace/members/{id}`, not actual ID | span |
| `tenantRef` | internal opaque GUID | **Server-derived authenticated Tenant** only; restricted field/query, **not a metric label** | restricted trace index |
| `actorRef` | internal opaque GUID | Server-derived authenticated user; never browser asserted | restricted trace index |
| `status` | stable enum | Ok / Error / Cancelled / Unknown | span/read API |
| `captureState` | enum | Captured/PartiallyCaptured/SampledOut/Expired/Redacted/NotEnabled/Unavailable | query response |
| `sqlFingerprint` | bounded digest/string | Normalized query signature, no unapproved SQL literals or identifiers | SQL span |
| `sqlCommandKind` | enum | SELECT / INSERT / UPDATE / DELETE / EXEC / OTHER | SQL span |
| `dbSystem` | stable string | `mssql`; no credentials or server address from connection string | SQL span |
| `domainErrorCode` | bounded source-owned key | Business-safe code; never arbitrary exception.Message | span/log |
| `sessionRef` | server-generated opaque ID | Diagnostic Session linkage; not controlled by public header | restricted trace |
| `policyVersion` | positive integer | Freeze capture semantics in emitted telemetry | event/read API |

Never add query text, tenant id, user id, `trace_id`, `span_id`, SQL parameter values, endpoint URLs or exception messages as Prometheus metric labels. High cardinality must remain queryable through restricted trace/log indexes, not metrics dimensions.

## 2. Read API envelope (stable concept, exact OpenAPI later)

Recommended result shape (informational example, not shipped JSON):

```json
{
  "requestId": "01J...opaque-server-id",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "captureState": "PartiallyCaptured",
  "sampled": true,
  "ingestionAsOfUtc": "2026-11-11T12:30:00Z",
  "fromUtc": "2026-11-11T12:25:00Z",
  "toUtc": "2026-11-11T12:35:00Z",
  "nextPageToken": null,
  "warnings": ["request_body_not_collected", "sql_parameters_redacted"],
  "data": []
}
```

All example data is illustrative. HTTP API future behavior:
- `200`: query performed; `data=[]` is genuine empty for evaluated window only and `ingestionAsOfUtc` included.
- `400`: invalid TraceId/filter/unsafe page token, or unbounded time window; never raw provider errors.
- `401`: no authenticated System identity; `403`: valid identity without live granular permission; `404`: not found **or not visible** (avoid tenant enumeration).
- `409`: policy version conflict on preview/apply; no partial policy changes.
- `429`: bounded query cost or rate limit.
- `503`: telemetry backend disconnected/unavailable; **not** an empty healthy result.
- Headers: `Cache-Control: no-store`, no sensitive exception detail, no backend credentials.
- All `fromUtc`/`toUtc` explicit offset-aware timestamps. Max range defaults (candidate): 24h traces, 72h aggregates, hard server caps; server-side pagination 50 default/200 max, opaque signed expiring tokens bound to actor+scope+filter hash and policy version.
- System facade `IControlTowerQueryService` may depend on provider interfaces `ITraceRepository`, `ILogSearchRepository`, `IMetricsQueryRepository`, `IDiagnosticAuditWriter`. No direct vendors in System controller DTOs.

## 3. Trace detail object

```json
{
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "captureState": "Captured",
  "serviceCount": 2,
  "spanCount": 6,
  "root": {
    "spanId": "00f067aa0ba902b7",
    "operationName": "POST /api/appointments",
    "serviceName": "foundation-web",
    "startUtc": "2026-11-11T12:30:00.000Z",
    "durationMs": 2400.25,
    "status": "Ok"
  },
  "spans": [
    {
      "spanId": "00f067aa0ba902b7",
      "parentSpanId": null,
      "links": [],
      "category": "HttpServer",
      "operationName": "POST /api/appointments",
      "serviceName": "foundation-web",
      "startUtc": "2026-11-11T12:30:00.000Z",
      "durationMs": 2400.25,
      "selfDurationMs": 90.1,
      "status": "Ok",
      "hasSafeAttributes": true
    }
  ],
  "warnings": []
}
```

The sample is a UX fixture, NOT a claim of a currently deployed appointments module. Trace waterwalls/relative bars are computed using time ranges, not sum of child durations. Partial traces may be missing parent spans because of sampling or expiration, and must show `PartiallyCaptured`. Span-specific safe input/output fields are queried only after elevated authorization and written access audit.

## 4. SQL event and query fingerprint contract

```json
{
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "spanId": "00f067aa0ba902b7",
  "dbSystem": "mssql",
  "dbContextName": "AppointmentsDbContext",
  "sqlCommandKind": "SELECT",
  "sqlFingerprint": "sha256:bounded-digest-placeholder",
  "normalizedSql": "SELECT ... WHERE TenantId = @tenant",
  "normalizedSqlCaptureState": "Redacted",
  "parameterDescriptors": [
    {"name": "@tenant", "dbType": "UniqueIdentifier", "value": null, "captureState": "Redacted"}
  ],
  "executionCount": 1,
  "durationMs": 38.6,
  "rowsAffected": null,
  "transactionOutcome": "Unknown",
  "retryCount": 0,
  "exceptionCode": null,
  "queryStoreReference": null
}
```

- SQL text is withheld by default; normalization must suppress literals/comments, bounded length, avoid colliding customer values. SQL fingerprint is derived by reviewed canonical normalization, normalized source key and hash algorithm/version. Do not consider the digest cryptographic anonymization if SQL reveals table/column names.
- Query-plan links, if available, refer to **aggregate Query Store fingerprint/time-window candidate**, not proof of a single command's plan. `queryStoreReference=null` explicitly means unavailable; never fabricate CPU/reads, wait types or rows returned.
- `rowsAffected` may be unavailable/negative for reader commands and must be null rather than guessed.
- Duplicate SQL Activity sources must be detected using command-id/context correlation: exactly **one** canonical DB span per physical command execution (retries have separate attempts or clear attempt count).
- SqlParameter values, connection strings and raw SQL literals are secret/PII risks; debug rendering must not use interpolated query string. Approved value-capture policy excludes tokens, emails, personal IDs, passwords and arbitrary JSON/text by design.

## 5. Captured DTO field provider and classification

```csharp
// Proposed source-owned interfaces only. Not currently in the codebase.
public interface IDiagnosticFieldsProvider<in T>
{
    IReadOnlyDictionary<string, SafeDiagnosticValue> ExtractSafeFields(
        T input, DiagnosticCaptureContext context);
}
public sealed record SafeDiagnosticValue(
    string? Value, string Classification, bool Truncated);
```

- A *per-DTO provider* extracts an explicit allowlist of properties. Unknown fields are never serialized. Providers must never traverse arbitrary object graphs.
- Classifications: `PublicMetadata`, `InternalMetadata`, `TenantBusiness`, `PersonalData`, `CredentialOrSecret`, `FinancialSensitive`. Production capture can be configured only for fields with an explicitly reviewed safe classification and redactor; `CredentialOrSecret` always dropped.
- Response DTOs get independent providers; a sanitized request DTO does not imply its response is safe to capture.
- Diagnostics payloads must carry `captureState` and `truncated` flags. Redactors run before data enters SDK/Collector queues. Max-field length, max field count and max event bytes are checked *before* materialization.
- Response bodies must never be captured through catch-all exception logging on auth/recovery/checkout/upload endpoints.

## 6. Capture policy and diagnostic session domain contracts

Policies are **System centrally defined** and must be validated against source-declared endpoint keys. Unrecognized endpoint/module = deny/ignore, not an “all endpoints” wildcard.

Proposed `CapturePolicy` fields:
```text
PolicyId GUID
Version int (optimistic concurrency)
Environment enum (Development, Staging, Production)
ServiceKey code-owned
EndpointKey code-owned, nullable for safe standard defaults
Mode enum (Standard, Detailed)
ApprovedFieldProfileKey code-owned, nullable
RequestSampleRate decimal [0,1]
MaxSpansPerTrace, MaxEventsPerRequest, MaxAttributeBytes
MaxIngressBytesPerMinute, MaxDiagnosticBytesTotal
EffectiveFromUtc, ExpiresAtUtc
ApprovalState enum (Draft, Reviewed, Active, Disabled, Expired)
ApprovedBy/ReviewedBy identifiers, ReasonCode, LastChangedUtc
```

Proposed `DiagnosticSession` fields:
```text
SessionId GUID; TenantId GUID? (optional narrowly scoped)
ServiceKey / EndpointKey (source-owned required)
InitiatorUserId, ReviewerUserId, JustificationCode
CaptureMode = Detailed; StartUtc; ExpireUtc; StopUtc
MaxRequests; MaxCapturedBytes; CapturedRequestCount
PolicyVersion; Status; AuditCorrelationId
```

Rules:
- Production diagnostics: short bounded TTL, hard cap (candidate: <=15 minutes and <=100 requests; initial design target, not legal/legal limit), 2-person review for sensitive exposure, cannot select all Tenants/all services, cannot bypass permanently denied fields.
- Preview/Apply uses the same central Registry preview-drift and effective role checks as other System permission surfaces; no database-authored executable rules.
- Applied policy is versioned, signed/validated and centrally distributed with monotonic version. Expired versions must fail to safe Standard, not stay Detailed.
- All create/approve/start/stop/extend/read-payload/export actions write durable `ControlTowerAuditEvent` with who, when, scoped target, reason, policy version and outcome, **not** captured body.
- A diagnostic session never retroactively fills earlier missing traces. Scope selection is only via authenticated server context and operator privileges.

## 7. Event, availability and incident statuses

- `TelemetryPipelineHealth`: `Healthy`, `Degraded`, `Unavailable`, `Unknown` with latest ingest time and backlog/drops; avoid health based solely on HTTP 200 from the query facade.
- `IngestionGap`: `NotExpected`, `Sampling`, `BackendUnavailable`, `OverBudget`, `Expired`, `Unknown`.
- `ControlTowerIncident`: event ID, rule ID, severity, service, normalized route, started/end UTC, current state, deduplication key and safe diagnostic links. Integrates later with Notification Center via **outbox/event contract**; no unconditional SMS/email from a request middleware.
- Unknown sampling state must not be described as full trace. `traceId` alone cannot establish an event happened or reveal its Tenant.

## 8. API security and UX contract

Roles:
- `Overview.Read` -> aggregate health only.
- `Traces.Read` -> redacted trace list/detail with System-specific scope.
- `Sql.Read` -> normalized SQL and approved metadata; separately audited.
- `Diagnostics.Manage` -> policy preview/approved session lifecycle; dual-control high-risk.
- `Audit.Read` -> administrative diagnostic access/change history.
- `Alerts.Manage` -> later phase.

These labels describe conceptual permissions and **are not seeded** by this document. Later central manifest registration/first-install SuperAdmin grants require explicit reviewed PR and tests. Enforce on server, not only in Angular navigation. BFF session checks and CSRF apply to diagnostic writes. Never expose arbitrary SQL execution/trace backends to browsers.

Facade returns sanitized/permission-filtered data; `traceId` lookups always use a filter imposed by server-granted scope. Export requires explicit permission, bounded volume, watermark/attribution, TTL, encryption as relevant, and durable export audit.

## 9. Operational and developer test matrix

| ID | Scenario | Minimum expected evidence |
| --- | --- | --- |
| OT-001 | Request A, handler, EF SQL success | Same TraceId, parent links, UTC and duration, one SQL span |
| OT-002 | Dapper/SqlClient transaction with rollback | SQL spans and Rollback outcome without inserted Audit rows |
| OT-003 | Client forged TenantId/baggage/TraceId | Scoped Tenant independent from metadata; 403/404 foreign data |
| OT-004 | Password and Refresh Token fields present | No secret bytes in SDK buffers, Collector, exporter files or UI |
| OT-005 | Temporary Diagnostic Session | TTL, source endpoint allowlist, cap and approval enforced |
| OT-006 | Session stops/expires during active traffic | Future captures disabled safely; audit persisted |
| OT-007 | Backend outage / storage unavailable | Commercial operations unaffected; UI 503/Unavailable |
| OT-008 | Two Tenants interleaved | No cross-Tenant raw events; no TenantId metrics label |
| OT-009 | 1,000 same-fingerprint SQL in one request | Bounded event capture + N+1 candidate; never unbounded memory |
| OT-010 | Sampled-out/expired trace | Honest `SampledOut`/`Expired` UI where determinable |
| OT-011 | System permission revoked | Next read denied by backend despite stale Angular page |
| OT-012 | Roleless user guesses a traceId | No data, query cost cap and no enumeration oracle |
| OT-013 | Long asynchronous Queue job | Span Link to producer trace, not false synchronous child |
| OT-014 | High load normal vs telemetry-enabled | Meets measured p95/CPU/memory/drop budgets |
| OT-015 | SQL Query Store information | Label aggregate historical, never exact per-request execution plan |
| OT-016 | Replay/duplicate SQL diagnostics from multiple hooks | Exactly one canonical physical execution span |
| OT-017 | Operator examines redacted payload | Durable per-access audit, exact policy snapshot/version |
| OT-018 | No deployed Collector | No false success claim; telemetry feature reports NotConfigured |

## 10. Interface evolution and versioning

- `schemaVersion` on serialized diagnostic documents; `policyVersion` on capture events.
- Changing a field from optional to required requires versioned API and client support. New Capture fields default **off**.
- OpenTelemetry semantic conventions can evolve; normalize at ingest/query facade, not expose raw vendor attribute names to Angular.
- Avoid combining `unknown`, `not_collected`, `redacted`, `expired` into a UI zero or empty string.
