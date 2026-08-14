# 1700_003_auth_people_bulk

## Purpose

v1.7 Authentication and People Administration foundation.

This forward migration adds stable Microsoft Entra ID identity binding to the
existing `UserIdentityProfiles` table.

## Columns

- `EntraTenantId UNIQUEIDENTIFIER NULL`
- `EntraObjectId UNIQUEIDENTIFIER NULL`

The two values are a pair. A row must contain either both values or neither.

## Identity rule

Microsoft Entra authentication must use the stable:

`Tenant ID + Object ID`

pair as the identity key.

Email, display name, employee number and UPN must not be treated as the
permanent Entra identity key.

## Compatibility

This migration is additive.

It does not modify:

- `Users`
- `UserRoles`
- `UserTeamScopes`
- Trip / mileage / approval workflow
- approved Snapshots
- Demo Login behavior

Entra authentication is enabled in a later application checkpoint.

## People batch administration

The Entra binding columns will also be available to the v1.7 People
Administration export/import workflow.

Internal HR facts remain separate from authorization settings.

## Deployment state

**NOT YET APPLIED.**

Do not execute this migration in Azure SQL until:

1. Backend build succeeds.
2. Unit tests pass.
3. Migration diff has been reviewed.
4. UAT confirms `1.7.0-002` is already present.

After the first successful UAT application, this migration becomes immutable.
Any later database change must use a new forward migration.
