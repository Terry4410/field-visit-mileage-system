# Future Backlog / Technical Debt — after v1.6.0

These items are intentionally **not** blockers for v1.6.0 RC because they are outside the frozen scope or production-only improvements.

- v1.7.0 Google Routes `TWO_WHEELER` and production Geocoding providers, quota/billing/error policy.
- Production Microsoft Entra ID login, App Registration, Conditional Access/MFA and removal of Demo Login.
- Production DMZ/Intranet architecture, WAF, Key Vault, SIEM and enterprise monitoring.
- Distributed Background Job lease/queue for multi-instance production hosting (v1.6 UAT worker assumes a simple UAT host).
- Immediate token revocation/versioning after an Admin changes another user's roles/scopes; v1.6 requires re-login.
- Exact 1:1 PDF template refinement if the original corporate Excel/PDF source template becomes available.
- Structured master selectors inside Correction UI for changing Project/VisitType references; v1.6 correction preserves historical snapshot text/IDs.
- Expand automated integration/UI coverage beyond the RC smoke/unit baseline.
- Optional master-data export workbooks (current requirement covers master import and trip-query export).
