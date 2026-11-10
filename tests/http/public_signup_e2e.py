"""Disposable Development/CI-only real signup -> login -> tenant-workspace HTTP flow.
Run while the SQL Server-backed ASP.NET Core API is live with Identity:E2E:Enabled,
Identity:E2E:CaptchaCode=ABCDE and AllowInsecureHttpForE2E=true.
"""
import json
import os
import urllib.error
import urllib.parse
import urllib.request

BASE = os.environ.get("SHINERA_E2E_API_URL", "http://127.0.0.1:5069").rstrip("/")
CAPTCHA = os.environ.get("Identity__E2E__CaptchaCode", "ABCDE")


def request(method, path, data=None, token=None, form=False):
    if data is not None:
        raw = (urllib.parse.urlencode(data).encode("utf-8") if form
               else json.dumps(data).encode("utf-8"))
    else:
        raw = None
    headers = {"Content-Type": "application/x-www-form-urlencoded" if form
               else "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(BASE + path, raw, headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=20) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as err:
        body = err.read().decode("utf-8")
        try:
            return err.code, json.loads(body)
        except ValueError:
            return err.code, {"body": body[:500]}


def captcha():
    status, result = request("GET", "/api/system/Auth/captcha/new")
    assert status == 200 and result.get("token"), (status, result)
    return result["token"]


def menu_routes(categories):
    """Read the actual server-filtered menu, including nested entries."""
    result = []
    def visit(items):
        for item in items:
            if item.get("route"):
                result.append(item["route"])
            visit(item.get("childs") or [])
    for category in categories:
        visit(category.get("items") or [])
    return result


SYSTEM_ROOTS = {
    "/system/dashboard", "/system/admins", "/system/users",
    "/system/roles", "/system/groups", "/system/permissions",
    "/system/tenants", "/system/modules", "/system/resources",
    "/system/api-resources", "/system/ui-resources",
    "/system/menu-management", "/system/feature-registry", "/system/plan-catalog",
}
TENANT_ROOTS = {
    "/dashboard", "/dashboard/business", "/dashboard/branches",
    "/dashboard/members", "/dashboard/roles", "/dashboard/groups",
}


def main():
    # NEVER run the first-login fixture against a real Tenant or hosted SQL.
    assert (
        os.environ.get("ASPNETCORE_ENVIRONMENT") == "Development"
        and os.environ.get("Identity__E2E__Enabled") == "true"
        and os.environ.get("SHINERA_E2E_DB_MUTATION") == "stale-jwt-ci-only"
        and os.environ.get("DataBaseServer") in ("127.0.0.1,1433", "localhost,1433")
        and os.environ.get("DBNAME", "").startswith("Shinera_E2E_")
        and os.environ.get("SHINERA_DEVELOPMENT_ADMIN_PASSWORD")
        and CAPTCHA == "ABCDE"
    ), "Requires disposable local Development SQL, not remote Staging"

    # First login: System Admin must see the COMPLETE code-owned System
    # sidebar and the 14 root PAGE decisions, no self-assignment action.
    status, proof = request("GET", "/api/system/Auth/captcha/new")
    assert status == 200 and proof.get("token")
    status, system_login = request("POST", "/api/system/Auth/login", {
        "grant_type": "password", "client_id": "postman", "username": "admin",
        "password": os.environ["SHINERA_DEVELOPMENT_ADMIN_PASSWORD"],
        "login_context": "system",
        "scope": "openid profile email api offline_access",
        "captchaid": proof["token"], "userEnteredCaptchaCode": CAPTCHA,
    }, form=True)
    assert status == 200 and system_login.get("access_token"), status
    system_token = system_login["access_token"]
    status, system_menu = request("GET", "/api/system/Menus/me", token=system_token)
    assert status == 200 and isinstance(system_menu, list), status
    system_routes = menu_routes(system_menu)
    assert set(system_routes) == SYSTEM_ROOTS and len(system_routes) == 14, (
        "Initial System admin MUST see all 14 code-owned roots, without duplicates")
    status, system_access = request("GET", "/api/system/UiAccess/me", token=system_token)
    assert status == 200 and system_access.get("entries"), status
    actual_allowed_roots = {
        entry["route"] for entry in system_access["entries"]
        if entry.get("route") in SYSTEM_ROOTS and entry.get("allowed") is True
    }
    assert actual_allowed_roots == SYSTEM_ROOTS, (
        "System admin root PAGE permissions must be live on FIRST login")
    status, _ = request("GET", "/api/tenant/workspace", token=system_token)
    assert status == 403, "System Admin is not a Tenant Owner"

    slug = "ci-signup-onboarding"
    mobile = "09123456789"
    password = "SignupE2E123!"
    payload = {
        "planKey": "salon-pro",
        "billingCycle": "monthly",
        "promoCode": None,
        "tenant": {"name": "CI Salon", "slug": slug, "culture": "fa-IR",
                   "timezone": "Asia/Tehran", "currency": "IRR"},
        "businessProfile": {"displayName": "CI Salon", "activityType": "salon",
                            "phone": None, "city": "Tehran", "address": "CI test address",
                            "postalCode": None, "instagram": None},
        "owner": {"firstName": "CI", "lastName": "Owner", "mobile": mobile,
                  "email": "ci-signup@example.test", "password": password},
        "branch": None,
        "acceptTerms": True, "acceptPrivacy": True,
        "captchaToken": captcha(), "captchaCode": CAPTCHA,
    }

    status, available = request("GET", "/api/public/signup/slug-available?slug=" + slug)
    assert status == 200 and available["available"], (status, available)

    status, created = request("POST", "/api/public/signup/checkout", payload)
    assert status == 200 and created["slug"] == slug, (status, created)
    assert created["planStatus"] == "pending_activation", created
    assert created["accountUsername"] == mobile, created
    assert created["tenantId"] and created["ownerUserId"] and created["mainBranchId"]

    status, available = request("GET", "/api/public/signup/slug-available?slug=" + slug)
    assert status == 200 and not available["available"], (status, available)

    status, _ = request("GET", "/api/tenant/workspace")
    assert status == 401, status

    status, tokens = request("POST", "/api/system/Auth/login", {
        "grant_type": "password", "client_id": "postman", "username": mobile,
        "password": password, "login_context": "tenant", "tenant": slug,
        "scope": "openid profile email api offline_access",
        "captchaid": captcha(), "userEnteredCaptchaCode": CAPTCHA
    }, form=True)
    assert status == 200 and tokens.get("access_token"), (status, tokens)

    access_token = tokens["access_token"]
    status, user_info = request("GET", "/api/system/Auth/userinfo", token=access_token)
    assert status == 200 and user_info.get("account_type") == "tenant", (status, user_info)
    assert user_info.get("tenant") == slug, user_info

    status, workspace = request("GET", "/api/tenant/workspace", token=access_token)
    assert status == 200, (status, workspace)
    assert workspace["tenant"]["id"] == created["tenantId"], workspace
    assert workspace["mainBranch"]["id"] == created["mainBranchId"], workspace
    assert workspace["profile"]["requestedPlanKey"] == "salon-pro", workspace
    assert workspace["subscriptionStatus"] == "pending_activation", workspace

    # First Tenant Owner login: real signup's Role+RolePermissions must
    # render every permitted menu immediately, never through Owner fallback
    # or a manual self-grant. Test API access alongside UI presentation.
    status, categories = request("GET", "/api/tenant/workspace/navigation/me",
                                 token=access_token)
    assert status == 200 and isinstance(categories, list), status
    routes = menu_routes(categories)
    assert len(routes) == 6 and set(routes) == TENANT_ROOTS, (
        "Fresh Tenant Owner must see exact six menus with no missing privileged pages")
    status, current = request("GET", "/api/system/Menus/me", token=access_token)
    assert status == 200 and set(menu_routes(current)) == TENANT_ROOTS
    status, ui_access = request("GET", "/api/tenant/workspace/ui-access/me",
                                token=access_token)
    assert status == 200 and len(ui_access["entries"]) == 17, status
    assert len({item["key"] for item in ui_access["entries"]}) == 17
    assert all(item["allowed"] for item in ui_access["entries"]), (
        "Signup must atomically grant all 17 Tenant PAGE/ACTION permissions")
    status, caps = request("GET", "/api/tenant/workspace/admin-capabilities",
                           token=access_token)
    assert status == 200, status
    for field in ("canViewAdministration", "canViewMembers", "canViewRoles",
                  "canViewGroups", "canCreateRoles", "canAssignMemberRoles",
                  "canManageBranches", "canCreateGroups", "canAssignGroupRoles"):
        assert caps[field] is True, "Fresh Owner missing " + field
    for page in ("members", "roles", "groups", "branches"):
        status, _ = request("GET", "/api/tenant/workspace/" + page,
                            token=access_token)
        assert status == 200, ("Owner API route missing", page, status)
    status, _ = request("GET", "/api/system/UiAccess/me", token=access_token)
    assert status == 403, "Tenant must never receive System UI privileges"

    # Duplicate submission must not provision a second Tenant/Owner/Branch.
    # Use a new CAPTCHA token; the slug unique constraint still wins.
    payload["captchaToken"] = captcha()
    status, duplicated = request("POST", "/api/public/signup/checkout", payload)
    assert status == 409, (status, duplicated)
    print("PASS: FIRST System Admin login has all 14 System menus/PAGE rights; FIRST newly signed-up Tenant Owner has six menus, 17 PAGE/ACTION rights, full management APIs. No self-grant, no System/Tenant crossover or duplicate signup.")


if __name__ == "__main__":
    main()
