# IT-HANDOFF — Field Visit Mileage System v1.6.0

## 1. Technology baseline

- Frontend: React 18 + TypeScript + Vite
- Backend: ASP.NET Core .NET 8
- Database: Microsoft SQL Server 2019+ / Azure SQL UAT
- Authentication UAT: Demo JWT (Production must switch to Microsoft Entra ID)
- Route / Geocoding v1.6.0: Mock Provider only
- Report: Server-side OpenXML `.xlsx` + SkiaSharp PDF

## 2. Trust boundaries

UI is not an authorization boundary. Backend revalidates Role, Organization, Team Scope, ownership, status, and referenced Location/Project/VisitType IDs. Multi-role users send `X-Active-Role`; the server only accepts a role already present in the signed JWT. `/me` returns the complete role/scope profile.

## 3. Data history

Approved data is immutable from the reporting perspective. `VisitTripSnapshots` / `VisitTripSnapshotStops` are the source of truth for Approved history and report export. Correction creates a new Snapshot version and never overwrites the prior one.

## 4. Data access scope

- Visitor: own trips.
- Leader: all active `UserTeamScopes`.
- Admin: own Organization CRUD/query.
- Supervisor: own Organization read/export only.

## 5. Database migrations

v1.6.0 FINAL migration is under `database/migrations/1600_002_final/` and contains `Up.sql`, `Verify.sql`, `Rollback.sql`. Run only after application Build/Test succeeds. Rollback is conservative and intentionally does not delete generated LocationCode or Snapshot history.

## 6. Provider replacement

Application calls `IRouteCalculationService` and `IGeocodingService`. v1.6.0 dependency injection allows only `Mock`. v1.7.0 can add Google providers without changing Domain workflows.

## 7. Secrets

Never commit SQL connection strings, JWT secrets, Entra secrets/certificates, Google keys, or PDF font files. Use Azure App Service settings / Key Vault / corporate secret store.

## 8. PDF CJK font

PDF searches common CJK system fonts and supports `Report__PdfFontPath`. Production/UAT host must have a licensed Traditional-Chinese-capable system font installed. Font files are not part of the repository.

## 9. Production work still required

Production Entra ID, corporate DMZ/Intranet topology, real Google Routes/Geocoding, production secret management, enterprise monitoring/SIEM, backup/restore and final penetration/security testing remain IT production tasks.
