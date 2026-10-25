import sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import uat010_catalog_facets as f
class TestFacets(unittest.TestCase):
 @classmethod
 def setUpClass(cls):cls.parts,cls.pins=f.inputs()
 def test_expected_parts(self):self.assertEqual(len(self.parts),6);self.assertIn("sys.triggers",self.parts[4])
 def test_extra_facet_denied(self):
  s=(f.ROOT/f.CATALOG).read_text()+" + COALESCE((SELECT 1 FOR JSON PATH),N'[]')"
  with self.assertRaises(f.Hold):f.split(s)
 def test_malformed_reference_denied(self):
  rows=[{"Facet":n,"Digest":"A"*64} for n in f.NAMES]
  with self.assertRaises(f.Hold):f.seal(rows[:-1],self.pins)
  with self.assertRaises(f.Hold):f.seal(rows+rows[:1],self.pins)
 def test_no_live_digest_or_identity_leak(self):
  ref=f.seal([{"Facet":n,"Digest":"A"*64} for n in f.NAMES],self.pins)
  q=f.query(self.parts,self.pins,ref)
  self.assertIn("MISMATCH_HOLD",q)
  self.assertIn("REFERENCE_INCOMPLETE_HOLD",q)
  self.assertNotIn(" AS Digest",q)
  self.assertNotIn("ORIGINAL_LOGIN",q)
 def test_synthetic_cannot_be_approved(self):
  ref=f.seal([{"Facet":n,"Digest":"A"*64} for n in f.NAMES],self.pins)
  for k,v in (("g1","PASS"),("full63TableVerified",True),("catalogSha256","0"*64)):
   bad=dict(ref);bad[k]=v
   with self.assertRaises(f.Hold):f.query(self.parts,self.pins,bad)
 def test_offline_guard(self):self.assertIn("G1_FACET_OFFLINE_ONLY",f.query(self.parts,self.pins))
if __name__=="__main__":unittest.main()
