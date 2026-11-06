# BFF-05.5b.1 — atomic session-provider startup gate

## Verified BFF-05.5a + BFF-05.5b.1 merged checkpoint — 2026-11-06

- **Merged race mitigation:** [PR #28](https://github.com/sholehamed/foundation-backend/pull/28) at `ccbaddcad3d8e7c413a505ba7c8e2e0a04e59298`. [Final PR-head CI #42210307369](https://github.com/sholehamed/foundation-backend/actions/runs/42210307369) passed solution build, **88 BFF + 60 Identity tests**. Protected session tickets carry a 24-hour original-issuance absolute ceiling, revoked keys receive a 48-hour tombstone, and Retrieve/Renew validate revocation before/after cached reads/writes; deterministic concurrency tests prove the single-shared-consistent-cache interleavings.
- **Merged non-Development guard:** [PR #29](https://github.com/sholehamed/foundation-backend/pull/29) at `b9a6bb77cedcc55511f2d45edff28667b20f5734`. [Final PR-head CI #42211389822](https://github.com/sholehamed/foundation-backend/actions/runs/42211389822): full solution build, **94 BFF + 60 Identity tests**. Opt-in BFF startup in Staging/Production now rejects a plain `IDistributedCache` without a registered singleton `IBffAtomicTicketStore`; its provider is actually selected for Cookie `ITicketStore`. Development/default-off behavior remains unchanged.
- **Important:** `IBffAtomicTicketStore` is a CONTRACT, not a concrete reviewed provider. **No SQL Server/Redis atomic backend exists yet**; an implementation registered by DI alone is not proof of its atomicity. [Issue #30](https://github.com/sholehamed/foundation-backend/issues/30) tracks transactional SQL Server provider, cross-instance consistency/eviction/failover and Data Protection keyring tests. BFF browser opt-in remains OFF until these and other BFF-05 gates are satisfied.
- **Remaining:** logout-all/upstream OIDC token revocation, refresh coordination, idle policy, actual SQL Server/browser/trusted reverse proxy and multi-node E2E, stamp-change race during callback. Angular Password Grant/localStorage/guards unchanged; [frontend BFF-06 #10](https://github.com/sholehamed/foundation-frontend/issues/10) blocked, initial F00 PR #2 untouched.
- **Handoff docs:** [renewal races](development/BFF-05-5-TICKET-REVOCATION-RACES.md), [startup guard](development/BFF-05-5B-ATOMIC-PROVIDER-GATE.md), [master BFF-05 issue](https://github.com/sholehamed/foundation-backend/issues/20).


> [PR #29](https://github.com/sholehamed/foundation-backend/pull/29) merged; CI #42211389822 green: 94 BFF + 60 Identity. **No SQL/Redis atomic provider is implemented here.** This PR only prevents accidental deployment of the non-atomic default and supplies a narrow extension contract.

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
