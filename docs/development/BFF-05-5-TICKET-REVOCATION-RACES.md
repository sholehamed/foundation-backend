# BFF-05.5a — prevent ticket resurrection during logout versus renewal

## Verified BFF-05.5a + BFF-05.5b.1 merged checkpoint — 2026-11-06

- **Merged race mitigation:** [PR #28](https://github.com/sholehamed/foundation-backend/pull/28) at `ccbaddcad3d8e7c413a505ba7c8e2e0a04e59298`. [Final PR-head CI #42210307369](https://github.com/sholehamed/foundation-backend/actions/runs/42210307369) passed solution build, **88 BFF + 60 Identity tests**. Protected session tickets carry a 24-hour original-issuance absolute ceiling, revoked keys receive a 48-hour tombstone, and Retrieve/Renew validate revocation before/after cached reads/writes; deterministic concurrency tests prove the single-shared-consistent-cache interleavings.
- **Merged non-Development guard:** [PR #29](https://github.com/sholehamed/foundation-backend/pull/29) at `b9a6bb77cedcc55511f2d45edff28667b20f5734`. [Final PR-head CI #42211389822](https://github.com/sholehamed/foundation-backend/actions/runs/42211389822): full solution build, **94 BFF + 60 Identity tests**. Opt-in BFF startup in Staging/Production now rejects a plain `IDistributedCache` without a registered singleton `IBffAtomicTicketStore`; its provider is actually selected for Cookie `ITicketStore`. Development/default-off behavior remains unchanged.
- **Important:** `IBffAtomicTicketStore` is a CONTRACT, not a concrete reviewed provider. **No SQL Server/Redis atomic backend exists yet**; an implementation registered by DI alone is not proof of its atomicity. [Issue #30](https://github.com/sholehamed/foundation-backend/issues/30) tracks transactional SQL Server provider, cross-instance consistency/eviction/failover and Data Protection keyring tests. BFF browser opt-in remains OFF until these and other BFF-05 gates are satisfied.
- **Remaining:** logout-all/upstream OIDC token revocation, refresh coordination, idle policy, actual SQL Server/browser/trusted reverse proxy and multi-node E2E, stamp-change race during callback. Angular Password Grant/localStorage/guards unchanged; [frontend BFF-06 #10](https://github.com/sholehamed/foundation-frontend/issues/10) blocked, initial F00 PR #2 untouched.
- **Handoff docs:** [renewal races](development/BFF-05-5-TICKET-REVOCATION-RACES.md), [startup guard](development/BFF-05-5B-ATOMIC-PROVIDER-GATE.md), [master BFF-05 issue](https://github.com/sholehamed/foundation-backend/issues/20).


## BFF-05.5 progression — merged race mitigation, atomic provider gate under review (2026-11-06)

- **Merged BFF-05.5a:** [PR #28](https://github.com/sholehamed/foundation-backend/pull/28) at `ccbaddcad3d8e7c413a505ba7c8e2e0a04e59298`; [final PR-head CI #42210307369](https://github.com/sholehamed/foundation-backend/actions/runs/42210307369) green: build, **88 BFF + 60 Identity tests**. Tombstone-before-delete, before/after cached reads/renewals and bounded 24-hour session expiry prevent late renewal resurrection under one strongly consistent shared cache. This is *not* a verified Redis/SQL multi-node atomic provider.
- **Draft BFF-05.5b.1:** [PR #29](https://github.com/sholehamed/foundation-backend/pull/29) adds `IBffAtomicTicketStore` and refuses enabled BFF startup in **Staging/Production** unless a singleton atomic provider is explicitly registered and used for `ITicketStore`. A SQL provider need not also register `IDistributedCache`. `Development` can still use the tombstone-backed default; disabled BFF is unchanged. Latest-head CI pending; a test-only interface stub does not satisfy Production security review.
- **Implementation outstanding:** [Issue #30](https://github.com/sholehamed/foundation-backend/issues/30) tracks the real transactional **SQL Server** atomic session provider, cross-instance interleavings, durability/eviction/failover, restart/keyring/SQL tests. No real provider is included in PR #29; BFF is **still disabled by default**, Angular Password Grant/localStorage unchanged. Backend [Issue #20](https://github.com/sholehamed/foundation-backend/issues/20) remains the master gate; original F00 PR #2 remains open.


> **Status:** [Backend draft PR #28](https://github.com/sholehamed/foundation-backend/pull/28), CI pending. This is not a complete production/multi-node gate. No Angular runtime changes.

## Vulnerability and invariant

Previous `DistributedBffTicketStore.RenewAsync(key, ticket)` wrote through `IDistributedCache.SetAsync` with no check for logout. If renewal read the ticket and later performed its write **after** `RemoveAsync`, the same opaque cookie key could authenticate again.

**Invariant for the supported single strongly-consistent shared cache:** once `RemoveAsync` has persisted a revocation marker for a key, no subsequent `RetrieveAsync` returns an authenticating ticket for that key, even if a renewal write or stale cached read overlaps logout. `IDistributedCache` is not an atomic compare-and-set interface; this protocol is not proof of failover/eviction/replica-lag consistency.

## Implemented protocol

1. `StoreAsync` creates a cryptographically random 256-bit opaque key and records `.bff.absolute_expires_utc` in protected server-side authentication properties; maximum is **24 hours from initial issuance**. It clamps ticket expiry to this upper bound.
2. `RenewAsync` requires the unchanged absolute-expiry property, confirms the original key is still present and unrevoked, then writes. It checks revocation **again after** the write and removes a late write if logout won the race. Renewal cannot extend beyond the initial absolute expiry.
3. `RemoveAsync` writes a separate cache revocation marker **before** it deletes the ticket. The marker remains for **48 hours after revocation**, longer than the maximal 24-hour ticket lifetime, even with permitted future clock skew. A failed marker write causes logout failure instead of a false-success revocation.
4. `RetrieveAsync` checks the marker before reading ticket bytes **and again after** reading/deserializing bytes. Thus even a read that fetched pre-logout bytes cannot authenticate after the marker is visible. Missing absolute-expiry metadata (including legacy tickets), expired sessions and corrupt tickets fail closed.
5. All stored values are protected with ASP.NET Core Data Protection; the browser still sees only the original opaque cookie. Revocation cache entries contain only a constant marker, not tokens or PII.

## Deterministic tests

The test suite deliberately orchestrates two independent `DistributedBffTicketStore` instances sharing the same provider, without arbitrary sleeps:

- Renewal paused *just before writing*, logout completes, renewal finishes late: old cookie stays invalid and late ticket write is removed.
- Ticket read paused *after retrieving old bytes*, logout completes, read resumes: second revocation check denies it.
- Renewal and storage replay after logout do not restore access.
- Absolute lifetime is preserved across sliding renewal, future-expiring tickets are capped, already-overdue original sessions and baseline-free renewal fail closed.
- Tombstone is separately persisted and ticket key is deleted; existing end-to-end OpenIddict, CSRF/logout and Web live-validator suites must remain green.

## Residual security and production gates

- **Multi-node:** `IDistributedCache` has **no atomic CAS/transaction**, and eviction can remove a tombstone while stale writes exist. This slice does **not** certify arbitrary Redis/SQL providers, multi-region reads, failover, replication lag, per-node in-memory stores or replica inconsistency. Production must use a shared provider with proven strong marker visibility and no premature marker eviction, ideally implement provider-specific atomic renew-if-active/revoke operations and integration stress tests.
- Any request authenticated **before** the revocation linearization point may still be in flight; account/permission checks on sensitive operations and request-level cancellation/authorization policy remain separately necessary.
- Security stamp/role/tenant invalidation is handled by Web adapter in merged PR #24/#26; prove DB outage and timing between OIDC exchange/callback before production.
- Add explicit multi-node restart/outage tests, shared Data Protection keyring, durable session storage, logout-all/upstream refresh-token revocation, distributed refresh locking and rotation, browser/SQL Server/trusted reverse-proxy acceptance.
- Absolute 24-hour maximum is enforced in server ticket store and does not change the configured 8-hour *sliding* cookie interval. New sessions must authenticate again after the absolute ceiling; this is a behavioral change only once BFF is opted in.
- The feature flag `Identity:Bff:Enabled=false` remains unchanged. Angular continues legacy Password Grant/localStorage; do not migrate yet.

## Acceptance evidence

- [ ] Latest PR-head CI full solution build passes.
- [ ] Existing and new BFF unit/HTTP tests pass.
- [ ] Identity and two-TestServer OIDC integration tests pass.
- [ ] Documentation reflects exactly which deployment guarantees are **not** proven.

## Handoff

Continue from [backend issue #20](https://github.com/sholehamed/foundation-backend/issues/20), [frontend issue #10](https://github.com/sholehamed/foundation-frontend/issues/10) and this PR. Review latest PR-head test logs before merging; the original F00 baseline PR #2 remains open.
