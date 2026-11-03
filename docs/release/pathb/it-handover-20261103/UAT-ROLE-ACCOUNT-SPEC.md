# UAT role and test-account specification

UAT authentication remains Demo mode. Enterprise Entra SSO is an IT Production
scope item. Passwords are never stored in source, documents, GitHub or evidence.

| Test actor | Required scope | Allowed examples | Required denial evidence | Status |
|---|---|---|---|---|
| Visitor | One Organization, one Team, own identity | Own drafts/trips, approved/temporary locations, submit/correct own trips | Other users, Teams and Organizations; approval/admin actions | NOT PROVISIONED |
| Leader | Same Organization, explicitly effective-dated Team scope | Team queue, mileage decision, return/approve, authorized history | Other Team/Organization; own unauthorized high-risk approval | NOT PROVISIONED |
| Admin | One Organization, independent from high-risk requestor | Master data, duplicate/merge review, controlled imports and governance | Cross-Organization; self-approval where separation is required | NOT PROVISIONED |

## Provisioning rules

- Use three unique, nonproduction account identifiers. Suggested labels are
  `pathb-visitor01`, `pathb-leader01`, and `pathb-admin01`; actual identifiers
  may follow IT naming policy.
- Do not reuse an Owner administrator account as a Business UAT actor.
- Place every actor in explicit Organization/Team test fixtures and record only
  sanitized actor IDs in evidence.
- Keep credentials in the Owner/IT secret channel. Rotate or disable them after
  UAT or on custody transfer.
- Preserve Visitor, Leader and Admin authentication through Owner-led UAT.
- Add enterprise Entra SSO only during IT acceptance/Production preparation.

## Minimum account gate

Before releasing the URL, each actor must authenticate, return the intended
role/scope from `/me`, be denied at least one cross-scope operation and complete
one authorized Browser → API → SQL action. Admin self-approval separation and
Leader effective-date boundaries must be demonstrated.

