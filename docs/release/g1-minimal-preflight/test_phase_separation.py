"""Only new dependency tests; accepted14 cases are not rerun for totals."""
import copy,unittest
import validate as v
class Phases(unittest.TestCase):
 def setUp(self):
  self.m=v.pins()
  self.e=dict(contract='G1-MINIMAL-REVIEW-3',source=v.SOURCE,rc=v.RC,pins=self.m['files'],groups=[[
   dict(code=c,status='UNKNOWN' if c in v.UNPROVEN else 'PASS') for c in g] for g in v.GROUPS])
 def test_catalog_unknown_permission_observed_overall_unknown(self):
  self.assertEqual(v.validate(self.e,self.m),'UNKNOWN')
 def test_catalog_known_fail_permission_observed_overall_fail(self):
  self.e['groups'][2][3]['status']='FAIL';self.assertEqual(v.validate(self.e,self.m),'FAIL')
 def test_wrong_target_stops_before_marker(self):
  self.e['groups'][0][0]['status']='FAIL'
  with self.assertRaises(ValueError):v.validate(self.e,self.m)
 def test_marker_unknown_stops_before_catalog(self):
  self.e['groups'][1][0]['status']='UNKNOWN'
  with self.assertRaises(ValueError):v.validate(self.e,self.m)
 def test_malformed_catalog_stops_before_permission(self):
  self.e['groups'][2][0]['code']='PRIVATE_UNREVIEWED'
  with self.assertRaises(ValueError):v.validate(self.e,self.m,False)
 def test_prefix_valid_final_incomplete_refused(self):
  self.e['groups']=self.e['groups'][:3]
  self.assertEqual(v.validate(self.e,self.m,False),'UNKNOWN')
  with self.assertRaises(ValueError):v.validate(self.e,self.m)
 def test_unknown_cannot_be_promoted(self):
  self.e['groups'][2][0]['status']='PASS'
  with self.assertRaises(ValueError):v.validate(self.e,self.m)
 def test_extra_resultset_refused(self):
  self.e['groups'].append([])
  with self.assertRaises(ValueError):v.validate(self.e,self.m)
if __name__=='__main__':unittest.main()
