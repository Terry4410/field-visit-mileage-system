import json
import unittest
import g1_blob_call_journal_REVIEW_ONLY as l
import g1_readonly_recovery_REVIEW_ONLY as r
from p3_install_gates import PAYLOAD_SHA256, RELEASE, SERVER, DATABASE, INSTALLER_SHA
from p3_blob_guard import CONTRACT, RUN_ID
OP='a'*32; ART='b'*64; INST='c'*64; FP='d'*64

def fixture():
    claim=json.dumps({'contract':CONTRACT,'operation_id':OP,'artifact_sha256':ART,
      'instance_sha256':INST,'release':RELEASE,'server':SERVER,'database':DATABASE,
      'installer_sha256':INSTALLER_SHA,'run_id':RUN_ID,
      'status':'CONSUMED_BEFORE_INSTALLER_SQL'}).encode()
    cs=r.verify_original_claim(claim,OP,ART,INST)
    records={}
    for i,sha in enumerate(PAYLOAD_SHA256):
        for ph,exit in [('BEFORE',None),('AFTER','SUCCESS')]:
            obj=l.construct(OP,cs,i,ph,sha,exit)
            records[obj.key]=obj.payload
    terminal=l.construct_postcheck(OP,cs,FP)
    records[terminal.key]=terminal.payload
    return claim,records

class RecoveryTests(unittest.TestCase):
    def test_all_success_strict_schema(self):
        claim,rec=fixture()
        get=lambda method,key,_body,_headers:(200,'etag',rec[key]) if key in rec else (404,'',b'')
        out=r.read_progress(OP,claim,ART,INST,get)
        self.assertEqual(out['status'],'SQL_SCHEMA_VERIFIED_EVIDENCE')
        self.assertEqual(out['externally_confirmed_successful_calls'],16)
    def test_lost_before_never_means_no_SQL(self):
        claim,rec=fixture()
        del rec[l.construct(OP,r.verify_original_claim(claim,OP,ART,INST),1,'BEFORE',PAYLOAD_SHA256[1]).key]
        get=lambda method,key,_body,_headers:(200,'etag',rec[key]) if key in rec else (404,'',b'')
        out=r.read_progress(OP,claim,ART,INST,get)
        self.assertEqual(out['status'],'HOLD_IRREVERSIBLE_OR_UNKNOWN')
        self.assertEqual(out['externally_confirmed_successful_calls'],1)
        self.assertEqual(out['sql_replay'],'FORBIDDEN')
    def test_lost_after_must_hold(self):
        claim,rec=fixture()
        del rec[l.construct(OP,r.verify_original_claim(claim,OP,ART,INST),0,'AFTER',PAYLOAD_SHA256[0],'SUCCESS').key]
        get=lambda method,key,_body,_headers:(200,'etag',rec[key]) if key in rec else (404,'',b'')
        self.assertEqual(r.read_progress(OP,claim,ART,INST,get)['status'],'HOLD_IRREVERSIBLE_OR_UNKNOWN')
    def test_missing_postcheck_is_not_schema_pass(self):
        claim,rec=fixture();del rec[l.construct_postcheck(OP,r.verify_original_claim(claim,OP,ART,INST),FP).key]
        get=lambda method,key,_body,_headers:(200,'etag',rec[key]) if key in rec else (404,'',b'')
        self.assertEqual(r.read_progress(OP,claim,ART,INST,get)['status'],'HOLD_POSTCHECK_UNKNOWN')
    def test_wrong_claim_hash_or_target_fails(self):
        claim,rec=fixture()
        with self.assertRaises(r.RecoveryHold):r.verify_original_claim(claim,OP,ART,'e'*64)
    def test_wrong_postcheck_data_fails(self):
        claim,rec=fixture()
        post=l.construct_postcheck(OP,r.verify_original_claim(claim,OP,ART,INST),FP)
        rec[post.key]=rec[post.key].replace(b'"tables":69',b'"tables":68')
        get=lambda method,key,_body,_headers:(200,'etag',rec[key]) if key in rec else (404,'',b'')
        self.assertEqual(r.read_progress(OP,claim,ART,INST,get)['status'],'HOLD_POSTCHECK_UNKNOWN')
    def test_no_network_entrypoint(self):self.assertEqual(r.main(),1)

if __name__=='__main__':unittest.main()
