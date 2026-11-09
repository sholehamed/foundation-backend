"""FR-14B security gate: only a reviewed inert System account creation is allowed.

System Users remain legacy-write-denied; System Admins have a *single*
opt-in, no-credential create path. Never dispatch old shared User commands.
"""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
USERS = ROOT / "src/modules/Modules.System.Identity/Web/Endpoints/Users.cs"
ADMINS = ROOT / "src/modules/Modules.System.Identity/Web/Endpoints/Admins.cs"
WRITER = ROOT / "src/modules/Modules.System.Identity/Web/Util/SystemPendingAccountWriter.cs"
VERBS = {
    "UserCreate": "MapPost",
    "UserUpdate": "MapPut",
    "UserDelete": "MapDelete",
    "UserChangeState": "MapPatch",
    "UserChangeLockState": "MapPatch",
}


def assert_account_boundary(source: str, allow_inert_admin_create: bool):
    for method, verb in VERBS.items():
        assert re.search(r"\." + verb + r"\(\s*" + method + r"\s*[,)]", source), (
            f"{verb}({method}) route disappeared; audit its replacement")
        if method == "UserCreate" and allow_inert_admin_create:
            assert re.search(r"public\s+async\s+Task<IResult>\s+UserCreate\s*\(", source)
            for required in ("SystemPendingAccountWriter.CreateRequest",
                             "SystemPendingAccountWriter.CreateAsync",
                             "Identity:PlatformAccounts:PendingCreationEnabled",
                             "http.Request.IsHttps",
                             "RequireRateLimiting(\"platform-account-create\")"):
                assert required in source, f"Safe System create contract lost {required}"
        else:
            signature = (r"public\s+ForbidHttpResult\s+" + method +
                         r"\s*\([^)]*\)\s*=>\s*TypedResults\.Forbid\(\)\s*;")
            assert re.search(signature, source), f"{method} legacy write re-enabled"
    for legacy in ("UserCreateCommand", "UserUpdateCommand", "UserDeleteCommand",
                   "UserChangeStateCommand", "UserChangeLockStateCommand"):
        assert legacy not in source, f"Unscoped command {legacy} reintroduced"


class SystemAccountSecurityGateTests(unittest.TestCase):
    def test_users_deny_all_old_writes_and_admins_allow_only_inert_create(self):
        assert_account_boundary(USERS.read_text(encoding="utf-8-sig"), False)
        assert_account_boundary(ADMINS.read_text(encoding="utf-8-sig"), True)

    def test_inert_writer_has_no_client_password_or_scope_or_grant(self):
        code = WRITER.read_text(encoding="utf-8-sig")
        for required in ("UserType.System, isActive: false", "RandomNumberGenerator.GetBytes(48)",
                         "AuthorizedPlatformMasterWrite", "IsolationLevel.Serializable",
                         "SystemAccountLifecycleAuditEntries", "JsonUnmappedMemberHandling.Disallow"):
            assert required in code, f"Missing critical inert account guard: {required}"
        contract = re.search(r"record CreateRequest\s*\((.*?)\)\s*;", code, re.S)
        assert contract, "Create contract missing"
        assert all(word not in contract.group(1) for word in (
            "Password", "TenantId", "UserType", "RoleId", "IsActive", "PermissionId"))

    def test_mutation_removing_forbid_fails(self):
        source = USERS.read_text(encoding="utf-8-sig")
        altered = source.replace(
            "UserUpdate(Guid id) => TypedResults.Forbid();",
            "UserUpdate(Guid id) => TypedResults.NoContent();")
        self.assertNotEqual(source, altered)
        with self.assertRaises(AssertionError):
            assert_account_boundary(altered, False)

    def test_mutation_reintroducing_legacy_admin_dispatch_fails(self):
        source = ADMINS.read_text(encoding="utf-8-sig")
        altered = source.replace("SystemPendingAccountWriter.CreateAsync",
                                 "sender.Send(new UserCreateCommand())")
        self.assertNotEqual(source, altered)
        with self.assertRaises(AssertionError):
            assert_account_boundary(altered, True)


if __name__ == "__main__":
    unittest.main()
