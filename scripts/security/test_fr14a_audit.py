"""Mutation-style regression tests for the FR-14A source security gate."""
import unittest
from pathlib import Path

from fr14a_audit import AuditError, ENDPOINTS, FEATURES, TENANT_CATALOG, audit_sources

def sources():
    return {p.stem: p.read_text(encoding="utf-8-sig")
            for p in ENDPOINTS.glob("*.cs")}

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
