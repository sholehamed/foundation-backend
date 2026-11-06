# BFF-05.5b.1 — atomic session-provider startup gate

> [Draft PR #29](https://github.com/sholehamed/foundation-backend/pull/29), CI pending. **No SQL/Redis atomic provider is implemented here.** This PR only prevents accidental deployment of the non-atomic default and supplies a narrow extension contract.

## Why this gate is necessary

Merged [PR #28](https://github.com/sholehamed/foundation-backend/pull/28) added a 48-hour revocation marker and a 24-hour absolute session lifetime with deterministic concurrent renewal tests. However, `IDistributedCache` cannot guarantee compare-and-swap and provider-specific eviction/replication/operational behavior. Tombstones are useful defense-in-depth, not sufficient proof of Production-grade durable session revocation.

## Startup contract

- `IBffAtomicTicketStore : ITicketStore` is defined in `Modules.System.Bff`. It does not import any SQL/Redis, Tenant or Identity persistence code. Any provider must preserve atomic renew-if-active/revoke across all nodes, enforce absolute and idle expiration, and protect all tokens at rest.
- When `Identity:Bff:Enabled=true` **outside Development**, `AddBffAuthentication` demands the host register `IBffAtomicTicketStore` with **Singleton** DI lifetime. Missing or scoped providers trigger a clear `InvalidOperationException` *before* the host starts serving browser routes.
- The CookieAuthentication `ITicketStore` is replaced with **the registered provider**; it cannot silently fall back to `DistributedBffTicketStore`. When an atomic SQL provider is registered, no separate `IDistributedCache` is required by BFF.
- `Development` may still use the tombstone-backed `IDistributedCache` store for local tests. When BFF is **disabled**, no extra service is required. Existing bearer/legacy client configuration remains unchanged.
- A test stub implementing the interface confirms Startup/DI behavior **only**. The interface alone is **not** a cryptographic, transactional, provider-quality or production acceptance certificate.

## Automated acceptance

- [ ] Production and Staging reject BFF startup with only a plain `IDistributedCache`.
- [ ] Production rejects a scoped `IBffAtomicTicketStore`; singleton is required.
- [ ] Production selects a singleton atomic adapter for `ITicketStore` even when **no** extra `IDistributedCache` exists.
- [ ] Development supports previous default store, and disabled BFF remains unconfigured.
- [ ] Latest-head CI solution build/BFF/Identity suites all green; document verified counts.

## Remaining implementation and rollout gates

See [Issue #30](https://github.com/sholehamed/foundation-backend/issues/30) for the recommended first **SQL Server transactional** provider: protected ticket blob, non-guessable session identifier, current revision, revoked flag, absolute expiration; UPDATE-IF-ACTIVE/transactional revoke; no reinsert on renewal; two-instance forced-interleaving tests, outages and failover. An optional Redis Lua-backed implementation can be introduced separately. Also require shared Data Protection keyring, logout-all and upstream token revocation, trusted proxies, real browser/SQL Server acceptance, refresh rotation locking, and a verified deployment runbook.

**Do not opt BFF into Staging or Production, or migrate Angular, on this PR alone.** Both real provider and cross-instance security evidence remain mandatory.
