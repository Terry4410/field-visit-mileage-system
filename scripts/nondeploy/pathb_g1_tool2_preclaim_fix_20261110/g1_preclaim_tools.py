#!/usr/bin/env python3
"""Acquire the five immutable G1 Debian packages before claim consumption.

This module is deliberately independent of Azure, Blob, SSH and SQL.  It
accepts only the exact tool list already bound by the original Candidate.  A
single Microsoft-documented geofence redirect is permitted for TOOL_2 only;
the target host, path and query are exact and a second redirect is rejected.
"""
from __future__ import annotations

import hashlib
import os
from pathlib import Path
import re
import stat
import urllib.error
import urllib.parse
import urllib.request

MAX_BYTES = 75_000_000
TIMEOUT_SECONDS = 90
ORIGIN_HOSTS = {"packages.microsoft.com", "deb.debian.org"}
GEOFENCE_HOST = "pmc-geofence.trafficmanager.net"
TOOL2_NAME = "mssql-tools18"
TOOL2_PATH = "/debian/12/prod/pool/main/m/mssql-tools18/mssql-tools18_18.6.1.1-1_amd64.deb"


class ToolHold(Exception):
    """Safe fixed-code failure; never contains a URL or raw exception."""

    def __init__(self, code: str):
        if not re.fullmatch(r"TOOL_[1-5]_[A-Z0-9_]{2,40}", code):
            code = "TOOL_1_INTERNAL_HOLD"
        super().__init__(code)
        self.code = code


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _fail(index: int, suffix: str):
    raise ToolHold(f"TOOL_{index}_{suffix}")


def _validate_origin(index: int, item: dict) -> urllib.parse.SplitResult:
    if set(item) != {"name", "sha256", "url"}:
        _fail(index, "CONFIG_HOLD")
    if not re.fullmatch(r"[a-z0-9-]{2,40}", str(item.get("name", ""))):
        _fail(index, "CONFIG_HOLD")
    if not re.fullmatch(r"[a-f0-9]{64}", str(item.get("sha256", ""))):
        _fail(index, "CONFIG_HOLD")
    parsed = urllib.parse.urlsplit(str(item.get("url", "")))
    if (parsed.scheme != "https" or parsed.hostname not in ORIGIN_HOSTS or
            parsed.username or parsed.password or parsed.fragment or
            parsed.port not in (None, 443) or parsed.query):
        _fail(index, "URL_POLICY_HOLD")
    if index == 2 and (item["name"] != TOOL2_NAME or parsed.path != TOOL2_PATH):
        _fail(index, "URL_POLICY_HOLD")
    return parsed


def _validate_tool2_location(index: int, origin, location: str) -> str:
    target = urllib.parse.urlsplit(urllib.parse.urljoin(origin.geturl(), location))
    if (target.scheme != "https" or target.hostname != GEOFENCE_HOST or
            target.port not in (None, 443) or target.username or target.password or
            target.fragment or target.path != TOOL2_PATH or
            urllib.parse.parse_qsl(target.query, keep_blank_values=True) !=
            [("geofence", "true")]):
        _fail(index, "REDIRECT_POLICY_HOLD")
    return urllib.parse.urlunsplit(target)


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def _open_no_redirect(opener, request, timeout):
    return opener.open(request, timeout=timeout)


def _read_verified_response(index: int, response, expected_sha: str) -> bytes:
    try:
        status = int(response.getcode())
    except Exception:
        _fail(index, "HTTP_HOLD")
    if status != 200:
        _fail(index, "HTTP_HOLD")
    try:
        declared = response.headers.get("Content-Length")
        declared_size = int(declared) if declared is not None else None
    except Exception:
        _fail(index, "SIZE_HOLD")
    if declared_size is not None and not (0 < declared_size <= MAX_BYTES):
        _fail(index, "SIZE_HOLD")
    data = response.read(MAX_BYTES + 1)
    if not (0 < len(data) <= MAX_BYTES):
        _fail(index, "SIZE_HOLD")
    if declared_size is not None and declared_size != len(data):
        _fail(index, "SIZE_HOLD")
    if digest(data) != expected_sha:
        _fail(index, "SHA256_HOLD")
    return data


