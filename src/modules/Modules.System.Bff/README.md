# Modules.System.Bff — implementation status

- **Accepted architecture:** [ADR-0001](../../../docs/decisions/ADR-0001-browser-bff.md)
- **Architecture and delivery checkpoint:** [Browser BFF](../../../docs/architecture/browser-bff.md)
- **HTTP contract:** [Browser auth](../../../docs/reference/browser-auth-contract.md)
- **Project handoff:** [Project state](../../../docs/PROJECT-STATE.md) | [Roadmap](../../../docs/PROJECT-ROADMAP.md)
- **Status:** BFF-01 ticket-store primitives and BFF-02 Identity interactive-session primitives are implemented. The complete browser BFF is **not operational**.

## BFF-01 — server-side ticket store

- `DistributedBffTicketStore` implements ASP.NET Core `ITicketStore`, using `IDistributedCache` and Data Protection.
- Random opaque 256-bit lookup keys, protected serialized tickets, absolute expiry and explicit removal.
- `AddBffSessionInfrastructure()` registers an isolated cookie scheme with `__Host-` host-only cookie settings, server-side session store and antiforgery primitives.
- The module is deliberately **not wired into `src/Web` yet**: premature registration would imply interactive OIDC works when it does not.

## BFF-02 — Identity interactive session

- `Modules.System.Identity` owns a separate `__Host-Foundation.Identity` cookie.
- Credential verification is shared with the legacy Password Grant; CAPTCHA, antiforgery and login IP rate limiting are applied to the new interactive endpoint.
- This endpoint does not issue OAuth tokens and is not yet connected to the authorization endpoint.

## Next slices

1. BFF-03: authorize endpoint that authenticates the Identity cookie, revalidates user/security stamp/tenant and issues Authorization Code + PKCE for a registered confidential BFF client.
2. BFF-04: BFF OIDC challenge/callback, server-to-server code exchange, protected token storage and opaque BFF cookie backed by `ITicketStore`.
3. BFF-05: session introspection, CSRF, logout/revocation, tenant/host binding and failure/restart/concurrency tests.
4. BFF-06: Angular migration to `/bff/session`; remove browser token persistence only after cross-repo E2E passes.
5. BFF-07: disable Password Grant after every legacy consumer has migrated; production secrets, Data Protection key-ring and distributed-store verification.

See the central roadmap for acceptance criteria and dependency order.
