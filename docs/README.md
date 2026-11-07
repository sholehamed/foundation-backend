# Foundation Backend Documentation

## Project continuity

- [Current project state and handoff](PROJECT-STATE.md)
- [BFF-03 implementation and verification notes](development/BFF-03-IMPLEMENTATION.md)
- [Implementation roadmap and acceptance gates](PROJECT-ROADMAP.md)

این پوشه مرجع فنی کد فعلی و تصمیم‌های معماری Foundation است. هر تغییر رفتار مهم باید همراه کد و تست در همان PR مستند شود.

- [ADR-0001 — Full BFF for browser clients](decisions/ADR-0001-browser-bff.md)
- [BFF implementation design](architecture/browser-bff.md)
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
