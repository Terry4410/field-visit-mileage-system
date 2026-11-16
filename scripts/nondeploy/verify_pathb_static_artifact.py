#!/usr/bin/env python3
"""Offline verification only. No Azure calls, secrets, or deployment."""
import hashlib
import io
import sys
import zipfile

EXPECTED_SHA = "9fb1a014872648a411dce635c6f1444f8c5a3be5a3a389682ad00bb5a3441a9a"
EXPECTED_API = 'API_BASE_URL: "https://api-fieldvisit-pathb-uat.azurewebsites.net/api/v1"'

def verify(path: str) -> None:
    data = open(path, "rb").read()
    if hashlib.sha256(data).hexdigest() != EXPECTED_SHA:
        raise ValueError("ARTIFACT_SHA=HOLD")
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        names = z.namelist()
        if len(names) != 5 or len(set(names)) != 5:
            raise ValueError("ARTIFACT_SHAPE=HOLD")
        if any(n.startswith("/") or ".." in n.split("/") or "\\" in n or n.endswith("/") for n in names):
            raise ValueError("ARTIFACT_PATHS=HOLD")
        if set(names[:]) != {"index.html", "config.js"} | {n for n in names if n.startswith("assets/")}:
            raise ValueError("ARTIFACT_FILES=HOLD")
        if len([n for n in names if n.startswith("assets/")]) != 3:
            raise ValueError("ARTIFACT_ASSETS=HOLD")
        config = z.read("config.js").decode("utf-8")
        index = z.read("index.html").decode("utf-8")
        if EXPECTED_API not in config or "AUTH_MODE: \"Demo\"" not in config:
            raise ValueError("ARTIFACT_CONFIG=HOLD")
        if "api-fieldvisit-uat-" in config or "localhost:5080" in config:
            raise ValueError("ARTIFACT_LEGACY=HOLD")
        if 'src="./config.js"' not in index:
            raise ValueError("ARTIFACT_INDEX=HOLD")
    print("PATHB_ARTIFACT_OFFLINE=PASS")
    print("AZURE_ACTION=NO")
    print("DEPLOYMENT=NO")

if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit("Usage: verify_pathb_static_artifact.py artifact.zip")
    try:
        verify(sys.argv[1])
    except (OSError, ValueError, zipfile.BadZipFile) as exc:
        sys.exit(str(exc))
