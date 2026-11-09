# FR-14A — API ↔ PAGE/ACTION authorization inventory and gate

**Scope:** Source-level, regression-tested authorization classification across the existing Foundation Identity endpoint map, and a guarded, optional real System feature permission requirement. Cross-repo Angular route audit is in Frontend PR #50. No Staging deployment, SQL migration, catalog Apply, grant changes, or feature flag activation.

## Audit domains (never conflate them)

| Surface | Auth boundary | Registry vs live enforcement | Status after FR-14A |
|---|---|---|---|
| System CRUD (13 root resources, their nested UI pages and mapped HTTP API) | Live `SystemAdministrator` on every map group | Every mapped System Feature Endpoint must declare a unique known code-owned Resource/Permission pair. FR-14A adds `SystemFeatureEndpointPermissionRequirement` automatically for **every** `WithFeatureEndpoint`. When the reviewed `Identity:PlatformPermissions:EnforcePermissionEndpoints` flag is ON, it uses the shared live `EffectivePermissionEvaluator` (direct deny takes priority, active account/Master scope/role/group required); when OFF, coarse existing SystemAdministrator policy still applies. | Auth *contract and opt-in live requirement* implemented; deployment opt-in not authorized |
| System internal UI-access | `SystemAdministrator` + own live UI projection | `/api/System/UiAccess/me*` is **read-only**, not a CRUD Feature Registry endpoint, and intentionally returns live UI hints, not permission grants | Explicitly classified exception |
| User current navigation | Authenticated current-user scope + live permission-aware menu reader | `/api/System/Menus/me` is shared by System/Tenant; **not** SystemAdministrator only | Explicitly classified exception |
| Public tenant slug resolve | Explicit `AllowAnonymous` + narrow read-only slug projection | Cannot be a privileged System Feature Endpoint | Explicitly classified exception |
| Tenant Workspace | Every private API has `RequireAuthorization` and handler-local active Tenant/Home/Writable Owner and current target/grant checks. A group Owner flag alone is not an API permission. | Explicit 17-code `TenantWorkspacePermissionCatalog`; especially sensitive actions enforce live PAGE+ACTION, exact row-version CAS, audited transaction. No generic automatic cross-endpoint policy is claimed. | Classified with independent SQL and real JWT HTTP suite |
| Public signup/first login/invitation | Explicit `AllowAnonymous` plus per-action rate limiting, CAPTCHA/XSRF and short-lived proofs where applicable | Not Tenant admin grants | Rate-limit declarations checked, protocol controls separately tested |
| Identity Auth / OpenIddict Connect | Identity login, CAPTCHA, Identity cookie, authorization code/PKCE, OIDC state/nonce | This is a distinct OAuth protocol with mixed anonymous/challenge endpoints, **not** System feature CRUD; a uniform RequireSystemPermission would break authentication | Classified but requires separate BFF-05/06 lifecycle/operational certification |
| BFF cookie endpoints | Opaque host-scoped cookie, CSRF and live identity/session validation | Outside Identity endpoint group; security evidence comes from BFF/SQL/HTTPS browser tests and rollout gates | Separate protocol (outside this source scanner) |

### Automated evidence

`python3 -m unittest discover -s scripts/security -p 'test_*.py' -v`
`python3 scripts/security/fr14a_audit.py --out /tmp/fr14a-identity-surface.json`

`bff-ci.yml` runs both on every PR and `main`, failing on:
1. Missing/duplicate/unregistered System `FeatureEndpointMetadata`, moving a permission to a wrong Resource, missing SystemAdministrator route group or exposing a cataloged endpoint anonymously.
2. Unknown mutation using a READ code; new POST-as-READ routes except the reviewed read-only paged `SYSTEM.GROUPS.MEMBERS-LIST`.
3. A private Tenant Workspace route without `RequireAuthorization`, a new unreviewed public Tenant route or a supported public flow missing explicit rate-limit/anonymous routing.
4. Public Signup route missing rate limiting.
5. Accidental loss of the recognized source inventory; one JSON row per classified HTTP registration is emitted as the `fr14a-identity-security-inventory` Actions artifact. **The artifact proves declared route registration, not successful authorization against a deployed DB**.

Frontend companion verifies all 13 root System routes, 32 nested lazy pages via existing tests, guard inheritance on all Tenant Admin children and independent Owner capability gates on sensitive views.

### Runtime defense added

Before FR-14A, most System endpoints had `WithFeatureEndpoint` **descriptions** but only Roles/Permissions had opt-in `RequireSystemPermission`; the feature description was not an authorization requirement. Now the extension installs an actual per-endpoint `SystemFeatureEndpointPermissionRequirement`. The handler first verifies the authenticated System SuperAdmin claims and exact code-owned resource pairing, then checks the **live Master-scope grant** on opt-in. Direct denied UserPermission blocks grants inherited from any role or group. Existing `SystemAdministrator` policy and all existing explicit requirements stay in place; no downgrade or superadmin fallback is allowed once the granular flag is ON.

**The flag is left OFF.** The existing `PlatformGranularCutoverStartupGuard` already refuses Production opt-in without reviewed catalog, data and scope. No implicit migration, backfill or role grants. This is a staged security gate, not a Production authorization rollout.

### Findings that are *not* secretly considered fixed

- **FR-14B / high:** Existing System User/Admin Create/Update code contracts are historically unsafe for System accounts (hardcoded TenantId and PasswordHash mutation). Their UI actions are intentionally disabled. Fix domain/API write semantics with tests before restoring users/admin editors; audited endpoint descriptors and coarse SystemAdministrator policy do NOT make these old mutation handlers safe.
- **FR-14B / high:** System granular permission enforcement is default OFF. Approved Master SQL inventory, safe Registry Preview/Apply, active role grants and 401/403 real-browser tests are mandatory **before** enablement. System roles/permissions opt-in checks and new general endpoint enforcement should be inspected together under active flags.
- **FR-14C / high:** Tenant menu/feature ownership, complete tenant resource/endpoint semantics and delegated role/branch/Own/Child policies are not finalized. Tenant reads/Owner writes already have server checks, but generic tenant admin permission delegation is not a completed contract.
- **BFF-05/06/07 / high:** Angular still uses legacy Password Grant/localStorage, and BFF remains OFF in shipped builds; upstream Identity refresh/SSO revocation, replay/refresh coordination, production HA/Data Protection key rotation, real deployed proxy and client migration remain release gates.
- **Operational FR-13 / critical release gate:** no real Staging infrastructure. SQL schema migrations, explicit Legacy catalog collision cleanup, approved Preview/Apply, backup/restore, trusted TLS/proxy, secrets/keyring, rollback and multi-account deployed browser acceptance require independent authorized operator work.
- **FR-14B / medium:** Legacy menu category reorder lacks a full backend-supported contract; additional positive browser CRUD tests for System editors are required. Subscription status is a placeholder rather than paid entitlement.

## Acceptance and boundaries

- Backend CI: source audit/mutation tests, .NET build/Identity/BFF unit tests, disposable SQL Server suite, separately run signed JWT/SQL HTTP E2E. Frontend CI: Angular build/unit + Chromium pilot. Code and tests should be merged only after latest PR HEAD CI, and verified on merged main.
- No change to any active authorization or feature pilot setting, no SQL/Registry Apply, staging deployment, user grants or permission data.
- FR-14A can close as **inventory and enforcement-gate development** when these checks pass. It is explicitly **not** equivalent to FR-14B/14C remediation, BFF production cutover, or deployed security certification.
