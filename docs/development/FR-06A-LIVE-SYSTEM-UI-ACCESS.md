# FR-06A — Live System UiAccess decision endpoint (first controlled rollout)

> 2026-11-07. This is **UI-level view/action access**, not server authorization enforcement. No new grants, no DB schema changes, no BFF mode changes, and no auto-application.

## API

`GET /api/System/UiAccess/me/{key}` is a fixed, read-only SystemAdministrator-only endpoint. Response `{ "key": "system.roles.permissions", "allowed": true|false }` with `Cache-Control: no-store`. Keys are from `FeatureRegistryDefinitions.Features` (13 System root PAGEs) or `UiElements` (32 nested PAGE + 9 ACTION entries). The caller supplies **only a stable UI key**; no user ID, permission code, roles, tenant ID or permissions claims accepted.

Backend checks **all** of these before returning true:
1. Authenticated System principal with a valid user ID, correct account type, and no tenant claims. The HTTP group separately requires *live* `SystemAdministrator`.
2. Root System UI resource exists exactly once, is active PAGE, has the expected route, belongs to the expected active System Module and Resource, and is linked to **exactly one** active matching Permission.Code. Legacy platform `ResourceId=null` permission ownership is accepted; a conflicting foreign ResourceId is not.
3. Root `*.READ` grant is live from current DB, with user/tenant/role/group activation, scope and direct deny precedence as the existing `EffectivePermissionEvaluator` defines.
4. If a subordinate PAGE/ACTION is requested, its UI resource also exists exactly once, is active, matches code-owned type and route, and has exactly one expected active PermissionUiResource link. Its action permission must also be live. Inactive-by-default Admin/User forms cannot pass.
5. Unknown/duplicate/disabled/misconfigured catalog entries are false; a failed HTTP call must also be interpreted by clients as **not allowed**. The endpoint never relies on stale JWT `permission_code` claims.

## First Angular pilot

Companion frontend PR implements a **single guarded route** `/system/roles/:roleId/permissions`, hides its list/menu entry if unavailable, and rechecks the **ACTION** `system.roles.action.assign-permissions` when the user saves a modified role. It uses a deployment-controlled opt-in `systemUiAccessPilotEnabled`, **OFF by default**, because the deployed DB may not have undergone the reviewed Registry Apply/Grant rollout. With the flag OFF, existing routes and role-management buttons are unchanged.

The frontend and backend are independent: enabling the pilot in Angular is not an API authorization policy. Existing Role endpoints have their own live SystemAdministrator boundary and opt-in `Identity:PlatformPermissions:EnforceRoleEndpoints` granular enforcement. Do not claim that client UI denial secures a direct API call. Turn on the API's reviewed granular role write enforcement only after the Master-scope grant backfill/cutover tests from the prior ADR/runbooks.

## Deployment order

1. Deploy the Backend first, then Angular. Verify the endpoint returns `allowed:false` for unknown, disabled, inactive and ungranted keys, and 401/403 for unauthorized principals.
2. Run the existing **read-only** real SQL catalog inventory, back up the target DB, inspect Registry Preview, resolve any manual-key conflicts, and carry out approved Apply under the default-OFF Registry flag.
3. Grant **`ROLES.READ`** and **`ROLES.ASSIGN-PERMISSIONS`** explicitly to the intended Master/System role (or directly) through a reviewed admin flow. Confirm group/direct deny and revoke behavior.
4. Enable `systemUiAccessPilotEnabled` only in a dedicated test build once all grants and resources are ready. Verify guarded navigation, hidden entry, and denied Save when a grant is revoked mid-session.
5. Keep the pilot OFF on production until an approved rollout and API policy cutover. Never enable BFF or Identity granular permission flags accidentally as a side effect of this change.

## Tests and limitations

Unit tests: active System+Master scoped role grant, missing parent READ, direct deny defeating inherited assignment, missing/disabled parent or child UI, unrelated PermissionUiResource links, tenant-claim spoof, revoked role, suspended user/master Tenant, inactive User create page. Existing Registry SQL Server tests still cover data consistency; this slice adds no new schema or test-only database writes.

Still open: extend access checks to remaining System root/child routes and ACTION controls in a gated migration; shared bulk current-user capability projection to avoid one HTTP call per button at scale; live server-side granular permissions on every write; Tenant scope and TenantPermissionRestriction, admin/user write workflow hardening; multi-writer Registry Apply SQL concurrency and stable Menu.Key scope. See [issue #44](https://github.com/sholehamed/foundation-backend/issues/44).
