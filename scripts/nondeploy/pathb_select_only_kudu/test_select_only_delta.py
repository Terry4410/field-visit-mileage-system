import base64, hashlib, json, os, tempfile, time, unittest, uuid
from pathlib import Path
from unittest import mock

import p3_select_only_app as app
import p3_select_only_parent as parent
import p3_kudu_transport as transport


def jwt(claims):
    enc=lambda obj: base64.urlsafe_b64encode(json.dumps(obj,separators=(",",":")).encode()).rstrip(b"=").decode()
    return enc({"alg":"none"})+"."+enc(claims)+".x"


class DeltaTests(unittest.TestCase):
    def test_frozen_sql_hash(self):
        self.assertEqual(app.sha(app.QUERY.read_bytes()), "e87dbd54d4740254b6f0041fe330cc4af71e51a214964c39e1001c4437075add")

    def test_boundary_hash_unchanged(self):
        self.assertEqual(app.sha((app.ROOT/"p3_select_only_boundary.py").read_bytes()), "df402302afde3a99255fef652bb6ec28ca12831d55941aa84bd2ece8f0ed5e9c")

    def test_config_loads_and_has_no_marker(self):
        cfg=app.load_config(); self.assertNotIn("marker", json.dumps(cfg).lower()); self.assertEqual(cfg["server"], app.TARGET_SERVER)

    def test_owner_token_pass_and_ttl_hold(self):
        oid,tid="11111111-1111-1111-1111-111111111111","22222222-2222-2222-2222-222222222222"
        cfg={"owner_oid_sha256":app.sha(oid.encode()),"tenant_sha256":app.sha(tid.encode()),"minimum_token_ttl_seconds":3000}
        good=jwt({"oid":oid,"tid":tid,"aud":"https://database.windows.net/","nbf":90,"exp":4000})
        app.validate_token(good,cfg,now=100)
        with self.assertRaises(app.Hold): app.validate_token(good,cfg,now=1500)

    def test_parent_token_audience_hold(self):
        claims={"oid":"x","tid":"y","aud":"bad","nbf":0,"exp":9999999999}
        with self.assertRaises(parent.Hold): parent.validate_token(jwt(claims),now=100)

    def test_row_pass(self):
        identity=uuid.UUID("11111111-1111-1111-1111-111111111111")
        cfg={"owner_oid_sha256":app.sha(str(identity).encode())}
        fields=["FV_SQL",app.TARGET_SERVER,app.TARGET_DATABASE,"5",identity.bytes.hex(),"1","1","0","TRUE","FEDAUTH","TCP"]+["0"]*11
        self.assertEqual(app.parse_row(("|".join(fields)+"\n").encode(),cfg),"DATABASE_EMPTY_PASS")

    def test_row_partial_schema_holds(self):
        identity=uuid.UUID("11111111-1111-1111-1111-111111111111"); cfg={"owner_oid_sha256":app.sha(str(identity).encode())}
        fields=["FV_SQL",app.TARGET_SERVER,app.TARGET_DATABASE,"5",identity.bytes.hex(),"1","1","0","TRUE","FEDAUTH","TCP"]+["0"]*10+["1"]
        with self.assertRaises(app.Hold): app.parse_row(("|".join(fields)+"\n").encode(),cfg)

    def test_exact_stage_has_only_four_members(self):
        path,raw,hashes=parent.exact_stage(app.ROOT)
        try:
            import zipfile,io
            with zipfile.ZipFile(io.BytesIO(raw)) as z: self.assertEqual(set(z.namelist()),set(hashes))
        finally: path.unlink(missing_ok=True)

    def test_authorization_exact_match(self):
        record={"contract":"FIELDVISIT_PATHB_SELECT_ONLY_KUDU_V1","approved":True,"distribution_sha256":"a"*64,"server":parent.SERVER,"database":parent.DATABASE,"max_sql_calls":1}
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/"a.json"; p.write_text(json.dumps(record)); parent.load_authorization(p,"a"*64)
            record["max_sql_calls"]=2; p.write_text(json.dumps(record))
            with self.assertRaises(parent.Hold): parent.load_authorization(p,"a"*64)

    def test_cleanup_is_exact_root_and_hash_bound(self):
        command=parent.cleanup_command({"x":"a"*64})
        self.assertIn(parent.REMOTE_STAGE.encode(),base64.b64decode(command.split(b"'")[1]))
        self.assertEqual(hashlib.sha256(app.RAM_RECEIPT).hexdigest(), parent.RAM_RECEIPT_SHA256)

    def test_no_live_entry_without_authorization(self):
        with mock.patch("sys.argv",["p"]): self.assertEqual(parent.main(),2)

    def test_installer_entry_is_disabled(self):
        with self.assertRaises(transport.TransportHold): transport.runner_command("a"*64)

    def test_live_sources_have_no_storage_blob_marker_or_installer_call(self):
        text=(app.ROOT/"p3_select_only_app.py").read_text()+ (app.ROOT/"p3_select_only_parent.py").read_text()
        for forbidden in ("get-access-token --resource https://storage.azure.com", "p3_app_installer_runner.py --execute", "once_marker", "blob marker"):
            self.assertNotIn(forbidden, text.lower())


if __name__ == "__main__": unittest.main()
