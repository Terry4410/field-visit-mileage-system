#!/usr/bin/env python3
"""Single-use G1-A: six allowlisted Azure ARM GETs, no SQL / state changes.

No raw Azure response, URL, subscription ID, deployment metadata or exception
is printed or saved. Only non-sensitive controlled classifications may leave
memory. Owner separately authorized management-only read on 2026-10-23.
"""
from __future__ import annotations

import datetime as dt
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

RG = "rg-fieldvisit-uat"
APP = "api-fieldvisit-uat"
SERVER = "sql-fieldvisit-jpe-uat"
DATABASE = "db-fieldvisit-uat"
REPOSITORY = "Terry4410/field-visit-mileage-system"
REF = "refs/heads/post-uat/v1.8.0"
MANAGEMENT_HOST = "https://management.azure.com"
MAX_GETS = 20
MAX_SECONDS = 600
BODY_LIMIT = 262144
GUID = re.compile(r"^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$")


class Stop(Exception):
    pass


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *_args, **_kwargs):
        raise Stop("REDIRECT_REFUSED")


def planned_paths(subscription: str) -> dict[str, str]:
    if not GUID.fullmatch(subscription):
        raise Stop("INVALID_SUBSCRIPTION")
    root = f"/subscriptions/{subscription}/resourceGroups/{RG}"
    sql = f"{root}/providers/Microsoft.Sql/servers/{SERVER}"
    app = f"{root}/providers/Microsoft.Web/sites/{APP}"
    return {
        "rg": f"/subscriptions/{subscription}/resourceGroups/{RG}?api-version=2021-04-01",
        "api": f"{app}?api-version=2024-04-01",
        "server": f"{sql}?api-version=2025-01-01",
        "database": f"{sql}/databases/{DATABASE}?api-version=2025-01-01",
        "retention": f"{sql}/databases/{DATABASE}/backupShortTermRetentionPolicies/default?api-version=2025-01-01",
        "deployments": f"{app}/deployments?api-version=2025-05-01",
    }


def validate_target(actual: object, expected: str) -> None:
    if not isinstance(actual, dict) or not isinstance(actual.get("id"), str) or actual["id"].lower() != expected.lower():
        raise Stop("TARGET_MISMATCH")


def classify(pages: dict[str, dict], subscription: str, now: dt.datetime) -> dict[str, str]:
    paths = planned_paths(subscription)
    for key in ("rg", "api", "server", "database", "retention", "deployments"):
        if key not in pages or not isinstance(pages[key], dict):
            raise Stop("INCOMPLETE_EVIDENCE")
    for key in ("rg", "api", "server", "database", "retention"):
        expected = paths[key].split("?", 1)[0]
        validate_target(pages[key], expected)
    if any(isinstance(p.get("nextLink"), str) and p["nextLink"] for p in pages.values()):
        raise Stop("PAGINATION_REFUSED")
    api_state = pages["api"].get("properties", {}).get("state")
    db_state = pages["database"].get("properties", {}).get("status")
    if not isinstance(api_state, str) or not isinstance(db_state, str):
        raise Stop("STATE_VISIBILITY_UNKNOWN")
    retention = pages["retention"].get("properties", {}).get("retentionDays")
    earliest = pages["database"].get("properties", {}).get("earliestRestoreDate")
    pitr = "HOLD"
    if isinstance(retention, int) and not isinstance(retention, bool) and 1 <= retention <= 35 and isinstance(earliest, str):
        try:
            stamp = dt.datetime.fromisoformat(earliest.replace("Z", "+00:00"))
            if stamp.tzinfo is not None and stamp <= now and (now - stamp) <= dt.timedelta(days=retention + 1):
                pitr = "CONFIRMED_METADATA_ONLY"
        except ValueError:
            pass
    deployment = pages["deployments"].get("value")
    if not isinstance(deployment, list) or len(deployment) > 100:
        raise Stop("DEPLOYMENT_SHAPE_REJECTED")
    return {
        "G1A": "OBSERVED_MANAGEMENT_ONLY",
        "Target": "PASS",
        "ApiState": "RUNNING" if api_state.lower() == "running" else "OTHER",
        "DatabaseState": "ONLINE" if db_state.lower() == "online" else "OTHER",
        "PITR": pitr,
        "AppDeploymentHistory": "RECORDED_NOT_BINARY_PROOF" if deployment else "NOT_PROVEN",
        "ActiveBinaryDigest": "UNKNOWN",
        "GitHubPages": "UNKNOWN",
        "SqlPrincipal": "UNKNOWN",
        "Recovery": "HOLD",
        "BusinessUAT": "NO_GO",
    }


