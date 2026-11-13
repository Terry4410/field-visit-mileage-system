"""Offline-only regression guard: G1 parent must select pinned SSH, not legacy Kudu.

This test reads source and AST only. No Azure, SSH, tokens, SQL, or deployment.
"""
import ast
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent

class TestParentSshBinding(unittest.TestCase):
    def test_parent_session_factory_is_pinned_ssh(self):
        source = (ROOT / "p3_kudu_cloudshell_parent.py").read_text("utf-8")
        tree = ast.parse(source)
        imports = {
            alias.name: node.module
            for node in ast.walk(tree)
            if isinstance(node, ast.ImportFrom)
            for alias in node.names
        }
        self.assertEqual(imports.get("SshExecSession"), "p3_g1_pinned_ssh")
        self.assertNotIn("KuduExecSession", imports)
        factories = [
            node for node in ast.walk(tree)
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef))
            and node.name == "new_session"
        ]
        self.assertEqual(len(factories), 1)
        returns = [node for node in ast.walk(factories[0]) if isinstance(node, ast.Return)]
        self.assertEqual(len(returns), 1)
        call = returns[0].value
        self.assertIsInstance(call, ast.Call)
        self.assertIsInstance(call.func, ast.Name)
        self.assertEqual(call.func.id, "SshExecSession")
        self.assertEqual(len(call.args), 1)
        self.assertEqual(len(call.keywords), 1)
        self.assertEqual(call.keywords[0].arg, "deadline")

    def test_no_live_execution_on_import(self):
        source = (ROOT / "p3_kudu_cloudshell_parent.py").read_text("utf-8")
        tree = ast.parse(source)
        self.assertTrue(any(
            isinstance(node, ast.If)
            and isinstance(node.test, ast.Compare)
            and isinstance(node.test.left, ast.Name)
            and node.test.left.id == "__name__"
            for node in tree.body
        ))
        self.assertIn('AUTHORIZATION_LOCKED_NONDEPLOY', source)

if __name__ == "__main__":
    unittest.main()
