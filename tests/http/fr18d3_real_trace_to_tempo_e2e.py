#!/usr/bin/env python3
"""FR-18D-3: real signed System HTTP -> CQRS -> EF/SQL -> OTLP -> Tempo proof.

Never creates a public test endpoint. Uses the existing SuperDashboardQuery
under an authenticated development SuperAdmin against disposable SQL Server.
Requires an explicit local CI-only environment and verifies actual persisted
parent/child span relationships, rather than trusting a synthetic OTLP payload.
"""
from __future__ import annotations

import base64
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

BASE = "http://127.0.0.1:5069"
TEMPO = "http://127.0.0.1:3200"
CAPTCHA_PATH = "/api/system/Auth/captcha/new"
LOGIN_PATH = "/api/system/Auth/login"
DASHBOARD = "/api/System/Dashboard/super-dashboard"
UNSAFE_SENTINEL = "FR18D3_PRIVATE_QUERY_SENTINEL_DO_NOT_EXPORT"

if (
    os.environ.get("GITHUB_ACTIONS") != "true"
    or os.environ.get("GITHUB_REPOSITORY") != "sholehamed/foundation-backend"
    or os.environ.get("ASPNETCORE_ENVIRONMENT") != "Development"
    or os.environ.get("Identity__E2E__Enabled") != "true"
    or os.environ.get("SHINERA_E2E_DB_MUTATION") != "stale-jwt-ci-only"
    or os.environ.get("DBNAME") != "Shinera_E2E_TenantIAM"
    or os.environ.get("DataBaseServer") != "127.0.0.1,1433"
    or os.environ.get("Identity__Bff__Enabled") != "false"
    or os.environ.get("Observability__Otlp__Enabled") != "true"
    or os.environ.get("Observability__TraceSampleRatio") != "1"
    or os.environ.get("Observability__Otlp__Endpoint") != "http://127.0.0.1:4317"
):
    raise RuntimeError("FR-18D3 runs only in disposable, loopback-only GitHub CI")

TRACE_ID = re.compile(r"^[0-9a-f]{32}$")
# The parent is an ephemeral CI caller; no custom Tenant/Role baggage can grant access.
DASHBOARD_TRACE_ID = "b5d03ef345ed4cb1b39da1c7f317a6e9"
CALLER_SPAN_ID = "0000000000000011"
AUTH_TRACE_ID = "c13a8843fbac4c569e610f8a020be291"


def http(method: str, url: str, *, data: bytes | None = None,
         headers: dict[str, str] | None = None) -> tuple[int, bytes, dict[str, str]]:
    request = urllib.request.Request(url, data=data, headers=headers or {}, method=method)
    try:
        with urllib.request.urlopen(request, timeout=12) as response:
            return response.status, response.read(4_000_000), {
                k.lower(): v for k, v in response.headers.items()
            }
    except urllib.error.HTTPError as error:
        return error.code, error.read(8192), {
            k.lower(): v for k, v in error.headers.items()
        }


def json_http(method: str, path: str, *, data=None,
              headers: dict[str, str] | None = None) -> tuple[int, dict, dict[str, str]]:
    h = dict(headers or {})
    raw = None
    if data is not None:
        h["Content-Type"] = "application/x-www-form-urlencoded"
        raw = urllib.parse.urlencode(data).encode("utf-8")
    status, body, response_headers = http(method, BASE + path, data=raw, headers=h)
    try:
        decoded = json.loads(body)
    except (json.JSONDecodeError, UnicodeDecodeError):
        decoded = {}
    return status, decoded, response_headers


def decode_span_id(value: str | None) -> str:
    if not value:
        return ""
    if re.fullmatch(r"[0-9a-fA-F]{16}", value):
        return value.lower()
    try:
        raw = base64.b64decode(value, validate=True)
    except (ValueError, base64.binascii.Error):
        raise AssertionError("Unrecognized span identifier returned by Tempo")
    assert len(raw) == 8, "Unexpected W3C span ID bytes"
    return raw.hex()


def attributes(span: dict) -> dict[str, object]:
    result = {}
    for item in span.get("attributes", []):
        key = item.get("key", "")
        value = item.get("value", {})
        if not isinstance(value, dict):
            continue
        result[key] = next(iter(value.values()), None)
    return result


def read_trace(trace_id: str) -> tuple[int, dict, str]:
    code, body, _ = http("GET", f"{TEMPO}/api/v2/traces/{trace_id}",
                         headers={"Accept": "application/json"})
    if code != 200:
        return code, {}, ""
    decoded = json.loads(body)
    return code, decoded, body.decode("utf-8", "replace")


