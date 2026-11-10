# FR-14C — Tenant Permission/UI cross-link integrity before dynamic menu cutover

Checkpoint: 2026-11-10. This change completes a **code-level Tenant catalog drift guard**, not an operational deployment or permission grant.

## Context

Only System Admin defines the global `tenant-workspace` Module, `tenant-administration` Resource, 17 PAGE/ACTION Permissions and their corresponding UI Resources. Tenant Owners only assign a restricted subset of these existing definitions to local roles. Each of the 17 definitions is **exactly one Permission -> one UI Resource**.

Until this change, `TenantPermissionCatalogSync.PreviewAsync` counted only the expected `PermissionUiResource` link. If a PAGE or ACTION had a second link to an unrelated Permission, the expected link was still present and the Preview incorrectly reported `Present`. Likewise, a Permission linked to an unrelated UI Resource could escape the conflict and the approved fingerprint, which is dangerous before dynamic navigation or role delegation.

## Changes

- `TenantPermissionCatalogSync.PreviewAsync` checks **all links touching either end** of each reviewed Tenant Permission/UI pair. Unexpected mapping, foreign Permission, missing intended link with a surviving foreign link, or duplicate mapping is a `Conflict`; `CanApply` becomes false. A clean missing link without drift remains `Create`. Existing stable canonical links remain `Present`.
- The SHA-256 Preview fingerprint now includes all those related link IDs and endpoints. An added/changed unrelated link makes an earlier reviewed fingerprint stale. The existing explicit, serializable, default-OFF `ApplyAsync` cannot adopt or repair the inconsistent graph.
- `CentralTenantNavigationReadiness.ReadAsync` verifies that the PAGE `UiResource` has **exactly one** link to the correct Tenant PAGE Permission, not merely the existence of that link. New diagnostic `page_ui_permission_link_conflict` is distinct from `page_ui_permission_link_missing`.
- Disposable real SQL Server regressions add a cross-link between the registered `TENANT.ROLES.READ` and Members PAGE, assert that both affected definitions fail, that an old fingerprint cannot Apply, that a missing intended link plus foreign link fails closed, that no user/role grant is created, and that central navigation readiness cannot turn green until explicit test-fixture cleanup.

## Operational boundary

- No data repair, auto-Apply, menus, permissions, user/role grants, migrations, toggles or external Staging changes.
- Any existing bad link makes the operator Preview read **Conflict**. A System Admin must review relevant rows and approve a separate remediation with backup/rollback; the application does not delete or reparent unknown links.
- This closes the cross-link **detection** gap. It does not declare the live external registry, System endpoint catalog, Tenant entitlement boundaries, branch-level RBAC, or actual dynamic sidebar ready.
- Before enabling the existing **default-OFF** `tenantDynamicNavigationPilotEnabled`, require clean System Feature Registry Preview, clean Tenant Permission Catalog Preview, all-six Tenant Navigation Readiness, no unregistered System Endpoint Integrity issues, and verified HTTP/SQL + two-Tenant browser denial on a reviewed target. The operator-controlled System Menu definitions remain the only source of navigation; Tenant never authors them.

## Verification

Existing `FeatureRegistrySqlServerTests` CI filter runs real SQL Server migrations and adversarial tests; BFF/Identity CI and Tenant IAM real HTTP/SQL E2E are independent gates. A passing disposable CI suite is not proof of Staging deployment. Document the PR-head and main-head run IDs and defer any manual rollout until environment evidence is available.
