"""FR-14B-1 source gate: no System endpoint may re-enable legacy account writes.

The old user commands have no trusted tenant boundary and can overwrite
password hashes. Role/permission writes use different audited endpoints.
"""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PATHS = [
    ROOT / "src/modules/Modules.System.Identity/Web/Endpoints/Users.cs",
    ROOT / "src/modules/Modules.System.Identity/Web/Endpoints/Admins.cs",
]
VERBS = {
    "UserCreate": "MapPost",
    "UserUpdate": "MapPut",
    "UserDelete": "MapDelete",
    "UserChangeState": "MapPatch",
    "UserChangeLockState": "MapPatch",
}


def assert_legacy_locked(source: str):
    for method, verb in VERBS.items():
        assert re.search(r"\." + verb + r"\(\s*" + method + r"\s*[,)]", source), (
            f"{verb}({method}) route disappeared; audit its replacement")
        signature = (r"public\s+ForbidHttpResult\s+" + method +
                     r"\s*\([^)]*\)\s*=>\s*TypedResults\.Forbid\(\)\s*;")
        assert re.search(signature, source), (
            f"{method} may dispatch an unscoped legacy account write")
    for disallowed in ("UserCreateCommand", "UserUpdateCommand", "UserDeleteCommand",
                       "UserChangeStateCommand", "UserChangeLockStateCommand"):
        assert disallowed not in source, f"Unscoped command {disallowed} must not be reachable"


class LegacyAccountWriteGateTests(unittest.TestCase):
    def test_system_users_and_admins_are_explicitly_closed(self):
        for path in PATHS:
            with self.subTest(path=path):
                assert_legacy_locked(path.read_text(encoding="utf-8-sig"))

    def test_mutation_removing_a_forbid_is_rejected(self):
        current = PATHS[0].read_text(encoding="utf-8-sig")
        altered = current.replace(
            "UserUpdate(Guid id) => TypedResults.Forbid();",
            "UserUpdate(Guid id) => TypedResults.NoContent();")
        self.assertNotEqual(current, altered)
        with self.assertRaises(AssertionError):
            assert_legacy_locked(altered)

    def test_mutation_reintroducing_legacy_dispatch_is_rejected(self):
        current = PATHS[1].read_text(encoding="utf-8-sig")
        altered = current.replace(
            "UserCreate() => TypedResults.Forbid();",
            "UserCreate(UserCreateCommand command) => sender.Send(command);")
        self.assertNotEqual(current, altered)
        with self.assertRaises(AssertionError):
            assert_legacy_locked(altered)


if __name__ == "__main__":
    unittest.main()
