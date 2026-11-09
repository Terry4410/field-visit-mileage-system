# Path B Owner Catalog — minimal durable receipt delta (NONDEPLOY)

Base: `d34a248394c2a583966fd7bf376a7f97827113c0` (R3 consumed; do NOT rerun).

One new **standalone** receipt helper, no Azure/SQL/SSH/Blob calls. It is *not* yet wired into a new one-shot SQL executor. It must not be run live or used to unlock the old R3. Future separately approved candidate must bind to a **new** operation ID, claim namespace, and Child claim path, not the old R3 or `fv-pathb-owner-catalog-v1-20261109` filenames.

In a future Child, when a single new SQL attempt completes, `store(operation_id, candidate_sha, status, sql_attempted)` must run **before** emitting the terminal status; if store fails, return HOLD and never retry SQL. A separate, explicitly authorized App read-only recovery can call `read_once(...)` with the same exact IDs and return only the safe status/attempt flag/hash. No SQL retry, no marker reset. `ABSENT` or `HOLD_RECEIPT_UNKNOWN` never proves SQL was not attempted.

Security: status allowlist, fixed exact fields, no token/account/SQL details, O_EXCL+NOFOLLOW, fsync file+directory, no modification on read, no deletion on write failure, rejects symlinks/tamper/cross-operation. Mounted /home durability and remote App readback require future acceptance testing; filesystem sync alone cannot guarantee availability on all Azure storage mounts. The test helper uses isolated temporary directories.

Local offline test: 12/12 PASS; Python compile PASS. No new SQL authorization exists. Owner remains single operator; no local tool install required for eventual Cloud Shell operation. Do not redo previous 62/62 suite unless integration modifies contracts.
