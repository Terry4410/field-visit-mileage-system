# Field Visit v1.8.0 — IT Clean Build evidence and release boundary (NONDEPLOY)

This is a *review* record for the company IT handoff, **not an executable/production installer** and **not authorization** to deploy, grant permissions, change login, or run SQL.

## Business baseline

- Business Owner approved Path B **Business UAT ACCEPT WITH OPEN ITEMS** on 2026-11-23: [Issue #41 decision](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-7099388100).
- The 26 original P0 IDs remain accounted for: 19 scoped actual PASS; 3 canceled Supervisor N/A; 2 superseded old business rules; 2 retained partial limitations `QUERY-02` and `E2E-01`. The **new Daily Plan** proposal is still pending business-user acceptance and **is not part of the released UAT baseline**.
- Never overwrite historical accepted test runs, status or signed release evidence. CI/SQL checks are **not Business UAT**.

## Locked component identities

| Component | Git source | Status |
| --- | --- | --- |
| Selected Path B API | `ab71aaa96d9ac259f66386555812448284527d15` | Accepted UAT source; selected package mount DLL byte fingerprint not independently confirmed |
| Selected Path B Frontend | `90fe9bd61c24be57ccfaf6d450bf053e9c646ac1` | Accepted UAT source from distinct line of commits |
| Historical Clean Build RC | `9d431ea318fcec34f04f3cc36a8fb6d01d00c48a` | 57 EF mapped tables; source used to build disposable 69-table SQL Server 2022 clean target |
| Corrected final 020 source | Git blob `ce7bbb6f204bf1ab69c106ae59a5b139077494bc` | `CK_ImportBatches_Status` includes `Confirming` |
| Controlled NONDEPLOY integration | Draft [PR #117](https://github.com/Terry4410/field-visit-mileage-system/pull/117) | Review-only, not merged |
| SQL Server | GitHub disposable SQL Server 2022 / container | NOT company internal SQL instance |
| Company Windows/IIS/AD SSO | IT versions/approach TBD | NOT RUN / NOT CERTIFIED |

## Supported evidence, not an installation claim

1. [020 focus test #45202494508](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/45202494508) succeeded: SQL2022 allowed `Confirming`, denied invalid Batch/Item status, source hash locked. No shared DB writes.
2. [Selected API + FE compile #45204798661](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/45204798661) succeeded, separate exact UAT component sources. All ZIPs have deliberately invalid configuration to **prevent accidental deployment**. No company SSO validation.
3. [69-table isolated clean build #45206735733](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/45206735733) succeeded: effective final CHECKs (including corrected 020), 69-table scoped catalog, independent sqlcmd replay and recover-verify, scoped API RC candidate evidence. Not full selected API business regression.
4. [Selected UAT API + live 69-table disposable SQL #45214245615](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/45214245615) **SUCCESS**: current accepted API started and returned `/health/ready` PASS against the still-existing independently replayed isolated SQL database; anonymous `GET /api/v1/me` correctly returned HTTP 401. Disposable replay and recovery databases and SQL container all cleaned. Exact integration head `2982498a7a7ecc7cdebf75d8d5a34165c08bbfa5`.
5. The earlier scoped [#45208992522 HOLD](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/45208992522) was traced by **source review** to a test sequencing fault: `ModelBaselineRunner` drops `OptionCIsolated_FieldVisitV180CleanInstall` in its `finally` block; the independent replay similarly deletes its DB on step exit. The old API probe targeted the removed model-runner DB after cleanup. No Azure defect or SQL Server schema incompatibility was established by that HOLD. The new probe runs **inside the replay step before cleanup**, with original cleanup trap preserved.
6. This PASS covers **API startup, DB readiness, and anonymous 401 only**. It does **not** validate real business records, cross-team negative authorization (QUERY-02), company Windows/IIS/AD SSO, or independently executed corporate IT Clean Build.

## Single evidence manifest and Windows SSO implementation boundary

The immutable source identities, exact nondeploy ZIP SHA-256 values, 13 SQL install layer count, 69 table scope, CI run links and **unclosed gates** are pinned in [IT-RELEASE-EVIDENCE-LOCK-20261124.json](./IT-RELEASE-EVIDENCE-LOCK-20261124.json). The three verified ZIPs are separate historical/selected artifacts, **not one deployable release**; the SQL artifact's own API candidate comes from the historical RC rather than the accepted Path B UAT API. The accepted API was independently checked against a still-live 69-table replay database, but no single company installer is certified.

**Specific authentication source audit:** the selected API `backend/src/FieldVisit.Application/V170AuthenticationRules.cs` defines only `Demo` and `Entra` modes. The selected `Program.cs` configures application JWT and conditional Entra JWT; `AuthService.cs` resolves a user by Demo account or Entra tenant/object ID. The frontend `App.tsx` uses the same app roles. There is **no source-supported / test-verified IIS Integrated Windows SSO flow** in the accepted API, so changing IIS settings alone is not sufficient to declare company employee login complete.

**Minimum IT decision before implementation:** confirm whether company standard mandates IIS Windows Authentication (Kerberos/Negotiate) or permits organization-approved Entra/OIDC federation, and provide domain-joined browser/server expectations, supported principal key (stable SID/object GUID or authorized mapping), HTTPS/reverse-proxy path, and IIS/SQL service identity policy. The application must validate a server-authenticated principal, map it to an *active* existing app User, and separately enforce Visitor/Leader/Admin business scopes. No browser-controlled identity header, Demo backdoor, automatic first-login role elevation, or tenant-wide implicit authorization. This is design input for IT, **not a claim SSO code has been written or tested**.

Microsoft's documented Windows Authentication options for ASP.NET Core 8 under IIS are at [Microsoft Learn — Windows Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-8.0). IT must choose the supported company approach before Windows SSO code is integrated.

## Minimum-cost enterprise login decision — source-audited (no auth code change)

This comparison is **not an IT policy decision** and is not an authentication/UAT PASS. It uses accepted API `ab71aaa96d9ac259f66386555812448284527d15` and selected frontend `90fe9bd61c24be57ccfaf6d450bf053e9c646ac1` (both pinned above).

| Company IT decision | Source-supported route | Still mandatory before production |
| --- | --- | --- |
| **A. Company accepts Microsoft Entra employee login integrated with corporate identities** | **Prefer first for lowest development cost**: existing frontend `auth-config.ts` / `entra-auth.ts` uses MSAL browser login **popup** and Entra API token; `auth.tsx` exchanges it for an application JWT through `/api/v1/auth/entra-login`. Existing API validates Entra tenant, audience, `tid`/`oid`, scope, and maps to existing active user/business roles. No new IIS Negotiate login code is needed merely to try this route. | IT's actual tenant/app registration, SPA redirect URI, authorized API delegated scope, HTTPS/CORS, stable user identity mapping, disabled/revoked users, negative access tests, group/role separation and corporate SSO approval. **Popup login is not automatic silent Windows logon**; corporate policy may disallow this approach. |
| **B. Company requires genuine intranet IIS Integrated Windows Authentication (Kerberos/Negotiate)** | Existing selected code **does not implement this login flow**. IIS can authenticate the Windows principal, but the app still requires a trusted identity-to-User mapping and an application JWT or equivalent business-role authorization integration. | IT must specify Windows domain, service/application pool identity, SPN/Negotiate policies, allowed browsers, IIS integration/reverse proxy model, stable principal identifier, and security test scope **before** new implementation. This is a new source change and separate integration test, not just a config toggle. |

**Existing Entra config keys to map, not a deployment instruction:** frontend `AUTH_MODE=Entra`, `ENTRA.TENANT_ID`, `ENTRA.SPA_CLIENT_ID`, `ENTRA.API_SCOPE`, `ENTRA.REDIRECT_URI`; API `Auth:Mode=Entra`, `Auth:Entra:TenantId`, `Auth:Entra:Audience`, `Auth:Entra:RequiredScope`, `Auth:Entra:AllowFirstLoginEmailBinding=false` (keep disabled pending explicit IT-controlled proof). The Demo endpoint must reject login in production Entra mode; do not expose Demo passwords or reuse Azure pilot accounts. IT-approved service identity, secure configuration, revocation behavior, invalid/audience-scope claims, Visitor/Leader/Admin scopes and cross-team denial require runtime tests before Production Ready.

**Recommendation:** ask company IT only whether **A** is acceptable under its corporate AD/SSO policy or whether **B** is mandatory, plus the chosen Windows/SQL versions and operating owner. **Do not ask Owner to invent a technical implementation.** No Azure or Windows settings should be changed based on this note alone.

References: [Microsoft Learn — ASP.NET Core 8 Windows Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-8.0) and [Microsoft Learn — MSAL authentication flows](https://learn.microsoft.com/en-us/entra/identity-platform/msal-authentication-flows).

## What IT must verify independently

1. Establish one final source/compiled API/compiled frontend/schema manifest and exact hashes; do not blindly merge divergent Git branches, or install old Azure-only scripts into a new company SQL target.
2. Obtain company-provided Windows Server, IIS, .NET 8 Hosting Bundle, SQL Server version, TLS/security/network and Windows SSO/AD architecture specification.
3. Have IT install to **fresh separate company test DB** using a reviewed company-target installer, approved service principal, authentic minimal role/reference data and no UAT-only Demo credentials. No fabricated historical migration markers.
4. Prove complete SQL catalog facets (columns, FKs, indexes, trusted checks, triggers, schema ledger, source and target SQL permissions); run actual deployed API/Frontend create/edit/approve/report and negative access tests on that same clean DB.
5. Verify foreign-Team/Org data denied for Visitor/Leader and allowed within proper scope for Admin: query, direct GET, exports, location writes. Retain original QUERY-02 business UAT exception until these real checks complete.
6. Verify IIS/Windows SSO login, HR identity to business roles, disabled/revoked users, secrets, Google route provider licensing/settings, Traditional Chinese fonts, backups/recovery, monitoring and operating owner.
7. Only after documented IT results and explicit go-live authorization can `IT Clean Build Passed` or `Production Ready` be declared.

## Status and stop conditions

- `Business UAT: ACCEPT WITH OPEN ITEMS` — actual Owner approval.
- `Code readiness: bounded CI source/build/SQL/API-readiness PASS` — per specific evidence only; no business workflow or company IT deployment acceptance.
- `IT Deployment Ready: NOT YET PROVEN` — release does not yet include company-target validated installation + SSO config.
- `IT Clean Build: NOT RUN BY IT`.
- `Production Ready: NO`.

There is **no Azure/Production/Shared SQL write**, no deployment, no Github protected-branch merge, no enterprise principal or permission change, and no new Daily Plan in this branch. All work must remain nondeploy without new scoped authorization.
