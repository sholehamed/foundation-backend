"""Offline-only checks for Foundation FR-13 staging preflight."""

import io
import json
import unittest
from email.message import Message
from urllib.error import HTTPError

from preflight import PreflightError, _anonymous_get, probe, validate


def sample(stage="baseline"):
    pilot = stage == "tenant-bff-pilot"
    return {
        "environment": "Staging", "stage": stage,
        "tenant_base_domain": "staging.shinera.ir",
        "system_origin": "https://system.staging.shinera.ir",
        "identity_origin": "https://identity.staging.shinera.ir",
        "tenant_origin": "https://demo.staging.shinera.ir",
        "backend": {"bff_enabled": pilot, "tenant_cookie_bridge_enabled": pilot, "registry_apply_enabled": False},
        "frontend": {"tenant_bff_pilot_enabled": pilot, "system_ui_access_pilot_enabled": False},
    }


class FakeResponse:
    def __init__(self, status, headers=None, body=b""):
        self.status = status
        self.headers = headers or {}
        self.stream = io.BytesIO(body)

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.stream.close()

    def read(self, n):
        return self.stream.read(n)


class FakeOpener:
    def __init__(self, responses):
        self.responses = iter(responses)
        self.urls = []

    def open(self, request, timeout):
        self.urls.append(request.full_url)
        return next(self.responses)


class PreflightTests(unittest.TestCase):
    def test_baseline_valid(self):
        self.assertEqual(validate(sample())["stage"], "baseline")

    def test_pilot_valid(self):
        self.assertEqual(validate(sample("tenant-bff-pilot"))["stage"], "tenant-bff-pilot")

    def test_production_zone_rejected(self):
        config = sample()
        config["tenant_base_domain"] = "shinera.ir"
        with self.assertRaisesRegex(PreflightError, "dedicated"):
            validate(config)

    def test_invalid_dns_labels_rejected(self):
        config = sample()
        config["tenant_base_domain"] = "staging..shinera.ir"
        with self.assertRaisesRegex(PreflightError, "valid lowercase DNS"):
            validate(config)
        config = sample()
        config["system_origin"] = "https://-system.staging.shinera.ir"
        with self.assertRaisesRegex(PreflightError, "invalid hostname"):
            validate(config)

    def test_http_rejected(self):
        config = sample()
        config["tenant_origin"] = "http://demo.staging.shinera.ir"
        with self.assertRaisesRegex(PreflightError, "HTTPS"):
            validate(config)

    def test_url_with_password_rejected(self):
        config = sample()
        config["system_origin"] = "https://name:secret@system.staging.shinera.ir"
        with self.assertRaises(PreflightError):
            validate(config)

    def test_foreign_origin_rejected(self):
        config = sample()
        config["tenant_origin"] = "https://demo.shinera.ir"
        with self.assertRaises(PreflightError):
            validate(config)

    def test_shared_identity_tenant_host_rejected(self):
        config = sample()
        config["identity_origin"] = config["tenant_origin"]
        with self.assertRaisesRegex(PreflightError, "distinct"):
            validate(config)

    def test_mismatched_pilot_flags_rejected(self):
        config = sample("tenant-bff-pilot")
        config["frontend"]["tenant_bff_pilot_enabled"] = False
        with self.assertRaisesRegex(PreflightError, "frontend.tenant_bff_pilot_enabled"):
            validate(config)

    def test_registry_apply_rejected(self):
        config = sample()
        config["backend"]["registry_apply_enabled"] = True
        with self.assertRaisesRegex(PreflightError, "Registry Apply"):
            validate(config)

    def test_ui_pilot_rejected_without_approval(self):
        config = sample()
        config["frontend"]["system_ui_access_pilot_enabled"] = True
        with self.assertRaisesRegex(PreflightError, "UI access pilot"):
            validate(config)

    def test_boolean_not_string(self):
        config = sample()
        config["backend"]["bff_enabled"] = "false"
        with self.assertRaisesRegex(PreflightError, "explicit boolean"):
            validate(config)

    def test_baseline_probes_not_allowed(self):
        with self.assertRaisesRegex(PreflightError, "require stage"):
            probe(sample())

    def test_pilot_probes_anonymous_and_read_only(self):
        c = sample("tenant-bff-pilot")
        opener = FakeOpener([
            FakeResponse(200, {}, json.dumps({"issuer": c["identity_origin"]}).encode()),
            FakeResponse(401, {"Cache-Control": "private, no-store"}),
            FakeResponse(401, {"Cache-Control": "no-store"}),
            FakeResponse(401),
        ])
        self.assertTrue(probe(c, opener))
        self.assertEqual(opener.urls, [
            c["identity_origin"] + "/.well-known/openid-configuration",
            c["tenant_origin"] + "/bff/session",
            c["system_origin"] + "/bff/session",
            c["tenant_origin"] + "/api/tenant/workspace",
        ])

    def test_wrong_discovery_issuer_fails_before_session(self):
        opener = FakeOpener([FakeResponse(200, {}, b'{"issuer":"https://attacker.test"}')])
        with self.assertRaisesRegex(PreflightError, "issuer does not match"):
            probe(sample("tenant-bff-pilot"), opener)
        self.assertEqual(len(opener.urls), 1)

    def test_bff_302_fails_not_interpreted_as_success(self):
        c = sample("tenant-bff-pilot")
        opener = FakeOpener([
            FakeResponse(200, {}, json.dumps({"issuer": c["identity_origin"]}).encode()),
            FakeResponse(302, {"Location": "https://other.example.test"}),
        ])
        with self.assertRaisesRegex(PreflightError, "HTTP 401"):
            probe(c, opener)

    def test_bff_cache_control_required(self):
        c = sample("tenant-bff-pilot")
        opener = FakeOpener([
            FakeResponse(200, {}, json.dumps({"issuer": c["identity_origin"]}).encode()),
            FakeResponse(401, {}),
        ])
        with self.assertRaisesRegex(PreflightError, "no-store"):
            probe(c, opener)

    def test_http_error_401_handled_without_follow(self):
        headers = Message()
        headers["Cache-Control"] = "no-store"
        class HttpErrorOpener:
            def open(self, request, timeout):
                raise HTTPError(request.full_url, 401, "Unauthorized", headers, io.BytesIO(b""))
        status, hs, body = _anonymous_get(HttpErrorOpener(), "https://demo.staging.shinera.ir/bff/session")
        self.assertEqual(status, 401)
        self.assertEqual(hs.get("Cache-Control"), "no-store")
        self.assertEqual(body, b"")


if __name__ == "__main__":
    unittest.main()
