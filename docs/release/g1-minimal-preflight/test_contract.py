import copy,unittest
import validate as v
class Contract(unittest.TestCase):
 def setUp(self):
  self.m=v.pins();self.e=dict(contract='G1-MINIMAL-REVIEW-3',source=v.SOURCE,rc=v.RC,pins=self.m['files'],groups=[[dict(code=c,status='PASS') for c in v.GROUPS[0]]])
 def reject(self,e):
  with self.assertRaises((ValueError,TypeError)):v.validate(e,self.m)
 def test_unknown_stops(self):
  self.e['groups'][0][0]['status']='UNKNOWN';self.assertEqual(v.validate(self.e,self.m),'UNKNOWN')
 def test_fail_stops(self):
  self.e['groups'][0][0]['status']='FAIL';self.assertEqual(v.validate(self.e,self.m),'FAIL')
 def test_missing_group(self): self.reject(self.e)
 def test_wrong_source(self):self.e['source']='0'*40;self.reject(self.e)
 def test_wrong_pin(self):self.e['pins']={};self.reject(self.e)
 def test_extra_metadata(self):self.e['groups'][0][0]['identity']='SYNTHETIC_PRIVATE';self.reject(self.e)
 def test_duplicate_code(self):self.e['groups'][0][0]['code']=self.e['groups'][0][1]['code'];self.reject(self.e)
 def test_unknown_code(self):self.e['groups'][0][0]['code']='UNREVIEWED';self.reject(self.e)
 def test_status_injection(self):self.e['groups'][0][0]['status']='PASS\nPRIVATE';self.reject(self.e)
 def test_after_stop(self):self.e['groups'][0][0]['status']='FAIL';self.e['groups'].append([dict(code='MARKER_010',status='PASS')]);self.reject(self.e)
 def test_no_false_full_pass(self):
  self.e['groups']=[[dict(code=c,status='PASS') for c in g] for g in v.GROUPS];self.reject(self.e)
 def test_duplicate_json(self):
  with self.assertRaises(ValueError):v.pairs([('code','A'),('code','B')])
 def test_empty(self):self.e['groups']=[];self.reject(self.e)
 def test_semantics_unknown(self):
  self.e['groups']=[[dict(code=c,status='UNKNOWN' if c in v.UNPROVEN else 'PASS') for c in g] for g in v.GROUPS]
  self.assertEqual(v.validate(self.e,self.m),'UNKNOWN')
if __name__=='__main__':unittest.main()
