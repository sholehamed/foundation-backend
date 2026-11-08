# FR-07A — Bulk live System PAGE + ACTION permissions

> 2026-11-08. Backend source of truth for the 13 System root pages, 32 nested PAGE routes and 35 ACTION descriptors (including delete, change-state, lock, resource sync and group membership). All new ACTION catalog rows are reviewed, additive and **do not grant users or roles access**.

## New current-user API

`GET /api/System/UiAccess/me` is **SystemAdministrator-only** and returns a single no-store, read-only `{schemaVersion:2,entries:[{key,route,type,allowed}]}` snapshot of every code-owned UI affordance (**80 entries: 45 PAGE + 35 ACTION**). It takes **no userId, roleId, tenantId, role list, permission code or requested keys**; it derives the System account and Master Tenant scope exclusively from the authenticated principal. The existing `GET /me/{key}` remains backward-compatible, using the same live projection.

The snapshot is built with a bounded number of EF queries regardless of menu/button counts. It resolves active System account/Master Tenant, scoped direct grants and denies, active direct/group roles and permission assignments. Deny wins. Every allowed UI entry also requires exactly one active code-owned UI row, matching resource/module/type/route and **exactly one** active matching PermissionUiResource link, with active Permission.Code owned by the correct Resource (or reviewed legacy platform permission with ResourceId null). A child always requires its owning root PAGE read permission. No local token Permission.Code snapshot is an authority. Unknown keys and duplicated/misconfigured records fail closed.

Permissions are re-evaluated on each HTTP request. Browser caching is prevented by `Cache-Control: no-store`. This projection is **UI presentation only**; it is not a substitute for API authorization. The existing `SystemAdministrator` and optional granular per-endpoint policies remain independently enforced.

## Registry additions

Previously 13 roots + 32 nested pages + 9 actions. FR-07 adds **26 more code-owned ACTION UI resources** (now 35), including deletion and state changes for ten administration features, Admin/User account-lock and direct-permission actions, Group membership mutations, and API resource sync. Each ACTION references an already-declared exact `Permission.Code` and creates no menu. Admin/User sensitive actions start **inactive** by default. Migration/production DB Apply is not performed here; manually existing records remain untouched.

Registry Preview must be reviewed before Apply because historical duplicate, wrong-scope, unlinked or manually owned records still block writes. `Identity:FeatureRegistry:EnableApply` stays OFF by default. The disposable SQL Server test suite verifies the new catalog count and no grants with the real EF migrations.

## Angular integration / rollout

The paired Angular PR uses one request per navigation to obtain current-user page/action decisions; every System executable leaf route is guarded by matching actual Angular route templates to backend PAGE routes (including resources nested under another module). Reusable `*systemCan` hides buttons by PAGE route template or ACTION stable key. The nine previously declared actions plus new CRUD/state actions can now be rendered only with live permission hints. Sensitive writes should perform a fresh snapshot check when clicked, and the API MUST check independently.

To protect existing installations whose SQL catalogs are not yet registered/granted, `systemUiAccessPilotEnabled` remains **false in every Angular build environment**, and remains a required, explicit rollout step. With OFF, no currently working user workflows should be silently revoked. Enabling before Registry Apply and approved Master-scope grants will intentionally hide pages/actions, including Dashboard.

Safe release sequence: deploy backend/Angular, take DB backup, run read-only catalog inventory, verify Preview, approved Apply in test, explicitly grant intended System roles, enable frontend switch in a test build, test System root/children and denied/revoked routes, separately enable API granular policy gates only after reviewed grant migration, and only then consider production rollout. Neither this PR nor frontend can certify deployed real-user grants or SQL concurrency.

## Limitations remaining

- UI guards and hidden buttons are **not** API authorization; some System endpoints retain coarse administrator-only policies.
- No Tenant feature permissions, branch scopes, stable Menu.Key/Scope or real SQL Server concurrent Apply certification.
- Existing Admins/Users write forms are still inactive in the catalog and may need separate repair; their Angular routes are guarded only when the opt-in is enabled.
- Route and ACTION consistency is checked against the authoritative Backend catalog; end-to-end browser tests with real test grants remain a rollout gate.

Follow [issue #44](https://github.com/sholehamed/foundation-backend/issues/44).
