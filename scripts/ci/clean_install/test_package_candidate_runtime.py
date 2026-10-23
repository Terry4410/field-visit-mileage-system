import json
import tempfile
import unittest
from pathlib import Path

from package_candidate_runtime import package


RC_SHA = "9d431ea318fcec34f04f3cc36a8fb6d01d00c48a"
SOURCE_SHA = "a" * 40
CANDIDATE_CONTEXT_SHA = "40d7ce4079e634b2d048eb9badc209518a622dfe7bbb7a5f1c80d325e580d77f"


class CandidateRuntimePackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.api = self.root / "api"
        self.api.mkdir()
        (self.api / "FieldVisit.Api.dll").write_bytes(b"synthetic published binary")
        self.config = {
            "ConnectionStrings": {"DefaultConnection": "SET-IN-AZURE-APP-SERVICE"},
            "Auth": {"JwtKey": "SET-IN-AZURE-APP-SERVICE-AT-LEAST-32-CHARS",
                     "DemoPassword": "SET-IN-AZURE-APP-SERVICE"},
            "OptionC": {"Approval": {"Enabled": False}, "Candidate": {"Enabled": False}},
        }
        (self.api / "appsettings.json").write_text(json.dumps(self.config), encoding="utf-8")
        self.overlay = self.root / "overlay.json"
        self.overlay.write_text(json.dumps({
            "format": "field-visit-clean-install-rc-runtime-overlay-v1",
            "rcSourceSha": RC_SHA,
            "candidateSourceSha256": CANDIDATE_CONTEXT_SHA,
            "deploymentStatus": "NONDEPLOY_DISPOSABLE_CANDIDATE_ONLY",
        }), encoding="utf-8")
        self.output = self.root / "out"

    def tearDown(self):
        self.temp.cleanup()

    def test_package_is_default_off_and_readback_verified(self):
        first = package(self.api, self.output, SOURCE_SHA, RC_SHA, self.overlay)
        second = package(self.api, self.output, SOURCE_SHA, RC_SHA, self.overlay)
        self.assertEqual(first["package"]["sha256"], second["package"]["sha256"])
        self.assertEqual(first["optionCCandidateDefault"], "OFF")
        self.assertFalse(first["liveDeploymentAuthorized"])

    def test_default_on_package_is_rejected(self):
        self.config["OptionC"]["Candidate"]["Enabled"] = True
        (self.api / "appsettings.json").write_text(json.dumps(self.config), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "CANDIDATE_DEFAULT_OR_CONFIGURATION_UNSAFE"):
            package(self.api, self.output, SOURCE_SHA, RC_SHA, self.overlay)

    def test_embedded_credential_is_rejected(self):
        self.config["ConnectionStrings"]["DefaultConnection"] = "Server=unexpected"
        (self.api / "appsettings.json").write_text(json.dumps(self.config), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "CANDIDATE_DEFAULT_OR_CONFIGURATION_UNSAFE"):
            package(self.api, self.output, SOURCE_SHA, RC_SHA, self.overlay)


if __name__ == "__main__":
    unittest.main()
