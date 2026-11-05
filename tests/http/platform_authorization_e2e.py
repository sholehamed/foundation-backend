#!/usr/bin/env python3
"""Real OpenIddict HTTP authorization matrix against a disposable Development SQL Server.

Runs after the API is started by backend-sqlserver-ci.yml. No mocks, forged
server identities, TestServer shortcuts, third-party modules or credentials
embedded in the test. A failed assertion exits nonzero and fails GitHub Actions.
"""
import base64
import json
import os
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid

BASE = os.environ.get("SHINERA_E2E_BASE_URL", "http://127.0.0.1:5069").rstrip("/")
PASSWORD = os.environ["SHINERA_DEVELOPMENT_ADMIN_PASSWORD"]
GROUP_ID = "7fbf4183-c8ab-4e2e-805d-65fd7fa95247"
PERMISSION_ID = "45129362-d8b8-4bde-8600-98745e311923"
PERMISSION_RESOURCE_ID = "d051bd62-6828-421c-a0bf-70ca4ed29ae3"
FOREIGN_ID = "00000000-0000-0000-0000-000000000001"
PASSED = 0


def request(method, path, token=None, data=None, headers=None):
    """Return status and response bytes, including HTTP error bodies."""
    h = dict(headers or {})
    if token is not None:
        h["Authorization"] = "Bearer " + token
    if isinstance(data, (dict, list)):
        body = json.dumps(data).encode("utf-8")
        h["Content-Type"] = "application/json"
    elif isinstance(data, bytes):
        body = data
    else:
        body = None
    req = urllib.request.Request(BASE + path, data=body, headers=h, method=method)
    try:
        with urllib.request.urlopen(req, timeout=20) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()


def check(method, path, expected, *, token=None, data=None, headers=None):
    global PASSED
    code, body = request(method, path, token=token, data=data, headers=headers)
    if code != expected:
        # Never log request headers or bearer tokens to CI output.
        raise AssertionError(
            f"{method} {path}: expected HTTP {expected}, got {code}; "
            f"response={body.decode('utf-8', 'replace')[:350]}")
    PASSED += 1
    print(f"PASS [{PASSED:02d}] {method} {path} -> {code}", flush=True)
    return body


def claims_of(jwt):
    # This is ONLY metadata inspection to select the seed account's subject ID.
    # The authorization decision is made by the API validating the signed token.
    parts = jwt.split(".")
    if len(parts) != 3:
        raise AssertionError("Expected signed, non-encrypted OpenIddict JWT")
    payload = parts[1] + "=" * (-len(parts[1]) % 4)
    return json.loads(base64.urlsafe_b64decode(payload))


def login(username, context, tenant=None):
    challenge_bytes = check("GET", "/api/system/Auth/captcha/new", 200)
    challenge = json.loads(challenge_bytes)
    form = {
        "grant_type": "password",
        "client_id": "postman",
        "username": username,
        "password": PASSWORD,
        "login_context": context,
        "scope": "openid profile email api offline_access",
        "captchaid": challenge["token"],
        "userEnteredCaptchaCode": "ABCDE",
    }
    if tenant:
        form["tenant"] = tenant
    body = urllib.parse.urlencode(form).encode("ascii")
    result = check("POST", "/api/system/Auth/login", 200,
                   data=body,
                   headers={"Content-Type": "application/x-www-form-urlencoded"})
    access_token = json.loads(result)["access_token"]
    claims = claims_of(access_token)
    assert claims.get("account_type") == context, "Wrong account_type from login"
    assert uuid.UUID(claims["sub"]), "Missing or invalid subject ID"
    if context == "system":
        assert "tenant_id" not in claims, "System user must never have tenant_id"
    return access_token, str(uuid.UUID(claims["sub"])), json.loads(result).get("refresh_token")


def json_value(body):
    return json.loads(body.decode("utf-8"))


