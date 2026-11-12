"""Mutation-style regression tests for the FR-14A source security gate."""
import unittest
from pathlib import Path

from fr14a_audit import AuditError, ENDPOINTS, FEATURES, TENANT_CATALOG, audit_sources, audit_protocol_and_media, ROOT

def sources():
    result = {p.stem: p.read_text(encoding="utf-8-sig")
              for p in ENDPOINTS.glob("*.cs")}
    host_tower = ROOT / "src/Web/ControlTowerEndpoints.cs"
    if host_tower.exists():
        result[host_tower.stem] = host_tower.read_text(encoding="utf-8-sig")
    return result

def validate(data):
    return audit_sources(
        data,
        FEATURES.read_text(encoding="utf-8-sig"),
        TENANT_CATALOG.read_text(encoding="utf-8-sig"),
    )

class SecurityInventoryTests(unittest.TestCase):
    def test_current_source_has_all_endpoints_classified(self):
        rows = validate(sources())
        self.assertGreaterEqual(len(rows), 125)
        self.assertTrue(any(x.surface == "tenant" for x in rows))
        self.assertTrue(any(x.surface == "system" for x in rows))
        self.assertTrue(any(x.surface == "identity_protocol" for x in rows))
        self.assertEqual(len([x.endpoint_key for x in rows if x.endpoint_key]),
                         len(set(x.endpoint_key for x in rows if x.endpoint_key)))

    def test_separate_protocol_inventory_includes_BFF_media_and_dev_tooling(self):
        rows = audit_protocol_and_media(ROOT)
        self.assertEqual(len([r for r in rows if r.surface == "bff_protocol"]), 5)
        self.assertEqual(len([r for r in rows if r.surface == "media"]), 3)
        self.assertEqual(len([r for r in rows if r.surface == "dev_tooling"]), 3)
        self.assertTrue(all(r.security_boundary != "SYSTEM_ADMIN_PLUS_GRANULAR_OPT_IN"
                            for r in rows))

    def test_control_tower_uses_central_SystemAdministrator_group(self):
        data = sources()
        tower = data["ControlTower"]
        self.assertIn('app.MapGroup(this).RequireAuthorization("SystemAdministrator")', tower)
        compromised = dict(data)
        compromised["ControlTower"] = tower.replace(
            '.RequireAuthorization("SystemAdministrator")', '', 1)
        with self.assertRaisesRegex(AuditError, "System group lost SystemAdministrator"):
            validate(compromised)

    def test_control_tower_permission_owner_mismatch_is_blocked(self):
        data = sources()
        tower = data["ControlTower"]
        altered = tower.replace(
            '"SYSTEM.CONTROL-TOWER.TRACE-DETAIL", "control-tower",',
            '"SYSTEM.CONTROL-TOWER.TRACE-DETAIL", "roles",', 1)
        self.assertNotEqual(altered, tower)
        data["ControlTower"] = altered
        with self.assertRaisesRegex(AuditError, "Unregistered/wrong-resource"):
            validate(data)

    def test_control_tower_removing_rate_limit_fails_security_audit(self):
        data = sources()
        original = data["ControlTower"]
        required = '.RequireRateLimiting("control-tower-trace-read")'
        self.assertIn(required, original)
        data["ControlTower"] = original.replace(required, "", 1)
        with self.assertRaisesRegex(AuditError, "mandatory rate limiting"):
            validate(data)

    def test_new_system_endpoint_without_metadata_is_blocked(self):
        data = sources()
        data["Modules"] = data["Modules"].replace(
            '.MapDelete(ModuleDelete, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.MODULES.DELETE", "modules", "MODULES.DELETE", "ModuleDelete"))',
            '.MapDelete(ModuleDelete, "{id}")', 1)
        with self.assertRaisesRegex(AuditError, "missing registered System feature metadata"):
            validate(data)

    def test_resource_mismatch_is_blocked(self):
        data = sources()
        data["Modules"] = data["Modules"].replace(
            '"SYSTEM.MODULES.DELETE", "modules", "MODULES.DELETE"',
            '"SYSTEM.MODULES.DELETE", "roles", "MODULES.DELETE"', 1)
        with self.assertRaisesRegex(AuditError, "Unregistered/wrong-resource"):
            validate(data)

    def test_new_tenant_endpoint_without_authentication_is_blocked(self):
        data = sources()
        data["TenantWorkspace"] += (
            '\n// new insecure route registered in Map()\n'
            'app.MapPost("/api/tenant/workspace/unsafe", Unsafe).WithTags("tenant");\n')
        with self.assertRaisesRegex(AuditError, "Tenant workspace endpoint lost authentication"):
            validate(data)

    def test_unthrottled_public_provisioning_is_blocked(self):
        data = sources()
        data["PublicSignup"] = data["PublicSignup"].replace(
            '.RequireRateLimiting("public-signup")', '.WithTags("not-throttled")', 1)
        with self.assertRaisesRegex(AuditError, "PublicSignup must be anonymous"):
            validate(data)

    def test_removing_system_admin_group_policy_is_blocked(self):
        data = sources()
        data["Groups"] = data["Groups"].replace(
            '.RequireAuthorization("SystemAdministrator")', '', 1)
        with self.assertRaisesRegex(AuditError, "System group lost SystemAdministrator"):
            validate(data)

if __name__ == "__main__":
    unittest.main()
