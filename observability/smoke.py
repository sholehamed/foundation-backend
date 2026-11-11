#!/usr/bin/env python3
"""FR-18C disposable integration smoke: real OTLP HTTP -> Collector -> Tempo/Prometheus.

Pure standard-library client; never sends real customer data. This is a
telemetry-stack integration test, not production OpenIddict/BFF acceptance.
"""
from __future__ import annotations

import json
import secrets
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

TEMPO = "http://127.0.0.1:3200"
COLLECTOR = "http://127.0.0.1:4318"
COLLECTOR_HEALTH = "http://127.0.0.1:13133"
PROMETHEUS = "http://127.0.0.1:9090"
SECRET_SENTINEL = "SHOULD_NOT_LEAVE_COLLECTOR_27F1"


def request(url: str, data: dict | None = None) -> tuple[int, str]:
    payload = json.dumps(data).encode("utf-8") if data is not None else None
    req = Request(
        url,
        data=payload,
        headers={"Content-Type": "application/json", "Accept": "application/json"},
        method="POST" if payload is not None else "GET",
    )
    try:
        with urlopen(req, timeout=3) as response:
            return response.status, response.read(3_000_000).decode("utf-8", "replace")
    except HTTPError as error:
        return error.code, error.read(512).decode("utf-8", "replace")
    except (URLError, TimeoutError, ConnectionError):
        return 0, ""


def until(label: str, check, timeout: float = 80.0):
    deadline = time.monotonic() + timeout
    last_error = None
    while time.monotonic() < deadline:
        try:
            result = check()
            if result is not None and result is not False:
                print(f"[OK] {label}", flush=True)
                return result
        except (AssertionError, KeyError, ValueError, TypeError) as error:
            last_error = repr(error)
        time.sleep(2)
    raise AssertionError(f"{label} did not succeed before timeout ({last_error})")


def healthy(base: str):
    status, _ = request(base)
    return True if status == 200 else None


def main():
    until("Collector health", lambda: healthy(COLLECTOR_HEALTH))
    until("Tempo ready", lambda: healthy(TEMPO + "/ready"))
    until("Prometheus ready", lambda: healthy(PROMETHEUS + "/-/ready"))

    trace_id = secrets.token_hex(16)
    span_id = secrets.token_hex(8)
    now = time.time_ns()
    common_resource = {
        "attributes": [
            {"key": "service.name", "value": {"stringValue": "foundation-fr18c-integration-smoke"}}
        ]
    }
    trace = {
        "resourceSpans": [{
            "resource": common_resource,
            "scopeSpans": [{
                "scope": {"name": "foundation.fr18c.smoke"},
                "spans": [{
                    # OTLP/HTTP+JSON uses HEX identifiers, not protobuf's base64 encoding.
                    "traceId": trace_id,
                    "spanId": span_id,
                    "name": "ci.synthetic.trace",
                    "kind": 2,
                    "startTimeUnixNano": str(now - 100_000_000),
                    "endTimeUnixNano": str(now),
                    "attributes": [
                        {"key": "http.route", "value": {"stringValue": "/__ci/smoke/{id}"}},
                        {"key": "url.full", "value": {"stringValue": f"https://private.invalid/?password={SECRET_SENTINEL}"}},
                        {"key": "http.request.header.authorization", "value": {"stringValue": SECRET_SENTINEL}},
                    ],
                    "status": {"code": 1},
                }],
            }],
        }]
    }
    status, body = request(COLLECTOR + "/v1/traces", trace)
    assert status == 200, f"OTLP trace receiver returned {status}: {body[:250]}"
    print(f"OTLP receiver accepted synthetic trace {trace_id}", flush=True)

    metric = {
        "resourceMetrics": [{
            "resource": common_resource,
            "scopeMetrics": [{
                "scope": {"name": "foundation.fr18c.smoke"},
                "metrics": [{
                    "name": "foundation.fr18c.smoke",
                    "sum": {
                        "dataPoints": [{"timeUnixNano": str(now), "asInt": "1"}],
                        "aggregationTemporality": 2,
                        "isMonotonic": True,
                    },
                }],
            }],
        }]
    }
    status, body = request(COLLECTOR + "/v1/metrics", metric)
    assert status == 200, f"OTLP metric receiver returned {status}: {body[:250]}"
    print("OTLP receiver accepted synthetic metric", flush=True)

    def trace_saved():
        code, data = request(TEMPO + "/api/traces/" + trace_id)
        if code != 200 or "ci.synthetic.trace" not in data:
            return None
        assert SECRET_SENTINEL not in data, "Collector leaked forbidden authorization/URL attribute"
        assert "SHOULD_NOT_LEAVE_COLLECTOR" not in data, "Collector leaked forbidden attribute"
        assert "foundation-fr18c-integration-smoke" in data, "Trace service identity missing"
        return True

    until("Tempo persisted synthetic span and Collector removed sensitive tags", trace_saved)

    def metric_scraped():
        q = urlencode({
            "match[]": '{__name__=~"foundation_fr18c_smoke.*"}',
            "start": str(int(time.time()) - 120),
            "end": str(int(time.time()) + 60),
        })
        code, result = request(PROMETHEUS + "/api/v1/series?" + q)
        if code != 200:
            return None
        data = json.loads(result)
        values = data.get("data", [])
        return bool(values) or None

    until("Prometheus scraped Collector metrics", metric_scraped)
    print("FR-18C real Collector/Tempo/Prometheus smoke passed", flush=True)


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"[FAIL] FR-18C smoke: {exc}", file=sys.stderr, flush=True)
        raise
