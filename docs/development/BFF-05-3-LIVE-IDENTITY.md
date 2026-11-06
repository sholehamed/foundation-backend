# BFF-05.3 — Web-composed live Identity validation

## Current BFF-05.3 merge checkpoint — 2026-11-06

- **Merged:** [BFF-05.2 PR #23](https://github.com/sholehamed/foundation-backend/pull/23), commit `88239b7d3454fd03b87aa7bc20e70d1a820b088a` (server-side CSRF bootstrap, current-cookie logout, 82 BFF / 50 Identity tests).
- **Merged:** [BFF-05.3 PR #24](https://github.com/sholehamed/foundation-backend/pull/24), commit `a676919d0d7d777c2c72895c7e1c9a9cd7987aa7`. [Final PR-head CI #42206724851](https://github.com/sholehamed/foundation-backend/actions/runs/42206724851): solution build green, **82/82 BFF and 58/58 Identity tests**.
- New Web-host Identity bridge captures security stamp and scoped role/permission fingerprint in the **server-protected ticket**, and validates current User/Tenant/lockout/stamp/role/grant state on each BFF cookie read. Missing baseline or invalid DB authorization fails closed; tested directly and via HTTP CookieAuthentication middleware. BFF module itself has no Identity DB reference.
- **Remaining gate:** actual OIDC callback + Web adapter integration must be tested with the real OpenIddict two-host flow. Separate release blockers remain: race between code exchange and stamp capture, concurrent logout/renewal, durable shared session store and keyring, upstream token revocation/logout-all, expiry, real SQL Server/browser/trusted proxy and Angular BFF-06.
- `Identity:Bff:Enabled=false` remains the default, existing Angular Password Grant/localStorage untouched. Initial F00 baseline PR #2 stays open.


> [PR #24](https://github.com/sholehamed/foundation-backend/pull/24) merged at `a676919d0d7d777c2c72895c7e1c9a9cd7987aa7`, [CI #42206724851](https://github.com/sholehamed/foundation-backend/actions/runs/42206724851) passed 82 BFF + 58 Identity tests. Not production-ready; keep BFF disabled and Angular legacy auth unchanged.

## Dependency boundary

- `Modules.System.Bff` defines `IBffSessionAccessValidator` and `BffSessionAccessSnapshot` without referencing EF Core or Identity.
- `src/Web/Bff/IdentityBffSessionAccessValidator` is the host composition bridge. It reads current User/Tenant state, the Identity security stamp and fresh scoped grants through Identity abstractions.
- When `Identity:Bff:Enabled=true` **in the Web host**, an OIDC ticket-received callback captures server-only stamp and grants fingerprint into protected ticket properties. The BFF cookie principal is revalidated on each AuthenticateAsync before the browser session, CSRF or logout can use it.
- Disabled, locked, tenant-removed/renamed, security-stamp-changed or grants-changed snapshots are rejected. The current server-side ticket is removed on definitive invalidation. A thrown DB exception fails closed (the request must not be accepted).
- BFF cookies still hold opaque references, never tokens, raw stamps, user permissions or refresh tokens. Account type, tenant slug and scoped role/permission checks remain strict.

## Required follow-up gates

- Add a Web-host integration suite that tests the two event hooks with actual cookie middleware and database mutation. The existing independent BFF two-TestServer tests do **not** exercise the Web adapter.
- Verify race between authorization code exchange and stamp capture, renewal vs logout, per-request DB outage semantics, SQL Server and multi-node replay/cache/keyring.
- Upstream token revocation, logout-all, absolute/idle expiry and real System/Tenant browser workflow remain outstanding.
- Angular still uses Password Grant / localStorage; BFF is disabled by default.

Unit tests in `BffLiveIdentityValidationTests` check live System/Tenant, stamp rotation, disabled/locked users, role changes, account type and forged claims.

## Continuity

Merge only after build and complete BFF/Identity CI on latest PR head. The baseline before this branch is merged [PR #23](https://github.com/sholehamed/foundation-backend/pull/23) at `88239b7d3454fd03b87aa7bc20e70d1a820b088a`, with 82 BFF and 50 Identity tests green.

## BFF-05.3 verified adapter checkpoint — 2026-11-06

- [Draft PR #24](https://github.com/sholehamed/foundation-backend/pull/24) includes a BFF-only abstract validation contract and a Web-host Identity DB adapter. The production Web composition registers OIDC ticket capture and cookie `OnValidatePrincipal` live user/Tenant/security-stamp/grant checks only when `Identity:Bff:Enabled=true`.
- [Code-head CI #42206181883](https://github.com/sholehamed/foundation-backend/actions/runs/42206181883): **82 BFF + 58 Identity tests**, full solution build passed. Added direct adapter DB tests and HTTP CookieAuthentication tests for System/Tenant, security-stamp rotation, tenant deactivation and removed-session replay.
- The HTTP tests issue a **test-only baseline ticket**, not an actual OIDC callback; the new OIDC `OnTicketReceived` adapter hook still needs true end-to-end verification with the live Foundation Identity server, as do stamp-exchange callback races.
- Do **not** assert production readiness: BFF flag stays disabled, Angular legacy Password Grant/localStorage unchanged. Logout-all, remote token revocation, renewal races, actual browser, SQL Server and multi-node infrastructure still pending.