class FixedArmReader:
    def __init__(self, subscription: str, token: str):
        self.paths = planned_paths(subscription)
        if len(self.paths) > MAX_GETS or len(self.paths) != len(set(self.paths.values())):
            raise Stop("REQUEST_PLAN_INVALID")
        self.allowed_urls = {MANAGEMENT_HOST + p for p in self.paths.values()}
        self.token = token
        self.calls = 0
        self.started = time.monotonic()
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())

    def get(self, relative: str) -> dict:
        url = MANAGEMENT_HOST + relative
        if url not in self.allowed_urls or self.calls >= MAX_GETS or time.monotonic() - self.started >= MAX_SECONDS:
            raise Stop("REQUEST_BOUNDARY")
        self.calls += 1  # Count BEFORE request, including denied/error/redirect.
        request = urllib.request.Request(url, headers={"Authorization": "Bearer " + self.token, "Accept": "application/json"}, method="GET")
        try:
            with self.opener.open(request, timeout=20) as reply:
                if reply.status != 200 or reply.geturl() != url:
                    raise Stop("HTTP_OR_REDIRECT_STOP")
                raw = reply.read(BODY_LIMIT + 1)
                if len(raw) > BODY_LIMIT:
                    raise Stop("RESPONSE_LIMIT")
                decoded = json.loads(raw)
                if not isinstance(decoded, dict):
                    raise Stop("JSON_SHAPE")
                return decoded
        except (urllib.error.URLError, urllib.error.HTTPError, UnicodeError, ValueError, OSError) as error:
            raise Stop("HTTP_OR_JSON_STOP") from error


def private_azure_context() -> tuple[str, str]:
    env = os.environ
    if env.get("GITHUB_ACTIONS") != "true" or env.get("GITHUB_REPOSITORY") != REPOSITORY or env.get("GITHUB_REF") != REF or env.get("RUNNER_DEBUG") == "1":
        raise Stop("RUNNER_CONTEXT")
    subscription = env.get("AZURE_SUBSCRIPTION_ID", "")
    if not GUID.fullmatch(subscription) or env.get("AZURE_RESOURCE_GROUP") != RG or env.get("AZURE_SQL_SERVER_NAME") != SERVER or env.get("AZURE_SQL_DATABASE") != DATABASE:
        raise Stop("FIXED_TARGET")
    def az(*args: str) -> str:
        try:
            r = subprocess.run(["az", *args], text=True, capture_output=True, timeout=25, check=True)
            return r.stdout.strip()
        except Exception as e:
            raise Stop("OIDC_CONTEXT") from e
    if az("account", "show", "--query", "id", "-o", "tsv").lower() != subscription.lower():
        raise Stop("WRONG_SUBSCRIPTION")
    token = az("account", "get-access-token", "--resource", MANAGEMENT_HOST + "/", "--query", "accessToken", "-o", "tsv")
    if len(token) < 100 or "\n" in token or "\r" in token:
        raise Stop("INVALID_TOKEN")
    return subscription, token


def main() -> int:
    stage = "SOURCE_GUARD"
    try:
        subscription, token = private_azure_context()
        stage = "MANAGEMENT_GET"
        reader = FixedArmReader(subscription, token)
        pages = {}
        for key, path in reader.paths.items():
            stage = key
            pages[key] = reader.get(path)
        now = dt.datetime.now(dt.timezone.utc)
        stage = "CLASSIFY"
        outcome = classify(pages, subscription, now)
        if reader.calls != 6 or time.monotonic() - reader.started > MAX_SECONDS:
            raise Stop("REQUEST_COUNT_OR_DEADLINE")
        print("G1AManagement=METADATA_COLLECTED; GetCount=6; SQL=NOT_ACCESSED; LiveWrites=NONE;")
        for key, value in outcome.items():
            print(f"{key}={value};")
        print("ObservedUtc=" + now.strftime("%Y-%m-%dT%H:%M:%SZ") + ";")
        return 0
    except Exception:
        stage = stage if stage in {"SOURCE_GUARD", "MANAGEMENT_GET", "rg", "api", "server", "database", "retention", "deployments", "CLASSIFY"} else "UNKNOWN"
        print(f"G1AManagement=STOP; Stage={stage}; Details=SUPPRESSED; SQL=NOT_ACCESSED; LiveWrites=NONE;")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
