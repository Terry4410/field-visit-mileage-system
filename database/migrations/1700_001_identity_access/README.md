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
