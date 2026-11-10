#!/usr/bin/env python3
import json
import pathlib
import subprocess
import unittest

ROOT = pathlib.Path(__file__).resolve().parent
ORIGINAL = (ROOT / "g1_cloudshell_launcher_original_recovered.sh").read_text()
FIXED = (ROOT / "g1_cloudshell_launcher_fixed.sh").read_text()

OLD_BLOCK = '''read FQDN PNA TLS <<EOF
$(az sql server show -g rg-fieldvisit-uat -n sql-fieldvisit-jpe-pathb-uat --query '[fullyQualifiedDomainName,publicNetworkAccess,minimalTlsVersion]' -o tsv --only-show-errors)
EOF
[ "$FQDN" = "sql-fieldvisit-jpe-pathb-uat.database.windows.net" ] && [ "$PNA" = "Disabled" ] && [ "$TLS" = "1.2" ] || fail SQL_TARGET'''

NEW_BLOCK = '''SQL_TARGET_JSON=$(az sql server show -g rg-fieldvisit-uat -n sql-fieldvisit-jpe-pathb-uat --query '{fqdn:fullyQualifiedDomainName,pna:publicNetworkAccess,tls:minimalTlsVersion}' -o json --only-show-errors) || fail SQL_TARGET
printf '%s' "$SQL_TARGET_JSON" | python3 -c 'import json,sys; x=json.load(sys.stdin); ok=isinstance(x,dict) and set(x)=={"fqdn","pna","tls"} and x.get("fqdn")=="sql-fieldvisit-jpe-pathb-uat.database.windows.net" and x.get("pna")=="Disabled" and x.get("tls")=="1.2"; raise SystemExit(0 if ok else 1)' || fail SQL_TARGET
unset SQL_TARGET_JSON'''


def validate(payload):
    try:
        x = json.loads(payload)
    except Exception:
        return False
    return (
        isinstance(x, dict)
        and set(x) == {"fqdn", "pna", "tls"}
        and x.get("fqdn") == "sql-fieldvisit-jpe-pathb-uat.database.windows.net"
        and x.get("pna") == "Disabled"
        and x.get("tls") == "1.2"
    )


class SqlTargetFixTests(unittest.TestCase):
    def test_shell_syntax(self):
        for name in ("g1_cloudshell_launcher_original_recovered.sh", "g1_cloudshell_launcher_fixed.sh"):
            subprocess.run(["bash", "-n", str(ROOT / name)], check=True)

    def test_exact_original_tsv_boundary_recovered(self):
        self.assertEqual(ORIGINAL.count(OLD_BLOCK), 1)

    def test_only_sql_target_block_changed(self):
        self.assertEqual(ORIGINAL.replace(OLD_BLOCK, NEW_BLOCK), FIXED.replace(
            "# NONDEPLOY correction of the recovered outer launcher.  This file retains\n"
            "# every original gate and changes only SQL target acquisition/validation from\n"
            "# lossy TSV/read parsing to status-checked structured JSON.\n",
            "# Recovered from the originating Work Cloud Shell invocation that emitted\n"
            "# G1_PREGATE=HOLD_SQL_TARGET.  Historical evidence only; DO NOT EXECUTE.\n",
        ))

    def test_cli_status_is_not_discarded(self):
        self.assertIn(") || fail SQL_TARGET", NEW_BLOCK)

    def test_exact_payload_passes(self):
        self.assertTrue(validate('{"fqdn":"sql-fieldvisit-jpe-pathb-uat.database.windows.net","pna":"Disabled","tls":"1.2"}'))

    def test_wrong_values_fail_closed(self):
        for key, value in (("fqdn", "legacy.database.windows.net"), ("pna", "Enabled"), ("tls", "1.0")):
            x = {"fqdn": "sql-fieldvisit-jpe-pathb-uat.database.windows.net", "pna": "Disabled", "tls": "1.2"}
            x[key] = value
            self.assertFalse(validate(json.dumps(x)))

    def test_missing_null_extra_and_garbled_fail_closed(self):
        cases = [
            '{}',
            '{"fqdn":null,"pna":"Disabled","tls":"1.2"}',
            '{"fqdn":"sql-fieldvisit-jpe-pathb-uat.database.windows.net","pna":"Disabled","tls":"1.2","extra":1}',
            'not-json',
            '',
        ]
        for payload in cases:
            self.assertFalse(validate(payload))

    def test_security_bindings_preserved(self):
        required = [
            "8cc7a5aa4d888d1958380137ea6af46bf02d329b89850e7b27dcb26c0ac08141",
            "302216e00544d5dfcf0821fa362e2b0f6aebb7e0125121509f8bdfc8fabb6c45",
            "7dca8e3016693051656ddfa5efa77b92c2f0ea679ab140eb237a818c130d1b47",
            "976f4269f73c61a6c7b15538a6518272f937428e59fdd8f87d606a8501d74c9c",
            "2e3687eea0c142b70b4923094ea37c2e8a17f97815db894b58ae60379fccd604",
            "api-fieldvisit-pathb-uat",
            "FIELDVISIT_PATHB_G1_INSTALL_V1",
            "OWNER_APPROVED_PATHB_G1_ORIGINAL_INSTALLER_SINGLE",
        ]
        for item in required:
            self.assertEqual(ORIGINAL.count(item), FIXED.count(item), item)


if __name__ == "__main__":
    unittest.main(verbosity=2)

