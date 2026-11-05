"""Fail-closed NONDEPLOY G1 bridge source/identity contract. No Azure or SQL imports."""
import os
import pathlib
import re
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[2]
REPO = "Terry4410/field-visit-mileage-system"
BRANCH = "refs/heads/work/g1-readonly-bridge-nondeploy-20261021"
BASE = "be4d25e2c788c78cb108d2f7c819118f036d674d"
ALLOWED = {".github/workflows/g1-bridge-guards-nondeploy.yml",
           "scripts/ci/g1_bridge_guard.py", "scripts/ci/test_g1_bridge_guard.py",
           "scripts/ci/g1_bridge_connection_candidate.ps1",
           "scripts/ci/g1_bridge_handoff_nondeploy.ps1"}

class Stop(Exception):
    pass

def classify(identity_origin, claims_api_principal=False):
    if identity_origin != "DIAGNOSTIC_OIDC":
        raise Stop("UNREVIEWED_IDENTITY")
    if claims_api_principal:
        raise Stop("API_PRINCIPAL_SUBSTITUTION")
    return "DIAGNOSTIC_ONLY_API_PRINCIPAL_UNKNOWN"

def decide_live(*, azure_access=False, sql_open=False, authorization=False):
    if azure_access or sql_open or authorization:
        raise Stop("LIVE_OPERATION_NOT_IN_SCOPE")
    return "NONDEPLOY_ONLY"

def verify_source(repo, ref, sha, base, changed):
    if repo != REPO or ref != BRANCH or not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise Stop("SOURCE_REJECTED")
    if base != BASE or not changed or not set(changed).issubset(ALLOWED):
        raise Stop("UNREVIEWED_DELTA")
    return "SOURCE_PINNED"

def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, stderr=subprocess.DEVNULL, text=True).strip()

def main():
    try:
        sha = git("rev-parse", "HEAD")
        if os.environ.get("GITHUB_SHA") != sha or os.environ.get("EXPECTED_SHA") != sha:
            raise Stop("SOURCE_REJECTED")
        if git("merge-base", BASE, "HEAD") != BASE:
            raise Stop("SOURCE_REJECTED")
        changed = git("diff", "--name-only", BASE, "HEAD").splitlines()
        verify_source(os.environ.get("GITHUB_REPOSITORY"), os.environ.get("GITHUB_REF"),
                      sha, BASE, changed)
        if any(os.environ.get(k) for k in ("AZURE_CLIENT_ID", "AZURE_SUBSCRIPTION_ID",
                                           "UAT_SQL_CONNECTION", "PRODUCTION_SQL_CONNECTION")):
            raise Stop("CREDENTIAL_ENV_REJECTED")
        classify("DIAGNOSTIC_OIDC")
        decide_live()
        print("BridgeGuards=PASS; AzureAccess=FALSE; SqlOpened=FALSE; ApiPrincipal=UNKNOWN; G1=HOLD;")
    except (Stop, subprocess.SubprocessError, OSError):
        print("BridgeGuards=STOP; Details=SUPPRESSED;")
        raise SystemExit(1)

if __name__ == "__main__":
    main()
