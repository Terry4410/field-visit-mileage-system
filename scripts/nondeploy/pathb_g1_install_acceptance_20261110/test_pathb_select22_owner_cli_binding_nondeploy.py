"""Offline exact CLI-context contract tests; synthetic JWTs only."""
import base64
import json
import unittest
from unittest import mock
import pathb_select22_owner_cli_binding_nondeploy as c


def jwt(claims):
    body = base64.urlsafe_b64encode(json.dumps(claims).encode()).decode().rstrip("=")
    return "a." + body + ".signature-not-checked"


class CliBindingTests(unittest.TestCase):
    def test_no_runner_cannot_request_token(self):
        with self.assertRaisesRegex(c.OwnerCliBindingHold, "CLI_OWNER_READER_NOT_RELEASED"):
            c.verify_cli_owner_once(now=10000)

    def test_bad_account_never_requests_arm_token(self):
        calls = []
        def runner(command):
            calls.append(command)
            return {"id":"wrong", "tenantId":"wrong", "user":{"type":"user"}}
        with self.assertRaises(c.OwnerCliBindingHold):
            c.verify_cli_owner_once(runner=runner, now=10000)
        self.assertEqual(calls, [c.SUBSCRIPTION_COMMAND])

    def test_claim_failure_does_not_fallback_or_expose_token(self):
        calls = []
        # Synthetically bypass only the pins; no live CLI or Azure occurs.
        with mock.patch.object(c, "_digest", side_effect=lambda s: {
            "sub":"subhash", "tenant":"tenanthash", "owner":"ownerhash"}.get(s, "wrong")), \
             mock.patch.object(c, "SUB_SHA", "subhash"), \
             mock.patch.object(c, "TENANT_SHA", "tenanthash"), \
             mock.patch.object(c, "OWNER_SHA", "ownerhash"):
            def runner(command):
                calls.append(command)
                if command == c.SUBSCRIPTION_COMMAND:
                    return {"id":"sub","tenantId":"tenant","user":{"type":"user"}}
                return {"accessToken":jwt({"oid":"somebody-else","tid":"tenant",
                                            "aud":c.ARM_AUDIENCE,"nbf":9900,"exp":13000})}
            with self.assertRaisesRegex(c.OwnerCliBindingHold, "CLI_OWNER_BINDING_HOLD"):
                c.verify_cli_owner_once(runner=runner, now=10000)
        self.assertEqual(calls, [c.SUBSCRIPTION_COMMAND, c.TOKEN_COMMAND])

    def test_synthetic_matching_claims_are_not_authentication(self):
        with mock.patch.object(c, "_digest", side_effect=lambda s: {
            "sub":"S","tenant":"T","owner":"O"}.get(s, "wrong")), \
             mock.patch.object(c, "SUB_SHA", "S"), \
             mock.patch.object(c, "TENANT_SHA", "T"), \
             mock.patch.object(c, "OWNER_SHA", "O"):
            def runner(command):
                if command == c.SUBSCRIPTION_COMMAND:
                    return {"id":"sub","tenantId":"tenant","user":{"type":"user"}}
                return {"accessToken":jwt({"oid":"owner","tid":"tenant",
                                            "aud":c.ARM_AUDIENCE,"nbf":9900,"exp":13000})}
            result = c.verify_cli_owner_once(runner=runner, now=10000)
        self.assertEqual(result["cli_token_claims"], "PASS_UNVERIFIED_SIGNATURE")
        self.assertEqual(result["ssh_process_identity"], "NOT_ATTESTED")
        self.assertEqual(result["live_release"], "HOLD")

    def test_live_release_remains_blocked(self):
        with self.assertRaises(c.OwnerCliBindingHold):
            c.release_live(owner=True, token=True, ssh=True)


if __name__ == "__main__":
    unittest.main()
