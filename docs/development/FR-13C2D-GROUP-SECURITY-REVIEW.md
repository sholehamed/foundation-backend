# FR-13C-2D — Owner-only group security history and legacy-link assessment

**Date:** 2026-11-09. **Environment:** code/CI only; Staging and Production deployment deferred by user decision.

## Goal and scope

Join three **existing append-only** Tenant Group ledgers into one trustworthy, bounded, group-specific timeline. Supply **read-only** counts that flag historical `UserGroup`/`GroupRole` relationships requiring manual review. Do NOT silently delete, edit, repair, deactivate, backfill or mark legacy grants as unauthorized.

### API contract

`GET /api/tenant/workspace/groups/{groupId}/audit?page=1` returns:

- `groupId`, current `groupName`, `pageNumber`, `hasNext`, up to **20** `items`.
- Events in descending UTC timestamp, then GUID (stable tie-break): `group` creation/name/state; `group_member` grant/revoke; `group_role` grant/revoke. Each item includes event ID, actor ID/name if still present, optional target member/role ID/name snapshot, group old/new name/state where applicable. **No email, password, token, hash, session, TenantId parameter or mutable link payload.**
- `integrity`: `memberLinks`, `roleLinks`, `ownerMemberLinks`, `invalidMemberLinks`, `invalidRoleLinks`, `unattributedMemberLinks`, `unattributedRoleLinks`.

No new ACTION/permission catalog change is needed for a read-only endpoint. Backend requires a live **local Owner** plus **all three** existing PAGE grants `TENANT.GROUPS.READ`, `TENANT.MEMBERS.READ`, `TENANT.ROLES.READ` independently. A staff user with all three reads is **still 403**. SQL Server is required; 503 if absent. The endpoint validates page 1..1000 and group GUID; no group or foreign Tenant group is 404. All responses set `Cache-Control: no-store`.

SQL reader filters **every** union ledger branch by both server-derived `TenantId` and URL `GroupId`; even actor and target username joins require the same TenantId. A foreign-ledger row with the *same group GUID* cannot leak. Bounded `OFFSET/FETCH NEXT 21` obtains 20 events and `hasNext` only. The reader issues no writes.

### Legacy read-only assessment

All counters describe **currently persisted link rows**, not future effective access, and are subject to concurrent writes between the two read queries. The integrity counters use group and TenantId filters, and count:

| Key | Meaning |
| --- | --- |
| memberLinks / roleLinks | Total existing rows of UserGroup/GroupRole for this Tenant + Group |
| ownerMemberLinks | Group members who already have direct local OWNER Role assignments |
| invalidMemberLinks | Member target missing, foreign Tenant or non-Tenant User |
| invalidRoleLinks | Role target missing, foreign Tenant, OWNER or non-Tenant Role scope |
| unattributedMemberLinks | Current UserGroup links with **no matching grant event at all** in the retained GroupMembership ledger |
| unattributedRoleLinks | Current GroupRole links with **no matching grant event at all** in the retained GroupRole ledger |

**Unattributed ≠ unauthorized.** Existing links may predate the introduction of append-only ledgers, and presence of a ledger grant is not proof of current authorization; a matching event indicates only historical traceability. Inactive groups, inactive users and inactive roles are not counted as invalid; evaluator semantics determine effective access. For any flagged link, operators must evaluate actual effective grants and original data provenance separately. No repair API is introduced.

### Acceptance and rollout

Disposable SQL tests: 20/5 pagination across all three ledgers, newer foreign ledger row sharing GroupId cannot appear, Owner-only including triple-read staff denial, 400 invalid page/404 absent Group, append-only preserved, legacy/unattributed/Owner role counters exact, and no relationship mutations. Real signed HTTP tests: 401 anonymous, 403 System/staff, 404 foreign ID, 400 page, 200 Owner with complete categories and bounded overview. Angular page + gate tests: live canViewGroupAudit capability, read-only, pagination, Persian Jalali rendering and 403/404 fail closed.

No new migration: reads **existing** three audit tables from FR-13C-2A/B/C. Prior schema migrations and reviewed permission catalog must still be separately operator-approved before any later Staging/Production rollout. Do not infer deployed readiness from merged code/CI. Next potential phase: a **separately reviewed migration/reconciliation plan** with export, human sign-off, and rollback, not destructive auto-fixes.
