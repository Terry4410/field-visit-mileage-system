# Delivery status and critical path

## Completed and reusable

- Approved application source SHA fixed and complete.
- Accepted CI Runs `40438452922` and `40438452961` remain SUCCESS.
- API, Frontend and supporting artifacts have exact IDs and digests.
- Original SQL package, Manifest, Seal, Installer, 14 layers, 34 Seal members
  and 33 checksum-index members were verified.
- Path B SQL server, database, VNet integration, private endpoint and private DNS exist.
- SQL public access is Disabled, TLS minimum 1.2 and firewall rule count is zero.
- PR #84 supplies corrected preflight controls and offline evidence.
- Canonical 25 Business UAT cases and three-role contract are preserved.

## Current Business UAT blockers

| Blocker | Why it blocks | Smallest resolution |
|---|---|---|
| Private SQL operator channel | `az webapp exec` timed out before a verified App session | Prove one fixed read-only Portal SSH/official exec session on the existing App; no SQL |
| Once-marker reconciliation | Cloud Shell parent marker is consumed; App marker is unknown | Read App marker/temp state, then obtain Owner decision; never clear blindly |
| Database state | 22-field preflight and post-attempt schema state were not executed | One approved SELECT-only assessment after private channel works |
| Schema installation | 69 tables and three markers not installed/verified | Separately approved one-time install only after all fresh gates PASS |
| Runtime security | Runtime principal/ACL and signed activation not applied | Apply and verify object-scoped ACL, then sign exact facts |
| Paired application deployment | Current deployed API/Frontend hashes and Frontend host not proven | Deploy or bind accepted pair and verify health/version/config |
| Test actors | Visitor/Leader/Admin not provisioned on verified runtime | Create three unique Demo UAT actors and prove scope isolation |

## Shortest executable UAT path

1. Use the existing Azure Portal/App Service only. Prove one read-only built-in
   SSH/exec connection and inspect App-side markers/temp paths.
2. With a new bounded Owner approval, reconcile the consumed parent marker and
   run one SELECT-only 22-field database-state check.
3. If and only if the database is truly empty and markers are absent, authorize
   one exact installation attempt; otherwise take a separate recovery decision.
4. Verify 69 tables/three markers/fingerprint, apply runtime ACL and signed activation.
5. Deploy/bind the accepted API and Frontend pair, provision three Demo actors,
   run minimum role smoke, then release the URL and execute all 25 cases.

Cloud Shell cannot directly reach the private SQL endpoint. GitHub-hosted runners
do not provide an approved private route. Kudu command endpoints or a temporary
migration API would require a new token-handling/deployment design and therefore
are not the fast path. Portal built-in SSH is the only existing no-new-resource
alternative worth proving if CLI exec remains unavailable.

## Work that can proceed in parallel

- IT review of source, schema, artifacts, configuration and SOPs.
- Download accepted artifacts before retention expiry.
- Prepare secret custody and three nonproduction actor identifiers.
- Prepare the 25-case test data/evidence sheets without marking results PASS.
- Record backup retention and identify the first post-install restorable point procedure.

## Deferred until IT acceptance or Production

- Enterprise Entra SSO.
- Production vulnerability scanning and penetration testing.
- Production infrastructure hardening, monitoring/SIEM and final DR acceptance.
- Full isolated PITR plus paired application recovery rehearsal.
- Documentation styling and other nonblocking engineering refinements.

