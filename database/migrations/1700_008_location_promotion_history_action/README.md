# Migration 1.7.0-008 — Location promotion history action

## Root cause

The API endpoint for admin location promotion is working and reaches the
new backend code, but SQL Server rejects the LocationApprovalHistory insert.

The application writes:

- Action = `PromotedToOfficial`

The legacy constraint:

- `CK_LocationApprovalHistory_Action`

does not yet allow that action. Because the Location update, history insert,
and audit insert are saved in the same transaction, the database error rolls
back `IsTemporary = false`, producing HTTP 500 and leaving the location as
temporary.

## What this migration changes

Only the CHECK constraint on `dbo.LocationApprovalHistory.Action`.

It preserves the exact current constraint definition and appends:

`Action = N'PromotedToOfficial'`

It does NOT:

- change any existing Location data;
- create a second Location;
- change historical Trip/Snapshot data;
- change ApprovalStatus or GeocodingStatus;
- change Project data.

## Prerequisite

`SchemaVersions` must contain `1.7.0-007`.

## Apply

Run `Up.sql` once in Azure SQL UAT, then run `Verify.sql`.

After verification, retry the UI action:

Admin → 地點管理 → 南投縣政府 → 轉正式

Expected:

- HTTP 200
- `IsTemporary = false`
- UI type becomes `正式`
- `轉正式` button disappears
- LocationApprovalHistory contains `PromotedToOfficial`
- the same LocationId remains in use
