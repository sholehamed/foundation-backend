"""Offline-only checks for Foundation FR-13 staging preflight."""

import io
import json
import tempfile
import unittest
from pathlib import Path
from email.message import Message
from urllib.error import HTTPError

from preflight import PreflightError, _anonymous_get, main, probe, validate


def sample(stage="baseline"):
    pilot = stage == "tenant-bff-pilot"
    return {
        "environment": "Staging", "stage": stage,
        "tenant_base_domain": "staging.shinera.ir",
        "system_origin": "https://system.staging.shinera.ir",
        "identity_origin": "https://identity.staging.shinera.ir",
        "tenant_origin": "https://demo.staging.shinera.ir",
        "second_tenant_origin": "https://other.staging.shinera.ir",
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

    def test_two_tenants_required_for_pilot(self):
        config = sample("tenant-bff-pilot")
        del config["second_tenant_origin"]
        with self.assertRaisesRegex(PreflightError, "HTTPS origin"):
            validate(config)

    def test_two_tenant_host_collision_rejected(self):
        config = sample("tenant-bff-pilot")
        config["second_tenant_origin"] = config["tenant_origin"]
        with self.assertRaisesRegex(PreflightError, "distinct hosts"):
            validate(config)

    def test_second_tenant_cannot_use_external_zone(self):
        config = sample("tenant-bff-pilot")
        config["second_tenant_origin"] = "https://other.shinera.ir"
        with self.assertRaisesRegex(PreflightError, "subdomain"):
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

    def test_manifest_does_not_accept_secrets_or_unknown_flags(self):
        config = sample()
        config["client_secret"] = "accidental-disclosure"
        with self.assertRaisesRegex(PreflightError, "unknown top-level"):
            validate(config)
        config = sample()
        config["backend"]["data_base_password"] = "must-never-be-in-manifest"
        with self.assertRaisesRegex(PreflightError, "non-secret schema"):
            validate(config)
        config = sample()
        config["frontend"]["tenant_dynamic_navigation_pilot_enabled"] = True
        with self.assertRaisesRegex(PreflightError, "non-secret schema"):
            validate(config)

    def test_workflow_requires_manual_main_only_read_only_probe(self):
        # Workflow is workflow_dispatch only; no push/PR event can touch an
        # externally deployed Staging URL. No credentials or SQL writes.
        workflow = (Path(__file__).resolve().parents[2] /
                    ".github/workflows/staging-readonly-gate.yml").read_text(encoding="utf-8")
        self.assertIn("workflow_dispatch:", workflow)
        self.assertNotIn("\n  push:", workflow)
        self.assertNotIn("\n  pull_request:", workflow)
        self.assertIn("github.ref == 'refs/heads/main'", workflow)
        self.assertIn("PROBE_ISOLATED_STAGING", workflow)
        self.assertIn("vars.FOUNDATION_STAGING_MANIFEST_JSON", workflow)
        self.assertIn("--probe", workflow)
        self.assertIn("contents: read", workflow)

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
            FakeResponse(401, {"Cache-Control": "no-store"}),
            FakeResponse(401),
            FakeResponse(401),
        ])
        self.assertTrue(probe(c, opener))
        self.assertEqual(opener.urls, [
            c["identity_origin"] + "/.well-known/openid-configuration",
            c["tenant_origin"] + "/bff/session",
            c["second_tenant_origin"] + "/bff/session",
            c["system_origin"] + "/bff/session",
            c["tenant_origin"] + "/api/tenant/workspace",
            c["second_tenant_origin"] + "/api/tenant/workspace",
        ])

    def test_wrong_discovery_issuer_fails_before_session(self):
        opener = FakeOpener([FakeResponse(200, {}, b'{"issuer":"https://attacker.test"}')])
        with self.assertRaisesRegex(PreflightError, "issuer does not match"):
            probe(sample("tenant-bff-pilot"), opener)
        self.assertEqual(len(opener.urls), 1)

    def test_malformed_discovery_issuer_rejected(self):
        opener = FakeOpener([FakeResponse(200, {}, b'{"issuer":42}')])
        with self.assertRaisesRegex(PreflightError, "issuer does not match"):
            probe(sample("tenant-bff-pilot"), opener)

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

    def test_second_tenant_200_session_is_a_failure(self):
        c = sample("tenant-bff-pilot")
        opener = FakeOpener([
            FakeResponse(200, {}, json.dumps({"issuer": c["identity_origin"]}).encode()),
            FakeResponse(401, {"Cache-Control": "no-store"}),
            FakeResponse(200, {"Cache-Control": "no-store"}),
        ])
        with self.assertRaisesRegex(PreflightError, "second_tenant_origin.*401"):
            probe(c, opener)

    def test_second_tenant_workspace_must_not_be_public(self):
        c = sample("tenant-bff-pilot")
        opener = FakeOpener([
            FakeResponse(200, {}, json.dumps({"issuer": c["identity_origin"]}).encode()),
            FakeResponse(401, {"Cache-Control": "no-store"}),
            FakeResponse(401, {"Cache-Control": "no-store"}),
            FakeResponse(401, {"Cache-Control": "no-store"}),
            FakeResponse(401),
            FakeResponse(200),
        ])
        with self.assertRaisesRegex(PreflightError, "second_tenant_origin.*401"):
            probe(c, opener)

    def test_success_evidence_contains_no_hostnames_or_secrets(self):
        config = sample()
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "manifest.json"
            evidence = Path(folder) / "report.json"
            path.write_text(json.dumps(config), encoding="utf-8")
            self.assertEqual(main(["--config", str(path), "--evidence", str(evidence)]), 0)
            data = json.loads(evidence.read_text(encoding="utf-8"))
            self.assertTrue(data["manifestValidated"])
            self.assertFalse(data["anonymousHttpsProbePassed"])
            self.assertEqual(data["anonymousHttpsGetCount"], 0)
            self.assertFalse(data["stagingDeploymentCertified"])
            self.assertFalse(data["authenticatedAccessCertified"])
            self.assertEqual(len(data["manifestSha256"]), 64)
            self.assertNotIn("staging.shinera.ir", evidence.read_text())

    def test_failed_manifest_does_not_create_evidence(self):
        config = sample()
        config["tenant_origin"] = "https://demo.shinera.ir"
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "manifest.json"
            evidence = Path(folder) / "report.json"
            path.write_text(json.dumps(config), encoding="utf-8")
            self.assertEqual(main(["--config", str(path), "--evidence", str(evidence)]), 2)
            self.assertFalse(evidence.exists())

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
