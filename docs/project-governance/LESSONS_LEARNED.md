# Field Visit — Lessons Learned & Delivery Decisions

> **Status:** Owner management retrospective, prepared 2026-11-19.  
> **Scope:** Project-delivery lessons from August–November 2026. This is not a claim that any unverified technical root cause has been proven.  
> **Evidence:** [Canonical Issue #41](https://github.com/Terry4410/field-visit-mileage-system/issues/41); see also [CURRENT_STATUS.md](CURRENT_STATUS.md) for the dated operational snapshot.

## 1. Outcome, not activity

The goal is **complete Business UAT, then provide a reproducible IT handover package at the lowest reasonable total cost**. Build/CI/Smoke/Readiness PASS, SQL table counts, and a large number of PRs are useful technical evidence **but not substitutes** for an operator completing a role-scoped business process.

Track four different gates; never conflate them:

1. **Code Ready:** version-identified source builds and relevant tests pass.
2. **Business UAT Passed:** actual users complete the agreed end-to-end scenarios on a known environment/version, with evidence.
3. **IT Deployment Ready:** IT can independently build the application and recreate an empty database from versioned schema + reference data.
4. **Production Ready:** internal hosting, authentication/authorization, security assessment, operational controls and business acceptance are signed off.

No percent-complete estimate based solely on CI/test totals.

## 2. Retrospective findings and durable decisions

| ID | Failure pattern / lesson | Decision and prevention test |
| --- | --- | --- |
| LL-001 Scope drift | Continuing nonessential enhancements can defer the first usable release. | Freeze the Business UAT scope; classify new requests as blocker / required compliance or integrity / deferrable. Owner approves scope changes. |
| LL-002 Wrong deployment center of gravity | A highly controlled Azure environment may consume substantial effort even though IT plans a different production platform. | Treat Azure as **dev/UAT only** unless Owner changes direction. The IT handover target is **SQL Server + Windows Server/IIS + AD Windows SSO**, subject to IT confirmation. |
| LL-003 New database is not itself a release | A fresh database validates clean install but requires schema, identities, app access and minimal business reference data. | Version schema and required seed explicitly; prove clean build **and** business access. Do not install again just because login fails. |
| LL-004 HOLD-loop engineering | Repeated UNKNOWN/HOLD can cause a succession of scripts, gates and PRs without removing the user-facing blocker. | On two effective failures in the same class, **stop that class of live attempt**, consolidate evidence, identify the actual blocked user flow and compare simpler paths. Never bypass necessary security gates or consumed one-shot authorizations. |
| LL-005 Wrong success proxy | CI/Readiness PASS does not prove API CRUD permissions or user login. | Require role-scoped browser/API evidence for Business UAT. Always label evidence by environment, SHA, observation type and time. |
| LL-006 High coordination cost | Single Owner, no dedicated IT team, manual portals, ZIP/JSON exchanges and limited WORK budget are project costs. | Batch dependent nondeploy analysis, change and testing. Prefer CHAT + existing GitHub tools for work they can really do; request Owner input only for genuine decisions/permissions. |
| LL-007 Late IT involvement | Postponing IT platform decisions risks building infrastructure that cannot be handed over. | Confirm database/platform versions, IIS hosting standard, AD SSO, app service identity and security gates early. IT can prepare a clean build while HR UAT is progressing. |
| LL-008 Blind replay and unsafe repairs | An existing successful one-shot SQL installation/seed/identity operation must not be repeated just to diagnose a later runtime issue. | Reuse trusted PASS evidence; distinguish deployment, runtime identity, SELECT/DML grants, reference data and application defects. New live writes require new scope-specific Owner authorization. |
| LL-009 Hidden maintenance ownership | 'IT will host' is not the same as 'IT will maintain application code'. | Before go-live define application bug owner, release approver, database migration owner, rollback lead and post-launch support. |
| **LL-010 Failure to proactively compare an alternative access path** | Private-only App/Kudu/Token/transport troubleshooting persisted even though a short, narrowly authorized Azure SQL Query Editor window was a possible option. The Owner, not the assistant, identified the temporary-public-access approach. | On repeated failure, proactively compare (a) repairing the current route, (b) **an existing official tool with explicitly authorized, time-boxed minimal network access**, and (c) deferring noncritical work or IT handover. Compare time, Owner effort and security risk. Never equate a temporary public endpoint with unrestricted access or automatic permission; use only the specified client IP, encrypted authenticated access, the shortest approved window, restore **Public Network Access Disabled** and verify closure. This alternative can solve connectivity, **not** API SQL permissions or SQL correctness. |

## 3. Architecture decision (planned target, not IT sign-off)

**Desired enterprise host:** Microsoft SQL Server in the internal network; Windows Server + IIS for the React web frontend and ASP.NET Core API; Active Directory / Windows Integrated SSO for employees.

- IT rebuilds the database from **version-controlled schema** and narrowly scoped baseline/reference data. Azure SQL backups, connection strings, private networking, Managed Identity, test secrets or individual demo accounts are **not** production handover dependencies.
- Keep employee authentication separate from business authorization (Visitor/Leader/Admin and organization/team scope). IT supplies enterprise identity standards; HR owns business authorization requirements.
- App-to-SQL service identity and least-privilege SQL permissions belong to IT's deployment decision; no assumptions about user-delegation/double-hop.
- A successful clean install must be demonstrated on the **IT target SQL Server version**, not inferred from a disposable Azure SQL test or a `.dacpac` build alone.
- Security scanning (SAST/SCA/secrets/DAST as applicable), negative authorization tests, backups, monitoring and rollback are distinct production gates. IT must define its acceptance criteria before final deployment.

**Decision boundary:** These are Owner planning directions, **not** a claim that IT has approved exact Windows, IIS, SQL Server, .NET, Kerberos or scanner versions.

## 4. Preflight / stop-or-go rules for any proposed task

Before significant work, answer briefly:

1. Which **specific UAT or IT clean-build blocker** does it remove?
2. What actual evidence establishes the failure class and target version/environment?
3. Which prior PASS and one-use permissions must remain untouched?
4. Have **current route repair vs existing official-tool/controlled alternate access vs deferring to IT** been compared, including Owner time, WORK quota and security exposure?
5. What are the exact acceptance criteria, stop conditions and rollback/containment plan?
6. Can independent, related reviews/tests be completed in **one batch** without another Owner interaction?

**STOP** if there is no grounded blocker or justified delivery requirement, the action would repeat a consumed grant, its target/identity is ambiguous, or its expected outcome is only another uninformative UNKNOWN. Escalate an explicit decision instead of silently expanding technical work.

## 5. Evidence discipline and responsibility

- **Canonical technical evidence:** linked GitHub PRs/actions and [Issue #41](https://github.com/Terry4410/field-visit-mileage-system/issues/41) plus direct Owner-supplied live observations.
- **Current summary:** [CURRENT_STATUS.md](CURRENT_STATUS.md), a dated snapshot, never a replacement for the source.
- **Owner:** business scope, risk tolerance, live write approvals and HR UAT sign-off.
- **Assistant/development:** organize evidence, maintain code/schema, root-cause analysis, nondeploy testing, package and explain alternatives.
- **IT:** internal Windows/IIS/SQL/AD configuration, security standards, deployment, operational ownership; exact division of app maintenance must be agreed.

A proposal or local static check is NOT an executed Azure action. Disposable SQL behavior tests are NOT proof of Azure runtime permissions. An approved candidate is NOT deployed until separate live evidence establishes it.

## 6. Decision log and updates

When a **material** architecture or release decision changes, append a short decision record with date, options, cost/risk, Owner decision, evidence and exit criteria. Do **not** rewrite older decisions as if a new finding had always been known. For rapidly changing facts update [CURRENT_STATUS.md](CURRENT_STATUS.md), not this retrospective.

### Baseline record — 2026-11-19

The 2026-11-19 retrospective identified premature infrastructure complexity, diagnostic HOLD loops and conflation of technical PASS with Business UAT as avoidable management risks. The corrective principle is **minimum necessary delivery engineering; preserve verified evidence; stop unproductive loops; enable IT-independent clean build**. This record does not assign every delay to one tool or person.

### LL-010 Owner-discovered route — 2026-11-19

The Owner identified a **temporarily enabled Azure SQL public endpoint restricted to the current client IP**, followed by disabling public access after the operation. This made the existing Portal Query Editor an available route for bounded SQL checks and operations, avoiding a requirement to first perfect the private App/Kudu transport. This was a *conditional, Owner-authorized test-environment option*, not a production network standard and not a claim that opening a public endpoint is risk-free. SQL permissions and scripts still require separate validation and authorization. [Owner's observed read-only SQL checks and Disabled postcheck](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6981787678); [later Disabled-restoration and post-error restrictions](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6998225869).

**Process failure:** The assistant should have surfaced and compared this option earlier without relying on the Owner to discover the tool route. Preserve this *decision habit*, not an instruction to open public access by default.

### Evidence examples underlying this lesson

- [API ready after one-shot contained principal + CONNECT](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6981335831) is **not** business login proof.
- [Live seed/readback and login blocker reports](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6997120701) establish first denied object; they do **not** justify reinstalling SQL.
- [Latest NONDEPLOY disposable SQL2022 permission tests](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6997965303) are evidence of *generic behavior only*, not live Azure GRANT authorization.
