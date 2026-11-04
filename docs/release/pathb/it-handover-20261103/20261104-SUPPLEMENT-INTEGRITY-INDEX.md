# PR #85 supplementary document inventory — 2026-11-04

**Status: documentation only; not SQL installation permission.** This index uses GitHub's native Git blob SHA-1 identifiers for exact file identity, **not** a new SHA-256 checksum claim. Original nine file SHA-256 checksums are preserved verbatim in `checksums.sha256` and apply ONLY to their named files. A final IT release must issue a fresh all-files SHA-256 manifest after UAT and then freeze its commit. Adding further documentation changes makes this inventory historical, not automatically current.

File snapshot prior to this index refresh: `3a81eb302f3c02d525a51eff46bfc91e3d6cc6fb` (original documents and November 4 addenda).

| File | Git blob SHA-1 |
|---|---|
| `AZURE-CONFIG-INVENTORY.md` | `bd3edfda1ef3698c13f4dc58785df6eecfb01315` |
| `BUSINESS-UAT-25-TRACKER.md` | `9b0a2ddc8cfd4282bbb66f190fc55055c00361be` |
| `DELIVERY-STATUS.md` | `90ba7e201385c3bdc381c59a0fbfc9c5dbaa5feb` |
| `INSTALL-DEPLOY-SOP.md` | `fdc7e06850c1ffe0a9b31dacf2f67b34f69ba64d` |
| `README.md` | `fd85d1fc78465b32fdcb049c313200cd89eb6e65` |
| `RECOVERY-ROLLBACK-SOP.md` | `33dd7be52d6a9c9e6b0c8900e20714fe60364626` |
| `SOURCE-ARTIFACT-INVENTORY.md` | `61e5600475c68ee7c9600cd65abc278a5c2e1324` |
| `UAT-ROLE-ACCOUNT-SPEC.md` | `4ebf9b0a0a6b5dd9f3500ae0dec1c803b116e533` |
| `handover-manifest.json` | `bbc68ab12a9c59fe7e45c855950f707cb81a7510` |
| `20261104-LIVE-GATE-ADDENDUM.md` | `9d229d1732e59781e72725a5b7f5fd1b0ea40d55` |
| `20261104-PORTAL-TRANSPORT-ENGINEERING-DECISION.md` | `5a02eeb0146f2fd7bb296adb0e339643bfd94a95` |
| `20261104-ARTIFACT-RETENTION-ACTION.md` | `34ce0071eb6a6c41433414e6bc84725b11b90ca2` |

## Separate integrity coverage

- `checksums.sha256` remains the original nine-file SHA-256 manifest (it does not include this index or the two November 4 supplemental documents).
- Three supplemental files are pinned above by their verified, exact Git blob IDs and the snapshot commit; they have not been folded into the November 3 manifest.
- Before IT acceptance, take a new final commit snapshot and generate/verify a complete SHA-256 checksum list including all included files, then record actual UAT status, deployed binaries, SQL schema fingerprint, ACLs and recovery evidence. Do not imply that the documented 69 tables or 25 UAT cases are currently completed.

Canonical live record: [Issue #41](https://github.com/Terry4410/field-visit-mileage-system/issues/41). Critical SQL installation is **HOLD**, business UAT **NO-GO**. No secrets or tokens belong in this document.
