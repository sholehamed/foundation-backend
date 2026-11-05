# Modules.System.Bff — implementation status

- **Accepted architecture:** [ADR-0001](../../../docs/decisions/ADR-0001-browser-bff.md)
- **Implementation contract:** [Browser auth](../../../docs/reference/browser-auth-contract.md)
- **Status:** BFF-01 ticket storage implemented; **interactive BFF login, endpoint mapping and browser migration are NOT implemented**.

## First slice

- `DistributedBffTicketStore` implements ASP.NET Core `ITicketStore`, using `IDistributedCache` and Data Protection.
- Random opaque 256-bit lookup keys, protected serialized tickets, absolute expiry and explicit removal.
- `AddBffSessionInfrastructure()` registers isolated cookie scheme with `__Host-` host-only cookie settings, server-side session store and antiforgery primitives; it does not override OpenIddict's default bearer scheme.
- The module is deliberately **not wired into `src/Web` yet**: premature registration would give a misleading impression that interactive OIDC login works.

## Validation

`dotnet test tests/Foundation.Bff.Tests/Foundation.Bff.Tests.csproj -c Release`

## Next slices

- Authorization Code + PKCE interactive Identity endpoint and confidential client.
- BFF OIDC callback, explicit cookie authorization on `/bff/session`, CSRF and logout.
- Tenant/host validation, revocation index, store outage + concurrent refresh tests.
- Angular migration after full-stack E2E. Password grant remains unchanged meanwhile.
