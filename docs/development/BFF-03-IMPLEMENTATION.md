# BFF-03 — OpenIddict Authorization Code + PKCE (implementation record)

> Implemented: 2026-11-06 · **Merged, isolated HTTP tests passed; Full BFF not yet operational**  
> [Issue #9](https://github.com/sholehamed/foundation-backend/issues/9) · [Merged PR #11](https://github.com/sholehamed/foundation-backend/pull/11) · merge commit `c201e4c67d18c9d7896887a688eb31d3340d7930`  
> [Final-head CI 42130167589](https://github.com/sholehamed/foundation-backend/actions/runs/42130167589) — Build and **6 BFF + 41 Identity tests green**

## Purpose / boundaries

Provide the **Authorization Server** half of the Full BFF pattern without changing the current Angular Password Grant, API bearer defaults, or introducing a browser OAuth token.

## Shipped code

| Component | Source | Behavior |
| --- | --- | --- |
| Scoped claims factory | `src/modules/Modules.System.Identity/Application/Services/IdentityPrincipalFactory.cs` | Master-System vs owning-Tenant scopes; nested role, group and permission relationships constrained |
| Identity cookie validator | `src/modules/Modules.System.Identity/Application/Services/IdentitySessionValidator.cs` | Live revalidation of user, security stamp, account context and tenant; grants rehydrated from DB |
| Shared password authenticator | `src/modules/Modules.System.Identity/Application/Services/UserCredentialAuthenticator.cs` | Uses same scoped principal factory, preserves legacy password verification/lockout |
| Master System scope | `src/modules/Modules.System.Identity/Infrastructure/DefaultSystemTenantScope.cs` | Fixed canonical System scope, independent of request tenant |
| BFF client provisioner | `src/modules/Modules.System.Identity/Infrastructure/Persistence/OpenIddictBffClientSeeder.cs` | Feature-flagged confidential client, exact callback URLs, PKCE, external client secret; fails on unexpected configuration drift |
| Authorization endpoint | `src/modules/Modules.System.Identity/Web/Endpoints/Connect.cs` | GET `/api/connect/authorize`; explicit Identity cookie and session validation, client/context checks; code issued by OpenIddict |
| Protocol tests | `tests/Shinera.Identity.Tests/OpenIddictAuthorizationHttpTests.cs` | Full TestServer Authorization Code + PKCE code exchange, replay, wrong verifier, invalid redirect/client/scope, absent PKCE, anonymous redirect, tenant context, unsupported POST |
| Scope tests | `tests/Shinera.Identity.Tests/IdentitySessionAndScopeTests.cs` | Tenant role isolation, disabled user/tenant, security-stamp changes, revoked roles |

`Identity:Bff:Enabled` defaults to **false**; `ClientSecret` is **not** stored in source control. Supply `Identity__Bff__ClientSecret` through a secret manager/secure environment configuration and exact allowed HTTPS callback URLs. Existing client registration changes require reviewed migrations.

## Verification evidence

CI run [42130167589](https://github.com/sholehamed/foundation-backend/actions/runs/42130167589) for PR head `4d496631f6f1a9f77577b905e89cc3df152bf641`:

- Solution restore/build: passed.
- `tests/Foundation.Bff.Tests`: **6 passed**, 0 failed.
- `tests/Shinera.Identity.Tests`: **41 passed**, 0 failed; includes isolated real middleware TestServer code/PKCE and negative authorization cases.

Earlier failing builds/tests were corrected, not disabled: missing namespace imports; test fixture NormalizedEmail; OIDC `auth_time` numeric ClaimValueType (Integer64); Login prompt infinite-redirect prevention; TestServer response/redirect assertions. Green end result does not imply live deployed browser or SQL Server integration.

## Deliberate limitations

- Authorization endpoint supports **GET only**. POST is explicitly rejected to avoid accidental loss of authorization body through redirect continuation.
- `prompt=login` and `max_age` fail closed with `login_required` until a properly tested one-time reauthentication UX exists; no redirect loop.
- Current Angular sign-in logic rejects `/api/connect/authorize` returnUrl. A dedicated secure OIDC continuation is needed; do not weaken arbitrary returnUrl validation.
- BFF challenge/callback and `__Host-Foundation.Bff` ticket-backed browser session have **not** been implemented. No `/bff/*` browser endpoints are ready. Password Grant remains active for existing clients.
- Host/tenant binding behind trusted proxies, real SQL Server, multi-instance cache/keys, signing-key ops, secure client secrets and full browser E2E remain future gates.
- The configured BFF client uses first-party implicit consent only when feature enabled; unknown/unregistered clients are not auto-consented.

## Next milestone

[BFF-04 Issue #12](https://github.com/sholehamed/foundation-backend/issues/12): implement the optional confidential BFF OIDC challenge/callback middleware and protected server-side session, plus a **safe** Identity UI continuation preserving validated OIDC request. Before changing Angular, ensure BFF-05 session CSRF/revocation endpoints and real cross-repo E2E tests are in place.

See [PROJECT-STATE](../PROJECT-STATE.md), [ROADMAP](../PROJECT-ROADMAP.md) and [ADR-0001](../decisions/ADR-0001-browser-bff.md). Update this record only to correct an error or refer to a new ADR/implementation; use new BFF-04 notes for subsequent work.
