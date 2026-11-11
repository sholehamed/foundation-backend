- [FR-18E-1 — Privileged System Control Tower exact TraceId read and Angular Waterfall](development/FR-18E1-CONTROL-TOWER-TRACE-READ.md)
- [FR-18D-3 — Real signed HTTP request through CQRS/EF SQL with persisted correlated Tempo TraceId](development/FR-18D3-REAL-HTTP-SQL-TEMPO-E2E.md)
- [FR-18D-2 — Real raw SqlClient/Dapper spans and confirmed transaction outcome tracing](development/FR-18D2-RAW-SQLCLIENT-DAPPER-TRANSACTIONS.md)
- [FR-18D-1 — Real Dispatcher Command/Query/Notification spans and EF SQL execution traces](development/FR-18D1-CQRS-EF-SQL-SPANS.md)
- [FR-18C-1 — Real isolated Collector, Tempo and Prometheus stack; CI OTLP ingestion and privacy smoke](development/FR-18C1-ISOLATED-COLLECTOR-STORES.md)
- [FR-18B-1 — First real OpenTelemetry host tracing, privacy defaults, opt-in OTLP and test guide](development/FR-18B1-HOST-OTEL-TRACING.md)
## FR-18 — Control Tower (architecture baseline, documentation-only)

- [ADR-0003 — Isolation of observability from primary business SQL and transactions](decisions/ADR-0003-control-tower-observability-isolation.md)
- [FR-18A — Complete distributed tracing, SQL, method instrumentation, storage topology and security architecture](architecture/control-tower-observability.md)
- [FR-18 — Telemetry DTOs, trace/SQL read facade, diagnostic capture policies and acceptance](reference/control-tower-telemetry-contracts.md)
- [FR-18 — Implementation phases and checklists, FR-18B first executable slice](development/FR-18-CONTROL-TOWER-ROADMAP.md)
- [Angular companion UX blueprint](https://github.com/sholehamed/foundation-frontend/blob/main/docs/development/FR-18-CONTROL-TOWER-UX.md)

**Status:** documentation/decisions only as of 2026-11-11. No Collector, tracing SDK, request inspector API, capture policy storage or Control Tower UI is deployed by FR-18A. Existing System admin/BFF/SQL behavior is unchanged.

- [FR-17E-1 — Centrally issued coupons, settled redemption and zero-net subscription checkout](development/FR-17E1-CATALOG-COUPONS-ATOMIC-REDEMPTION.md)
- [FR-17C-2A — Tenant Orders, admin reviewed external manual settlement and atomic Subscription activation](development/FR-17C2A-TENANT-ORDERS-MANUAL-SETTLEMENT.md)
- [FR-17C-1 — Tenant Subscription/Order schema, verified-order Shadow Entitlement](development/FR-17C1-SUBSCRIPTION-SHADOW.md)
- [FR-17B-2C — SQL Server Published Plan snapshot immutability and EF trigger-safe mapping](development/FR-17B2C-IMMUTABLE-PLAN-SNAPSHOTS.md)
- [FR-17B-2B — System Admin Plan Catalog draft/review/publish endpoints and Angular companion](development/FR-17B2B-SYSTEM-PLAN-CATALOG-ADMIN.md)
- [FR-17B-2A — Published Plan Cards public read API, signup intent compatibility and Angular contract](development/FR-17B2A-PUBLIC-PLAN-CARDS.md)
# Foundation Backend Documentation

## Project continuity

- [Current project state and handoff](PROJECT-STATE.md)
- [BFF-03 implementation and verification notes](development/BFF-03-IMPLEMENTATION.md)
- [Implementation roadmap and acceptance gates](PROJECT-ROADMAP.md)

این پوشه مرجع فنی کد فعلی و تصمیم‌های معماری Foundation است. هر تغییر رفتار مهم باید همراه کد و تست در همان PR مستند شود.

- [ADR-0001 — Full BFF for browser clients](decisions/ADR-0001-browser-bff.md)
- [BFF implementation design](architecture/browser-bff.md)
- [FR-17A architecture — Modules/Resources, entitlement, plans, limits and extensible workflows](architecture/modular-entitlements-plans-limits.md)
- [FR-17B-1 — Versioned Plan Catalog, SQL schema, inherited capabilities and plan cards](development/FR-17B1-VERSIONED-PLAN-CATALOG.md)
- [FR-17A-2 — Typed Module/Resource/Permission and capability contract diagnostics](development/FR-17A2-TYPED-RESOURCE-CONTRACTS.md)
- [FR-17A SQL Server catalog/legacy entitlement inventory (SELECT-only)](sql/fr-17a-module-resource-entitlement-inventory.sql)
- [FR-17A source Resource/EndpointGroup inventory — verified System mappings, Tenant exceptions and pending SQL](development/FR-17A-RESOURCE-INVENTORY.md)
- [FR-17A delivery handoff — inventory, slices, gates and permanent acceptance tests](development/FR-17A-MODULAR-ENTITLEMENTS-HANDOFF.md)
- [ADR-0002 — Proposed separation of commercial entitlements and tenant RBAC](decisions/ADR-0002-commercial-entitlement-vs-authorization.md)
- [Feature Registry & Authorization — proposed design and phased handoff](architecture/feature-registry-authorization.md)
- [FR-04A current-user navigation API contract](architecture/current-user-navigation.md)
- [FR-01A — Identity catalog inventory and Menu read contract](development/FR-01A-IDENTITY-CATALOG-AND-MENU-CONTRACT.md)
- [FR-01B — Shared live permission evaluator and claim compatibility](development/FR-01B-PERMISSION-EVALUATOR.md)
- [FR-01 read-only SQL inventory](sql/fr-01-identity-catalog-inventory.sql)
- [Browser authentication HTTP contracts](reference/browser-auth-contract.md)

## Documentation policy

- ADR = چرایی و trade-off تصمیم؛ سند معماری = چگونگی؛ قرارداد = رفتار قابل تست.
- Statusهای Proposed / Accepted / Superseded برای ADR؛ تصمیم Accepted لزوماً به معنی Implemented نیست.
- در هر ماژولِ بالغ، README کوتاه نزدیک کد به سند مرکزی و تست‌های مربوط لینک می‌دهد.
- OpenAPI منبع حقیقت پارامترهای HTTP پیاده‌شده است؛ سند قرارداد رفتار امنیتی و مهاجرت را مشخص می‌کند.
- آنچه هنوز اجرا یا تست نشده، با برچسب Planned مستند می‌شود.

- [FR-02A — Code-owned Feature Registry with reviewed preview/apply](development/FR-02A-FEATURE-REGISTRY.md)

- [FR-02B-1 — Source-owned Users feature and backend manifest](development/FR-02B1-USERS-MANIFEST.md)

- [FR-02B-2 — All System root-page Registry definitions and endpoint metadata](development/FR-02B2-SYSTEM-PAGES.md)

- [FR-05A — Nested System PAGE/ACTION UI Registry](development/FR-05A-NESTED-UI-REGISTRY.md)

- [FR-06A — Live System UI key access endpoint, pilot route/action](development/FR-06A-LIVE-SYSTEM-UI-ACCESS.md)

- [FR-07A — Bulk live System PAGE/ACTION access projection](development/FR-07A-BULK-SYSTEM-UI-ACCESS.md)

- [FR-08A — System dashboard and Tenant Admin baseline](development/FR-08A-SYSTEM-ADMIN-DASHBOARD.md)

- [FR-08B — Tenant Workspace read-only starter](development/FR-08B-TENANT-WORKSPACE-STARTER.md)

- [FR-13A — Staging readiness, read-only preflight and deployment gates](development/FR-13A-STAGING-READINESS.md)

- [FR-13B-1 — Owner-created inactive Tenant employees, SQL audit and activation boundaries](development/FR-13B1-INACTIVE-TENANT-MEMBER-CREATE.md)

- [FR-13B-2 — Secure one-time Tenant member invitation and activation](development/FR-13B2-INVITATION-ACTIVATION.md)

- [FR-13B-3 — One-time temporary password and forced first login change](development/FR-13B3-TEMP-PASSWORD-FIRST-LOGIN.md)

- [FR-13B-3B — unified Tenant Login, first-password forced change](development/FR-13B3B-UNIFIED-TENANT-LOGIN.md)

- [FR-13B-3C — BFF Identity-host first-password change](development/FR-13B3C-BFF-IDENTITY-FORCED-CHANGE.md)

- [FR-13B-3A — Email-less local Tenant staff onboarding](development/FR-13B3A-EMAILLESS-TENANT-STAFF.md)
