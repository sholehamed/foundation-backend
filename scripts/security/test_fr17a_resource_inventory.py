"""FR-17A-1 read-only product catalog boundary and EndpointGroup mapping tests."""
from __future__ import annotations

import copy
import unittest

from fr14a_audit import AuditError, FEATURES, TENANT_CATALOG
from fr17a_resource_inventory import (
    build_source_inventory, generate, read_sources, tenant_permissions,
)


def base_sources():
    return read_sources()


def source_manifest(sources=None, feature=None, tenant=None):
    return build_source_inventory(
        sources if sources is not None else base_sources(),
        feature if feature is not None else FEATURES.read_text(encoding="utf-8-sig"),
        tenant if tenant is not None else TENANT_CATALOG.read_text(encoding="utf-8-sig"),
    )


class ProductResourceInventoryTests(unittest.TestCase):
    def test_all_15_real_system_resources_and_registered_endpoints_present(self):
        result = generate()
        self.assertEqual(result["counts"]["systemResources"], 15)
        self.assertGreaterEqual(result["counts"]["systemEndpointRegistrations"], 75)
        self.assertEqual(
            len({x["resourceCode"] for x in result["systemResources"]}), 15)
        self.assertFalse(result["commercialEntitlementEnforced"])
        self.assertTrue(all(x["moduleCode"] == "system"
                            for x in result["systemResources"]))
        self.assertTrue(all(x["moduleClass"] == "platform_core"
                            for x in result["systemResources"]))
        self.assertEqual(
            sum(len(r["registeredEndpoints"]) for r in result["systemResources"]),
            result["counts"]["systemEndpointRegistrations"],
        )

    def test_host_control_tower_is_scanned_as_a_real_system_resource(self):
        result = generate()
        tower = next(r for r in result["systemResources"]
                     if r["resourceCode"] == "control-tower")
        self.assertEqual(tower["moduleClass"], "platform_core")
        self.assertEqual(tower["endpointGroups"], ["ControlTowerEndpoints.cs"])
        self.assertEqual(
            tower["permissionCodes"],
            ["CONTROL-TOWER.READ", "CONTROL-TOWER.TRACES.READ"])
        self.assertEqual(len(tower["registeredEndpoints"]), 1)
        self.assertEqual(tower["registeredEndpoints"][0]["endpointKey"],
                         "SYSTEM.CONTROL-TOWER.TRACE-DETAIL")

    def test_shared_menu_resource_is_explicit_and_not_one_to_one(self):
        result = source_manifest()
        shared = [r for r in result["systemResources"]
                  if r["crossGroupException"]]
        self.assertEqual(len(shared), 1)
        self.assertEqual(shared[0]["resourceCode"], "menu-management")
        self.assertEqual(
            shared[0]["endpointGroups"], ["MenuCategories.cs", "Menus.cs"])

    def test_tenant_admin_not_automatically_sold_or_delegated(self):
        result = source_manifest()
        tenant = result["tenantAdminCatalog"]
        self.assertEqual(tenant["moduleCode"], "tenant-workspace")
        self.assertEqual(tenant["resourceCode"], "tenant-administration")
        self.assertEqual(len(tenant["permissionCodes"]), 17)
        self.assertTrue(all(x.startswith("TENANT.") for x in tenant["permissionCodes"]))
        self.assertEqual(len(set(tenant["permissionCodes"])), 17)
        self.assertFalse(result["commercialEntitlementEnforced"])

    def test_bff_protocol_media_and_dev_routes_are_not_commercial_resources(self):
        result = generate()
        classes = result["counts"]["routesBySurface"]
        self.assertEqual(classes["bff_protocol"], 5)
        self.assertEqual(classes["media"], 3)
        self.assertEqual(classes["dev_tooling"], 3)
        for endpoint in result["otherEndpoints"]:
            self.assertNotEqual(endpoint.get("moduleClass"), "commercial")
            self.assertEqual(
                endpoint["entitlementBindingStatus"],
                "not_applicable_core_or_unreviewed",
            )

    def test_unreviewed_cross_group_resource_owner_is_blocked(self):
        sources = base_sources()
        old = '"SYSTEM.ADMINS.DETAIL", "admins", "ADMINS.READ"'
        new = '"SYSTEM.ADMINS.DETAIL", "users", "USERS.READ"'
        self.assertIn(old, sources["Admins"])
        sources["Admins"] = sources["Admins"].replace(old, new, 1)
        # The key is still unique and the permission exists in the other
        # Resource, but moving an EndpointGroup to a shared Resource requires
        # explicit owner review, not silent acceptance.
        with self.assertRaisesRegex(AuditError, "unreviewed cross-EndpointGroup"):
            source_manifest(sources=sources)

    def test_missing_or_duplicate_tenant_catalog_definitions_fail_closed(self):
        source = TENANT_CATALOG.read_text(encoding="utf-8-sig")
        original = 'public const string MembersRead = "TENANT.MEMBERS.READ";'
        self.assertIn(original, source)
        missing = source.replace(original, "", 1)
        with self.assertRaisesRegex(AuditError, "17 unique"):
            tenant_permissions(missing)

        duplicate = source.replace(
            'public const string RolesRead = "TENANT.ROLES.READ";',
            'public const string RolesRead = "TENANT.MEMBERS.READ";', 1)
        with self.assertRaisesRegex(AuditError, "17 unique"):
            tenant_permissions(duplicate)

    def test_source_inventory_reuses_security_audit_for_unmapped_endpoint(self):
        sources = base_sources()
        marker = '.MapDelete(ModuleDelete, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.MODULES.DELETE", "modules", "MODULES.DELETE", "ModuleDelete"))'
        self.assertIn(marker, sources["Modules"])
        sources["Modules"] = sources["Modules"].replace(
            marker, '.MapDelete(ModuleDelete, "{id}")', 1)
        with self.assertRaisesRegex(AuditError, "missing registered System feature metadata"):
            source_manifest(sources=sources)


if __name__ == "__main__":
    unittest.main()
