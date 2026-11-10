# FR-14C — Reviewed System Feature Registry Apply (fingerprint + SQL)

Checkpoint: 2026-11-10. System Admin is the sole owner of global Module, Resource, PAGE/ACTION, Menu, API and Permission definitions. This change upgrades the existing **default-OFF** System Feature Registry Apply from a blind POST into an explicit, reviewable, serializable SQL operation.

## Operator contract

1. An authenticated System Administrator loads `GET /api/system/FeatureRegistry/preview` and the code-owned Manifest. The response now includes a 64-character SHA-256 `fingerprint` as well as `canApply`, `hasPending` and the per-row plan.
2. The operator reviews conflicts, pending additions and executable Angular routes. To apply the System catalog, the live persisted **System SuperAdmin** must explicitly confirm exactly that fingerprint with `POST /api/system/FeatureRegistry/apply` body `{ "fingerprint": "64-hex" }`. The existing `Identity:FeatureRegistry:EnableApply` server flag must be separately enabled for a short reviewed operator window (default OFF).
3. The server starts a `Serializable` SQL Server transaction, rereads the live code-owned endpoint metadata and all central catalog rows, re-computes the fingerprint, rejects changed/conflicting plans with HTTP 409, then applies only if it still matches. No role/user/group grants are created, changed or implicitly revoked. The approved transaction commits atomically.
4. An empty/malformed fingerprint receives HTTP 400, a non-SuperAdmin receives 403, and disabled Apply returns 409. Stale approval never silently applies new definitions. Angular must clear the stale preview on 409 and request a fresh one.
5. An already-consistent catalog with a current fingerprint is idempotent and has no writes. The response is the approved (pre-commit) plan; clients must reload Preview and independent endpoint/menu integrity checks rather than claim post-Apply readiness from the returned body.

## Fingerprint scope

Includes the sorted planned changes and live EndpointDataSource descriptors, the Module/Resource/Permission/UI/API/Menu/Category rows and their relevant active/ownership/route/permission-link information and SQL RowVersions. It never includes passwords, user identities, direct grants, Tenant accounts, or secrets. A changed active flag, unexpected link, or moved route invalidates the old token even if a plan item's status stays `Present`.

The low-level `FeatureRegistrySyncService.ApplyAsync` is retained for explicitly disposable code/data test fixtures. **Only `ApplyReviewedAsync` is wired to the public System Registry HTTP POST.** This is a deliberate request-contract change from the previously accepted empty JSON body and must be deployed with the matching Angular client; normal deployments continue with the Apply flag OFF.

## Tests

Disposable SQL Server integration verifies valid/stale fingerprints, exact no-write behavior, a live unauthenticated/non-System SuperAdmin denial, serializable successful/idempotent Apply, a drift in an already-`Present` UI active flag, and absence of Role/User grants. Existing System/Tenant E2E and CI remain mandatory. No live operator DB migration, flag activation, permissions backfill, grant or Staging deployment was executed by this PR.

## Remaining before enabling dynamic Tenant navigation

Run System Feature Registry and Tenant Permission Catalog previews against a **real deployed target**, resolve central records and six approved Tenant Menu routes with reviewable System Admin operations, check runtime endpoint integrity, verify current grants/entitlements/branch bounds and two-Tenant browser BFF revocation, then separately enable the independently OFF Tenant dynamic navigation pilot. This PR is a safe prerequisite, not operational Staging certification.

## Real signed HTTP follow-up acceptance

The existing disposable `tests/http/tenant_iam_chain_e2e.py` now confirms that an authenticated System SuperAdmin receives the 64-character live System Registry Preview fingerprint and that the same fingerprint cannot trigger System catalog Apply while the independently default-OFF deployment flag is not enabled (HTTP 409). It also demands anonymous 401 and Tenant Owner/staff 403 for the Apply route, and re-fetches the unchanged Preview fingerprint to detect any unintended SQL catalog write. The existing positive/rollback/stale-fingerprint checks run against a separate disposable real SQL integration fixture. Neither test touches Staging/Production or enables a feature flag.
