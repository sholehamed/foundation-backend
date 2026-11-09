"""FR-14B-2B static contract regression, complementing live SQL tests."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
IDENTITY = ROOT / "src/modules/Modules.System.Identity"
AUTH = IDENTITY / "Web/Endpoints/Auth.cs"
ADMINS = IDENTITY / "Web/Endpoints/Admins.cs"
WRITER = IDENTITY / "Web/Util/SystemFirstLoginWriter.cs"
CATALOG = IDENTITY / "Web/Util/FeatureRegistryDefinitions.cs"

def check_auth(source):
    assert 'MapPost(ResolveSystemFirstLogin, "system-first-login/resolve"' in source
    assert 'MapPost(CompleteSystemFirstLogin, "system-first-login/complete"' in source
    assert source.count('RequireRateLimiting("system-first-login")') >= 2
    assert "SystemFirstLoginHostAllowed(http, configuration)" in source
    assert 'Identity:PlatformAccounts:FirstLoginEnabled' in source
    assert "http.Request.IsHttps" in source
    assert "host == baseDomain" in source
    assert "SystemFirstLoginWriter.CompleteAsync" in source
    assert "requiresRelogin = true" in source


class SystemFirstLoginSecurityAuditTests(unittest.TestCase):
    def test_root_protocol_is_not_oauth_or_tenant_registration(self):
        check_auth(AUTH.read_text(encoding="utf-8-sig"))
        admin = ADMINS.read_text(encoding="utf-8-sig")
        catalog = CATALOG.read_text(encoding="utf-8-sig")
        writer = WRITER.read_text(encoding="utf-8-sig")
        for marker in (
            "SYSTEM.ADMINS.ISSUE-TEMP-PASSWORD", "ADMINS.ISSUE-TEMP-PASSWORD",
            'RequireRateLimiting("platform-account-create")',
            "SystemFirstLoginWriter.IssueAsync", "http.Request.IsHttps",
        ):
            self.assertIn(marker, admin)
        self.assertIn('"ADMINS.ISSUE-TEMP-PASSWORD"', catalog)
        for marker in (
            "IsolationLevel.Serializable", "SystemAccountFirstLoginChallenges",
            "SystemAccountTemporaryCredentials", "SHA256.HashData",
            "PasswordHash = hasher.HashPassword",
            "SecurityStamp = Guid.NewGuid()", "ConcurrencyStamp = Guid.NewGuid()",
            "temp_activated", "UsedAtUtc", "HasAnyGrantsAsync"):
            self.assertIn(marker, writer)

    def test_removing_origin_or_proof_rate_limit_is_rejected(self):
        original = AUTH.read_text(encoding="utf-8-sig")
        bad = original.replace(
            'RequireRateLimiting("system-first-login")',
            'RequireRateLimiting("identity-login")', 1)
        self.assertNotEqual(bad, original)
        with self.assertRaises(AssertionError):
            check_auth(bad)
        bad = original.replace("host == baseDomain", "host.Length > 0")
        self.assertNotEqual(bad, original)
        with self.assertRaises(AssertionError):
            check_auth(bad)


if __name__ == "__main__":
    unittest.main()
