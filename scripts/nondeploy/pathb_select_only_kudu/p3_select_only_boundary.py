#!/usr/bin/env python3
"""NONDEPLOY only. Pure orchestration boundary for frozen P3 preflight SQL.

NO standalone live/CLI entrypoint. The runner/callback must independently
establish exact artifact provenance, Owner identity, approved encrypted private
connection, one-use authorization, safe token delivery and cleanup. This module
must not be used as an Azure/SQL execution authorization or packaged as the
existing frozen installer. It does not obtain a token or connect to anything.
"""
import hashlib

FIXED_SELECT_SHA256 = 'e87dbd54d4740254b6f0041fe330cc4af71e51a214964c39e1001c4437075add'
MAX_RESPONSE_BYTES = 65536


def select_only(preflight_sql, *, verified_readonly_context, one_select, verify_row):
    """Call an injected SELECT-only client no more than once; return fixed labels.

    `verified_readonly_context` is a *separately attested* boolean supplied by
    a reviewed caller. It cannot itself attest transport, identity or tokens.
    A callback exception or invalid response is terminal HOLD: no retry.
    """
    if verified_readonly_context is not True:
        return 'HOLD_CONTEXT'
    if type(preflight_sql) is not bytes or hashlib.sha256(preflight_sql).hexdigest() != FIXED_SELECT_SHA256:
        return 'HOLD_SQL_HASH'
    if not callable(one_select) or not callable(verify_row):
        return 'HOLD_CALLBACK'
    try:
        # Only one callback; no marker, no installer, no storage token, no retry.
        result = one_select(preflight_sql)
        if type(result) is not bytes or len(result) > MAX_RESPONSE_BYTES:
            return 'HOLD_RESPONSE'
        if verify_row(result) != 'DATABASE_EMPTY_PASS':
            return 'HOLD_ROW'
        return 'PASS_SELECT_ONLY'
    except BaseException:
        # Safely suppress external SQL/identity/token/exception data.
        return 'HOLD_EXECUTION'
