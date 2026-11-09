import pathlib
import re
import unittest

SQL=pathlib.Path(__file__).with_name('pathb_catalog_readonly_REVIEW_ONLY.sql').read_text(encoding='utf-8')
STRIPPED='\n'.join(x for x in SQL.splitlines() if not x.lstrip().startswith('--'))

class ReviewOnlyCatalogTests(unittest.TestCase):
    def test_pinned_target(self):
        self.assertIn("N'db-fieldvisit-pathb-uat'",SQL)
        self.assertIn("N'sql-fieldvisit-jpe-pathb-uat'",SQL)
        self.assertIn("N'sql-fieldvisit-jpe-pathb-uat.database.windows.net'",SQL)
    def test_no_writes_or_grants(self):
        for word in ('CREATE','ALTER','DROP','INSERT','UPDATE','DELETE','MERGE','TRUNCATE','GRANT','DENY','REVOKE'):
            self.assertIsNone(re.search(r'(?mi)^\s*'+word+r'\b', STRIPPED),word)
    def test_single_result_receipt(self):
        self.assertIn("'FV_CAT|'",SQL)
        self.assertEqual(len(re.findall(r'\bAS CatalogReceipt\b',SQL)),1)
        self.assertEqual(len(re.findall(r'(?mi)^GO\s*$',SQL)),1)
    def test_conditional_version_read(self):
        self.assertIn("IF @version_exists = 1",SQL)
        self.assertIn('EXEC sys.sp_executesql',SQL)
        self.assertIn('FROM dbo.SchemaVersions;',SQL)
        self.assertIn("N''1.8.0-CREATE-LOWRISK''",SQL)
        self.assertIn("N''1.8.0-LOWRISK-DIRECT''",SQL)
        self.assertIn("N''1.8.0-EXCEL-CANONICAL-STATUS''",SQL)
    def test_no_secrets_or_owner_sid(self):
        for word in ('SUSER_SID','access_token','IDENTITY_HEADER','password','client_secret'):
            self.assertNotIn(word,STRIPPED)
    def test_readonly_purpose(self):
        self.assertIn('HAS_PERMS_BY_NAME',SQL)
        self.assertIn('sys.check_constraints',SQL)
        self.assertIn('sys.foreign_keys',SQL)
        self.assertIn('sys.triggers',SQL)

if __name__=='__main__':unittest.main()
