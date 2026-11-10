# FR-14C — Fail-closed central System Registry Preview for legacy drift

Checkpoint 2026-11-10. This is the companion to strict System feature Permission ownership. System Admin alone creates and manages all central definitions. Tenant Owner never authors Menu/Page/Resource/Endpoint/Permission. No dynamic-navigation flag, live database, or deployment is changed by this work.

## Problem

The additive `FeatureRegistrySyncService.PreviewAsync` previously reported a pre-existing System feature Permission.Code with `ResourceId=NULL` as `Present`, while the live `CurrentSystemUiAccessService` and granular endpoint guard must reject unowned permissions. It also accepted a valid expected PAGE or API permission link even when an additional **foreign** Permission linked to the same root or API. That could leave a green-looking operator Preview despite unsafe legacy links.

## New operator contract

- For every code-owned System permission, exactly one active `Permission` must belong to the specified System `Resource.Id` within the expected `Module`. Duplicate, inactive, null/unowned and moved records are **Conflict**. The code cannot repair or adopt one automatically.
- PAGE root, nested PAGE/ACTION, and API resource graph must each have at most one reviewed Permission link. A foreign link, multiple links (even to the expected Permission), or ambiguous ownership is **Conflict**, not `Present` or a generic `Create`.
- `CanApply=false` on any conflict. The existing `EnableApply=false` deployment default remains unchanged, and no grants or inactive records are adjusted.
- A System operator must inspect SQL catalog and audit history, preserve old data, explicitly reconcile values via approved migration/administrative controls, and re-run Preview and the independent live `system-endpoints/integrity` audit. **Do not force-Apply over a conflict.**

## Tested

- InMemory fail-closed regression: a duplicate/unowned preexisting `ROLES.READ` Permission is never silently adopted; an explicitly reviewed fixture cleanup is required before an idempotent Apply.
- Root PAGE, nested PAGE and API Resource each reject extra foreign Permission links with no mutations when Preview is conflicting.
- Existing SQL Server Feature Registry, System/Tenant role grants, real HTTP + SQL and BFF CI must stay green. No production/staging switch, migration, grant, or dynamic menu rollout is performed.

## Next before dynamic menu

The **central System Registry Apply** endpoint still needs a reviewed request-bound Preview fingerprint and atomic SQL recheck (equivalent to Tenant Catalog Apply) before operator use should be deemed cutover-ready. The DB-owned central Tenant navigation routes must also be reconciled via System Admin on a real target. Dynamic Angular Tenant menu stays OFF pending both gates and authenticated browser checks.
