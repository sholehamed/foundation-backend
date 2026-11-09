#!/usr/bin/env python3
"""Disposable CI-only real HTTP + OpenIddict + SQL Server Tenant IAM chain.

No TestServer, mock authentication, privileged Tenant mutation endpoint or
production DB. Run only against a disposable local Development fixture after
seed-tenant-iam, with a reviewed System catalog preview/apply (flag opt-in).
"""
import json
import os
import urllib.error
import urllib.parse
import urllib.request
import uuid

BASE = os.environ.get("SHINERA_E2E_BASE_URL", "http://127.0.0.1:5069").rstrip("/")
PASSWORD = os.environ["SHINERA_DEVELOPMENT_ADMIN_PASSWORD"]
CAPTCHA = os.environ.get("Identity__E2E__CaptchaCode", "")
SLUG = "e2e-http-ci"
TENANT_PATH = "/api/tenant/workspace"
REGISTRY_PATH = "/api/system/FeatureRegistry/tenant-permissions"
STAFF_NAME = "e2e-iam-staff"
MEMBERS_READ = "TENANT.MEMBERS.READ"
FOREIGN_ID = "00000000-0000-0000-0000-000000000001"

if (
    os.environ.get("ASPNETCORE_ENVIRONMENT") != "Development"
    or os.environ.get("Identity__E2E__Enabled") != "true"
    or os.environ.get("Identity__TenantPermissions__EnableCatalogApply") != "true"
    or os.environ.get("SHINERA_E2E_DB_MUTATION") != "stale-jwt-ci-only"
    or not os.environ.get("DBNAME", "").startswith("Shinera_E2E_")
    or os.environ.get("DataBaseServer") not in ("127.0.0.1,1433", "localhost,1433")
    or CAPTCHA != "ABCDE"
):
    raise RuntimeError("Tenant IAM HTTP E2E requires explicitly enabled disposable Development SQL fixture")

CHECKS = 0


def request(method: str, path: str, token: str = "", data=None,
            headers=None, form=False):
    incoming = dict(headers or {})
    if token:
        incoming["Authorization"] = "Bearer " + token
    if data is not None:
        if form:
            payload = urllib.parse.urlencode(data).encode("utf-8")
            incoming["Content-Type"] = "application/x-www-form-urlencoded"
        else:
            payload = json.dumps(data).encode("utf-8")
            incoming["Content-Type"] = "application/json"
    else:
        payload = None
    req = urllib.request.Request(BASE + path, payload, incoming, method=method)
    try:
        with urllib.request.urlopen(req, timeout=35) as resp:
            return resp.status, resp.read()
    except urllib.error.HTTPError as e:
        return e.code, e.read()


def check(method, path, expected, *, token="", data=None, headers=None, form=False):
    global CHECKS
    code, body = request(method, path, token, data, headers, form)
    if code != expected:
        # Never display credentials, bearer tokens or full response payloads.
        raise AssertionError(f"{method} {path}: expected {expected}, got {code}; "
                             f"body-prefix={body[:180].decode('utf-8', 'replace')}")
    CHECKS += 1
    print(f"PASS {CHECKS:03d} {method} {path} -> {code}", flush=True)
    return json.loads(body) if body else None


def login(username: str, context: str, tenant: str = "") -> str:
    challenge = check("GET", "/api/system/Auth/captcha/new", 200)
    data = {
        "grant_type": "password", "client_id": "postman", "username": username,
        "password": PASSWORD, "login_context": context,
        "captchaid": challenge["token"], "userEnteredCaptchaCode": CAPTCHA,
        "scope": "openid profile email api offline_access",
    }
    if tenant:
        data["tenant"] = tenant
    response = check("POST", "/api/system/Auth/login", 200, data=data, form=True)
    token = response["access_token"]
    assert token and token.count(".") == 2, "Expected signed Development OpenIddict access JWT"
    return token


def member_permission(owner: str, role_id: str):
    return check("GET", f"{TENANT_PATH}/roles/{role_id}/permissions", 200, token=owner)


