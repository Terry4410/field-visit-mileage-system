"""Offline tests for the authorization-locked NON-SQL transport. No Azure/SSH/SQL."""
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch
import zipfile

import p3_nonsql_ssh_transport as t


class FakeTunnel:
    calls = []
    def __init__(self, subscription, instance, port):
        self.subscription, self.instance, self.port = subscription, instance, port
        self.password = bytearray(b"synthetic-only")
        self.calls.append((subscription, instance, port))
    def __enter__(self): return self
    def __exit__(self, *_):
        for i in range(len(self.password)): self.password[i] = 0


class FakeSSH:
    calls = []
    receipts = []
    def __init__(self, argv, password, deadline):
        self.index = len(self.calls); self.sent = []
        self.calls.append(self)
    def login(self, operation_id): self.operation_id = operation_id
    def send(self, data): self.sent.append(data)
    def expect(self, pattern, seconds): return self.receipts[self.index]
    def close(self): pass


class Tests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.handler = b"handler exact bytes"
        self.probe = b"probe exact bytes"
        self.stage = b"stage exact bytes"
        self.binding = {
            "contract":"FIELDVISIT_P3_NONSQL_TRANSPORT_BINDING_V1",
            "approved_release":t.RELEASE,"resource_group":t.RG,"app":t.APP,
            "max_duration_seconds":600,"max_live_transport_attempts":1,
            "max_ssh_sessions":2,"sql_token_select_ddl_blob_marker":"FORBIDDEN",
            "transport_file":"p3_nonsql_ssh_transport.py",
            "authorization_pins":["transport_sha256","binding_sha256","handoff_sha256","instance_id_sha256"],
            "files":{
                "p3_nonsql_stage_handler.py":{"sha256":t.sha256(self.handler),"git_blob_sha1":t.git_blob_sha1(self.handler)},
                "p3_nonsql_abi_probe.py":{"sha256":t.sha256(self.probe),"git_blob_sha1":t.git_blob_sha1(self.probe)}}}
        self.handoff = self.make_handoff()

    def make_handoff(self, *, manifest_delta=None, extra=None):
        manifest={"contract":t.HANDOFF_CONTRACT,"live_execution_authorized":False,
                  "stage_sha256":t.sha256(self.stage),"remote_entrypoint":"p3_nonsql_stage_handler.py",
                  "remote_sql_token_ddl":"FORBIDDEN","scripts":{}}
        for name, raw in (("p3_nonsql_stage_handler.py",self.handler),("p3_nonsql_abi_probe.py",self.probe)):
            manifest["scripts"][name]={"sha256":t.sha256(raw),"git_blob_sha1":t.git_blob_sha1(raw)}
        if manifest_delta: manifest.update(manifest_delta)
        buf=io.BytesIO()
        with zipfile.ZipFile(buf,"w",zipfile.ZIP_STORED) as z:
            z.writestr("manifest.json",json.dumps(manifest,separators=(",",":")))
            z.writestr("stage.zip",self.stage)
            z.writestr("p3_nonsql_stage_handler.py",self.handler)
            z.writestr("p3_nonsql_abi_probe.py",self.probe)
            if extra: z.writestr(extra,b"x")
        return buf.getvalue()

    def write_json(self, name, obj):
        path=self.root/name; path.write_text(json.dumps(obj,separators=(",",":"))); return path

    def test_default_is_authorization_locked(self):
        with contextlib.redirect_stdout(io.StringIO()) as out:
            self.assertEqual(t.main([]),0)
        self.assertEqual(out.getvalue(),"AUTHORIZATION_LOCKED_NONDEPLOY\n")

    def test_handoff_sender_receiver_binding(self):
        result=t.inspect_handoff(self.handoff,self.binding)
        self.assertEqual(result["stage_sha256"],t.sha256(self.stage))

    def test_handoff_tamper_and_extra_member_hold(self):
        with self.assertRaises(t.Hold): t.inspect_handoff(self.make_handoff(extra="owner-authorization.json"),self.binding)
        bad=dict(self.binding); bad["files"]={k:dict(v) for k,v in self.binding["files"].items()}
        bad["files"]["p3_nonsql_abi_probe.py"]["sha256"]="0"*64
        with self.assertRaises(t.Hold): t.inspect_handoff(self.handoff,bad)

    def test_binding_rejects_scope_expansion(self):
        t.validate_binding(self.binding)
        bad=dict(self.binding);bad["sql_token_select_ddl_blob_marker"]="ALLOWED"
        with self.assertRaises(t.Hold): t.validate_binding(bad)

    def test_authorization_exact_hash_instance_and_expiry(self):
        transport=Path(t.__file__); binding=self.write_json("binding.json",self.binding)
        handoff=self.root/"handoff.zip"; handoff.write_bytes(self.handoff)
        instance="/subscriptions/s/resourceGroups/r/providers/Microsoft.Web/sites/a/instances/i"
        now=int(time.time())
        auth={"decision":t.AUTH_DECISION,"approved_release":t.RELEASE,"resource_group":t.RG,"app":t.APP,
              "operation_id":"a"*32,"max_duration_seconds":600,"max_live_transport_attempts":1,
              "expires_at_epoch":now+600,"transport_sha256":t.sha256(transport.read_bytes()),
              "binding_sha256":t.sha256(binding.read_bytes()),"handoff_sha256":t.sha256(self.handoff),
              "instance_id_sha256":t.sha256(instance.lower().encode())}
        t.validate_authorization(auth,transport,binding,self.handoff,instance,now)
        auth["expires_at_epoch"]=now-1
        with self.assertRaises(t.Hold): t.validate_authorization(auth,transport,binding,self.handoff,instance,now)

    def test_exact_receipt_or_hold(self):
        auth={"operation_id":"b"*32,"instance_id_sha256":"c"*64}
        expected={"contract":t.RECEIPT_CONTRACT,"operation_id":auth["operation_id"],
                  "instance_id_sha256":auth["instance_id_sha256"],"handoff_sha256":"d"*64,
                  "stage_sha256":"e"*64,"status":"PASS_STATIC_ONLY","cleanup":"PASS",
                  "sql_token_select_ddl_blob_marker":"NOT_RUN"}
        line=t.RECEIPT_PREFIX+json.dumps(expected,separators=(",",":"),sort_keys=True).encode()
        self.assertEqual(t.parse_receipt(line,auth,"d"*64,"e"*64),expected)
        expected["cleanup"]="UNKNOWN"
        line=t.RECEIPT_PREFIX+json.dumps(expected,separators=(",",":"),sort_keys=True).encode()
        with self.assertRaises(t.Hold): t.parse_receipt(line,auth,"d"*64,"e"*64)

    def test_once_journal_prevents_replay(self):
        journal=self.root/"once.json"
        t.consume_journal(journal,"f"*32,"1"*64,"2"*64)
        with self.assertRaises((t.Hold,FileExistsError)):
            t.consume_journal(journal,"f"*32,"1"*64,"2"*64)
        t.update_journal(journal,"PASS_STATIC_ONLY")
        self.assertEqual(json.loads(journal.read_text())["status"],"PASS_STATIC_ONLY")

    def remote_frame(self, handoff, operation):
        lines=[f"FV_PAYLOAD {len(handoff)} {operation}\n".encode()]
        for offset in range(0,len(handoff),t.TRANSFER_CHUNK):
            import base64
            lines.append(base64.b64encode(handoff[offset:offset+t.TRANSFER_CHUNK])+b"\n")
        lines.append(b"FV_PAYLOAD_END\n")
        return b"".join(lines)

    def test_real_remote_bootstrap_receipt_and_cleanup(self):
        operation=hashlib.sha256(str(self.root).encode()).hexdigest()[:32]
        root=Path(t.REMOTE_PREFIX+operation);self.assertFalse(root.exists())
        handler=b"import sys\nprint('P3_NONSQL_STAGE=PASS_STATIC_ONLY')\nprint('SSH_TOKEN_SQL_MARKER=NOT_RUN')\n"
        old_handler=self.handler;self.handler=handler
        binding=self.binding["files"]["p3_nonsql_stage_handler.py"]
        binding["sha256"]=t.sha256(handler);binding["git_blob_sha1"]=t.git_blob_sha1(handler)
        handoff=self.make_handoff();self.handler=old_handler
        cp=subprocess.run([sys.executable,"-c",t.REMOTE_BOOTSTRAP,str(root),t.sha256(handoff),operation,"a"*64],
                          input=self.remote_frame(handoff,operation),capture_output=True,timeout=30,check=False)
        self.assertEqual(cp.returncode,0);self.assertEqual(cp.stderr,b"");self.assertFalse(root.exists())
        line=cp.stdout.strip();self.assertTrue(line.startswith(t.RECEIPT_PREFIX))
        receipt=json.loads(line.split(b"=",1)[1]);self.assertEqual(receipt["status"],"PASS_STATIC_ONLY")
        self.assertEqual(receipt["cleanup"],"PASS")

    def test_remote_bootstrap_failure_is_hold_and_cleanup(self):
        operation=hashlib.sha256((str(self.root)+"fail").encode()).hexdigest()[:32]
        root=Path(t.REMOTE_PREFIX+operation);self.assertFalse(root.exists())
        cp=subprocess.run([sys.executable,"-c",t.REMOTE_BOOTSTRAP,str(root),t.sha256(self.handoff),operation,"b"*64],
                          input=self.remote_frame(self.handoff,operation),capture_output=True,timeout=30,check=False)
        self.assertEqual(cp.returncode,0);self.assertEqual(cp.stderr,b"");self.assertFalse(root.exists())
        receipt=json.loads(cp.stdout.strip().split(b"=",1)[1])
        self.assertEqual(receipt["status"],"HOLD");self.assertEqual(receipt["cleanup"],"PASS")

    def test_transport_source_has_no_live_sql_or_token_command(self):
        source=Path(t.__file__).read_text(encoding="utf-8")
        for forbidden in ("get-access-token","storage blob","sql server","p3_app_installer_runner.py --execute"):
            self.assertNotIn(forbidden,source)

    def prepare_execute(self):
        binding=self.write_json("binding.json",self.binding)
        handoff=self.root/"handoff.zip";handoff.write_bytes(self.handoff)
        instance="/subscriptions/s/resourceGroups/r/providers/Microsoft.Web/sites/a/instances/i"
        operation="1"*32; now=int(time.time())
        auth={"decision":t.AUTH_DECISION,"approved_release":t.RELEASE,"resource_group":t.RG,"app":t.APP,
              "operation_id":operation,"max_duration_seconds":600,"max_live_transport_attempts":1,
              "expires_at_epoch":now+600,"transport_sha256":t.sha256(Path(t.__file__).read_bytes()),
              "binding_sha256":t.sha256(binding.read_bytes()),"handoff_sha256":t.sha256(self.handoff),
              "instance_id_sha256":t.sha256(instance.lower().encode())}
        authorization=self.write_json("auth.json",auth)
        return binding,handoff,authorization,self.root/"journal.json",instance,auth

    def test_integrated_positive_two_sessions_no_live_calls(self):
        binding,handoff,authorization,journal,instance,auth=self.prepare_execute()
        receipt={"contract":t.RECEIPT_CONTRACT,"operation_id":auth["operation_id"],
                 "instance_id_sha256":auth["instance_id_sha256"],"handoff_sha256":auth["handoff_sha256"],
                 "stage_sha256":t.sha256(self.stage),"status":"PASS_STATIC_ONLY","cleanup":"PASS",
                 "sql_token_select_ddl_blob_marker":"NOT_RUN"}
        FakeTunnel.calls=[];FakeSSH.calls=[]
        FakeSSH.receipts=[t.RECEIPT_PREFIX+json.dumps(receipt,sort_keys=True,separators=(",",":")).encode(),
                          b'FV_P3_NONSQL_CLEANUP={"operation_id":"'+auth["operation_id"].encode()+b'","status":"PASS"}']
        with patch.object(t,"management_preflight",return_value="instance-name"),\
             patch.object(t,"Tunnel",FakeTunnel),patch.object(t,"known_host"),patch.object(t,"SSH",FakeSSH),\
             patch.object(t,"SUB_SHA",t.sha256(b"sub")),contextlib.redirect_stdout(io.StringIO()) as out:
            rc=t.main(["--execute","--binding",str(binding),"--handoff",str(handoff),
                       "--authorization",str(authorization),"--journal",str(journal),
                       "--subscription","sub","--instance-id",instance])
        self.assertEqual(rc,0);self.assertEqual(len(FakeTunnel.calls),2);self.assertEqual(len(FakeSSH.calls),2)
        self.assertEqual(json.loads(journal.read_text())["status"],"PASS_STATIC_ONLY")
        self.assertIn("TOKEN_SQL_SELECT_DDL_BLOB_MARKER=NOT_RUN",out.getvalue())

    def test_unknown_receipt_stops_and_cleanup_is_not_replay(self):
        binding,handoff,authorization,journal,instance,auth=self.prepare_execute()
        FakeTunnel.calls=[];FakeSSH.calls=[]
        FakeSSH.receipts=[b'FV_P3_NONSQL_RECEIPT={"status":"UNKNOWN"}',
                          b'FV_P3_NONSQL_CLEANUP={"operation_id":"'+auth["operation_id"].encode()+b'","status":"PASS"}']
        with patch.object(t,"management_preflight",return_value="instance-name"),\
             patch.object(t,"Tunnel",FakeTunnel),patch.object(t,"known_host"),patch.object(t,"SSH",FakeSSH),\
             patch.object(t,"SUB_SHA",t.sha256(b"sub")),contextlib.redirect_stdout(io.StringIO()) as out:
            rc=t.main(["--execute","--binding",str(binding),"--handoff",str(handoff),
                       "--authorization",str(authorization),"--journal",str(journal),
                       "--subscription","sub","--instance-id",instance])
        self.assertEqual(rc,1);self.assertEqual(len(FakeTunnel.calls),2);self.assertEqual(len(FakeSSH.calls),2)
        self.assertEqual(json.loads(journal.read_text())["status"],"ATTEMPTED")
        self.assertIn("P3_NONSQL_TRANSPORT=HOLD",out.getvalue())

    def test_management_requires_one_exact_instance(self):
        account={"id":"sub","tenantId":"tenant","user":{"type":"user"}}
        user={"id":"owner"};app={"name":t.APP,"state":"Running"}
        instance={"id":"/x/instances/i","name":"instance-name"}
        def fake_az(args,timeout=60):
            if args[:2]==["account","show"]:return account
            if args[:3]==["ad","signed-in-user","show"]:return user
            if args[:2]==["webapp","show"]:return app
            if args[:2]==["webapp","list-instances"]:return [instance]
            raise AssertionError(args)
        with patch.object(t,"az",side_effect=fake_az),patch.object(t,"run",return_value=b"--instance --port --timeout"),\
             patch.object(t,"SUB_SHA",t.sha256(b"sub")),patch.object(t,"TENANT_SHA",t.sha256(b"tenant")),\
             patch.object(t,"OWNER_SHA",t.sha256(b"owner")):
            self.assertEqual(t.management_preflight("sub",instance["id"]),"instance-name")
        with patch.object(t,"az",side_effect=lambda args,timeout=60: [instance,instance] if args[:2]==["webapp","list-instances"] else fake_az(args,timeout)),\
             patch.object(t,"run",return_value=b"--instance --port --timeout"),patch.object(t,"SUB_SHA",t.sha256(b"sub")),\
             patch.object(t,"TENANT_SHA",t.sha256(b"tenant")),patch.object(t,"OWNER_SHA",t.sha256(b"owner")),self.assertRaises(t.Hold):
            t.management_preflight("sub",instance["id"])


if __name__ == "__main__": unittest.main()
