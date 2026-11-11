# FR-18D-2 — Source-owned SqlClient, Dapper and transaction diagnostics

**Date:** 2026-11-11  
**Status:** Implementation branch; PR-head and main CI acceptance required before calling shipped.  
**Scope:** Raw SqlClient and explicitly opted-in Dapper operations; authoritative transaction commit/rollback when observed; no duplicate EF Core SQL spans, no secrets in telemetry. No new business schemas, roles or APIs.

## 1. Contract and design

FR-18D-1 introduced EF Core DbCommand interception and CQRS Handler Activities. Many Foundation operations bypass EF Core command interception by calling `new SqlCommand(...).Execute*Async`, particularly atomic quota/metering, BFF ticket storage and privileged denial audits. A global SqlClient DiagnosticListener plus EF's own hook would risk **duplicating one physical SQL operation**. FR-18D-2 instead uses **explicit source-owned wrappers**, never registering a global SqlClient diagnostic listener:

- EF execution owner: `FoundationEfCommandInterceptor` from FR-18D-1.
- Raw `Microsoft.Data.SqlClient.SqlCommand` owner: `Infrastructure.SharedKernel.Observability.SourceOwnedSqlTrace`.
- Dapper owner: `SourceOwnedSqlTrace.RunDapperAsync` around an actual caller-owned Dapper operation; Dapper does *not* create another EF span.
- No automatic instrumentation of arbitrary third-party SqlClient commands. A new source-owned module or raw SQL repository must call the wrapper or accept an explicit coverage gap.

All raw SqlClient/Dapper spans are published by `ActivitySource("Foundation.SqlClient")`, subscribed to by `FoundationObservabilityRegistration` (part of existing .NET Host). They descend from a currently **recorded** `Activity.Current`; the wrapper never creates a trace root for standalone SQL and does not override sampling, Tenant context, Auth/CSRF or caller transactions.

**Auth/OIDC exclusion (security hardening):** `SourceOwnedSqlTrace.CanCapture()` requires a code-owned `foundation.http.pipeline` Activity ancestor before emitting any EF/raw/transaction SQL child beneath an ASP.NET Core inbound root. The existing FR-18B middleware intentionally creates that Activity only for permitted routes, so a forged W3C `traceparent` with sampled flag cannot by itself turn on SQL child recording for sensitive `/api/system/Auth/*` or `/bff/auth/*` paths. Background work without an ASP.NET ancestor still requires an actual recorded parent. This is a source-owned emission gate, **not an authorization mechanism** or a guarantee that all application logs are safe.

The wrapper is a pure `System.Diagnostics` extension in `Infrastructure.SharedKernel`, with no new external logger exporter or SQL persistence. It does not own connections, does not read results, does not mutate query text/parameters or isolation level, and does not retry operations on its own.

## 2. Runtime integration

| Existing real production source | Operation now instrumented | Scope |
| --- | --- | --- |
| `TenantAtomicUsageService` | SqlClient `ExecuteReaderAsync`, `ExecuteNonQueryAsync`, `ExecuteScalarAsync` | Quota app-lock, balance/event read/write, reversals. Existing Serializable transaction left intact |
| `TenantCommercialOperationExecutor` | long-lived `db.sqlclient.transaction` observation | One commercial atomic transaction: commit confirmed after awaited `CommitAsync`, commit-outcome-unknown once commit attempted, `not_committed` for early return |
| `PlatformAccessDenialAudit` | `ExecuteNonQueryAsync` | Privileged audit SQL only when request is already sampled/recorded; audit durability is independent from lossy telemetry |
| `SqlServerBffAtomicTicketStore` | Scalar, Reader and NonQuery methods | Session save/renew/read/revoke/startup schema validation. OIDC/auth paths excluded by FR-18B filtering: don't assume sensitive auth SQL always captured |
| `SqlServerBffSessionCleanup` | NonQuery execution | Existing separately managed maintenance connection and row limit preserved |
| Future commercial module using Dapper | `RunDapperAsync<T>(sqlTemplate, () => connection.QueryAsync(...))` | Opt in only. No production Commercial module added for this PR |

The Dapper wrapper is exercised in a **real disposable SQL Server test**, but no present production Dapper callsites require modification. Dapper parameters are not captured. For Dapper calls returning unmaterialized readers, the observed `Task` duration **does not** include subsequent enumeration unless that enumeration is inside the delegate.

## 3. Event and fingerprint contracts

Raw SQL event:
- Name: `db.sqlclient.command`; type: `Client`.
- Low-cardinality tags: `foundation.db.client` (`sqlclient` / `dapper`), `foundation.db.execution` (`reader`, `scalar`, `nonquery` or `dapper`), `foundation.db.command_type`, `foundation.db.parameter_count` (SqlCommand only), `foundation.db.outcome` (`success`, `failed`, `canceled`) and optional `foundation.error.type` (safe CLR exception *type*, not Message/StackTrace).
- Duration is the **actual asynchronous delegate execution time** between call and completion; no fictional start timestamp. It includes client command execution but not external reader enumeration after `ExecuteReaderAsync` returns.
- SQL command text is **never placed in Activity tags/name/logs**. A per-process 256-bit random HMAC key hashes at most 32,768 characters of command text and emits a bounded hex fingerprint and `foundation.db.fingerprint_scope=process_sqlclient`. Input is not normalized. This HMAC is intentionally different from EF Core's existing `process` fingerprint domain; **do not group EF and raw SQL by digest**. Fingerprints are not stable between processes or deployments and may vary for SQL containing literals. Do not claim universal N+1 or historical cross-worker analysis yet.
- If command text exceeds cap or is empty, report `foundation.db.fingerprint_state=not_collected`, not a guessed hash. No actual database name/connection string, parameter **values**, SQL result, DTO, user ID, Tenant ID, HTTP body, Query Store plan or arbitrary exception text.
- Exception handling always rethrows the same exception after marking activity error; cancellation is distinct from failure. Null arguments rejected before any operation.

