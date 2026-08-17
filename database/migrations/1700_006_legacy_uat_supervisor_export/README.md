# 1700_006 Legacy UAT Supervisor Export Capability Correction

## Purpose

This migration fixes the exact legacy UAT supervisor `gov01` that was normalized by `1.7.0-004`.

`1.7.0-004` intentionally created these capabilities as OFF:

- `ExportExcel`
- `ExportPdf`

UAT now requires this supervisor to download both Excel and PDF reports, so `1.7.0-006` changes only those two existing legacy capability rows to ON.

## Safety scope

The migration is deliberately narrow:

- OrganizationCode = `UAT`
- UserCode = `gov01`
- Email = `gov01@example.com`
- requires exactly one current External Supervisor
- requires exactly two legacy export capability rows
- requires both rows to still be OFF and `GrantedByUserId IS NULL`
- stops with an error if capability history appears to have been maintained after `1.7.0-004`
- does not grant export to every Supervisor
- does not change query scope, report layout, mileage/subsidy calculations, approvals, or snapshots

Production environments without an OrganizationCode `UAT` receive only the SchemaVersion registration and no business-data update.

## Apply order

1. `1700_005_people_bulk_confirm_claim/Up.sql`
2. `1700_006_legacy_uat_supervisor_export/Up.sql`
3. `1700_006_legacy_uat_supervisor_export/Verify.sql`

## Expected UAT result

After applying and verifying:

- `gov01` can download Excel.
- `gov01` can download PDF.
- Supervisor remains read-only.
- Existing data scope remains unchanged.
- New/other supervisors continue to use their own `CanExportExcel` / `CanExportPdf` settings.

## Verification note

`1700_004_legacy_supervisor_normalization/Verify.sql` is a point-in-time verification for the state immediately after migration `1.7.0-004`, where both exports were expected to be OFF. Do not use that old verification as the final-state check after `1.7.0-006`; use this folder's `Verify.sql`.