def fetch_one(index: int, item: dict, destination: Path, *, opener=None) -> Path:
    """Fetch exactly once, with one strictly pinned TOOL_2 redirect at most."""
    parsed = _validate_origin(index, item)
    opener = opener or urllib.request.build_opener(_NoRedirect())
    request = urllib.request.Request(item["url"], headers={
        "User-Agent": "fieldvisit-g1-preclaim-tools/1",
        "Accept-Encoding": "identity",
    })
    response = None
    try:
        response = _open_no_redirect(opener, request, TIMEOUT_SECONDS)
    except urllib.error.HTTPError as exc:
        if index != 2 or exc.code not in {301, 302, 303, 307, 308}:
            _fail(index, "HTTP_HOLD")
        location = exc.headers.get("Location")
        if not location:
            _fail(index, "REDIRECT_POLICY_HOLD")
        target = _validate_tool2_location(index, exc, location)
        redirected = urllib.request.Request(target, headers={
            "User-Agent": "fieldvisit-g1-preclaim-tools/1",
            "Accept-Encoding": "identity",
        })
        try:
            response = _open_no_redirect(opener, redirected, TIMEOUT_SECONDS)
        except Exception:
            _fail(index, "HTTP_HOLD")
    except Exception:
        _fail(index, "HTTP_HOLD")
    try:
        data = _read_verified_response(index, response, item["sha256"])
    finally:
        try:
            response.close()
        except Exception:
            pass
    destination = Path(destination)
    try:
        fd = os.open(destination, os.O_WRONLY | os.O_CREAT | os.O_EXCL |
                     getattr(os, "O_NOFOLLOW", 0), 0o600)
    except Exception:
        _fail(index, "DESTINATION_HOLD")
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
    except Exception:
        try:
            destination.unlink()
        except Exception:
            pass
        _fail(index, "DESTINATION_HOLD")
    return destination


def read_private_tool(index: int, path: Path, expected_sha: str) -> bytes:
    try:
        fd = os.open(Path(path), os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    except Exception:
        _fail(index, "CACHE_HOLD")
    try:
        info = os.fstat(fd)
        if (not stat.S_ISREG(info.st_mode) or info.st_uid != os.geteuid() or
                stat.S_IMODE(info.st_mode) != 0o600 or info.st_nlink != 1 or
                info.st_size <= 0 or info.st_size > MAX_BYTES):
            _fail(index, "CACHE_HOLD")
        chunks = []
        remaining = info.st_size
        while remaining:
            chunk = os.read(fd, min(1024 * 1024, remaining))
            if not chunk:
                _fail(index, "SIZE_HOLD")
            chunks.append(chunk)
            remaining -= len(chunk)
        if os.read(fd, 1):
            _fail(index, "SIZE_HOLD")
        data = b"".join(chunks)
        if len(data) != info.st_size:
            _fail(index, "SIZE_HOLD")
        if digest(data) != expected_sha:
            _fail(index, "SHA256_HOLD")
        return data
    finally:
        os.close(fd)


def verify_private_tool(index: int, path: Path, expected_sha: str) -> int:
    return len(read_private_tool(index, path, expected_sha))


def acquire_all(tools: list[dict], cache_dir: Path, *, opener=None) -> dict:
    if not isinstance(tools, list) or len(tools) != 5:
        raise ToolHold("TOOL_1_CONFIG_HOLD")
    cache_dir = Path(cache_dir)
    info = cache_dir.lstat()
    if (not stat.S_ISDIR(info.st_mode) or info.st_uid != os.geteuid() or
            stat.S_IMODE(info.st_mode) != 0o700):
        raise ToolHold("TOOL_1_CACHE_HOLD")
    manifest = {}
    for index, item in enumerate(tools, 1):
        path = cache_dir / (item["name"] + ".deb")
        fetch_one(index, item, path, opener=opener)
        size = verify_private_tool(index, path, item["sha256"])
        manifest[item["name"]] = {
            "url": item["url"], "sha256": item["sha256"], "size": size,
        }
    return manifest


def copy_preverified(tools: list[dict], cache_dir: Path, url: str,
                     destination: Path, expected_sha: str) -> None:
    """Network-free replacement for the original Parent's download_once."""
    matches = [(i, t) for i, t in enumerate(tools, 1)
               if t.get("url") == url and t.get("sha256") == expected_sha]
    if len(matches) != 1:
        raise ToolHold("TOOL_1_CONFIG_HOLD")
    index, item = matches[0]
    source = Path(cache_dir) / (item["name"] + ".deb")
    data = read_private_tool(index, source, expected_sha)
    destination = Path(destination)
    try:
        fd = os.open(destination, os.O_WRONLY | os.O_CREAT | os.O_EXCL |
                     getattr(os, "O_NOFOLLOW", 0), 0o600)
    except Exception:
        _fail(index, "DESTINATION_HOLD")
    with os.fdopen(fd, "wb") as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())
    verify_private_tool(index, destination, expected_sha)

