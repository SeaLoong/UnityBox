"""Regressions for real package version syntax encountered by the type checker."""
import unittest

from compile_assemblies import allowed, version_matches


class AssemblySelectionTests(unittest.TestCase):
    def test_release_does_not_equal_prerelease_at_open_bound(self):
        self.assertTrue(version_matches("3.7.0", "(3.7.0-beta.2,)"))
        self.assertFalse(version_matches("3.7.0-beta.2", "3.7.0"))
        self.assertTrue(version_matches("1.11.0", "[0,2.0.0)"))
        self.assertFalse(version_matches("2.0.0", "[0,2.0.0)"))

    def test_missing_sdk_excludes_integration_but_keeps_negated_fallback(self):
        symbols = {"UNITY_EDITOR", "UNITY_INCLUDE_TESTS"}
        self.assertFalse(allowed(["UNITY_INCLUDE_TESTS", "SDK_AVAILABLE || NDMF_AVAILABLE"], symbols))
        self.assertTrue(allowed(["UNITY_EDITOR", "!SDK_AVAILABLE"], symbols))
        symbols.add("SDK_AVAILABLE")
        self.assertFalse(allowed(["UNITY_EDITOR", "!SDK_AVAILABLE"], symbols))


if __name__ == "__main__":
    unittest.main()
