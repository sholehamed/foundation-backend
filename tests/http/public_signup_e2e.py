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


def main():
    assert os.environ.get("Identity__E2E__Enabled") == "true", "Disposable E2E mode required."
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

    # Duplicate submission must not provision a second Tenant/Owner/Branch.
    # Use a new CAPTCHA token; the slug unique constraint still wins.
    payload["captchaToken"] = captcha()
    status, duplicated = request("POST", "/api/public/signup/checkout", payload)
    assert status == 409, (status, duplicated)
    print("PASS: anonymous signup creates one workspace, Owner can log in, branch is present, billing pending, duplicates rejected.")


if __name__ == "__main__":
    main()
