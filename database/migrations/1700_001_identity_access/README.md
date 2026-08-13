# v1.7.0-001 Identity & Access Foundation

## Purpose

This migration introduces the v1.7 identity and authorization foundation while
preserving the v1.6.1 runtime compatibility path.

## Additive design

The migration does **not** delete or repurpose:

- `Users`
- `UserRoles`
- `UserTeamScopes`
- trip workflow tables
- snapshot tables

Existing `UserRoles` and `UserTeamScopes` are backfilled into new
effective-dated assignment tables.

`UserTeamScopes` remains the current compatibility projection until the v1.7
authorization cutover is complete.

## HR data

The migration intentionally does **not** invent hire, leave or termination
dates for existing users.

A user without an employment row is treated as a legacy eligible user until HR
Sync is introduced.

## Supervisor design

A supervisor's read visibility is represented by `UserDataScopes`.

`UserDataScopes` must never be interpreted as team membership and must never
grant approve, return or mutation authority.

Excel/PDF exports are separate capabilities in `UserCapabilities`.

## Rollback / forward-fix policy

This package is additive.

Before any v1.7 production data exists, rollback may be done by dropping the
six v1.7 tables in reverse FK order.

After v1.7 assignments, scopes or capabilities have been created, prefer a
forward-fix migration instead of destructive rollback.

Do not modify previously applied v1.6 migration scripts.

## Execution

1. Backup / confirm UAT restore point.
2. Run `Up.sql`.
3. Run `Verify.sql`.
4. Verify application Build/Test.
5. Run v1.6.1 regression tests before enabling new UI.

## Supervisor migration policy

v1.6.1 supervisors had Organization-wide read visibility.

The migration preserves that current visibility by creating an explicit
Organization `UserDataScope`. This prevents an unexpected loss of access
during the v1.7 cutover.

This backfill does **not** mean that every Supervisor is an External user.
`UserType` and `Role` remain independent.

Supervisor Excel/PDF capabilities are **not** automatically backfilled.
Under the v1.7 policy, an administrator must explicitly grant export
capabilities.

## EmployeeNo and external identity

`EmployeeNo` is an HR identifier, not a generic login identifier.

Starting with v1.7:

- internal users may continue to use EmployeeNo;
- external users may have `EmployeeNo = NULL`;
- external users receive a stable `UserCode`;
- external UAT login uses Email;
- production external login is designed for Entra ID B2B after IT Gate;
- a filtered unique index preserves uniqueness for non-null EmployeeNo values.

Do not generate fake employee numbers for external users.

## Effective-dated source of truth and legacy projections

For users with a `UserIdentityProfile`, runtime profile resolution reads
`UserRoleAssignments` and `UserTeamAssignments` using the current business date.

`UserRoles`, `UserTeamScopes`, and `Users.TeamId` remain temporary
v1.6 compatibility projections only.

Current-state admin writes synchronize those legacy projections transactionally.
Future-dated assignments do not overwrite today's legacy projection early.

The v1.7 runtime therefore does not depend on a scheduled job for a future Role
or Team assignment to become effective.

The old v1.6 `SaveUserAccess` write path is transitional and must not be used by
the v1.7 People & Access UI after that UI is switched to the v1.7 endpoints.