def spans_from_trace(payload: dict) -> list[dict]:
    # Tempo V2 returns OTLP JSON: {trace:{resourceSpans:[...]}}.
    trace = payload.get("trace", payload)
    spans = []
    for resource in trace.get("resourceSpans", []):
        for scope in resource.get("scopeSpans", []):
            for span in scope.get("spans", []):
                spans.append(span)
    return spans


def verify_persisted_lineage(trace_id: str) -> None:
    deadline = time.monotonic() + 100
    last = "not_ingested"
    while time.monotonic() < deadline:
        code, doc, raw = read_trace(trace_id)
        if code == 200:
            spans = spans_from_trace(doc)
            names = {span.get("name") for span in spans}
            if {
                "foundation.http.pipeline", "cqrs.query", "db.ef.command"
            }.issubset(names):
                assert UNSAFE_SENTINEL not in raw, "Potential URL-query leakage into Tempo"
                assert "FR18D3_UNTRUSTED_BAGGAGE" not in raw, "Client baggage leaked"
                assert "Bearer " not in raw, "Auth token leaked into Tempo"
                assert "password=" not in raw.lower(), "Credential/query leaked into Tempo"
                assert TRACE_ID.fullmatch(trace_id)
                pipeline = [s for s in spans if s.get("name") == "foundation.http.pipeline"]
                handlers = [s for s in spans if s.get("name") == "cqrs.query"]
                sql = [s for s in spans if s.get("name") == "db.ef.command"]
                assert len(pipeline) >= 1 and len(handlers) >= 1 and len(sql) >= 3, (
                    "System dashboard should execute real EF SQL under the query handler"
                )
                by_id = {decode_span_id(span.get("spanId")): span for span in spans}
                assert len(by_id) == len(spans), "Duplicated span IDs in the trace"
                assert any(attributes(s).get("foundation.cqrs.request_type") ==
                           "SuperDashboardQuery" for s in handlers), (
                    "Wrong CQRS handler: the real SuperDashboardQuery was not observed"
                )
                cqrs_sql = []
                for span in sql:
                    parent_id = decode_span_id(span.get("parentSpanId"))
                    parent = by_id.get(parent_id)
                    assert parent is not None, "Orphaned SQL command without recorded parent"
                    # Authentication and permission evaluation can run genuine
                    # SQL *before* the CQRS handler is invoked. These database
                    # commands rightly belong under the HTTP pipeline rather
                    # than falsely being called business Handler children.
                    if parent.get("name") == "cqrs.query":
                        cqrs_sql.append(span)
                    else:
                        assert parent.get("name") == "foundation.http.pipeline", (
                            "SQL span was neither under CQRS nor under the request pipeline"
                        )
                    tags = attributes(span)
                    digest = tags.get("foundation.db.fingerprint")
                    assert isinstance(digest, str) and re.fullmatch(r"[0-9a-f]{64}", digest), (
                        "Missing safe, bounded HMAC query fingerprint"
                    )
                    assert tags.get("foundation.db.client") == "ef-core"
                    assert "db.statement" not in tags and "db.query.text" not in tags
                    assert int(span.get("endTimeUnixNano", 0)) >= int(
                        span.get("startTimeUnixNano", 0)), "Invalid observed EF SQL timing"
                assert len(cqrs_sql) >= 3, (
                    "Real SQL commands for SuperDashboardQuery did not appear "
                    "as CQRS descendants; authorization SQL alone is insufficient"
                )
                for handler in handlers:
                    assert by_id.get(decode_span_id(handler.get("parentSpanId")), {}).get(
                        "name") == "foundation.http.pipeline", (
                        "CQRS handler detached from the real HTTP request pipeline"
                    )
                # Existing ASP.NET Core server instrumentation must have emitted
                # a trace parent; this is not just three hand-created Activities.
                assert any(s.get("kind") in (2, "SPAN_KIND_SERVER") for s in spans), (
                    "Missing actual ASP.NET Core inbound server Activity"
                )
                print(f"PASS: Tempo returned {len(spans)} real spans on TraceId {trace_id}; "
                      f"CQRS={len(handlers)} CQRS-EF-SQL={len(cqrs_sql)} "
                      f"auth/pipeline-SQL={len(sql) - len(cqrs_sql)}",
                      flush=True)
                return
            last = f"trace_incomplete: spans={len(spans)}, names={sorted(str(x) for x in names)}"
        else:
            last = f"tempo_http_{code}"
        time.sleep(2)
    raise AssertionError(f"Real trace never became complete in Tempo: {last}")


