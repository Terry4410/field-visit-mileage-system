# 1700_004_legacy_supervisor_normalization

## Purpose

Normalize the legacy UAT `gov01` demo supervisor into the v1.7
External Supervisor model.

This migration exists because the original UAT seed created `gov01`
as an employee-style user with `EmployeeNo = gov01`. The v1.7 model
requires external supervisors to:

- use `UserCode` as the application identity;
- have no fake `EmployeeNo`;
- not be a Team Member;
- receive read-only visibility through `UserDataScopes`;
- receive Excel/PDF export permission independently through
  `UserCapabilities`.

## Scope

The data correction is intentionally restricted to the exact UAT
identity:

- `OrganizationCode = UAT`
- `UserCode = gov01`
- `Email = gov01@example.com`

It does **not** convert every user with a Supervisor role to External.

On an environment without that exact UAT record, the business-data
portion is intentionally a no-op while SchemaVersion `1.7.0-004` is
still registered.

## Prerequisite

`1.7.0-003` must already be present in `dbo.SchemaVersions`.

`1700_001`, `1700_002`, and `1700_003` are frozen and must not be
modified.

## Execution

1. Review `Up.sql`.
2. Execute `Up.sql` exactly once.
3. Execute `Verify.sql`.
4. Require `VerifyStatus = PASS`.
5. Do not re-run `Up.sql`.

After successful UAT application, this migration becomes frozen.

## Expected UAT result

`gov01` becomes:

- UserType: `External`
- UserCode: `gov01`
- EmployeeNo: `NULL`
- Team membership: none
- Role: Supervisor
- Data Scope: Organization
- Excel export: OFF
- PDF export: OFF
- Demo login remains available through UserCode
