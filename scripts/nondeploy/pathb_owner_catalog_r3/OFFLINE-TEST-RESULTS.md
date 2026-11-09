# Offline verification — 2026-11-09 UTC

- Python compile: PASS
- Focused security/integration/unit tests: 62/62 PASS
- Candidate ZIP CRC: PASS (3/3 members)
- Candidate ZIP SHA-256: `025a054b18440996a9cec51c020f69e7eb4bee1b2482f86ffacd1b01932bb4a6`
- Binding index SHA-256: `e1120fcaa2c6faac5120dfcd4219f73f8039f8bfd46000633d7cac2503baae03`
- Azure/Token/Blob/SSH/SQL: NOT RUN
- Installer/DDL/DML/deployment/RBAC/network change: NOT RUN

The tests cover deterministic build and byte binding, tamper rejection, exact
Owner/tenant/audience/`nbf`/TTL token validation, one-token counters, exact App
resource and unique instance checks, complete-line terminal markers, pinned SSH
host configuration, no-echo readiness, atomic conditional Blob create, unknown
claim handling, local O_EXCL journal, fixed catalog result shape, partial schema
rejection, five pinned SQL tool files, cleanup scope, and no replay.

The live adapter remains authorization-locked. A fresh authorization must bind
the eventual branch HEAD, adapter SHA, candidate SHA, binding SHA, exact instance
ID hash, operation ID, and an expiry no more than 30 minutes in the future.
