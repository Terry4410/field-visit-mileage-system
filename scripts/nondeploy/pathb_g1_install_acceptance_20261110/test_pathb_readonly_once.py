"""Synthetic-only tests for single-use NONDEPLOY orchestration."""
import ast
import unittest
from pathlib import Path
from pathb_readonly_once import evaluate_once, ExecutionHold

def fixture():
    fields=['FV_SQL','synthetic-server','db-fieldvisit-pathb-uat','5',
            'AABBCC','0','1','0','TRUE','FEDAUTH','TCP']+['0']*11
    return ('|'.join(fields)+'\n').encode()

class OnceTests(unittest.TestCase):
    def test_single_injection_and_redacted_receipt(self):
        seen=[]
        def fake(argv, sql):
            seen.append((argv,sql))
            return fixture()
        result=evaluate_once(fake,Path('/verified/sqlcmd'),Path('/ram/sql-token'))
        self.assertEqual(len(seen),1)
        self.assertEqual(result.schema,'EMPTY_CANDIDATE')
        self.assertNotIn('AABBCC',str(result))
        self.assertEqual(seen[0][0][0],'/verified/sqlcmd')
    def test_failure_is_hold_without_retry(self):
        seen=[]
        def fail(argv,sql):
            seen.append(1)
            raise RuntimeError('PRIVATE SECRET')
        with self.assertRaises(ExecutionHold) as caught:
            evaluate_once(fail,Path('/verified/sqlcmd'),Path('/ram/sql-token'))
        self.assertEqual(len(seen),1)
        self.assertNotIn('PRIVATE SECRET',str(caught.exception))
    def test_bad_pins_prevent_invoke(self):
        seen=[]
        def fake(argv,sql):
            seen.append(1)
            return fixture()
        with self.assertRaises(ExecutionHold):
            evaluate_once(fake,Path('/verified/sqlcmd'),Path('/ram/sql-token'),Path('/nonexistent'))
        self.assertEqual(seen,[])
    def test_no_live_import_or_entrypoint(self):
        import pathb_readonly_once as mod
        tree=ast.parse(Path(mod.__file__).read_text())
        imports=set()
        for node in ast.walk(tree):
            if isinstance(node,ast.Import):imports.update(a.name for a in node.names)
            if isinstance(node,ast.ImportFrom):imports.add(node.module)
        self.assertEqual(imports,{'pathlib','pathb_readonly_isolated_gate',
                                  'pathb_readonly_sql_contract','pathb_readonly_receipt_parser'})

if __name__=='__main__':unittest.main()
