#!/usr/bin/env python3
"""Static NONDEPLOY fail-closed checks; does NOT execute T-SQL or Azure."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parent
READ = (ROOT / "catalog_readonly.sql").read_text()
WRITE = (ROOT / "conditional_create_connect.sql").read_text()
README = (ROOT / "README.md").read_text()

class ContractTests(unittest.TestCase):
    def test_readonly_does_not_mutate(self):
        for forbidden in ("CREATE USER", "GRANT CONNECT", "INSERT INTO", "UPDATE ",
                          "DELETE FROM", "EXEC ", "BEGIN TRAN", "DBCC "):
            # Comments contain verbs intentionally; limit to active statement starts.
            self.assertFalse(re.search(r"(?im)^\s*" + re.escape(forbidden) + r"\b", READ), forbidden)
        self.assertIn("PATHB_MI_CATALOG", READ)

    def test_exact_target_and_original_g1_schema(self):
        for source in (READ, WRITE):
            self.assertIn("sql-fieldvisit-jpe-pathb-uat", source)
            self.assertIn("db-fieldvisit-pathb-uat", source)
            self.assertIn("EngineEdition", source)
            self.assertIn("<> 69", source)
            self.assertIn("<> 3", source)
            self.assertIn("1.8.0-EXCEL-CANONICAL-STATUS", source)
            self.assertNotIn("sql-fieldvisit-jpe-uat.database.windows.net", source)
            self.assertNotIn("db-fieldvisit-uat'", source)

    def test_private_identity_and_fail_closed_gate(self):
        for source in (READ, WRITE):
            self.assertIn("FieldVisit.PathB.ApiMiObjectId", source)
            self.assertIn("FieldVisit.PathB.ApiMiClientId", source)
            self.assertIn("TRY_CONVERT(uniqueidentifier", source)
        self.assertIn("EXPLICIT_NEW_PATHB_MI_CONNECT_ONLY_ONCE_V1", WRITE)
        self.assertIn("INDEPENDENTLY_VERIFIED_NO_EFFECTIVE_GROUP_LOGIN", WRITE)
        self.assertIn("EXACT_OWNER_PRIVATE_TLS_VERIFIED", WRITE)
        self.assertIn("ALREADY_EXISTS_OR_COLLISION_HOLD", WRITE)
        self.assertIn("ROLLBACK TRANSACTION", WRITE)
        self.assertIn("WITH OBJECT_ID", WRITE)

    def test_only_essential_ddl_not_broad_rights(self):
        # Accept comments mentioning forbidden rights, but never a runnable SQL GRANT/role statement.
        for forbidden in (r"(?im)^\s*GRANT\s+(?:SELECT|INSERT|UPDATE|DELETE|ALTER)",
                          r"(?im)^\s*ALTER\s+ROLE", r"(?im)^\s*CREATE\s+TABLE",
                          r"(?im)^\s*DROP\s+USER", r"(?im)^\s*UPDATE\s+dbo\."):
            self.assertIsNone(re.search(forbidden, WRITE))
        self.assertIn("GRANT CONNECT TO", WRITE)
        self.assertIn("CREATE USER ", WRITE)

    def test_security_principal_inventory_visibility_and_sid_shape(self):
        # VIEW DEFINITION by itself cannot certify that all other users are visible.
        for source in (READ, WRITE):
            self.assertIn("N'ALTER ANY USER'", source)
            self.assertIn("PATHB_SECURITY_PRINCIPAL_VISIBILITY_HOLD", source)
            self.assertIn("DATALENGTH([sid]) NOT IN (16, 18)", source)
            self.assertIn("SUBSTRING([sid], 17, 2) <> 0xAADE", source)
            self.assertIn("authentication_type_desc", source)
            self.assertIn("PATHB_EXTERNAL_SID_SHAPE_HOLD", source)
        self.assertIn("PATHB_MI_GRANT_DUPLICATE_IDENTITY_HOLD", WRITE)

    def test_schema_readback_includes_frozen_constraint_trigger_safety(self):
        for source in (READ, WRITE):
            self.assertIn("sys.check_constraints", source)
            self.assertIn("sys.foreign_keys", source)
            self.assertIn("sys.triggers", source)
            self.assertIn("sys.sql_modules", source)
            self.assertIn("tr.is_disabled = 1 OR m.definition IS NULL", source)

    def test_no_automatic_remote_execution(self):
        for source in (READ, WRITE, README):
            self.assertNotIn("az login", source)
            self.assertNotIn("sqlcmd -", source)
            self.assertNotIn("--execute", source)
        self.assertIn("not an executable channel", README)

    def test_negative_mutations_are_rejected(self):
        mutations = [
            WRITE.replace("EXPLICIT_NEW_PATHB_MI_CONNECT_ONLY_ONCE_V1", "DANGEROUS_UNBOUND"),
            WRITE.replace("EXACT_OWNER_PRIVATE_TLS_VERIFIED", "ANY_CHANNEL"),
            WRITE.replace("GRANT CONNECT TO", "GRANT CONTROL TO"),
            WRITE.replace("ALREADY_EXISTS_OR_COLLISION_HOLD", "REUSE_EXISTING_USER"),
        ]
        for changed in mutations:
            with self.subTest(case=changed[:40]):
                self.assertFalse(
                    all(x in changed for x in (
                        "EXPLICIT_NEW_PATHB_MI_CONNECT_ONLY_ONCE_V1",
                        "EXACT_OWNER_PRIVATE_TLS_VERIFIED",
                        "GRANT CONNECT TO",
                        "ALREADY_EXISTS_OR_COLLISION_HOLD",
                    ))
                )

if __name__ == "__main__":
    unittest.main()