Transactions:
- `SourceOwnedTransactionTrace.Start("commercial_atomic")` opens an operation-level span (no business input), sets `not_committed` on early disposal (NOT proof of rollback), `commit_outcome_unknown` upon attempted Commit, and `committed` only after the awaited SQL/EF commit completed successfully. The lifecycle currently covers business transaction body and commit; it does not claim observed EF Rollback events unless explicitly hooked later.
- `SourceOwnedSqlTransactionCommands.CommitAsync`/`RollbackAsync` wrap **real directly owned SqlTransaction calls**, tag `committed`/`rolled_back` only after successful completion and `unknown_failure`/`unknown_canceled` on exceptions. They are not called from EF transactions; no duplicate EF transaction instrumentation.
- `not_committed` means the business operation never reported a completed commit; it may be rolled back upon disposal. It does **not** mean durable rollback was independently verified.

## 4. Test matrix and evidence

Added `tests/Shinera.Identity.SqlServerTests/SourceOwnedSqlClientAndDapperTracingTests.cs`; included in `.github/workflows/bff-ci.yml` SQL integration filter and explicit Dapper test PackageReference.

The test uses a **real disposable local SQL Server database** and a recorded parent Activity:
1. `SqlCommand INSERT` under a live SQL transaction → exactly one raw SQL span.
2. Source-owned Dapper `ExecuteScalarAsync<long>` under **same** transaction reads the uncommitted row → one Dapper span and correct result.
3. Explicit real `RollbackAsync` → independently verified rollback, no committed row, `rolled_back` transaction state.
4. New transaction INSERT + confirmed `CommitAsync` → committed row remains.
5. Real invalid SqlCommand with a secret argument → exactly one error span, type-only error, never secret content.
6. Span tree keeps parent TraceId; transaction children are nested; no raw SQL/table identifiers, secret sentinel, connection strings or SQL param values in tags/names.
7. Excluded inbound auth/OIDC routes with sampled trace context do not emit deep SQL/transaction children; underlying work still executes. The same central gate is shared with EF interception.\n8. Existing FR-18D-1 real EF Core test continues in **same required SQL CI filter**; no global SqlClient instrumentation is enabled, so EF execution remains exactly once.

Tests are in disposable CI, not a production running SaaS. PR-head and post-merge results must both be checked. No schema change to business SQL and no migration to the Identity context.

## 5. Developer implementation pattern

Explicit raw SqlClient:
```csharp
await using var command = new SqlCommand(
    "SELECT COUNT_BIG(*) FROM dbo.MyRows WHERE TenantId=@tenant", connection, transaction);
command.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = authorizedTenant;
var count = (long)(await SourceOwnedSqlTrace.ExecuteScalarAsync(command, cancellationToken))!;
```

Explicit Dapper:
```csharp
const string sql = "SELECT COUNT_BIG(*) FROM dbo.MyRows WHERE TenantId=@tenant";
var count = await SourceOwnedSqlTrace.RunDapperAsync(
    sql, () => connection.ExecuteScalarAsync<long>(
        sql, new { tenant = authorizedTenant }, transaction));
```

Use actual SQL template as HMAC input but do not concatenate browser parameters into SQL or pass raw SQL to Activity tags. Do not wrap an EF Core command in `SourceOwnedSqlTrace`; its own DbCommandInterceptor owns those spans.

## 6. Release limits / next phase

- **FR-18D-3:** bridge a real approved BFF/CQRS and SQL request through the running Collector to persisted Tempo Trace; strict two-Tenant proof, no sensitive data, no duplicate EF/raw command spans and telemetry outages. Add explicit source-owned tracing to uncovered raw SQL callsites where real traces justify it.
- **FR-18E:** authenticated System-only read/query API, central `system` Resource/Permission/seed, Angular Trace Explorer/Waterfall and SQL Analyzer using actual retained backend data. No hard-coded live mock telemetry.
- **FR-18F:** reviewed explicit DTO-safe allowlist, short-lived diagnostic policies, durable audit and any log pipeline. Avoid recording raw ILogger messages by default.
- No full automatic method profiler; instrumentation of **source-owned** operations is the safe Foundation contract.
- Collector and Tempo/Prometheus under `observability/` remain development/ephemeral-CI-only. Hosted Staging/Production requires separate approval and resource security.

Architecture: [FR-18A](../architecture/control-tower-observability.md), [FR-18D-1](FR-18D1-CQRS-EF-SQL-SPANS.md), [Roadmap](FR-18-CONTROL-TOWER-ROADMAP.md).
