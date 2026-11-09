# FR-13B-3B — unified Tenant Login with forced initial-password change

Date: 2026-11-09. Continues [FR-13B-3 #76](https://github.com/sholehamed/foundation-backend/issues/76), tracked in [#79](https://github.com/sholehamed/foundation-backend/issues/79), parent [FR-13 #70](https://github.com/sholehamed/foundation-backend/issues/70).

## Why this exists

The original FR-13B-3 required employees to choose a separate `/auth/first-login` entry. The product should have ONE standard Login screen; Identity decides whether a valid first-time password requires an immediate forced password change. **Do not make a normal authenticated session and then redirect in Angular.** Existing inactive Tenant users remain ineligible for normal OAuth/OpenIddict authentication until password change succeeds.

## New server contract

- `POST /api/public/tenant-login/resolve` body `{tenantSlug,userName,temporaryPassword}` (only one password field, named to match the existing FR-13B-3 writer). Called only by trusted Tenant-host Angular login when password has the exact generated pattern (4 blocks of six alphanumerics separated by hyphens).
- Requires the existing default-OFF `Identity:TenantFirstLogin:Enabled`, HTTPS, an exact server-validated `{tenantSlug}.{Tenant:WorkspaceBaseDomain}` host and existing per-IP `tenant-first-login` 5/min limiter. Response has `Cache-Control:no-store`.
- Server calls the existing **SQL-validated** `TenantFirstLoginWriter.StartAsync`; a verified, unexpired, non-revoked initial secret for an inactive local Tenant staff user produces `{status:'password_change_required',challenge:<64hex>,expiresAtUtc}`. This challenge is valid only for `/api/public/tenant-first-login/complete`, is single use and valid for 10 minutes; it **is not a JWT, bearer, BFF cookie or authenticated session**. It remains in transient browser component memory only.
- Any ordinary-looking password never invokes the temp writer; a shaped-but-wrong/unknown/expired temp returns `{status:'continue_login'}` with **no account-state disclosure** and no challenge. The unchanged existing login handler is then solely responsible for normal username/password/CAPTCHA and authorization. Existing five-attempt cap still applies to shaped password guesses for inactive accounts.
- **No change** to the OpenIddict standard login/token response shapes, the existing `UserCredentialAuthenticator`, tenant isolation filters or normal role/permissions/menus. No new code-owned Registry ACTION: the existing 12-key Tenant catalog stays 12.

## One-step Owner onboarding (new and optional)

- New `POST /api/tenant/workspace/members/with-initial-password` request is the existing `{userName,email,firstName,lastName}` shape. Requires `RequireAuthorization`, `tenant-member-create` rate limit, HTTPS, the enabled `Identity:TenantFirstLogin` feature and **both** independently registered/effective Owner-only `TENANT.MEMBERS.CREATE` and `TENANT.MEMBERS.ISSUE-TEMP-PASSWORD` Actions. Writer rechecks both, live writable home Tenant and direct-deny rules **inside** SERIALIZABLE SQL transaction.
- Transaction inserts inactive User (normal password remains random undisclosed hash; no roles/groups/permissions), a separate one-hour hashed CSPRNG initial credential, and immutable `create_inactive` + `temp_issued` audit events. Both User and credential are rolled back if either audit write fails. Response `{member:{...isActive:false},temporaryPassword,expiresAtUtc}` shows plaintext password **once** to Owner. No restore/read endpoint, no auto-copy, no storage/logging; only hand to human after in-person identity verification.
- Existing `POST /tenant/workspace/members` remains unchanged for non-initial-password creation and email-invitation workflows. Existing reset/reissue endpoint is still available. Owner must never use a shared default password.
- The initialized account is effectively *password-change-required*: inactive for ordinary login until the one-time temporary credential and change-only challenge complete in a serializable transaction. No separate boolean flag is needed to uphold this security rule; explicit state distinction will matter if future work adds forced changes for already-active users.

## Frontend and compatibility

- Existing Tenant Login form with CAPTCHA first classifies the credential **shape**. Normal passwords go directly to unchanged legacy Tenant login (no resolver call, no rate-limit consumption). Valid temp-shaped secrets invoke the trusted same-origin HTTPS resolver with native `fetch` `credentials:'omit'`, `redirect:'error'`, `referrerPolicy:'no-referrer'`, `cache:'no-store'` and no OAuth/BFF interceptor.
- On `password_change_required`, the same Login component hides username/password and shows *new password / confirmation* fields, retaining challenge only in memory. Submits unchanged secure `/public/tenant-first-login/complete`; clears form/challenge and returns to fresh normal Login only after `{passwordChanged:true,requiresRelogin:true}`. Ordinary API access is impossible during this flow.
- The separate `/auth/first-login` remains as compatibility fallback but is **not advertised** on standard Tenant Login. **The opt-in BFF pilot's external Identity-host interactive login is not yet integrated into this decision**: its separate OAuth/PKCE origin must be handled in its own reviewed host-specific follow-up, not by allowing anonymous cross-origin credential verification. No identity-host origin or signed continuation contract was modified.
- Existing Tenant creation still requires a valid email, even if SMTP is not used. Optional-email onboarding remains [FR-13B-3A #78](https://github.com/sholehamed/foundation-backend/issues/78).

## Operator + test gates

- All new paths are guarded by **existing default OFF** backend and Angular feature flags. No auto SQL migration, Production grant, Feature Registry Apply, SMTP enablement, BFF rollout or Staging deployment. Existing FR-13B-3 SQL migrations + the previously registered 12-action catalog must be applied in a separately reviewed nonproduction environment before enabling.
- Disposable SQL Server tests assert normal password / unknown / incorrect initial password fall through, verified initial password yields only a change-only challenge, one-step creation is inactive and unassigned, full activation requires a new password and the entire create+secret is rolled back on audit failure. Angular tests cover credential classification, exact contract, one-step UI, revoke fail-close and credential-free request settings. End-to-end deployed HTTPS/real-user browser and Identity-host BFF behavior need separate staging validation.
- **Security warning:** manual handoff cannot prove recipient identity; Owner must verify in person. UI/HTTP proxy should not log credential body. Gateway-level distributed limits remain necessary for multi-instance deployment.
