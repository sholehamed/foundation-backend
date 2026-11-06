# BFF-05.3 — Web-composed live Identity validation

> Draft [PR #24](https://github.com/sholehamed/foundation-backend/pull/24). Do not treat as production-ready, enable BFF or migrate Angular before latest-head CI and remaining integration gates.

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
