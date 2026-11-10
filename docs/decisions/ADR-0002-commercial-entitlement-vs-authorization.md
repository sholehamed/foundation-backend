# ADR-0002 — Separate commercial entitlements from scoped user authorization

- **Status:** Proposed (requires owner review; not yet accepted or implemented)
- **Date:** 2026-11-10
- **Scope:** Foundation Backend, frontend plan display, Tenant authorization, commercial billing

## Context

Foundation already stores centrally defined Modules/Resources/Permissions, role/user/group grants and System/Tenant navigation. In the product model, a `Resource` is an EndpointGroup/Controller boundary, and Module membership groups these resources. A Tenant purchases a Plan that includes Modules, may receive extra Features and quantitative Limits, and its Owner assigns a subset of *eligible* permissions to its users. Existing `TenantModules` is not a subscription contract; startup/registration `RequestedPlanKey` records intent only.

The design must support fully DB-driven public plan cards and parent/child plan offerings, sale at a zero total using one-use/expiring 100% or fixed discounts, verified external settlements, and optional modules altering business workflows without cloning controllers or services.

## Decision proposal

1. **Independent planes:** Global System-owned definition Registry; tenant-wide commercial entitlement; Tenant local authorization; tenant business configuration. A commercial purchase never creates user grants. Grant without entitlement is ineffective; both gates are required for product operation.
2. **Controlled Resource grain:** Resource corresponds approximately to a Controller/EndpointGroup and contains stable Permission codes. ApiResources are individual endpoints, UiResources are pages/actions/fields/widgets; they do not define a second user permission system.
3. **Typed capability catalog:** Stable, code-validated boolean feature, count limit, periodic quota and allowed-mode definitions; System Admin configures approved values, not executable policies or arbitrary expressions. Public card text is descriptive projection linked to underlying definitions.
4. **Immutable published versions:** Plan identity may have ParentPlanId; each published PlanVersion pins a specific parent version and resolves a flattened module/capability snapshot. Subscriptions hold version/snapshot and never change merely because parent pricing or display changes.
5. **No duplicate entitlement truth:** Subscription plus approved add-ons and overrides determine access. Existing TenantModules must be inventoried and migrated to a safe read-only projection or retired; never act as a parallel license override.
6. **Typed quota with durable accounting:** A server-side meter/ledger with idempotent reservations, transaction-safe enforcement and explicit periods. Count predicates and unlimited semantics are explicit.
7. **Workflow extension by consistency:** Synchronous policy for required preconditions; Outbox events for optional after-commit effects; persisted workflow for mandatory cross-module payment. Feature disabled behavior is specified rather than ad-hoc scattered `if` logic.
8. **Commerce vs privilege:** Discount reduces Order price, never gives permissions directly. A zero-priced, valid, committed Order can be fulfilled without a gateway. Manual external settlement requires distinct audited proof; a 100% discount is not evidence of real-world payment. Activation idempotent.
9. **Platform and onboarding continuity:** System Admin core and Tenant Owner subscription-management/recovery access are independent of purchased commercial modules; do not create deadlocks for unpaid tenants.

## Alternatives and trade-offs

| Alternative | Why not |
| --- | --- |
| Plan directly stores a list of PermissionIds and gives them to all users | Allows privilege escalation and couples subscriptions to RBAC implementation; hides Module ownership |
| One boolean flag/column per plan feature | Every new feature needs schema/API/form changes; brittle and duplicate maintenance |
| PlanVersion inherits live parent Plan rather than a pinned version | Retroactively changes existing contracts/checkout behavior |
| Trust TenantModules.IsEnabled as license source | Conflicting multiple sources, no invoice/subscription provenance |
| Enforce quotas in Angular or distributed cache only | Races, direct HTTP bypass and inaccurate billing |
| Event-only handling of mandatory deposit/payment | Core workflow may incorrectly complete before verification |
| Use full discount as synonymous with offline money received | Financial reporting and audit are incorrect |
| Dynamically execute SQL/script/HTML configured as plan feature | Severe tenant isolation/security/code-injection risks |

## Consequences

- New versioned Catalog, Billing, Subscription, Quota and Outbox integrations are required. No one-step entitlement migration; backward-compatible staging plan and existing Tenant inventory first.
- Existing seed of first-login Owner administrative rights is retained, but must be explicitly classified as core and remain available to buy/renew even when no paid plan is active.
- All product endpoints must eventually carry stable Resource/Permission metadata, and all server decisions must be enforced at request/use point; menus are projections only.
- A change in deployment/module availability must not silently create or revive tenant access.
- Design choices D-01..D-10 remain unresolved and explicit in the [canonical architecture](../architecture/modular-entitlements-plans-limits.md#11-explicit-decisions-to-settle-before-codemigrations).

## Rollout boundary

**This ADR and FR-17A documentation are design-only.** No accepted architecture is represented as shipped runtime. Continue with a read-only EndpointGroup/Resource inventory, validation tests and a reviewed code/SQL migration proposal first. Separate human approval required before any paid capability enforcement, grants, existing data reconciliation, or staging/prod rollout.

## References

- [Architecture: modular entitlements / plans / limits / workflows](../architecture/modular-entitlements-plans-limits.md)
- [FR-17A implementation handoff](../development/FR-17A-MODULAR-ENTITLEMENTS-HANDOFF.md)
- [Current Feature Registry & Authorization](../architecture/feature-registry-authorization.md)
- [FR-16C first-login admin bootstrap](../development/FR-16C-FIRST-LOGIN-ADMIN-READY.md)
