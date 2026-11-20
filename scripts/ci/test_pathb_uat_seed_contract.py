import hashlib,re,unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
SQL=(ROOT/'database/uat/FieldVisit_PathB_UAT_ThreeRole_Seed_FINAL_CANDIDATE.sql').read_text()
READBACK=(ROOT/'database/uat/FieldVisit_PathB_UAT_ThreeRole_Seed_READBACK.sql').read_text()

class Contract(unittest.TestCase):
 def test_source_binding(self): self.assertIn('d6c220877b9976b6478d85b5171d4e06293595ae',SQL)
 def test_exact_accounts(self):
  for a in ('pilotv01','pilotl01','pilota01'): self.assertIn(a,SQL)
  self.assertNotRegex(SQL.lower(),r'pilotv03|pilotl03|pilots02|pilots04')
 def test_exact_target_tls(self):
  for x in ('sql-fieldvisit-jpe-pathb-uat','db-fieldvisit-pathb-uat','encrypt_option'): self.assertIn(x,SQL)
 def test_transaction_and_collision(self):
  for x in ('SERIALIZABLE','sp_getapplock','UPDLOCK','HOLDLOCK','@LockTimeout=0','XACT_ABORT ON'): self.assertIn(x,SQL)
 def test_no_ddl_or_privilege(self):
  body=re.sub(r'--.*','',SQL).upper()
  self.assertNotRegex(body,r'\b(CREATE|ALTER|DROP|GRANT|DENY|REVOKE)\s+(TABLE|USER|ROLE|LOGIN|INDEX|VIEW|DATABASE|CONNECT)\b')
 def test_exact_write_tables(self):
  got=re.findall(r'(?im)^\s*INSERT\s+(?:INTO\s+)?dbo\.([A-Za-z]+)',SQL)
  self.assertEqual(got,['Organizations','Teams','Roles','Users','UserIdentityProfiles','UserEmploymentPeriods','UserRoleAssignments','UserTeamAssignments'])
 def test_rows_and_modern_chain(self):
  self.assertIn('20 AS TotalRows',SQL)
  for t in ('UserIdentityProfiles','UserEmploymentPeriods','UserRoleAssignments','UserTeamAssignments'): self.assertIn(t,SQL)
  self.assertNotRegex(SQL,r'(?im)^\s*INSERT\s+(?:INTO\s+)?dbo\.UserRoles\b')
 def test_fixed_effective_date_taipei_postcheck(self):
  self.assertIn("CONVERT(date,'20200101',112)",SQL); self.assertIn('DATEADD(MINUTE,480,SYSUTCDATETIME())',SQL)
 def test_preserves_failures(self):
  self.assertRegex(SQL,r'BEGIN CATCH\s+IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;\s+THROW;',re.S)
 def test_readback_is_read_only(self):
  self.assertNotRegex(READBACK.upper(),r'\b(INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|GRANT)\b')
 def test_digest_is_stable_shape(self): self.assertEqual(len(hashlib.sha256(SQL.encode()).hexdigest()),64)

if __name__=='__main__': unittest.main()
