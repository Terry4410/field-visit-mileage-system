#!/usr/bin/env python3
"""Read-only WORK security check for checked-in ASP.NET appsettings overrides.

Only repository JSON is inspected. Azure App Service env vars, Key Vault and
external configuration providers are NOT visible here, and require separate
IT runtime attestation. No release GO condition is ever produced.
"""
from __future__ import annotations

import json
from pathlib import Path


def checked_in_config_is_off(api_directory: Path) -> tuple[bool, str]:
    files = sorted(api_directory.glob("appsettings*.json"))
    base = api_directory / "appsettings.json"
    if not base.is_file() or not files:
        return False, "Missing appsettings.json"
    found = []
    try:
        for file in files:
            settings = json.loads(file.read_text(encoding="utf-8"))
            if not isinstance(settings, dict):
                return False, "Invalid JSON root in " + file.name
            package = settings.get("PackageB", {})
            if not isinstance(package, dict):
                return False, "Invalid PackageB in " + file.name
            b3 = package.get("B3", {})
            if not isinstance(b3, dict):
                return False, "Invalid PackageB:B3 in " + file.name
            if "Enabled" in b3 and b3["Enabled"] is not False:
                return False, "B3 Enabled override in " + file.name
            if file == base and b3.get("Enabled") is not False:
                return False, "Base B3 OFF requirement missing"
            auth = settings.get("Auth", {})
            if not isinstance(auth, dict):
                return False, "Invalid Auth in " + file.name
            guard = auth.get("LiveSessionGuard", {})
            if not isinstance(guard, dict):
                return False, "Invalid Auth:LiveSessionGuard in " + file.name
            if "Enabled" in guard and guard["Enabled"] is not False:
                return False, "LiveSessionGuard Enabled override in " + file.name
            if file == base and guard.get("Enabled") is not False:
                return False, "Base LiveSessionGuard OFF requirement missing"
            found.append(file.name)
    except (OSError, ValueError, UnicodeError) as exc:
        return False, "Malformed/unreadable appsettings JSON: " + str(exc)
    return True, "checked-in OFF: " + ", ".join(found)
