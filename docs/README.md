# Foundation Backend Documentation

## Project continuity

- [Current project state and handoff](PROJECT-STATE.md)
- [BFF-03 implementation and verification notes](development/BFF-03-IMPLEMENTATION.md)
- [Implementation roadmap and acceptance gates](PROJECT-ROADMAP.md)

این پوشه مرجع فنی کد فعلی و تصمیم‌های معماری Foundation است. هر تغییر رفتار مهم باید همراه کد و تست در همان PR مستند شود.

- [ADR-0001 — Full BFF for browser clients](decisions/ADR-0001-browser-bff.md)
- [BFF implementation design](architecture/browser-bff.md)
- [Browser authentication HTTP contracts](reference/browser-auth-contract.md)

## Documentation policy

- ADR = چرایی و trade-off تصمیم؛ سند معماری = چگونگی؛ قرارداد = رفتار قابل تست.
- Statusهای Proposed / Accepted / Superseded برای ADR؛ تصمیم Accepted لزوماً به معنی Implemented نیست.
- در هر ماژولِ بالغ، README کوتاه نزدیک کد به سند مرکزی و تست‌های مربوط لینک می‌دهد.
- OpenAPI منبع حقیقت پارامترهای HTTP پیاده‌شده است؛ سند قرارداد رفتار امنیتی و مهاجرت را مشخص می‌کند.
- آنچه هنوز اجرا یا تست نشده، با برچسب Planned مستند می‌شود.
