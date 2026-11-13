"""Offline-only fail-closed tests; no network, Azure, SSH or SQL."""
import unittest
from pathb_ssh_trust_delta_20261113 import (
    classify_fingerprint, transport_readiness, TrustHold,
    LEGACY_PIN, PORTAL_AND_TUNNEL_OBSERVED, EXACT_APP, EXACT_RG,
)

class TestTrustDelta(unittest.TestCase):
    def gate(self, fp=PORTAL_AND_TUNNEL_OBSERVED, **kwargs):
        return classify_fingerprint(fp, app=EXACT_APP, resource_group=EXACT_RG, **kwargs)

    def test_legacy_rejected(self):
        with self.assertRaises(TrustHold): self.gate(LEGACY_PIN, verified_out_of_band=True)

    def test_unknown_rejected(self):
        with self.assertRaises(TrustHold): self.gate("SHA256:"+"A"*43, verified_out_of_band=True)

    def test_unapproved_observation_rejected(self):
        with self.assertRaises(TrustHold): self.gate()

    def test_explicitly_verified_observation_is_not_live_go(self):
        self.assertEqual(self.gate(verified_out_of_band=True), "OBSERVED_KEY_MATCH_ONLY_NO_LIVE_GO")

    def test_wrong_target_rejected(self):
        with self.assertRaises(TrustHold):
            classify_fingerprint(PORTAL_AND_TUNNEL_OBSERVED, app="other", resource_group=EXACT_RG, verified_out_of_band=True)

    def test_invalid_shape_rejected(self):
        with self.assertRaises(TrustHold): self.gate("not-a-fingerprint", verified_out_of_band=True)

    def test_transport_fails_closed(self):
        keys=["authenticated_noninteractive","secret_safe","once_safe","deadline_safe","disconnect_safe"]
        for missing in keys:
            with self.subTest(missing=missing):
                values={k: True for k in keys};values[missing]=False
                with self.assertRaises(TrustHold): transport_readiness(**values)

    def test_all_contract_checks_still_not_live_approval(self):
        self.assertEqual(transport_readiness(authenticated_noninteractive=True,secret_safe=True,
            once_safe=True,deadline_safe=True,disconnect_safe=True), "CONTRACT_READY_NOT_LIVE_AUTHORIZED")

if __name__ == "__main__":
    unittest.main()
