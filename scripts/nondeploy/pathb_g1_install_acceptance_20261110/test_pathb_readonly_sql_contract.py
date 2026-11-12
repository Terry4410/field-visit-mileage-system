"""Synthetic-only tests of the isolated SQL argument contract."""
import ast
import unittest
from pathlib import Path
import pathb_readonly_sql_contract as contract

class ReadonlySQLContractTests(unittest.TestCase):
    def test_exact_pinned_query(self):
        raw=(Path(contract.__file__).parent / 'preflight.sql').read_bytes()
        args=contract.sqlcmd_argv(Path('/verified/sqlcmd'), Path('/ram/sql-token'), raw)
        self.assertEqual(args[args.index('-S')+1],contract.SERVER)
        self.assertEqual(args[args.index('-d')+1],contract.DATABASE)
        self.assertNotIn('--execute',args)
    def test_modified_sql_holds(self):
        with self.assertRaises(contract.ReadonlyHold):
            contract.sqlcmd_argv(Path('/verified/sqlcmd'),Path('/ram/sql-token'),b'SELECT 1;')
    def test_unpinned_paths_hold(self):
        raw=(Path(contract.__file__).parent / 'preflight.sql').read_bytes()
        for binary,token in [(Path('sqlcmd'),Path('/ram/sql-token')),
                             (Path('/verified/bash'),Path('/ram/sql-token')),
                             (Path('/verified/sqlcmd'),Path('sql-token')),
                             (Path('/verified/sqlcmd'),Path('/ram/other'))]:
            with self.subTest(binary=binary,token=token),self.assertRaises(contract.ReadonlyHold):
                contract.sqlcmd_argv(binary,token,raw)
    def test_no_runtime_capabilities(self):
        tree=ast.parse(Path(contract.__file__).read_text())
        imports=set()
        for n in ast.walk(tree):
            if isinstance(n,ast.Import):imports.update(a.name for a in n.names)
            if isinstance(n,ast.ImportFrom):imports.add(n.module)
        self.assertEqual(imports,{'pathlib','hashlib'})
        self.assertNotIn('subprocess',Path(contract.__file__).read_text())

if __name__=='__main__':unittest.main()
