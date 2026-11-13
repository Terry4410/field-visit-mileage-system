"""Offline fail-closed contract tests; no Azure/SQL/Token calls."""
import importlib.util
import pathlib
import unittest

HERE = pathlib.Path(__file__).resolve().parent
TARGET = HERE / "pathb_readonly_offline_binding_20261113.py"
spec = importlib.util.spec_from_file_location("readonly_binding", TARGET)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

class ReadOnlyBindingTests(unittest.TestCase):
    def test_fixed_sql_hash(self):
        self.assertTrue(mod.validate_sql((HERE / "preflight.sql").read_bytes()))
        self.assertFalse(mod.validate_sql(b"SELECT 1"))
        self.assertFalse(mod.validate_sql((HERE / "preflight.sql").read_bytes() + b" "))
    def test_owner_hashes_fail_closed(self):
        self.assertFalse(mod.validate_identity("", "", "", "user"))
        self.assertFalse(mod.validate_identity("x", "y", "z", "servicePrincipal"))
    def test_tool_hash_fail_closed(self):
        self.assertFalse(mod.validate_tool(b"unverified"))
    def test_no_live_entrypoint(self):
        self.assertFalse(hasattr(mod, "execute_sql"))
        self.assertFalse(hasattr(mod, "request_token"))
        self.assertFalse(hasattr(mod, "send_ssh"))

if __name__ == "__main__":
    unittest.main()
