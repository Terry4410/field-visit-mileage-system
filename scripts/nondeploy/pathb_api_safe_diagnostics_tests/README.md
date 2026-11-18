# Path B API SQL failure observation — NONDEPLOY

This candidate does not execute any SQL or Token requests during CI and does not change the API health endpoints. It only logs a bounded, coarse category when an **existing** EF Core SQL connection fails. SQL exception details, tokens, principal IDs, connection strings and business data must never be logged.

18456 is deliberately classified as `SQL_AUTH_OR_DOSGUARD_UNKNOWN`: do not infer missing SQL user, permissions, or root cause from this category. This does not fix database access. No extra SQL probes, retries, routes, or new Azure resources are added.

The workflow builds and publishes an **offline** API candidate and SHA-256 manifest. It contains no Azure login or deployment step. The artifact is not a release until an independent review proves the exact Path B deployment target, actual currently deployed build, rollback artifact, App Service configuration retention, logging visibility, startup behavior, and controlled one-shot rollout/stop conditions.

Never run the existing `.github/workflows/api-azure.yml` to deliver this candidate; its target is not pinned to Path B. Do not merge or deploy under this NONDEPLOY authorization.
