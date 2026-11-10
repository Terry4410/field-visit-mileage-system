# G1 SSH tunnel cleanup merged — NONDEPLOY ONLY

This is the original G1 integrated candidate, with exactly one runtime-file patch from the independently supplied targeted SSH cleanup delta. The immutable original 14 SQL layers, frozen installer, Preflight, postcheck, token adapter and 20 other runtime-file hashes remain unchanged.

## Binding and verification
- Original full ZIP SHA-256: `5ebc77d35297aedded73f929478fb9b0913a2f77593a0e2f7041f79bd10ae909`
- Original `p3_g1_pinned_ssh.py` SHA-256: `4b970f2f4d2f590e3b4a0879143a246ff4551410145680edbfc81ba47838d77a`
- Merged `p3_g1_pinned_ssh.py` SHA-256: `1d5c68d425d29fd65c17f2ee7d108324944c810745a04e8c421e529c062cd882`
- Original 41 offline tests: previously PASS, retained historical receipt only; not rerun here.
- Two new fault-injection tests plus one existing exact-binding test run after merge: **3/3 PASS**.
- README and original test result retained; new receipt in `TEST-RESULTS-G1-SSH-DELTA-MERGE.txt`.

## Safety
The `AppSshTunnel.start()` failure paths now call `self.close()` before rethrowing. This prevents the launched Azure CLI tunnel subprocess from remaining open if host-key verification or initialization fails. The patch does NOT establish real SSH compatibility, reliable App-instance pinning, Azure Blob access, SQL installation or Business UAT. NONDEPLOY, no owner live authorization, no Cloud Shell commands. Do not use as a live installer until independent integrated security review and separate new exact-SHA authorization.
