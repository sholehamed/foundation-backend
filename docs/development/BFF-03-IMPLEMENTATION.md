# BFF-03 — implementation notes and verification handoff

> Date: 2026-11-06  
> Issue: [#9](https://github.com/sholehamed/foundation-backend/issues/9)  
> Draft PR: [#11](https://github.com/sholehamed/foundation-backend/pull/11)  
> Branch: `feat/bff-03-authorization-endpoint`  
> **Status: Work in progress. Not merged. Not production-ready.**

## Purpose

Provide the Authorization Server half of Full BFF without altering the installed Angular token-based login or the existing bearer API authentication scheme.

## Completed in branch

- `Application/Abstractions/IIdentityPrincipalFactory.cs` and `Application/Services/IdentityPrincipalFactory.cs`: query current user Role, Group, direct permission and their nested assignments under the correct master-System or tenant scope. Build a principal with `sub`, `account_type`, optional `tenant_id`/`tenant`, `security_stamp` and `auth_time`.
- `Application/Abstractions/IIdentitySessionValidator.cs` and `Application/Services/IdentitySessionValidator.cs`: re-read account and tenant state, require matching stamp, deny inactive or wrong-context account, rebuild current grants rather than trusting cookie snapshots.
- `Application/Services/UserCredentialAuthenticator.cs`: share the principal factory with the original password-grant logic, preserving password verification and lockout.
- `Infrastructure/DefaultSystemTenantScope.cs`: supplies canonical master System tenant scope without allowing tenant scope to bleed into System claims.
- `Infrastructure/Persistence/OpenIddictBffClientSeeder.cs`: feature-flagged confidential OpenIddict client seeded from `Identity:Bff` with exact redirects, minimum 32-character external secret, PKCE and limited scopes. Existing-client configuration is validated; no silent overwriting of secrets or redirects.
- `Web/Endpoints/Connect.cs`: handler mapped to `/api/connect/authorize`, uses OpenIddict protocol request and explicit Identity cookie authentication, validates session/context, limits implicit-consent flow to configured confidential BFF client, sends accepted principal to OpenIddict for code issuance.
- `DependencyInjections.cs`: register scoped services and configure OpenIddict endpoint.
- `tests/Shinera.Identity.Tests/IdentitySessionAndScopeTests.cs`: checks scope/role isolation and revoked/disabled user and tenant scenarios.

## Configuration (do not commit real values)

```json
{
  "Identity": {
    "Bff": {
      "Enabled": false,
      "ClientId": "foundation-browser-bff",
      "RedirectUris": [],
      "PostLogoutRedirectUris": []
    },
    "LoginUrls": {
      "System": "/system/auth/login",
      "Tenant": "/auth/login"
    }
  }
}
```

When enabled, deploy `Identity__Bff__ClientSecret` via a secret manager or environment variable, not Git. Redirect URIs must be exact HTTPS URLs (development HTTP loopback exception). Configuration changes to existing OpenIddict client registrations require an explicit reviewed migration.

## Verification and acceptance matrix

| Concern | Tests/implementation present | Remaining gate |
| --- | --- | --- |
| Role and permission tenant isolation | In-memory Identity unit tests | SQL Server/HTTP verification; include nested Group roles |
| Identity cookie security-stamp / disable / revoke | Session validator + unit tests | Real-cookie HTTP lifecycle integration |
| Confidential client and PKCE | Client seeder + OpenIddict configuration | Real authorization redirect/token exchange |
| `/api/connect/authorize` protocol validation | OpenIddict passthrough handler | Invalid client/URI/scope/PKCE/nonce and code replay E2E |
| Consent | Only configured BFF implicit-consent client | Confirm integration and future additional client policies |
| Interactive sign-in continuation | Existing Angular login (legacy Password Grant) | New Identity UI flow must preserve safe authorization returnUrl |
| Production secrets / keyring / multitenancy | Documented only | Deploy-time secret and proxy trust verification |

## Known blockers and risks

1. Angular `SignInComponent.getRedirectUrl()` currently rejects an `/api/connect/authorize` returnUrl. Do **not** weaken general returnUrl validation to get past this; implement a dedicated validated OIDC continuation with the Identity login UI / BFF.
2. Existing Angular stores tokens in localStorage; it must not be modified until BFF browser session endpoints are tested and ready for an atomic migration.
3. The BFF session cookie `__Host-Foundation.Bff` is not issued by BFF-03; BFF-04 handles callback and ticket creation.
4. No protocol E2E test has yet proven consent, code issuance, token exchange, PKCE, replay and cross-host behavior. The draft PR should **remain Draft** until these gates are satisfied.
5. The repo CI unit-test workflow is not equivalent to a SQL Server or real-browser integration test. Do not conflate a green unit-test job with protocol readiness.
6. Multi-host tenant login must verify trusted request host and callback registration; never infer Tenant identity solely from URL parameters.

## Next actions

1. Obtain green Build and all BFF/Identity unit tests on PR #11.
2. Add HTTP-level integration tests using a test host and controlled OpenIddict database/client configuration, then verify code round-trip and failure cases.
3. Design and test an Identity UI OIDC continuation that cannot redirect off-origin or cross contexts.
4. Close BFF-03 acceptance tests, update [PROJECT-STATE](../PROJECT-STATE.md), merge reviewed PR only then.
5. Begin BFF-04 (opt-in BFF client middleware, callback, protected server-side tokens and opaque browser cookie); keep Angular Password Grant until BFF-05/06 completion.
