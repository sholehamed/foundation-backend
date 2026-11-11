# FR-18D-1 — Real CQRS Handler and EF Core SQL execution spans

**Date:** 2026-11-11  
**Status:** Implementation PR; **pending PR-head CI, SQL Server acceptance and post-merge main**.  
**Scope:** Actual source-owned CQRS execution and real EF Core relational DbCommand spans in existing Foundation Host; no raw query/body/parameters, no Identity schema or new commercial module.

## Purpose

A genuine traced HTTP request should contain its existing ASP.NET Core/Foundational pipeline Activity plus nested CQRS Command/Query spans and, where an EF Core DbContext is created via the shared infrastructure registration, actual SQL **command-execution** spans. Each SQL span is measured by EF Core's `CommandEndEventData.StartTime` and `Duration`, not faked by start/stop around its post-execution callback.

This is the first **actual method+EF SQL** instrumentation and intentionally stops short of automatically tracing Dapper/raw SqlClient, transaction begin/commit/rollback, raw SQL templates, parameter values and arbitrary method input/output. The latter require separate source-specific adapters and reviewed capture policies.

## Source and real integration points

- `src/shared/Application.Sharedkernel/Dispatcher/Dispatcher.cs`: source-owned `ActivitySource("Foundation.Cqrs")` begins `cqrs.command`, `cqrs.query` and `cqrs.notification` for existing DI Dispatcher calls. A stable compile-time request type name is attached; DTO payload/result are never inspected. Exceptions preserve original semantics, tag **type only**, and set error status; canceled requests are not described as failed. If an IDispatcher handler throws, the original exception is rethrown.
- `src/modules/Modules.System.Observability/FoundationObservabilityRegistration.cs`: registers `.AddSource("Foundation.Cqrs")` alongside existing host source; singleton telemetry provider remains independent from Identity DB. Scoped `IDbCommandInterceptor` implementation is resolved from central DI.
- `src/shared/Infrastructure.Sharedkernel/DependencyInjection.cs`: existing `AddBaseInfrastructureServices<TDbContext>` now appends DI-provided `IDbCommandInterceptor` to EF Core DbContextOptions. Shared Infrastructure does **not** refer to Observability project or Identity. Other manually created DbContexts must opt in explicitly; they are not silently captured.
- `src/modules/Modules.System.Observability/FoundationEfCommandInterceptor.cs`: one `db.ef.command` Activity per **successfully executed or failed** SQL command (sync or async Reader/Scalar/NonQuery, CommandFailed) with method, code-owned DbContext class, CommandSource, param count, and real execution duration.
- Required tests: `tests/Shinera.Identity.Tests/FoundationCqrsTraceTests.cs` (nested parent traces, real dispatch, failure and no secrets) and `tests/Shinera.Identity.SqlServerTests/FoundationEfSqlTracingTests.cs` (real disposable SQL Server insert/read/failure, timing, span parent, no raw values or connection strings). The latter is added to the SQL filter in `.github/workflows/bff-ci.yml`.

## SQL fingerprint privacy and explicit limitations

The initial fingerprint is `HMACSHA256(random per-process 256-bit key, raw EF DbCommand.CommandText)`, emitted **only as the 64-character digest** and `foundation.db.fingerprint_scope=process`. The key is never exported or persisted. This avoids direct offline dictionary attacks against low-entropy SQL or literal-sensitive statements, unlike a bare unsalted SHA256. Commands longer than a bounded maximum are reported as fingerprint omitted.

Implications:
- Identical SQL templates *within one process lifetime* have matching fingerprints, useful for duplicate command/N+1 candidate discovery. The fingerprint is **not stable across restarts, workers or deployments**, so it cannot yet serve cross-instance long-range SQL analytics.
- The digest input is **not normalized** and raw SQL with interpolated literal values may yield different fingerprints. This is intentional; SQL parsing/normalization and optional approved fingerprint key rotation belong to FR-18D-2/FR-18G.
- **No raw SQL text, literals, parameter values, connection string, response rows, result data or exception.Message** is emitted, even in detailed mode. `GetTagItem("foundation.db.fingerprint")` is not SQL source.
- `ReaderExecuted` measures SQL command execution **until the reader is obtained**, not complete enumeration/materialization of result rows. QueryStore execution plans are not correlated to individual commands in this stage.
- Only commands under an already **recorded** Activity are emitted. Unsampled/standalone SQL is not a new trace root; metric completeness differs from trace sampling.

**EF Core warning:** `EnableSensitiveDataLogging`, `LogTo` and existing provider logs may themselves expose values outside this interceptor. This change does not rewrite old logger calls or make all operator logs safe. Logging export remains disabled until FR-18F security review.

## Testing / rollback

- Compile all .NET projects and run current Unit + BFF + Tenant HTTP/SQL workflows.
- Real SQL Server disposal: generate unique local DB under loopback Master test connection; Create table using `EnsureCreatedAsync`, execute parameterized insert + SELECT + deliberate failed command; verify exactly one SQL span per failure and no secret payload.
- Verify no changes to Tenant filters, SSO/BFF, CSRF, Plan entitlement, grant rules, or domain write semantics.
- Existing HTTP `TraceId` header still identifies request but neither completeness nor retention is guaranteed; Collector from FR-18C is dev/CI, not production.
- To disable the exporter, `Observability:Otlp:Enabled=false`; to disable trace emission without an HTTP Collector, configure head sampling to zero. Removing `IDbCommandInterceptor` DI registration opts EF out independently.
- No SQL migration, Feature Registry resource, System Admin new menu/permissions or Angular client changes.

## Acceptance evidence and next steps

- Tests must verify **actual** parent/child Activity relationships and real EF `StartTime`/`Duration` from disposable SQL, not fabricated timeline values.
- One-span-per-physical-command holds for the registered EF interceptor only; **SqlClient/Dapper raw commands are not yet captured**. Do not turn on a general SqlClient instrumentation package at the same time without a de-duplication ownership rule, or the same EF command may appear twice.
- FR-18D-2: SQL interception for `IDbConnection`/Dapper/raw SqlClient with single ownership, transaction and rollback/commit Activities, safe query-type/fingerprint strategy and real HTTP -> Collector -> Tempo E2E.
- FR-18E: system-scoped query facade/central Resource, authorized Angular Request Inspector + Waterfall from real stored data; never build fake metrics.

Source architecture: [FR-18A](../architecture/control-tower-observability.md), [ADR-0003](../decisions/ADR-0003-control-tower-observability-isolation.md), [Roadmap](FR-18-CONTROL-TOWER-ROADMAP.md).
