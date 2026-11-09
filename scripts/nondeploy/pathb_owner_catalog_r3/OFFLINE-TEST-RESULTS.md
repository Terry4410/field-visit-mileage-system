# Path B R4 durable catalog — offline verification, 2026-11-09

- Immutable R3 base: `d34a248394c2a583966fd7bf376a7f97827113c0`; **R3 consumed and unchanged**
- R4 branch: `work/pathb-uat-catalog-durable-integration-reviewonly-20261109`
- Python unit/security/integration contract tests: **82/82 PASS** (isolated offline reconstruction)
- Candidate deterministic ZIP, four members, zip CRC: **PASS**
- Candidate ZIP SHA-256: `c3317aeb4a61ce69c685986aedb1cbb711563f379446619a8a13437691ad3693`
- Binding SHA-256: `bb90f177e3c5dbc0556324ad0f0004d3fc07ab798cc980eee238f0a92cbea13a`
- Exact fixed SQL SHA-256: `d5a4f5825cd95227661921ed8577882053998f0fb103c49b64658f69ccca39b0`
- R4 unique Blob marker: `operator-once/pathb-owner-catalog-once-r4-durable-20261109.json`

## Targeted integration checks
One operation-specific local claim and immutable receipt; candidate hash
correctly bound to receipt; ZIP+manifest includes receipt module; Parent,
Adapter and Child agree on a new R4 namespace; success never printed before
receipt persistence; persistence failure yields HOLD; invalid Operation ID or
unproven Blob claim never reaches SQL; stale R3 authority cannot satisfy R4
authorization decision. Existing R3 target, SSH and token scope safeguards
retained and regression-tested offline.

## Not established
- No App Service `/home/LogFiles` fsync/ownership validation in a real Azure instance.
- No real SSH, Token, Blob, SQL, SQLCMD, cloud disconnect or recovery execution.
- No real 69-table/fingerprint/API/Business UAT proof.
- No independently authorized R4 one-shot live attempt.

Do not run any live adapter from this record. R3 and prior one-shots stay
consumed. Any new live attempt requires separate exact-version security review
and explicit Owner authorization. IT documentation remains deferred.
