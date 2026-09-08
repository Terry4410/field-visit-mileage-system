# v1.8.0 UAT Master Data Readiness

Status: Business preparation template only. No seed/import has been executed and no value has been inferred by AI.

Workbook: `templates/POST-UAT-v1.8.0-UAT-MASTER-DATA-SEED-TEMPLATE.xlsx`

## Ownership and gate

Business is accountable for the codes, names, relationships, dates and rates in the workbook. HR/Business data owners must complete it; the application team validates structure and referential integrity; IT approves the controlled import mechanism. AI must not invent missing master data, derive organizational ownership from names, or choose rates/effective dates.

The workbook is not an executable SQL script. A completed workbook must pass Business sign-off and a non-writing preview/validation report before any separately approved import.

## Required sheets and import order

| Order | Sheet | Business decision represented | Required upstream key |
|---:|---|---|---|
| 1 | `Centers` | Center master and lifecycle | Existing `OrganizationCode` |
| 2 | `Team-Center` | Effective Team → Center ownership | Existing `TeamCode`; completed Center |
| 3 | `Deployment Sites` | Site master and its authoritative Location | Center; existing `LocationCode` |
| 4 | `Team-Site` | Sites a Team may use during a period | Team-Center and Deployment Site |
| 5 | `Employment-Primary Site` | One Primary Deployment Site per Employment/date | Existing `EmployeeNo`; Team-Site/Site |
| 6 | `Mileage Rates` | Organization-specific Motorcycle/Car rate lifecycle | Existing `OrganizationCode` |

## Mandatory validation rules

- Codes are identifiers and are preserved as text, including leading zeroes.
- Dates use `yyyy-mm-dd`; `EffectiveTo` may be blank for open-ended records.
- Periods are inclusive. For consecutive assignments, the next start date is the day after the prior end date.
- `EffectiveTo` cannot precede `EffectiveFrom`.
- Center and Team must belong to the same Organization.
- Deployment Site period must sit within its Center period.
- Team-Site must sit within a valid same-Center Team-Center assignment.
- Employment Primary Site must have `IsPrimary=TRUE`; one Employment cannot have overlapping Primary Site periods.
- Mileage rate must be numeric, non-negative and use only `Motorcycle` or `Car`; active periods cannot overlap for the same Organization + VehicleType.
- `LocationCode`, `EmployeeNo`, Team and Organization keys must already exist or be included in an earlier approved import source; fuzzy/name matching is prohibited.
- Blank required fields, unresolved key matches, overlaps and duplicate business keys are blocking errors—not values for AI or the importer to repair.

## Sheet definitions

### Centers

Required: `OrganizationCode`, `CenterCode`, `CenterName`, `EffectiveFrom`, `IsActive`. Optional: `EffectiveTo`, `Notes`.

Business key: `OrganizationCode + CenterCode`.

### Team-Center

Required: `OrganizationCode`, `TeamCode`, `CenterCode`, `EffectiveFrom`. Optional: `EffectiveTo`, `ChangeReason`.

One Team may have only one Center on a given date.

### Deployment Sites

Required: `OrganizationCode`, `CenterCode`, `SiteCode`, `SiteName`, `LocationCode`, `EffectiveFrom`, `IsActive`. Optional: `EffectiveTo`, `ChangeReason`, `Notes`.

`LocationCode` is a reference to the existing company Location Master; Google Geocoding must not create this master row.

### Team-Site

Required: `OrganizationCode`, `TeamCode`, `SiteCode`, `EffectiveFrom`. Optional: `EffectiveTo`.

### Employment-Primary Site

Required: `OrganizationCode`, `EmployeeNo`, `SiteCode`, `IsPrimary`, `EffectiveFrom`. Optional: `EffectiveTo`.

This template is for Primary assignments, so Business must enter `TRUE` for `IsPrimary`; secondary-site requirements, if any, require a separate decision.

### Mileage Rates

Required: `OrganizationCode`, `RuleName`, `VehicleType`, `RatePerKm`, `EffectiveFrom`, `IsActive`. Optional: `EffectiveTo`.

Business must provide both Motorcycle and Car decisions where those vehicles are allowed. The template contains no suggested amount.

## Readiness checklist

- [ ] Business data owner named for each sheet.
- [ ] Organization, Team, Location and Employment reference extracts are dated and approved.
- [ ] All required sheets are complete; no placeholder/example rows remain.
- [ ] Effective-date overlaps and gaps have been reviewed by Business.
- [ ] Motorcycle/Car rates and effective dates have Finance/Business approval.
- [ ] Cross-sheet key validation and duplicate report are zero-error.
- [ ] Import preview row counts and reject report are approved.
- [ ] Import identity/environment/workflow is separately approved.
- [ ] UAT restore/concurrency gate is approved before any write.

Completion of this readiness package does not authorize Azure SQL Migration, seed/import, role changes or deployment.
