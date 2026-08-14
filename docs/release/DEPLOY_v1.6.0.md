# v1.6.0 FINAL — Codespaces → DB → GitHub → UAT

## A. Apply Release Candidate

From repository root:

```bash
git status
git rev-parse HEAD
```

HEAD must be `4bc7ab6706485d490ce7be0a4fb86d2c92e8381a`. Then:

```bash
rm -rf /tmp/v160-final
mkdir -p /tmp/v160-final
unzip -o field-visit-mileage-system_v1.6.0_FINAL_RC.zip -d /tmp/v160-final
python3 /tmp/v160-final/field-visit-mileage-system_v1.6.0_FINAL_RC/apply_v160_final.py
```

If the apply script refuses the SHA or reports a Baseline mismatch, stop. Do not force apply.

> Historical note: the legacy Python RC verifier used during v1.6.0 development was retired from v1.7 and later branches. On newer branches, use the documented build/test/UAT gates instead.

## B. Build / Test Gate

Frontend:

```bash
cd frontend
npm install
npm test
npm run build
cd ..
```

`npm install` creates/updates `package-lock.json`; commit it with the release.

Backend:

```bash
dotnet restore backend/src/FieldVisit.Api/FieldVisit.Api.csproj
dotnet build backend/src/FieldVisit.Api/FieldVisit.Api.csproj -c Release --no-restore
dotnet test backend/tests/FieldVisit.Application.Tests/FieldVisit.Application.Tests.csproj -c Release
```

Do not run the DB migration until all above commands succeed.

## C. Azure SQL Migration

Azure Portal → `db-fieldvisit-uat` → Query Editor.

1. Execute `database/migrations/1600_002_final/Up.sql`.
2. If successful, execute `Verify.sql`.
3. Continue only when all Result values are `PASS` and all ErrorCount values are `0`.
4. Do **not** execute `Rollback.sql` unless recovery is actually required.

## D. Commit / Push

```bash
git status
git add -A
git commit -m "Complete v1.6.0 UAT release candidate"
git push origin main
```

GitHub Actions will run Frontend test/build/deploy and API build/test/publish/deploy.

## E. After deploy

1. Verify both workflow runs are green.
2. Open `/health/live` and `/health/ready` on the API.
3. Logout/login again so v1.6 role/team claims are refreshed.
4. Run `docs/uat/UAT-SMOKE-TEST-v1.6.0.md`.
5. Verify one Excel export and one PDF export, including Traditional Chinese glyphs.

If all smoke tests pass, the release status can be recorded as `UAT Deployed`.
