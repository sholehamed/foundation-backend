"""FR-15D: dynamic nav browser rollout cannot escape disposable Actions main runner."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
SHELL = (ROOT / "tests/browser/run_live_https_bff.sh").read_text()
MUTATOR = (ROOT / "tests/Shinera.Identity.E2E.SqlMutator/Program.cs").read_text()
BROWSER = (ROOT / "tests/browser/tenant-bff-real.spec.ts").read_text()

class EphemeralMenuPilotContractTests(unittest.TestCase):
    def test_runner_opt_in_stays_scoped_to_trusted_main_and_disposable_database(self):
        for required in (
            "GITHUB_ACTIONS", "GITHUB_REPOSITORY", "GITHUB_REF",
            "refs/heads/main", "Shinera_E2E_RealBffBrowser",
            "SHINERA_E2E_DB_MUTATION", "FR15D_E2E_DYNAMIC_NAVIGATION",
            "tenantDynamicNavigationPilotEnabled: false,",
            "tenantDynamicNavigationPilotEnabled: true,",
            "-- seed-central-tenant-navigation",
        ):
            with self.subTest(required=required):
                self.assertIn(required, SHELL)
        self.assertIn("System-owned", MUTATOR)
        self.assertIn("CentralTenantNavigationReadiness.ReadAsync", MUTATOR)
        self.assertIn('databaseName != "Shinera_E2E_RealBffBrowser"', MUTATOR)
        self.assertIn('GITHUB_REF") != "refs/heads/main"', MUTATOR)
        self.assertIn("RolePermissions.IgnoreQueryFilters", MUTATOR)
        self.assertIn("UserPermissions.IgnoreQueryFilters", MUTATOR)

    def test_real_chromium_asserts_grant_revoke_and_system_boundary(self):
        self.assertIn("flattenMenuRoutes", BROWSER)
        self.assertIn("FR15D_E2E_DYNAMIC_NAVIGATION", BROWSER)
        self.assertIn("'/api/tenant/workspace/navigation/me'", BROWSER)
        self.assertIn("central-access/readiness", BROWSER)
        self.assertIn("toContain('/dashboard/members')", BROWSER)
        self.assertIn("not.toContain('/dashboard/members')", BROWSER)
        self.assertIn("toHaveCount(0)", BROWSER)

if __name__ == "__main__":
    unittest.main()