def main() -> None:
    status, captcha, _ = json_http("GET", CAPTCHA_PATH)
    assert status == 200 and captcha.get("token"), "Real disposable captcha unavailable"
    password = os.environ["SHINERA_DEVELOPMENT_ADMIN_PASSWORD"]
    status, auth, headers = json_http("POST", LOGIN_PATH, data={
        "grant_type": "password", "client_id": "postman",
        "username": "admin", "password": password, "login_context": "system",
        "captchaid": captcha["token"], "userEnteredCaptchaCode": "ABCDE",
        "scope": "openid profile email api offline_access",
    })
    assert status == 200 and auth.get("access_token", "").count(".") == 2, (
        "Real signed OpenIddict System JWT required"
    )
    token = auth["access_token"]

    # Auth/OIDC routes must never emit the diagnostic TraceId header, even
    # if a caller supplies a sampled traceparent or sensitive URL.
    assert "x-trace-id" not in headers, "Auth response wrongly enabled capture"
    code, body, auth_probe_headers = http(
        "GET", BASE + CAPTCHA_PATH + "?secret=" + UNSAFE_SENTINEL,
        headers={"traceparent": f"00-{AUTH_TRACE_ID}-{CALLER_SPAN_ID}-01"})
    assert code == 200 and "x-trace-id" not in auth_probe_headers, (
        "Unauthenticated auth path exported a trace support reference"
    )

    # A real authorized SystemAdministrator query calls Dispatcher, then
    # SuperDashboardQueryHandler, then many actual IdentityDbContext EF SQL queries.
    status, dashboard, response_headers = json_http(
        "GET", DASHBOARD + "?external_parameter=" + UNSAFE_SENTINEL,
        headers={
            "Authorization": "Bearer " + token,
            "traceparent": f"00-{DASHBOARD_TRACE_ID}-{CALLER_SPAN_ID}-01",
            "baggage": "tenant.id=FR18D3_UNTRUSTED_BAGGAGE,role=superadmin",
        })
    assert status == 200, f"Signed System dashboard query failed with HTTP {status}"
    assert isinstance(dashboard.get("totalTenants"), int), (
        "Dashboard did not return its real SQL-backed DTO"
    )
    trace_id = response_headers.get("x-trace-id", "")
    assert trace_id == DASHBOARD_TRACE_ID, (
        "Request's TraceId did not correlate with W3C inbound traceparent"
    )
    verify_persisted_lineage(trace_id)

    # FR-18E1: exercise the real System-only REST facade, not just Tempo
    # directly. No browser may receive a Tempo endpoint, raw OTLP or SQL.
    public = BASE + "/api/system/control-tower/traces/" + trace_id
    code, _, _ = http("GET", public)
    assert code == 401, "Anonymous trace access must be unauthorized"
    code, projected, _ = http("GET", public, headers={
        "Authorization": "Bearer " + token})
    assert code == 200, f"Authorized trace facade returned HTTP {code}"
    sanitized = json.loads(projected)
    assert sanitized.get("state") in ("Captured", "PartiallyCaptured")
    assert sanitized.get("traceId") == trace_id
    assert len(sanitized.get("spans", [])) >= 10
    assert "resourceSpans" not in sanitized, "Raw OTLP leaked through facade"
    assert UNSAFE_SENTINEL not in projected.decode("utf-8")
    assert "Bearer " not in projected.decode("utf-8")
    assert "db.statement" not in projected.decode("utf-8")
    code, _, _ = http("GET", BASE + "/api/system/control-tower/traces/not-a-trace",
                      headers={"Authorization": "Bearer " + token})
    assert code == 400, "Malformed trace lookup should be rejected"
    print("PASS: live System trace facade is read-only, authorized and sanitized",
          flush=True)

    # The trace for a sensitive auth route should not be retained, even after
    # the authorized trace has reached the same Collector/Tempo storage.
    code, doc, _ = read_trace(AUTH_TRACE_ID)
    assert code in (404, 400) or not spans_from_trace(doc), (
        "Excluded authentication request unexpectedly retained diagnostic spans"
    )
    print("PASS: excluded sampled auth path not retained; no secrets in Tempo", flush=True)
    print("FR-18D-3 full real signed HTTP->CQRS->SQL->Collector->Tempo E2E passed",
          flush=True)


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        # Do not print bearer tokens, passwords, SQL connection strings or
        # arbitrary HTTP response bodies from authentication endpoints.
        print(f"FAIL FR-18D3: {type(exc).__name__}: {str(exc)[:400]}",
              file=sys.stderr, flush=True)
        raise SystemExit(1)
