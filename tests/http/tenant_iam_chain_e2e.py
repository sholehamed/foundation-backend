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
    check("POST", TENANT_PATH + "/roles", 403, token=staff,
          data={"name": "Unauthorized", "description": None})
    check("GET", TENANT_PATH + "/members", 403, token=owner,
          headers={"X-Tenant-Id": FOREIGN_ID})

    # Operator-reviewed explicit catalog registration. No grants are issued
    # by Apply, and this opt-in remains OFF in normal environments.
    preview = check("GET", REGISTRY_PATH + "/preview", 200, token=admin)
    assert preview["canApply"], "The disposable catalog unexpectedly conflicts"
    assert preview["hasPending"], "The fixture must exercise real SQL registration"
    assert len([x for x in preview["changes"] if x["entity"] == "Permission"]) == 9
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