def run():
    print("Running disposable SQL Server + real OpenIddict HTTP security matrix", flush=True)
    admin, admin_id, admin_refresh = login("admin", "system")
    viewer, viewer_id, _ = login("e2e-viewer", "system")
    tenant, tenant_id, _ = login("e2e-tenant", "tenant", "e2e-http-ci")
    assert admin_id != viewer_id != tenant_id

    roles = "/api/system/Roles"
    users = "/api/system/Users"
    groups = "/api/system/Groups"
    perms = "/api/system/Permissions"
    role_payload = {"name": "E2E HTTP Scoped Operator",
                    "description": "Disposable authorization test", "isActive": True}

    # Both read and write paths enforce the HTTP authentication boundary.
    check("GET", roles + "/lookup", 401)
    check("POST", roles, 401, data=role_payload)
    check("GET", roles + "/lookup", 401, token="invalid.signature.token")
    check("GET", roles + "/lookup", 403, token=viewer)
    check("POST", roles, 403, token=viewer, data=role_payload)
    check("GET", roles + "/lookup", 403, token=tenant)
    check("POST", roles, 403, token=tenant, data=role_payload)
    check("GET", roles + "/lookup", 403, token=tenant,
          headers={"X-Tenant-Id": FOREIGN_ID})
    role_lookup = json_value(check("GET", roles + "/lookup", 200, token=admin))
    reserved_roles = [r for r in role_lookup
                      if r.get("title", "").casefold() == "superadmin"]
    assert len(reserved_roles) == 1, "Master System role lookup must return exactly one SuperAdmin"
    superadmin_role_id = str(uuid.UUID(reserved_roles[0]["id"]))

    permission_lookup = json_value(
        check("GET", perms + "/lookup?text=ROLES.CREATE", 200, token=admin))
    reserved_permissions = [p for p in permission_lookup
                            if p.get("code", "").upper() == "ROLES.CREATE"]
    assert len(reserved_permissions) == 1, "Reserved platform permission missing"
    reserved_permission_id = str(uuid.UUID(reserved_permissions[0]["id"]))

    # Forged caller target must be rejected, not silently rewritten or
    # mapped to HTTP 500. No untrusted tenant body can select Master storage.
    check("PUT", roles + "/" + FOREIGN_ID + "/permissions", 403,
          token=admin, data={"permissionIds": []})
    check("PUT", roles + "/" + superadmin_role_id + "/permissions", 403,
          token=admin, data={"permissionIds": []})
    check("DELETE", roles + "/" + superadmin_role_id, 403, token=admin)
    check("PUT", users + "/" + viewer_id + "/roles/" + superadmin_role_id,
          403, token=admin)
    check("PUT", users + "/" + viewer_id + "/permissions/" + reserved_permission_id + "/allow",
          403, token=admin)
    check("POST", perms, 403, token=admin,
          data={"code": "ROLES.CREATE", "name": "Forbidden catalog overwrite",
                "resourceId": FOREIGN_ID, "isActive": True,
                "permissionResources": []})


    # Complete the signed-OpenIddict Permission CRUD HTTP matrix while the
    # actual System administrator is still privileged. Never forge principals.
    permission_payload = {
        "code": "E2E.PERMISSION-CRUD", "resourceId": PERMISSION_RESOURCE_ID,
        "name": "E2E HTTP Permission", "description": "Disposable", "isActive": True,
        "permissionResources": [],
    }
    check("POST", perms, 401, data=permission_payload)
    check("GET", perms + "/" + PERMISSION_ID, 401)
    check("POST", perms, 403, token=viewer, data=permission_payload)
    check("PUT", perms + "/" + PERMISSION_ID, 403, token=viewer,
          data=permission_payload)
    check("DELETE", perms + "/" + PERMISSION_ID, 403, token=viewer)
    check("POST", perms, 403, token=tenant, data=permission_payload)
    check("PATCH", perms + "/" + PERMISSION_ID, 403, token=tenant)

    missing = FOREIGN_ID
    check("GET", perms + "/" + missing, 404, token=admin)
    check("PUT", perms + "/" + missing, 404, token=admin,
          data=permission_payload)
    check("DELETE", perms + "/" + missing, 404, token=admin)
    check("PATCH", perms + "/" + missing, 404, token=admin)
    check("POST", perms, 400, token=admin,
          data={**permission_payload, "resourceId": missing})
    check("POST", perms, 400, token=admin,
          data={**permission_payload, "resourceId": str(uuid.UUID(int=0))})
    check("POST", perms, 400, token=admin,
          data={**permission_payload, "code": "   "})
    check("POST", perms, 400, token=admin,
          data={**permission_payload, "name": ""})
    check("POST", perms, 400, token=admin,
          data={**permission_payload,
                "permissionResources": [{"type": "API", "id": missing}]})
    check("POST", perms, 400, token=admin,
          data={**permission_payload,
                "permissionResources": [{"type": "UNKNOWN", "id": missing}]})

    # Reserved catalog definitions are immutable via every write verb.
    for method in ("PUT", "DELETE", "PATCH"):
        check(method, perms + "/" + reserved_permission_id, 403,
              token=admin, data=permission_payload if method == "PUT" else None)
    check("PUT", perms + "/" + PERMISSION_ID, 403, token=admin,
          data={**permission_payload, "code": "PERMISSIONS.CREATE"})

    permission_id = str(uuid.UUID(json_value(
        check("POST", perms, 201, token=admin, data=permission_payload))))
    created_perm = json_value(
        check("GET", perms + "/" + permission_id, 200, token=admin))
    assert str(uuid.UUID(created_perm["id"])) == permission_id
    assert created_perm["code"] == "E2E.PERMISSION-CRUD"
    assert str(uuid.UUID(created_perm["resourceId"])) == PERMISSION_RESOURCE_ID

    check("POST", perms, 409, token=admin, data=permission_payload)
    check("PUT", perms + "/" + permission_id, 400, token=admin,
          data={**permission_payload, "name": " "})
    check("PUT", perms + "/" + permission_id, 400, token=admin,
          data={**permission_payload, "resourceId": missing})
    check("PUT", perms + "/" + permission_id, 400, token=admin,
          data={**permission_payload,
                "permissionResources": [{"type": "UI", "id": missing}]})
    updated_perm = {**permission_payload,
                    "code": "e2e.permission-crud-updated",
                    "name": "E2E Updated Permission"}
    check("PUT", perms + "/" + permission_id, 204,
          token=admin, data=updated_perm)
    persisted_perm = json_value(
        check("GET", perms + "/" + permission_id, 200, token=admin))
    assert persisted_perm["code"] == "E2E.PERMISSION-CRUD-UPDATED"
    assert persisted_perm["name"] == "E2E Updated Permission"
    check("PATCH", perms + "/" + permission_id, 204, token=admin)
    inactive_perm = json_value(
        check("GET", perms + "/" + permission_id, 200, token=admin))
    assert inactive_perm["isActive"] is False
    check("PATCH", perms + "/" + permission_id, 204, token=admin)
    check("DELETE", perms + "/" + permission_id, 204, token=admin)
    check("GET", perms + "/" + permission_id, 404, token=admin)
    check("DELETE", perms + "/" + permission_id, 404, token=admin)

    role_id = str(uuid.UUID(json_value(check("POST", roles, 201,
                                             token=admin, data=role_payload))))
    stored = json_value(check("GET", roles + "/" + role_id, 200, token=admin))
    assert str(stored["id"]).lower() == role_id
    assert stored["name"] == role_payload["name"], "Created Role was not persisted"

    updated_name = "E2E HTTP Scoped Operator Updated"
    check("PUT", roles + "/" + role_id, 204, token=admin,
          data={"name": updated_name, "description": "Updated via HTTP",
                "isActive": True})
    updated = json_value(check("GET", roles + "/" + role_id, 200, token=admin))
    assert updated["name"] == updated_name, "Role update was not persisted"

    # Role Permission replacement is performed by real CQRS/EF and audited.
    check("PUT", roles + "/" + role_id + "/permissions", 204,
          token=admin, data={"permissionIds": [PERMISSION_ID]})

    role_link = users + "/" + viewer_id + "/roles/" + role_id
    check("PUT", role_link, 204, token=admin)
    check("PUT", role_link, 204, token=admin)  # No-op
    check("GET", roles + "/lookup", 403, token=viewer)  # Not SuperAdmin
    check("DELETE", role_link, 204, token=admin)
    check("DELETE", role_link, 204, token=admin)  # No-op
    check("PUT", users + "/" + tenant_id + "/roles/" + role_id, 403, token=admin)

    # Group role / membership grants and revocations with known Master fixture.
    group_role = groups + "/" + GROUP_ID + "/roles/" + role_id
    member = groups + "/" + GROUP_ID + "/members/" + viewer_id
    check("PUT", group_role, 204, token=admin)
    check("POST", groups + "/" + GROUP_ID + "/addmembers", 204,
          token=admin, data={"userIds": [viewer_id]})
    check("POST", groups + "/" + GROUP_ID + "/addmembers", 204,
          token=admin, data={"userIds": [viewer_id]})
    check("DELETE", member, 204, token=admin)
    check("DELETE", member, 204, token=admin)  # Idempotent revoke
    check("DELETE", group_role, 204, token=admin)
    check("DELETE", group_role, 204, token=admin)  # No-op
    # A revoke must physically remove the old grant link. Soft deletion
    # would leave a duplicate (GroupId, RoleId) and block a new grant.
    check("PUT", group_role, 204, token=admin)
    check("DELETE", group_role, 204, token=admin)
    check("DELETE", groups + "/" + GROUP_ID + "/members/" + tenant_id,
          403, token=admin)

    # Direct non-reserved permissions support Allow -> Deny -> Clear.
    override = users + "/" + viewer_id + "/permissions/" + PERMISSION_ID
    check("PUT", override + "/allow", 204, token=admin)
    check("PUT", override + "/allow", 204, token=admin)
    check("PUT", override + "/deny", 204, token=admin)
    check("DELETE", override, 204, token=admin)
    check("DELETE", override, 204, token=admin)

    # The privileged actor cannot modify their own grants via a normal API.
    check("PUT", users + "/" + admin_id + "/roles/" + role_id, 403, token=admin)

    # Finish actual platform Role CRUD after related grant links are removed.
    check("DELETE", roles + "/" + role_id, 204, token=admin)

    # Last step: prove an ALREADY SIGNED privileged JWT cannot continue using
    # SystemAdministrator endpoints when its live Master SuperAdmin grant is
    # revoked at SQL level. This tool operates solely on the disposable E2E DB;
    # there is intentionally no production/development HTTP backdoor.
    check("GET", roles + "/lookup", 200, token=admin)
    if not admin_refresh:
        raise AssertionError("Expected an OpenIddict offline_access refresh token")
    tool = "tests/Shinera.Identity.E2E.SqlMutator/Shinera.Identity.E2E.SqlMutator.csproj"
    try:
        mutation = subprocess.run(
            ["dotnet", "run", "--project", tool, "-c", "Release",
             "--no-build", "--", "revoke-seeded-superadmin", admin_id],
            check=True, capture_output=True, text=True, timeout=90)
    except (OSError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as exc:
        raise AssertionError("Disposable database revocation did not succeed") from exc
    if "PASS: disposable SQL Server removed exactly one" not in mutation.stdout:
        raise AssertionError("Disposable revocation tool did not report the expected single change")
    print("PASS: seeded SuperAdmin was revoked in the disposable SQL Server", flush=True)

    # EXACT SAME signed JWT (same serialized bytes) has valid token lifetime,
    # but must now fail privileged authorization because role membership is
    # re-read from persisted data on every sensitive HTTP request.
    check("GET", roles + "/lookup", 403, token=admin)
    check("POST", roles, 403, token=admin, data=role_payload)

    # The outstanding refresh token is also denied: an old role snapshot
    # must not mint a fresh privileged access token after the persisted revoke.
    refresh_form = urllib.parse.urlencode({
        "grant_type": "refresh_token", "client_id": "postman",
        "refresh_token": admin_refresh,
    }).encode("ascii")
    response = check("POST", "/api/system/Auth/login", 400,
                     data=refresh_form,
                     headers={"Content-Type": "application/x-www-form-urlencoded"})
    assert json_value(response).get("error") == "invalid_grant", (
        "Stale privileged refresh must fail with invalid_grant")

    # Independently inspect committed SQL Server security audit decisions.
    # This verifies middleware-level 401/403, endpoint-level reserved-catalog
    # 403, and an already-issued JWT denied after its grant is revoked.
    try:
        audit = subprocess.run(
            ["dotnet", "run", "--project", tool, "-c", "Release", "--no-build",
             "--", "assert-denied-audit", admin_id, viewer_id],
            check=True, capture_output=True, text=True, timeout=90)
    except (OSError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as exc:
        raise AssertionError("Committed SQL denial audit verification failed") from exc
    assert "PASS: committed denied-access audit rows and SQL append-only trigger." in audit.stdout, (
        "CI-only query did not confirm append-only denied-access ledger")
    print("PASS: durable denied-access audit (anonymous, viewer, reserved and revoked)", flush=True)

    print(f"PASS: {PASSED} real HTTP status checks and persisted authorization/audit invariants", flush=True)


if __name__ == "__main__":
    try:
        run()
    except Exception as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        sys.exit(1)
