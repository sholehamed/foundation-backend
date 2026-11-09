# FR-13C-2E — Explain live Tenant member permission decisions (2026-11-09)

**Status:** implementation proposed; staging/production intentionally deferred. No migration or permission-catalog modification.

## Read contract

`GET /api/tenant/workspace/members/{userId}/effective-permissions` is authenticated and **read-only**. It requires a live active local Owner in the current home Tenant, plus effective `TENANT.MEMBERS.READ`, `TENANT.ROLES.READ`, and `TENANT.GROUPS.READ` permissions. Every call rechecks permissions, requires SQL Server and sends `Cache-Control: no-store`. The server derives actor/TenantId; a user GUID is the only client-selected filter. Inactive same-Tenant members can be inspected, but all live decisions are denied. Cross-Tenant/system/missing users return 404 after Owner authorization; non-owners/system identities are denied with 403.

Response: user ID, user name, active state, Owner flag, and one entry for every explicit known Tenant PAGE/ACTION catalog key (17 at this stage), preserving stable catalog order. Entry has code/name/kind/route, registration status, **`allowed` produced by the existing authoritative `TenantWorkspacePermissionEvaluator.AllowsAsync`** using the target user's own principal context, decision reason, and up to 20 bounded candidate provenance links (`role`, `group_role`, `direct_allow`, `direct_deny`, `direct_conflict`, `owner_fallback`). Role/group names and IDs are tenant-bound; candidate links are **evidence**, not a second permission decision. `MoreSources` indicates truncation of long provenance chains.

A direct user deny always wins over all indirect grants and Owner; an explicit direct allow wins over role/group. Duplicate direct overrides fail closed. Active local role and group links can grant permissions only with active local Tenant role/group and a registered, active permission definition. Missing/duplicate/inactive definitions fail closed except the documented legacy local Owner fallback for a **missing** definition. A disabled user receives no effective permissions. No JWT role claims or UI snapshots are treated as authority.

**Scope caveat:** `allowed` is the **raw live Tenant workspace PAGE/ACTION permission decision**, not proof that a specific write endpoint can execute. Privileged writes additionally check local Owner, writable Tenant, feature flags, registered action/UI resource and target-specific restrictions. The Angular UI must display this distinction explicitly rather than presenting a raw ACTION grant as definitive operation authorization. This phase does not expose arbitrary platform-wide resources, system permissions or endpoint-list introspection.

## Safety and release

- Only known code-owned Tenant permission definitions; no cross-tenant arbitrary permission enumeration or bulk member export. No email, password/initial secret, token, audit credential, JWT or contact fields.
- No endpoint to assign grants, no new permissions, no snapshot/history persistence, no catalog apply or Tenant mutations. Intended for troubleshooting and explanation, not changing authorization.
- Real SQL acceptance tests compare every returned `allowed` bit against `AllowsAsync`, prove direct deny versus inherited GroupRole, active/inactive group/user, Owner fallback, cross-tenant 404 and non-owner 403. Signed HTTP E2E verifies access boundaries and immediate GroupRole grant/revoke reflected without token replacement.
- No Staging/Production deployment; schema/registry promotion remains an independent approved operator step.

**Next:** optional endpoint-level *eligibility* explanation (write action prerequisites) and more general Resource.Action provenance should be separately designed. Do not confuse this initial 17-key Tenant catalog report with universal authorization decisions.
