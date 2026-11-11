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
