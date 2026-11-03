# Canonical Business UAT 25-case tracker

These IDs and meanings are frozen. Additional scenarios use a `BIZ-` prefix and
do not replace or duplicate canonical PASS counts. Google Routes success and
failure/manual fallback remain distinct mandatory cases.

| ID | Canonical case | Result |
|---|---|---|
| VIS01 | Visitor login, navigation and draft lifecycle | NOT RUN |
| VIS02 | Multi-stop trip and real Google Routes success | NOT RUN |
| VIS03 | Route failure to explicit manual fallback | NOT RUN |
| VIS04 | Returned trip modification and resubmission | NOT RUN |
| VIS05 | Approved and temporary locations | NOT RUN |
| VIS06 | Own history and data isolation | NOT RUN |
| LEAD01 | Authorized Team pending trips | NOT RUN |
| LEAD02 | Mileage processing | NOT RUN |
| LEAD03 | Final mileage decision and approval | NOT RUN |
| LEAD04 | Return reason and audit history | NOT RUN |
| LEAD05 | Leader history limited by effective period | NOT RUN |
| LEAD06 | Cross-Team and concurrency isolation | NOT RUN |
| ADMIN01 | Location create and duplicate warning | NOT RUN |
| ADMIN02 | Location merge and history preservation | NOT RUN |
| ADMIN03 | High-risk management approval | NOT RUN |
| ADMIN04 | Admin/HR self-approval denied | NOT RUN |
| XLS01 | Valid Excel import preview | NOT RUN |
| XLS02 | Import completion and source tracking | NOT RUN |
| XLS03 | Invalid and partly invalid data | NOT RUN |
| XLS04 | Query-result export | NOT RUN |
| XLS05 | Import/export permission isolation | NOT RUN |
| TRIP01 | Complete Visitor → Leader trip lifecycle | NOT RUN |
| TRIP02 | Physical iPhone operation and error recovery | NOT RUN |
| SEC01 | Role, Organization and Team isolation | NOT RUN |
| SEC02 | History, concurrency and audit integrity | NOT RUN |

For each execution, record timestamp, sanitized test-data ID, expected result,
observed result, evidence link, tester and PASS/FAIL/BLOCKED. Do not mark a case
PASS from unit, CI, synthetic SQL or an administrator-only probe.

