#!/usr/bin/env python3
"""One-shot Path B App Service MI -> Azure SQL LOGIN ONLY. No SQL statements.
NONDEPLOY source candidate; never invoke without separate exact-SHA Owner approval.
"""
import ipaddress
import json
import os
import socket
import struct
import sys
import urllib.request

HOST = "sql-fieldvisit-jpe-pathb-uat.database.windows.net"
DB = "db-fieldvisit-pathb-uat"
PRIVATE_IP = "10.90.2.4"
RESOURCE = "https://database.windows.net/"
TOKEN_ATTR = 1256  # SQL_COPT_SS_ACCESS_TOKEN


def fail(code):
    print("PATHB_MI_LOGIN=" + code, flush=True)
    return 2


def preflight():
    # Reject running on an arbitrary Cloud Shell/CI host. Never print identity env.
    if not os.environ.get("WEBSITE_INSTANCE_ID"):
        return "HOLD_NOT_APP_CONTAINER"
    if not os.environ.get("IDENTITY_ENDPOINT") or not os.environ.get("IDENTITY_HEADER"):
        return "HOLD_APP_MI_ENDPOINT_ABSENT"
    try:
        import pyodbc
        if "ODBC Driver 18 for SQL Server" not in pyodbc.drivers():
            return "HOLD_ODBC18_ABSENT"
    except (ImportError, OSError):
        return "HOLD_PYODBC_ABSENT"
    try:
        ips = {x[4][0] for x in socket.getaddrinfo(HOST, 1433, type=socket.SOCK_STREAM)}
        if ips != {PRIVATE_IP} or not all(ipaddress.ip_address(x).is_private for x in ips):
            return "HOLD_PRIVATE_DNS_MISMATCH"
    except (OSError, ValueError):
        return "HOLD_PRIVATE_DNS_UNVERIFIED"
    return None


def run():
    hold = preflight()
    if hold:
        return fail(hold)
    # Exactly one HTTP token request; no retry handlers or fallback credential.
    endpoint = os.environ["IDENTITY_ENDPOINT"]
    if not endpoint.startswith("http://127.0.0.1:") and not endpoint.startswith("http://localhost:"):
        return fail("HOLD_MI_ENDPOINT_NOT_LOCAL")
    from urllib.parse import urlencode, urlsplit, urlunsplit, parse_qsl
    u = urlsplit(endpoint)
    query = dict(parse_qsl(u.query))
    if "resource" in query or "api-version" in query:
        return fail("HOLD_MI_ENDPOINT_UNEXPECTED_QUERY")
    query.update({"resource": RESOURCE, "api-version": "2019-08-01"})
    token_url = urlunsplit((u.scheme, u.netloc, u.path, urlencode(query), ""))
    request = urllib.request.Request(token_url, headers={"X-IDENTITY-HEADER": os.environ["IDENTITY_HEADER"]})
    try:
        with urllib.request.urlopen(request, timeout=8) as response:
            payload = json.loads(response.read(16384))
        token = payload.get("access_token")
        if not isinstance(token, str) or not token:
            return fail("HOLD_MI_TOKEN_RESPONSE")
    except Exception:
        return fail("HOLD_MI_TOKEN_REQUEST_UNKNOWN")
    # The ODBC access token buffer must be UTF-16LE with a 4-byte byte-length prefix.
    encoded = token.encode("utf-16-le")
    token_buffer = struct.pack("<I", len(encoded)) + encoded
    del token, encoded, payload
    import pyodbc
    cs = ("DRIVER={ODBC Driver 18 for SQL Server};SERVER=tcp:" + HOST +
          ",1433;DATABASE=" + DB + ";Encrypt=yes;TrustServerCertificate=no;" +
          "Connection Timeout=8;")
    try:
        # Exactly one connection attempt; do not create a cursor or execute SQL.
        connection = pyodbc.connect(cs, attrs_before={TOKEN_ATTR: token_buffer}, timeout=8,
                                    autocommit=True)
    except pyodbc.Error as exc:
        # Do not print provider messages (may contain sensitive data).
        states = [str(x) for x in exc.args if isinstance(x, str) and len(x) == 5 and x.isalnum()]
        if "28000" in states:
            return fail("SQL_AUTH_DENIED_OR_DOSGUARD_UNKNOWN")
        if "08S01" in states or "08001" in states:
            return fail("TRANSPORT_FAILURE_OR_UNKNOWN")
        return fail("MI_LOGIN_UNKNOWN")
    except Exception:
        return fail("MI_LOGIN_UNKNOWN")
    try:
        connection.close()
    except Exception:
        return fail("MI_LOGIN_PASS_CLOSE_UNKNOWN")
    print("PATHB_MI_LOGIN=PASS_NO_SQL_EXECUTED", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(run())
