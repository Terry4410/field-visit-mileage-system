# Historical API DLL rebuild against live hash (NONDEPLOY)

Source: historical API deployment Run 38334707319, exact Git SHA `db235938b8b13ca8a65ab3b525de1df10d911645`. Live `/home/site/wwwroot/source-sha.txt` matched that SHA; Owner observed on-disk `FieldVisit.Api.dll` SHA256 `da3d658d01d076e7b4026638fb1157779466e0f8b83e5efee51ad7106b288418`. The older deployment had no archived full API published binary in available GitHub Actions artifacts.

New PR83 `legacy-api-dll-hash-nondeploy` compares an isolated `dotnet 8.0.x` restore/build/publish of that *exact historic source* to the Owner-observed hash and uploads a *checksum/receipt JSON ONLY* to GitHub Actions. It never exports or uploads any executable, source credentials, entire API package, or Azure environment values, and never uses Azure/SQL or writes the UAT deployment.

`MATCH` means a single source-pinned reconstructed DLL was byte-identical to the Owner-observed on-disk DLL. `MISMATCH_HOLD` can result from .NET SDK patch, build inputs or historical toolchain drift and does *not* prove corruption. The GitHub job can succeed even on `MISMATCH_HOLD`: inspect `receipt.json.result`; do not interpret the job green check as a byte match.

The test is **not** a backup or a byte-identical *full* published bundle. Exact active runtime assembly in memory, appsettings, all dependencies, frontend assets, actual SQL principal/permissions and paired API+Frontend+PITR rollback remain HOLD. No migrations, deploys, grants, restore, new paid resources or feature activation are authorized by this test. Previously approved G1 SQL/Trigger checks are retained without repeat.
