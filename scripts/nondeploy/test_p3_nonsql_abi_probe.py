"""Synthetic negative tests. Never calls Azure, SSH, Token, SQL or DDL."""
import contextlib
import io
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch, MagicMock
import p3_nonsql_abi_probe as p

class Tests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)

    def test_no_live_inputs(self):
        with patch.object(p,"fixed_bundle",side_effect=p.Hold):
            with contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(p.main(["--bundle",str(self.root),"--debs",str(self.root)]),1)
        self.assertIn("HOLD",output.getvalue())
        self.assertNotIn("ERROR",output.getvalue())

    def test_config_hash_guard(self):
        (self.root/"executor_config.json").write_text("{}")
        with self.assertRaises(p.Hold):
            p.fixed_bundle(self.root)

    def test_binding_index_requires_independent_sha(self):
        cfg = self.root / "executor_config.json"
        cfg.write_bytes(b"{}")
        binding = self.root / "binding-index.json"
        binding.write_text('{"contract":"FIELDVISIT_PATHB_P3_FINAL_BINDING_V1","files":{}}')
        with patch.object(p, "CFG_SHA", p.sha_file(cfg)):
            with self.assertRaises(p.Hold):
                p.fixed_bundle(self.root)

    def test_symlink_config_rejected(self):
        (self.root/"target").write_text("{}")
        (self.root/"executor_config.json").symlink_to("target")
        with self.assertRaises(p.Hold):
            p.fixed_bundle(self.root)

    def test_binary_hash_guard_before_extraction(self):
        debs=self.root/"debs";debs.mkdir()
        cfg={"tools":[{"name":name,"sha256":"0"*64} for name in p.EXPECTED_NAMES]}
        with patch.object(p.subprocess,"run") as run:
            with self.assertRaises(p.Hold):
                p.safe_extract_toolset(cfg,debs,self.root/"work")
            run.assert_not_called()

    def test_success_flow_mocked_no_remote(self):
        with patch.object(p,"fixed_bundle",return_value={}),patch.object(p,"machine_gate"),\
             patch.object(p,"safe_extract_toolset"),patch.object(p,"elf_gate"):
            with contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(p.main(["--bundle",str(self.root),"--debs",str(self.root)]),0)
            self.assertIn("PASS_STATIC_ONLY",output.getvalue())
            self.assertIn("NOT_RUN",output.getvalue())

    def test_elf_bad_architecture(self):
        cfg={"tool_files":{"f.so":"0"*64}}
        (self.root/"f.so").write_bytes(b"fake")
        cp=MagicMock(returncode=0,stdout=b"ELF64 AArch64")
        with patch.object(p.subprocess,"run",return_value=cp),self.assertRaises(p.Hold):
            p.elf_gate(self.root,cfg)

    def test_elf_no_dynamic_needs(self):
        cfg={"tool_files":{"f.so":"0"*64}}
        (self.root/"f.so").write_bytes(b"fake")
        cp=MagicMock(returncode=0,stdout=b"ELF64 X86-64")
        with patch.object(p.subprocess,"run",return_value=cp),self.assertRaises(p.Hold):
            p.elf_gate(self.root,cfg)

    def test_cleanup_on_failure(self):
        with patch.object(p,"fixed_bundle",return_value={}),patch.object(p,"machine_gate"),\
             patch.object(p,"safe_extract_toolset",side_effect=RuntimeError):
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(p.main(["--bundle",str(self.root),"--debs",str(self.root)]),1)
        self.assertFalse(list(Path("/tmp").glob("fv-p3-nonsql-abi-*")))

if __name__=="__main__":
    unittest.main()
