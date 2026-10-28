"""Player verification requires the fixture providers for all selected packages."""
import unittest
import verify_player


class PlayerScopeTests(unittest.TestCase):
    def test_smoke_and_composed_scope_have_different_provider_requirements(self):
        report = {"build_exit_code": 0, "build": {"result": "Succeeded"}, "player_exit_code": 0,
                  "player": {"passed": True, "checks": 1, "scenarios": 1, "providers": []}}
        self.assertTrue(verify_player.player_passed(report, []))
        required = list(verify_player.PLAYER_PROVIDERS.values())
        self.assertFalse(verify_player.player_passed(report, required))
        report["player"]["providers"] = ["Fixture." + value for value in required]
        self.assertTrue(verify_player.player_passed(report, required))
        report["player"]["checks"] = 0
        self.assertFalse(verify_player.player_passed(report, required))

    def test_exit_and_nunit_independent_player_failure_propagates(self):
        report = {"build_exit_code": 0, "build": {"result": "Succeeded"}, "player_exit_code": 1,
                  "player": {"passed": True, "checks": 10, "scenarios": 1, "providers": []}}
        self.assertFalse(verify_player.player_passed(report, []))
        report["player_exit_code"] = 0
        report["build"]["result"] = "Failed"
        self.assertFalse(verify_player.player_passed(report, []))


if __name__ == "__main__":
    unittest.main()
