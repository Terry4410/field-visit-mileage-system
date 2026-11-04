# Path B Azure configuration inventory

This inventory records names and configuration requirements only. Secret values
are deliberately excluded.

## Existing resources

| Resource | Exact value | Current evidence |
|---|---|---|
| Resource group | `rg-fieldvisit-uat` | Existing |
| Region | Japan East | Existing Path B resources |
| App Service plan | `asp-fieldvisit-pathb-uat` / Linux B1 | Existing |
| API App Service | `api-fieldvisit-pathb-uat` | Running; VNet integrated |
| SQL server | `sql-fieldvisit-jpe-pathb-uat.database.windows.net` | Public access Disabled; TLS 1.2 |
| SQL database | `db-fieldvisit-pathb-uat` | Exact Path B target only; current schema state requires fresh readback |
| VNet | `vnet-fieldvisit-pathb-uat`, `10.90.0.0/16` | Existing |
| App subnet | `10.90.1.0/24` | Existing VNet integration |
| Private endpoint subnet | `10.90.2.0/24` | Existing |
| SQL private endpoint | `10.90.2.4` | Approved/Succeeded evidence |
| Private DNS | SQL private zone linked to Path B VNet | Existing evidence |
| Storage | `stfvsc261026a/schema-evidence` | Original installer custody |
| SQL firewall | 0 rules | Fresh readback after stopped attempt |

Frontend hosting name and current deployed package identity were not proven in
the latest execution checkpoint. They must be read from Azure and bound to the
accepted Frontend digest before Business UAT GO.

## Required App Service configuration names

Values belong in Azure App Service settings, Key Vault or the future IT secret
store. Never add values to GitHub, screenshots or this package.

| Setting group | Required configuration |
|---|---|
| Database | Exact Path B server/database; runtime identity must use object-scoped permissions |
| `App` | Version `1.8.0`; schema profile `PathBNewEmpty` after successful installation |
| `Auth` | UAT `Demo`; unique Visitor/Leader/Admin accounts; strong shared secret held privately |
| `Cors` | Exact Path B Frontend origin only |
| `Providers` | Route `GoogleMaps` for canonical VIS02/VIS03; key stored privately |
| `OptionC` | Candidate and Approval enabled only after signed activation |
| `BackgroundJobs` | Worker enabled for mileage processing; cleanup disabled |
| `Swagger` | UAT policy decision; disable for Production unless IT approves |
| Release trust | Signed activation binds source, API, Frontend, schema fingerprint, SQL principal and exact target |

## Required identities and permissions

- Migration operator: temporary, exact database only, sufficient DDL for one
  create-only installation; remove or disable after successful installation.
- Runtime principal: no `sysadmin`, `db_owner`, `db_datawriter`, database
  `ALTER` or `CREATE TABLE`; object-scoped CRUD only; protected migration tables
  deny INSERT/UPDATE/DELETE; bounded VIEW DEFINITION where runtime validation needs it.
- Owner interactive administrator: retained only for controlled UAT preparation;
  no credentials are transferred in this package.

