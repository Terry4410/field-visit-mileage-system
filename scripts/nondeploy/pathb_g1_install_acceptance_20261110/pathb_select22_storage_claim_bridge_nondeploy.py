"""NONDEPLOY lazy Storage-token to OnceGuard adapter. No live entrypoint.

The independently reviewed Owner Parent must supply the true one-shot
Storage token provider, authenticated target proof and explicit exact-key
Storage grant. A supplied boolean or this adapter itself proves none of them.
The first Storage token is requested only INSIDE the Blob PUT call, after
SELECT22 stage/receiver READY in the existing handoff choreography.
"""
from pathb_select22_storage_once_nondeploy import OnceGuard
from pathb_select22_storage_https_nondeploy import exact_https_response
from pathb_select22_direct_https_nondeploy import direct_https_once


class StorageBridgeHold(Exception):
    pass


class LazyStorageRequest:
    """Single PUT then single GET; no replay, fallback or token refresh."""

    def __init__(self, token_provider, clock, transport):
        if not callable(token_provider) or not callable(clock) or not callable(transport):
            raise StorageBridgeHold("STORAGE_BRIDGE_PREGATE_HOLD")
        self._provider = token_provider
        self._clock = clock
        self._transport = transport
        self._token = None
        self._phase = "NEW"

    def __call__(self, method, account, container, key, headers, body):
        if ((self._phase == "NEW" and method != "PUT")
                or (self._phase == "PUT_DONE" and method != "GET")
                or self._phase not in ("NEW", "PUT_DONE")):
            raise StorageBridgeHold("STORAGE_BRIDGE_ONCE_HOLD")
        was_put = self._phase == "NEW"
        # Consume phase BEFORE requesting token or dispatching HTTP.
        self._phase = "UNKNOWN"
        try:
            if was_put:
                token = self._provider()
                if type(token) is not str or not token:
                    raise ValueError()
                self._token = token
            response = exact_https_response(
                method, account, container, key, headers, body,
                token=self._token, clock=self._clock,
                transport=self._transport)
            if was_put:
                # Only exact successful claim PUT permits GET.
                if response.status != 201:
                    raise ValueError()
                self._phase = "PUT_DONE"
            else:
                self._phase = "COMPLETE"
                self._token = None
            return response
        except Exception:
            self._token = None
            self._phase = "HOLD"
            raise StorageBridgeHold("STORAGE_BRIDGE_OUTCOME_UNKNOWN") from None


def make_storage_once_guard(storage_token_provider, clock, *,
                            transport=direct_https_once):
    """Construct inert guard, without requesting tokens or accessing storage.

    Caller MUST first establish a separately approved, verified live Owner
    Parent and supply authenticated token provider. Never use this function
    to infer a new Owner grant from previous SELECT-only authorization.
    """
    return OnceGuard(LazyStorageRequest(storage_token_provider, clock, transport))


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_BLOB_OR_TOKEN_ENTRYPOINT")
