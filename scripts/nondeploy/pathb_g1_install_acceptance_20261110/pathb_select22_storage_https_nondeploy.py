"""NONDEPLOY fixed-key Azure Storage HTTPS request adapter.

This module is a source candidate, NOT an authorized live entrypoint.
No network on import; all HTTP is injected into make_request.
The independently audited Owner Parent must authenticate the caller,
obtain a one-use Storage-audience token, verify the exact fresh grant,
and prohibit redirections, retries, alternate keys or output leakage.
"""
from urllib.parse import urlsplit
from pathb_select22_storage_once_nondeploy import (
    ACCOUNT, CONTAINER, KEY, Response,
)
from p3_ssh_transport_contract import validate_owner_token


class StorageTransportHold(Exception):
    pass


HOST = ACCOUNT + ".blob.core.windows.net"
URL = "https://" + HOST + "/" + CONTAINER + "/" + KEY
VERSION = "2023-11-03"
MAX_READBACK = 1024


def exact_https_response(method, account, container, key, headers, body,
                         *, token, clock, transport):
    """Adapter for OnceGuard(request), requiring an audited HTTPS transport.

    transport(method, url, headers, body, timeout_seconds) returns
    (status, headers, body). transport MUST enforce TLS hostname validation,
    no redirects, no proxies, no retries, zero secret logging and max read.
    This adapter cannot independently prove those properties.
    """
    try:
        if (method not in ("PUT", "GET") or account != ACCOUNT
                or container != CONTAINER or key != KEY
                or not callable(transport) or not callable(clock)
                or type(token) is not str):
            raise ValueError()
        validate_owner_token(token, "storage", clock())
        if urlsplit(URL).hostname != HOST:
            raise ValueError()
        if method == "PUT":
            if (type(body) is not bytes or len(body) > MAX_READBACK or
                    type(headers) is not dict or
                    set(headers) != {"If-None-Match", "x-ms-blob-type",
                                     "Content-Type", "Content-Length"} or
                    headers.get("If-None-Match") != "*" or
                    headers.get("x-ms-blob-type") != "BlockBlob" or
                    headers.get("Content-Type") != "application/json" or
                    headers.get("Content-Length") != str(len(body))):
                raise ValueError()
        else:
            if (body != b"" or type(headers) is not dict or
                    set(headers) != {"If-Match"} or
                    not isinstance(headers["If-Match"], str) or
                    not headers["If-Match"] or
                    len(headers["If-Match"]) > 128):
                raise ValueError()
        safe_headers = dict(headers)
        safe_headers["Authorization"] = "Bearer " + token
        safe_headers["x-ms-version"] = VERSION
        result = transport(method, URL, safe_headers, body, 20)
        if (type(result) is not tuple or len(result) != 3
                or type(result[0]) is not int or type(result[1]) is not dict
                or type(result[2]) is not bytes):
            raise ValueError()
        status, response_headers, payload = result
        if len(payload) > MAX_READBACK:
            raise ValueError()
        # Normalize trusted wire schema; reject duplicate/missing ETag at client boundary.
        etag = response_headers.get("ETag")
        if (not isinstance(etag, str) or not 1 <= len(etag) <= 128
                or any(c in etag for c in "\r\n")):
            raise ValueError()
        if method == "PUT" and status != 201:
            if status == 412:
                return Response(412, etag)
            raise ValueError()
        if method == "GET" and status != 200:
            raise ValueError()
        return Response(status, etag, payload)
    except Exception:
        raise StorageTransportHold("SELECT22_STORAGE_HTTPS_HOLD") from None


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_HTTP_ENTRYPOINT")
