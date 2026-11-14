"""NONDEPLOY direct TLS Azure Blob HTTPS transport candidate.

Import-safe. No CLI, Azure/token acquisition, SSH, Blob or SQL on import.
The only callable may be invoked by a separately authorized and audited live
Owner Parent, never from this source as a script. Tests inject fake connection.
Direct http.client.HTTPSConnection intentionally bypasses environment proxies,
redirect handlers, cookies and transparent HTTP retry mechanisms.
"""
import http.client
import ssl
from urllib.parse import urlsplit
from pathb_select22_storage_https_nondeploy import URL, HOST, MAX_READBACK

EXPECTED_PATH = urlsplit(URL).path


class DirectHttpsHold(Exception):
    pass


def direct_https_once(method, url, headers, body, timeout_seconds,
                      *, connection_factory=None):
    """Exactly one HTTPS request to the fixed Blob URL; no redirect or retry.

    A real invocation requires independent authorization and authenticating
    Storage JWT; this low-level transport DOES NOT provide either. It strictly
    uses the system CA bundle, TLS peer/hostname verification and a direct
    HTTPSConnection. Caller must never serialize request headers or exceptions.
    """
    conn = None
    try:
        parsed = urlsplit(url)
        if (type(url) is not str or url != URL or parsed.scheme != "https"
                or parsed.hostname != HOST or parsed.port is not None
                or parsed.username is not None or parsed.password is not None
                or parsed.query or parsed.fragment
                or parsed.path != EXPECTED_PATH
                or method not in ("PUT", "GET")
                or type(timeout_seconds) is not int
                or not 1 <= timeout_seconds <= 20
                or type(headers) is not dict or type(body) is not bytes
                or len(body) > MAX_READBACK
                or not isinstance(headers.get("Authorization"), str)
                or not headers["Authorization"].startswith("Bearer ")
                or len(headers["Authorization"]) <= len("Bearer ")
                or len(headers["Authorization"]) > 16400
                or headers.get("x-ms-version") != "2023-11-03"):
            raise ValueError()
        if method == "PUT":
            if (set(headers) != {"Authorization", "x-ms-version",
                    "If-None-Match", "x-ms-blob-type",
                    "Content-Type", "Content-Length"}
                    or headers.get("If-None-Match") != "*"
                    or headers.get("x-ms-blob-type") != "BlockBlob"
                    or headers.get("Content-Type") != "application/json"
                    or headers.get("Content-Length") != str(len(body))):
                raise ValueError()
        elif (body or set(headers) != {"Authorization", "x-ms-version", "If-Match"}
              or not isinstance(headers["If-Match"], str)
              or not 1 <= len(headers["If-Match"]) <= 128
              or any(c in headers["If-Match"] for c in "\r\n")):
            raise ValueError()
        if any(not isinstance(k, str) or not isinstance(v, str)
               or "\r" in k or "\n" in k or "\r" in v or "\n" in v
               for k, v in headers.items()):
            raise ValueError()
        if connection_factory is None:
            context = ssl.create_default_context()
            # Never allow caller-supplied unverified SSL contexts in production.
            if context.verify_mode != ssl.CERT_REQUIRED or not context.check_hostname:
                raise ValueError()
            conn = http.client.HTTPSConnection(
                HOST, port=443, timeout=timeout_seconds, context=context)
        else:
            # Test seam only: a fake in-memory connection (no network).
            conn = connection_factory(HOST, 443, timeout_seconds)
        conn.request(method, EXPECTED_PATH, body=body if method == "PUT" else None,
                     headers=headers)
        response = conn.getresponse()
        status = response.status
        # A fixed bounded read, regardless of Content-Length or chunked encoding.
        payload = response.read(MAX_READBACK + 1)
        if (type(status) is not int or status not in (200, 201, 412)
                or type(payload) is not bytes or len(payload) > MAX_READBACK):
            raise ValueError()
        if (method == "PUT" and status not in (201, 412)
                or method == "GET" and status != 200):
            raise ValueError()
        etags = response.headers.get_all("ETag", [])
        if (len(etags) != 1 or not isinstance(etags[0], str)
                or not 1 <= len(etags[0]) <= 128
                or "\r" in etags[0] or "\n" in etags[0]):
            raise ValueError()
        return status, {"ETag": etags[0]}, payload
    except Exception:
        # Never emit raw HTTPS, token, headers, response body or error details.
        raise DirectHttpsHold("SELECT22_DIRECT_HTTPS_HOLD") from None
    finally:
        if conn is not None:
            try:
                conn.close()
            except Exception:
                pass


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_HTTPS_LIVE_ENTRYPOINT")
