"""FR-14C: a Tenant consumes the System Admin registry, never authors it.

This intentionally fails CI if a future change reopens the removed owner
navigation editor, its migrations, or a parallel Tenant menu catalog.
"""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
IDENTITY = ROOT / "src/modules/Modules.System.Identity"
WEB = IDENTITY / "Web"


class CentralMenuOwnershipTests(unittest.TestCase):
    def test_tenant_navigation_is_only_a_filtered_central_registry_projection(self):
        service = (WEB / "Util/TenantWorkspaceNavigationService.cs").read_text()
        menu = (WEB / "Endpoints/Menus.cs").read_text()
        workspace = (WEB / "Endpoints/TenantWorkspace.cs").read_text()
        self.assertIn("CurrentUserNavigationService(db)", service)
        self.assertIn("TenantWorkspacePermissionEvaluator.AllowsAsync", service)
        self.assertIn("CurrentUserNavigationService(db)", menu)
        self.assertIn('RequireAuthorization("SystemAdministrator")', menu)
        for verb in ("MenuCreate", "MenuUpdate", "MenuDelete", "ReorderMenus"):
            self.assertIn(verb, menu)
        self.assertIn("GetTenantNavigation", workspace)
        self.assertIn(".RequireAuthorization()", workspace)
        self.assertNotIn("TenantNavigationManagement.Map(", workspace)
        self.assertNotIn("OwnedCatalogEnabled", workspace + service + menu)

    def test_no_tenant_menu_definition_or_ownership_write_surface(self):
        for relative in (
            "Web/Util/TenantNavigationOwnerWriter.cs",
            "Web/Util/TenantOwnedNavigationReader.cs",
            "Web/Endpoints/TenantNavigationManagement.cs",
            "Migrations/20261109080000_tenantownednavigation.cs",
            "Migrations/20261109090000_tenantnavigationaudit.cs",
        ):
            with self.subTest(relative=relative):
                self.assertFalse((IDENTITY / relative).exists())
        catalog = (WEB / "Endpoints/TenantWorkspacePermissionCatalog.cs").read_text()
        self.assertNotIn("TENANT.NAVIGATION.MANAGE", catalog)
        self.assertNotIn("TENANT.NAVIGATION.READ", catalog)
        self.assertIn("TENANT.ROLES.ASSIGN-PERMISSIONS", catalog)

    def test_mutating_source_to_allow_tenant_menu_authoring_fails_policy(self):
        source = (WEB / "Endpoints/TenantWorkspace.cs").read_text()
        altered = source + "\nTenantNavigationManagement.Map(app, backendPrefix);\n"
        with self.assertRaises(AssertionError):
            self.assertNotIn("TenantNavigationManagement.Map(", altered)


if __name__ == "__main__":
    unittest.main()
