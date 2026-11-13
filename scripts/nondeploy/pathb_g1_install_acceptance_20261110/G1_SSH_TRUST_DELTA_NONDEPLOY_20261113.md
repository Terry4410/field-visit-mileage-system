# Path B G1 SSH trust/transport minimal delta (NONDEPLOY)

Base commit: `404704278e838a40b0569fc8865d05426db86914`.

## Evidence and limitations

- Legacy source pin: `SHA256:qlMlSfV+nfx3Y5BdqQC+4WY1VFChCdRZMjqE+T/NgOI`.
- Owner-reported Portal SSH and official tunnel observation: `SHA256:FKM/np9YzcuZaOsPqy3TL4gcae0szuZ3Z0dEG06KyZk`.
- The new observed value is **not automatically approved** as a trusted replacement. Independent out-of-band verification and exact App instance binding are required.
- Password authentication to the noninteractive tunnel failed; do not guess or reset passwords.
- Portal SSH success does **not** establish noninteractive secret-safe transport. Do not send SQL tokens through an unreviewed interactive terminal.
- Added a pure evidence gate and fail-closed transport contract only; no live executor, SSH connection, token handoff or SQL execution.
- Original `p3_ssh_transport_contract.py` and all frozen SQL, installer, seal and claim sources remain unchanged.

## Release decision

`SSH_TRUST=HOLD_PENDING_INDEPENDENT_APPROVAL`

`LIVE_TRANSPORT=HOLD`

`SQL_EXECUTED=NO`

`DDL_AUTHORIZED=NO`

`BUSINESS_UAT=NO_GO`

Run offline only: `python3 -m unittest test_pathb_ssh_trust_delta_20261113.py` from this directory.

Next gate is independent review of exact host key and a secure authenticated noninteractive channel with bounded deadlines, no secret echo, durable once semantics and disconnect behavior. This delta does not grant live authorization.
