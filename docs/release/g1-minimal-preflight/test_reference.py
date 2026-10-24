import json,pathlib,unittest
class Reference(unittest.TestCase):
 def setUp(self):self.r=json.loads((pathlib.Path(__file__).parent/'necessary-reference.json').read_text())
 def test_exact_known_facets(self):
  c=next(x for x in self.r['columns'] if x['table']=='ChangeRequests' and x['column']=='EntityKind')
  self.assertEqual((c['type'],c['max_length'],c['is_nullable']),('nvarchar',80,0))
 def test_rowversion_facet(self):
  c=next(x for x in self.r['columns'] if x['table']=='LocationCreateReservations' and x['column']=='RowVersion')
  self.assertEqual((c['type'],c['max_length'],c['is_identity']),('timestamp',8,0))
 def test_final_constraint_revision(self):
  c=next(x for x in self.r['checks'] if x['name']=='CK_OptionC_Published_Origin')
  self.assertEqual(c['migration'],'017');self.assertIn('VersionNumber=1 OR',c['definition'])
 def test_unique_keys(self):
  c=next(x for x in self.r['indexes'] if x['name']=='UX_OptionC_Published_Number')
  self.assertEqual(c['keys'],['LocationId','VersionNumber']);self.assertTrue(c['unique'])
 def test_fk_mapping(self):
  c=next(x for x in self.r['foreignKeys'] if x['name']=='FK_OptionC_Stage_Request')
  self.assertEqual((c['columns'],c['referencedTable'],c['referencedColumns']),(['ChangeRequestId'],'ChangeRequests',['ChangeRequestId']))
 def test_no_ledger_prerequisite(self):
  self.assertNotIn('FieldVisitMigrationLedger',json.dumps(self.r))
 def test_dropped_015_constraint_not_required_after_016(self):
  self.assertNotIn('CK_B3_ChangeRequestEvents_StageEvidenceEventType',{x['name'] for x in self.r['checks']})
  c=next(x for x in self.r['checks'] if x['name']=='CK_B3_ChangeRequestEvents_DecisionState')
  self.assertEqual(c['migration'],'018')
if __name__=='__main__':unittest.main()
