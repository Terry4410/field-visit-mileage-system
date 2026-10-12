#!/usr/bin/env python3
"""Fail-closed source-contract tripwire for the intentionally disabled B3 executor.

This is a narrow *source* contract, NOT C# semantic verification or release
authorization. We pin the entire ApproveAsync method body: allowed method
changes require a separately reviewed security decision and cannot be
silently accepted by a trailing throw/comment substring.
"""
from __future__ import annotations
import re

METHOD = re.compile(
    r"(?m)^[ \t]*public\s+async\s+Task<V180B3RequestView>\s+ApproveAsync\s*"
    r"\(\s*Guid\s+id\s*,\s*V180B3Review\s+input\s*,"
    r"\s*CancellationToken\s+ct\s*\)\s*\{"
)
# The exact B3 policy remains: readiness, current live HR / Admin gate,
# then unconditional refusal. NO request lock, approvals, DML or audit.
EXPECTED_BODY = (
    'awaitReadyAsync(ct);'
    'var(_,admin,_)=awaitLiveActorAsync(ct);'
    'if(!admin)thrownewUnauthorizedAccessException("B3_ADMIN_REQUIRED");'
    'thrownewInvalidOperationException("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED");'
)


def require_approve_deny_all(source: str) -> None:
    if not isinstance(source, str):
        raise ValueError("B3_APPROVAL_SOURCE_NOT_TEXT")
    hits = list(METHOD.finditer(source))
    if len(hits) != 1:
        raise ValueError("B3_APPROVAL_ENTRYPOINT_MISSING_OR_DUPLICATE")
    method = hits[0]
    # A narrow pinned-body boundary. A new nested block, method overload,
    # changed return type or rewritten control flow is *not* auto-approved.
    terminator = re.search(r"(?m)^[ \t]{4}\}[ \t]*(?://[^\r\n]*)?$",
                           source[method.end():])
    if terminator is None:
        raise ValueError("B3_APPROVAL_METHOD_BOUNDARY_MISSING")
    body = source[method.end():method.end()+terminator.start()]
    # Remove comments first so an apparent safety throw in a comment
    # cannot fool the validator. The remainder MUST be exact.
    body = re.sub(r"/\*[\s\S]*?\*/|//[^\r\n]*", "", body)
    normalized = re.sub(r"\s+", "", body)
    if normalized != EXPECTED_BODY:
        raise ValueError("B3_APPROVAL_EXECUTOR_BODY_NOT_DENY_ALL")
