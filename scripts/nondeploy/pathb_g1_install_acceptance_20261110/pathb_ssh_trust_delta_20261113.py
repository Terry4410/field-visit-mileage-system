"""NONDEPLOY-only SSH trust evidence gate; no live transport or credential handling.

Observed fingerprint is evidence, not an authorization to rotate a trusted key.
A separately approved, out-of-band verification must precede any live use.
"""
import re

LEGACY_PIN = "SHA256:qlMlSfV+nfx3Y5BdqQC+4WY1VFChCdRZMjqE+T/NgOI"
PORTAL_AND_TUNNEL_OBSERVED = "SHA256:FKM/np9YzcuZaOsPqy3TL4gcae0szuZ3Z0dEG06KyZk"
EXACT_APP = "api-fieldvisit-pathb-uat"
EXACT_RG = "rg-fieldvisit-uat"

class TrustHold(Exception):
    pass

def classify_fingerprint(fingerprint, *, app, resource_group, verified_out_of_band=False):
    """Return observation status only. Never creates an SSH invocation or trusts a key."""
    if app != EXACT_APP or resource_group != EXACT_RG:
        raise TrustHold("TARGET_HOLD")
    if not isinstance(fingerprint, str) or not re.fullmatch(r"SHA256:[A-Za-z0-9+/]{43}", fingerprint):
        raise TrustHold("FINGERPRINT_SHAPE_HOLD")
    if fingerprint == LEGACY_PIN:
        raise TrustHold("LEGACY_PIN_STALE_HOLD")
    if fingerprint != PORTAL_AND_TUNNEL_OBSERVED:
        raise TrustHold("UNRECOGNIZED_HOST_KEY_HOLD")
    if verified_out_of_band is not True:
        raise TrustHold("INDEPENDENT_TRUST_APPROVAL_REQUIRED")
    return "OBSERVED_KEY_MATCH_ONLY_NO_LIVE_GO"

def transport_readiness(*, authenticated_noninteractive, secret_safe, once_safe, deadline_safe, disconnect_safe):
    """No fallback to interactive PTY for token transfer; all conditions are mandatory."""
    if not all(x is True for x in (authenticated_noninteractive, secret_safe,
                                    once_safe, deadline_safe, disconnect_safe)):
        raise TrustHold("LIVE_TRANSPORT_HOLD")
    return "CONTRACT_READY_NOT_LIVE_AUTHORIZED"

if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_SSH_TOKEN_OR_SQL_ENTRYPOINT")