def member_matrix(owner: str, staff_id: str):
    return check("GET", f"{TENANT_PATH}/members/{staff_id}/roles", 200, token=owner)


def ui_allowed(token: str, code: str) -> bool:
    result = check("GET", TENANT_PATH + "/ui-access/me", 200, token=token)
    rows = [item for item in result["entries"] if item["key"] == code]
    assert len(rows) == 1, "Tenant permission must appear exactly once in UI access catalog"
    return rows[0]["allowed"]


def run():
    print("Starting real HTTP Tenant role -> permission -> member -> revocation E2E", flush=True)
    admin = login("admin", "system")
    owner = login("e2e-tenant", "tenant", SLUG)
    staff = login(STAFF_NAME, "tenant", SLUG)

    # All HTTP calls have live, independently validated OpenIddict signatures.
    check("GET", TENANT_PATH + "/members", 401)
    check("POST", TENANT_PATH + "/roles", 401,
          data={"name": "Anonymous", "description": None})
    check("GET", TENANT_PATH + "/roles", 403, token=admin)
    check("GET", TENANT_PATH + "/members", 403, token=staff)
    # Real HTTP route registration, same-origin authenticated Owner read,
    # no caller-selected TenantId, and non-Owner privacy gate.
    audit = check("GET", TENANT_PATH + "/members/audit", 200, token=owner)
    assert audit["pageNumber"] == 1 and isinstance(audit["items"], list)
    check("GET", TENANT_PATH + "/members/audit", 401)
    check("GET", TENANT_PATH + "/members/audit", 403, token=admin)
    check("GET", TENANT_PATH + "/members/audit", 403, token=staff)
    check("GET", TENANT_PATH + "/members/audit?page=0", 400, token=owner)
    check("POST", TENANT_PATH + "/roles", 403, token=staff,
          data={"name": "Unauthorized", "description": None})
    check("GET", TENANT_PATH + "/members", 403, token=owner,
          headers={"X-Tenant-Id": FOREIGN_ID})

    # Operator-reviewed explicit catalog registration. No grants are issued
    # by Apply, and this opt-in remains OFF in normal environments.
    preview = check("GET", REGISTRY_PATH + "/preview", 200, token=admin)
    assert preview["canApply"], "The disposable catalog unexpectedly conflicts"
    assert preview["hasPending"], "The fixture must exercise real SQL registration"
    assert len([x for x in preview["changes"] if x["entity"] == "Permission"]) == 17
    check("POST", REGISTRY_PATH + "/apply", 403, token=owner,
          data={"fingerprint": preview["fingerprint"]})
    check("POST", REGISTRY_PATH + "/apply", 409, token=admin,
          data={"fingerprint": "0" * 64})
    applied = check("POST", REGISTRY_PATH + "/apply", 200, token=admin,
                    data={"fingerprint": preview["fingerprint"]})
    assert applied["canApply"]
    done = check("GET", REGISTRY_PATH + "/preview", 200, token=admin)
    assert not done["hasPending"], "Catalog Apply must be idempotent"
    assert not ui_allowed(staff, MEMBERS_READ)

    capabilities = check("GET", TENANT_PATH + "/admin-capabilities", 200, token=owner)
    assert capabilities["canCreateRoles"] and capabilities["canAssignMemberRoles"]
    assert capabilities["canUpdateRoles"] and capabilities["canChangeRoleState"]
    assert capabilities["canCreateGroups"] and capabilities["canUpdateGroups"]
    assert capabilities["canChangeGroupState"]
    check("POST", TENANT_PATH + "/groups", 401,
          data={"name": "Anonymous Group", "description": None})
    check("POST", TENANT_PATH + "/groups", 403, token=staff,
          data={"name": "Staff Group", "description": None})
    check("POST", TENANT_PATH + "/groups", 403, token=admin,
          data={"name": "System Group", "description": None})
    group = check("POST", TENANT_PATH + "/groups", 200, token=owner,
                  data={"name": "  E2E Front Desk  ", "description": "Reception"})
    group_id = str(uuid.UUID(group["id"]))
    assert group["name"] == "E2E Front Desk" and group["isActive"] and group["version"]
    check("POST", TENANT_PATH + "/groups", 409, token=owner,
          data={"name": "e2e front desk", "description": None})
    check("POST", TENANT_PATH + "/groups", 400, token=owner,
          data={"name": "Owner", "description": None})
    groups = check("GET", TENANT_PATH + "/groups", 200, token=owner)
    assert any(x["id"] == group_id and x["version"] == group["version"]
               for x in groups["items"])
    renamed_group = check("PUT", f"{TENANT_PATH}/groups/{group_id}", 200, token=owner,
                          data={"name": "E2E Desk Updated", "description": None,
                                "expectedVersion": group["version"]})
    assert renamed_group["version"] != group["version"]
    check("PUT", f"{TENANT_PATH}/groups/{group_id}", 409, token=owner,
          data={"name": "Stale", "description": None, "expectedVersion": group["version"]})
    check("PUT", f"{TENANT_PATH}/groups/{FOREIGN_ID}", 403, token=owner,
          data={"name": "Foreign", "description": None,
                "expectedVersion": renamed_group["version"]})
    disabled_group = check("PATCH", f"{TENANT_PATH}/groups/{group_id}/state",
                           200, token=owner,
                           data={"isActive": False,
                                 "expectedVersion": renamed_group["version"]})
    assert not disabled_group["isActive"]
    check("PATCH", f"{TENANT_PATH}/groups/{group_id}/state", 409, token=owner,
          data={"isActive": True, "expectedVersion": renamed_group["version"]})
    check("PATCH", f"{TENANT_PATH}/groups/{group_id}/state", 200, token=owner,
          data={"isActive": True, "expectedVersion": disabled_group["version"]})
    check("GET", TENANT_PATH + "/groups", 403, token=staff)

    # Role is born with NO permissions. Independent role direct grants follow.
    new_role = check("POST", TENANT_PATH + "/roles", 200, token=owner,
                     data={"name": "E2E HTTP Reception", "description": "Disposable"})
    role_id = str(uuid.UUID(new_role["id"]))
    assert not new_role["isOwner"] and new_role["isActive"] and new_role["version"]
    check("POST", TENANT_PATH + "/roles", 409, token=owner,
          data={"name": "E2E HTTP RECEPTION", "description": None})
    check("POST", TENANT_PATH + "/roles", 400, token=owner,
          data={"name": "Owner", "description": None})
    raw_matrix = member_permission(owner, role_id)
    assert raw_matrix["canEdit"] and all(not x["granted"] for x in raw_matrix["permissions"])
    assert all(x["registered"] for x in raw_matrix["permissions"])

    members = check("GET", TENANT_PATH + "/members", 200, token=owner)
    fixture_users = [m for m in members["items"] if m["userName"] == STAFF_NAME]
    assert len(fixture_users) == 1
    staff_id = str(uuid.UUID(fixture_users[0]["id"]))
    owner_users = [m for m in members["items"] if m["userName"] == "e2e-tenant"]
    assert len(owner_users) == 1
    owner_id = str(uuid.UUID(owner_users[0]["id"]))
    # FR-13C-2E: Owner-only provenance of the live Tenant decision.
    effective_path = f"{TENANT_PATH}/members/{staff_id}/effective-permissions"
    check("GET", effective_path, 401)
    check("GET", effective_path, 403, token=admin)
    check("GET", effective_path, 403, token=staff)
    check("GET", f"{TENANT_PATH}/members/{FOREIGN_ID}/effective-permissions",
          404, token=owner)
    empty_provenance = check("GET", effective_path, 200, token=owner)
    assert empty_provenance["userId"] == staff_id
    assert len(empty_provenance["items"]) == 17
    assert all(x["code"].startswith("TENANT.") for x in empty_provenance["items"])
    assert all(not x["allowed"] for x in empty_provenance["items"])
    assert all("password" not in str(x).lower() and "email" not in str(x).lower()
               for x in empty_provenance["items"])

    # FR-13C-2B: one-member Group changes (not a whole-page replacement).
    check("GET", f"{TENANT_PATH}/groups/{group_id}/members", 401)
    check("GET", f"{TENANT_PATH}/groups/{group_id}/members", 403, token=staff)
    check("GET", f"{TENANT_PATH}/groups/{group_id}/members", 403, token=admin)
    matrix = check("GET", f"{TENANT_PATH}/groups/{group_id}/members", 200, token=owner)
    assert matrix["groupId"] == group_id and matrix["canEdit"]
    assert matrix["version"] and matrix["page"] == 1
    assert any(x["id"] == staff_id and not x["assigned"] for x in matrix["items"])
    assert all(x["id"] != owner_id for x in matrix["items"])
    member_path = f"{TENANT_PATH}/groups/{group_id}/members/{staff_id}"
    check("PUT", member_path, 401, data={"assigned": True,
          "expectedVersion": matrix["version"]})
    check("PUT", member_path, 403, token=staff, data={"assigned": True,
          "expectedVersion": matrix["version"]})
    check("PUT", member_path, 403, token=admin, data={"assigned": True,
          "expectedVersion": matrix["version"]})
    check("PUT", f"{TENANT_PATH}/groups/{group_id}/members/{owner_id}", 403,
          token=owner, data={"assigned": True, "expectedVersion": matrix["version"]})
    check("PUT", member_path, 400, token=owner,
          data={"assigned": True, "expectedVersion": "invalid"})
    linked = check("PUT", member_path, 200, token=owner,
                   data={"assigned": True, "expectedVersion": matrix["version"]})
    assert linked["version"] != matrix["version"]
    check("PUT", member_path, 409, token=owner,
          data={"assigned": False, "expectedVersion": matrix["version"]})
    current = check("GET", f"{TENANT_PATH}/groups/{group_id}/members", 200, token=owner)
    assert any(x["id"] == staff_id and x["assigned"] for x in current["items"])
    assert current["version"] == linked["version"]
    unchanged = check("PUT", member_path, 200, token=owner,
                      data={"assigned": True, "expectedVersion": linked["version"]})
    assert unchanged["version"] == linked["version"]
    unlinked = check("PUT", member_path, 200, token=owner,
                     data={"assigned": False, "expectedVersion": linked["version"]})
    assert unlinked["version"] != linked["version"]
    after = check("GET", f"{TENANT_PATH}/groups/{group_id}/members", 200, token=owner)
    assert any(x["id"] == staff_id and not x["assigned"] for x in after["items"])
    assert not ui_allowed(staff, MEMBERS_READ)

    protected = member_matrix(owner, owner_id)
    assert not protected["canEdit"], "Owner target is never role assignable"
    check("PUT", f"{TENANT_PATH}/members/{owner_id}/roles", 403, token=owner,
          data={"expectedVersion": protected["version"], "roleIds": [role_id]})

    # A role permission alone does NOT authorize staff before UserRole assignment.
    grants = check("PUT", f"{TENANT_PATH}/roles/{role_id}/permissions", 200, token=owner,
                   data={"expectedVersion": raw_matrix["version"], "codes": [MEMBERS_READ]})
    assert grants["version"] != raw_matrix["version"]
    check("PUT", f"{TENANT_PATH}/roles/{role_id}/permissions", 409, token=owner,
          data={"expectedVersion": raw_matrix["version"], "codes": []})
    check("PUT", f"{TENANT_PATH}/roles/{role_id}/permissions", 400, token=owner,
          data={"expectedVersion": grants["version"],
                "codes": ["SYSTEM.ROLES.CREATE"]})
    check("GET", TENANT_PATH + "/members", 403, token=staff)

    # FR-13C-2C: real signed GroupRole grant/revoke; same staff token sees
    # inherited permission immediately after linking a role through a group.
    roles_path = f"{TENANT_PATH}/groups/{group_id}/roles"
    check("GET", roles_path, 401)
    check("GET", roles_path, 403, token=admin)
    check("GET", roles_path, 403, token=staff)
    gr_matrix = check("GET", roles_path, 200, token=owner)
    assert gr_matrix["canEdit"] and gr_matrix["groupIsActive"]
    assert any(x["id"] == role_id and not x["assigned"] for x in gr_matrix["items"])
    assert all(x["name"].upper() != "OWNER" for x in gr_matrix["items"])
    pair_url = f"{roles_path}/{role_id}"
    check("PUT", pair_url, 401,
          data={"assigned": True, "expectedVersion": gr_matrix["version"]})
    check("PUT", pair_url, 403, token=staff,
          data={"assigned": True, "expectedVersion": gr_matrix["version"]})
    check("PUT", pair_url, 403, token=admin,
          data={"assigned": True, "expectedVersion": gr_matrix["version"]})
    check("PUT", f"{roles_path}/{FOREIGN_ID}", 403, token=owner,
          data={"assigned": True, "expectedVersion": gr_matrix["version"]})
    check("PUT", pair_url, 400, token=owner,
          data={"assigned": True, "expectedVersion": "invalid"})

    # A GroupRole alone doesn't grant staff until staff is a Group member.
    first_role = check("PUT", pair_url, 200, token=owner,
                       data={"assigned": True, "expectedVersion": gr_matrix["version"]})
    assert first_role["version"] != gr_matrix["version"]
    check("GET", TENANT_PATH + "/members", 403, token=staff)
    check("PUT", pair_url, 409, token=owner,
          data={"assigned": False, "expectedVersion": gr_matrix["version"]})
    current_members = check("GET", f"{TENANT_PATH}/groups/{group_id}/members",
                            200, token=owner)
    relink = check("PUT", member_path, 200, token=owner,
                   data={"assigned": True, "expectedVersion": current_members["version"]})
    check("GET", TENANT_PATH + "/members", 200, token=staff)
    assert ui_allowed(staff, MEMBERS_READ)
    inherited = check("GET", effective_path, 200, token=owner)
    member_key = next(x for x in inherited["items"] if x["code"] == MEMBERS_READ)
    assert member_key["allowed"] and member_key["decision"] == "group_role"
    assert any(src["kind"] == "group_role" and src["groupId"] == group_id
               and src["roleId"] == role_id for src in member_key["sources"])
    check("PUT", pair_url, 409, token=owner,
          data={"assigned": False, "expectedVersion": first_role["version"]})
    current_roles = check("GET", roles_path, 200, token=owner)
    assert current_roles["version"] == relink["version"]
    assert any(x["id"] == role_id and x["assigned"] for x in current_roles["items"])
    revoked_role = check("PUT", pair_url, 200, token=owner,
                         data={"assigned": False, "expectedVersion": relink["version"]})
    check("GET", TENANT_PATH + "/members", 403, token=staff)
    assert not ui_allowed(staff, MEMBERS_READ)
    assert revoked_role["version"] != relink["version"]
    revoked_report = check("GET", effective_path, 200, token=owner)
    revoked_key = next(x for x in revoked_report["items"] if x["code"] == MEMBERS_READ)
    assert not revoked_key["allowed"] and revoked_key["decision"] == "not_granted"

    # FR-13C-2D: Owner-only, read-only unified Group timeline and
    # historical (non-repairing) relationship health counters.
    group_audit_path = f"{TENANT_PATH}/groups/{group_id}/audit"
    check("GET", group_audit_path, 401)
    check("GET", group_audit_path, 403, token=admin)
    check("GET", group_audit_path, 403, token=staff)
    check("GET", group_audit_path + "?page=0", 400, token=owner)
    check("GET", f"{TENANT_PATH}/groups/{FOREIGN_ID}/audit", 404, token=owner)
    review = check("GET", group_audit_path, 200, token=owner)
    assert review["groupId"] == group_id and review["pageNumber"] == 1
    assert review["groupName"] == "E2E Desk Updated"
    assert all(e["category"] in ("group", "group_member", "group_role")
               for e in review["items"])
    assert any(e["category"] == "group_role" and e["action"] == "revoke"
               for e in review["items"])
    assert any(e["category"] == "group_member" and e["action"] == "grant"
               for e in review["items"])
    integrity = review["integrity"]
    assert integrity["memberLinks"] >= 1
    assert integrity["roleLinks"] == 0
    assert integrity["ownerMemberLinks"] == 0
    assert integrity["invalidRoleLinks"] == 0
    assert integrity["invalidMemberLinks"] == 0

    links = member_matrix(owner, staff_id)
    assert links["canEdit"] and links["version"]
    assert any(x["id"] == role_id and not x["assigned"] for x in links["roles"])
    added = check("PUT", f"{TENANT_PATH}/members/{staff_id}/roles", 200, token=owner,
                  data={"expectedVersion": links["version"], "roleIds": [role_id]})
    assert added["version"] != links["version"]
    check("PUT", f"{TENANT_PATH}/members/{staff_id}/roles", 409, token=owner,
          data={"expectedVersion": links["version"], "roleIds": []})
    check("PUT", f"{TENANT_PATH}/members/{staff_id}/roles", 400, token=owner,
          data={"expectedVersion": added["version"], "roleIds": [FOREIGN_ID]})
    check("GET", TENANT_PATH + "/members", 200, token=staff)
    assert ui_allowed(staff, MEMBERS_READ)
    # Reading Members PAGE never grants access to Owner security history.
    check("GET", TENANT_PATH + "/members/audit", 403, token=staff)

    # The same signed staff JWT must lose access immediately when the direct
    # RolePermission is revoked, with no browser logout/token rotation.
    current = member_permission(owner, role_id)
    removed = check("PUT", f"{TENANT_PATH}/roles/{role_id}/permissions", 200, token=owner,
                    data={"expectedVersion": current["version"], "codes": []})
    check("GET", TENANT_PATH + "/members", 403, token=staff)
    assert not ui_allowed(staff, MEMBERS_READ)

    restored = check("PUT", f"{TENANT_PATH}/roles/{role_id}/permissions", 200, token=owner,
                     data={"expectedVersion": removed["version"], "codes": [MEMBERS_READ]})
    assert restored["version"] != removed["version"]
    check("GET", TENANT_PATH + "/members", 200, token=staff)

    # Role lifecycle changes also affect existing UserRole links instantly.
    roles = check("GET", TENANT_PATH + "/roles", 200, token=owner)
    role_rows = [r for r in roles["items"] if r["id"] == role_id]
    assert len(role_rows) == 1
    disabled = check("PATCH", f"{TENANT_PATH}/roles/{role_id}/state", 200, token=owner,
                     data={"expectedVersion": role_rows[0]["version"], "isActive": False})
    check("GET", TENANT_PATH + "/members", 403, token=staff)
    enabled = check("PATCH", f"{TENANT_PATH}/roles/{role_id}/state", 200, token=owner,
                    data={"expectedVersion": disabled["version"], "isActive": True})
    assert enabled["isActive"]
    check("GET", TENANT_PATH + "/members", 200, token=staff)

    # Finally revoke UserRole: even an unchanged, previously signed JWT
    # immediately loses the permission. The audited SQL check runs separately.
    current_links = member_matrix(owner, staff_id)
    check("PUT", f"{TENANT_PATH}/members/{staff_id}/roles", 200, token=owner,
          data={"expectedVersion": current_links["version"], "roleIds": []})
    check("GET", TENANT_PATH + "/members", 403, token=staff)
    assert not ui_allowed(staff, MEMBERS_READ)
    print(f"SUCCESS: {CHECKS} real HTTP checks; grants, CAS, audit and live revocation.", flush=True)


if __name__ == "__main__":
    run()
