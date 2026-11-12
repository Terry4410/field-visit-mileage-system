# Path B G1 — exact-head live readiness decision (NONDEPLOY)

**Status: NO-GO. This document is not an Owner authorization.**

## Immutable offline evidence

- Draft PR: https://github.com/Terry4410/field-visit-mileage-system/pull/86
- Reviewed source head: `47a348e31a5ded5351d57bfb4df10d8608b57a02`
- Latest offline CI: https://github.com/Terry4410/field-visit-mileage-system/actions/runs/43234612644 — **SUCCESS**, 26 focused + 18 integration = **44/44 PASS**.
- Source-only artifact ID: `13950109647`; GitHub artifact outer digest: `622e98a46d717638703c5f5529442615db0e17bb7424e07360c7cd1e01858311`.
- Source ZIP inner SHA-256: `a9690b6cbe0d1bf44f00292bcfa7d92b64ee409462292b98f35e21d767d460fe`.
- Binding-index SHA-256: `e8fc28417976074ea8c3514c1047a939743641ba22b1bd7b0dae1314e7675272`.
- Exact source-only package contains 22 members, all binding-verified. The live parent now rejects extra, missing, tampered or symlink ZIP entries **before Azure management**.
- Parent source SHA-256: `a02dee9490f7c75be519be53bc7337cc221c7a0d038abe8c26535fac7ee2423a`.
- Frozen original installer SHA-256: `b38f124391d89351f2da6dd01cc4a93e7a515ac4bbda79b6746d5211ce44ba4f` — unchanged.
- Original preflight SQL SHA-256: `e87dbd54d4740254b6f0041fe330cc4af71e51a214964c39e1001c4437075add` — unchanged.

## Exact remaining blockers — do not infer PASS

1. **Independent security review:** the actual Kudu transport, durable one-time claim, disconnection/receipt behavior, exact file binding, and cleanup must be independently approved. Offline tests do not establish live Kudu or SQL reachability.
2. **Live distribution approval, not another packager:** independent local test of downloaded Artifact `13950109647` confirmed `validate_binding`, `validate_local_source`, and `validate_distribution_archive` all PASS (exit 0). The 22-member source ZIP is compatible with the parent `--distribution` input when extracted into the verified local source directory. The parent builds the remote Stage during an authorized live run by retrieving the frozen original SQL package, seal and five pinned tool packages. **No separate prebuilt full Stage ZIP is required by this interface**, but the source ZIP is not approved for live use and end-to-end transport/one-time-claim safety remains unverified. Old Tool2 Launcher pins are stale/consumed and must never be replayed.
3. **Current SQL schema:** UNKNOWN. The exact integrated original pre-DDL SELECT22 must prove the database empty before any DDL. Prior SELECT attempts and once-only claims are not reusable.
4. **Fresh, exact, short-lived Owner authorization:** requires independently reviewed exact distribution SHA, binding SHA, new Kudu contract/decision, exact App/SQL target and instance, unique operation ID, <=30-minute validity, one attempt. The code's approval constant does **not** grant approval.
5. **No new Azure resources or IT handover:** no VM/ACI/Bastion/NAT/VPN, deployment, merge, RBAC, Token, SQL, Blob, or old-claim replay without separately authorized live execution.

## Decision and UAT dependency

- **SOURCE_ONLY_OFFLINE = PASS**
- **INDEPENDENT_LIVE_SECURITY_REVIEW = PENDING**
- **SOURCE_DISTRIBUTION_INTERFACE = OFFLINE_PASS; LIVE_USE = NOT_APPROVED**
- **G1 LIVE = HOLD**
- **SQL CATALOG = UNKNOWN**
- **BUSINESS UAT = 0/25, NO-GO**

The shortest remaining path is to close the two **review/authorization** gates, then perform one approved exact-target live preflight/install attempt if and only if every safety gate passes. Do not create another diagnostic version or repeat previously passed offline tests merely to accumulate PASS labels.
