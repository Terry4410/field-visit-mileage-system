# Path B Owner catalog R3 — NONDEPLOY candidate

This directory contains the independently authorization-locked read-only SQL
catalog executor. It does not install schema, deploy an app, modify permissions,
or expose SQL publicly.

The sequence is fixed: exact artifact and Owner/tenant/subscription checks → one
exact App instance → pinned official SSH tunnel → exact stage and no-echo child
readiness → one conditional Blob `If-None-Match: *` claim → one Owner SQL token →
one encrypted connection and one fixed catalog SELECT → sanitized receipt → a
second independent, ownership-bounded cleanup session. Any unknown state is a
HOLD and the local journal/Blob claim are never reset or replayed.

`owner_catalog_azure_adapter_REVIEW_ONLY.py` is inert without `--execute` and a
fresh exact-SHA Owner authorization JSON. Creating that JSON and running the
adapter are outside this NONDEPLOY engineering delivery.
