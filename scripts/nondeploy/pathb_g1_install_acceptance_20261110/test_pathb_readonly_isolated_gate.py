"""No external services: isolated readonly gate contract tests."""
import ast
import json
import shutil
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import pathb_readonly_isolated_gate as gate

class IsolatedGateTests(unittest.TestCase):
    def test_real_pins(self):
        self.assertEqual(gate.verify()['PINNED_SQL'], 'PASS')
    def test_cli_offline_only(self):
        self.assertEqual(gate.main(['--offline']), 0)
        with self.assertRaises(SystemExit):
            gate.main([])
        with self.assertRaises(SystemExit):
            gate.main(['--live'])
    def test_no_executable_side_effect_imports(self):
        source = Path(gate.__file__).read_text()
        tree = ast.parse(source)
        imports = {alias.name.split('.')[0] for node in ast.walk(tree)
                   if isinstance(node, (ast.Import, ast.ImportFrom))
                   for alias in (node.names if isinstance(node, ast.Import) else [ast.alias(name=node.module or '')])}
        self.assertEqual(imports, {'argparse', 'hashlib', 'json', 're', 'pathlib'})
        self.assertFalse(any(isinstance(n, ast.Call) and isinstance(n.func, ast.Attribute)
                             and n.func.attr in {'run', 'Popen', 'connect', 'request', 'system'}
                             for n in ast.walk(tree)))
    def test_tampered_sql_or_target_holds(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            for name in ('preflight.sql','binding-index.json','executor_config.json'):
                shutil.copyfile(gate.HERE / name, root / name)
            (root/'preflight.sql').write_text('SELECT 1;')
            with self.assertRaises(gate.GateHold):gate.verify(root)
            shutil.copyfile(gate.HERE/'preflight.sql',root/'preflight.sql')
            cfg=json.loads((root/'executor_config.json').read_text())
            cfg['server']='other.example'
            (root/'executor_config.json').write_text(json.dumps(cfg))
            with self.assertRaises(gate.GateHold):gate.verify(root)
    def test_no_network_even_if_invoked(self):
        with patch('socket.socket',side_effect=AssertionError('network forbidden')):
            self.assertEqual(gate.verify()['SQL_CALLS'],'0')

if __name__=='__main__':unittest.main()
