# Path B G1 outer SQL-target entry fix — NONDEPLOY

This package recovers the exact outer Cloud Shell launcher boundary that emitted
`G1_PREGATE=HOLD_SQL_TARGET` and applies one scoped correction. It does not run
Azure, SSH, Token, Blob, SQL or DDL operations.

## Confirmed defect and evidence limit

The recovered launcher placed `az sql server show ... -o tsv` inside a here-doc
feeding `read FQDN PNA TLS`, then collapsed command failure, missing output,
parsing failure and value mismatch into the same `HOLD_SQL_TARGET` label. The
Azure CLI exit status was not tested and the three raw values were not retained.
Therefore the historical fixed label cannot prove which field differed or that
the SQL resource was misconfigured. Claiming a more specific historical cause
would be guesswork.

The correction removes only that dependency: it checks the Azure CLI exit status,
requests a named JSON object, requires exactly `fqdn`, `pna`, and `tls`, and checks
their exact approved values. All artifact, Owner, Tenant, App-instance,
authorization, firewall, SQL-admin and App gates remain byte-equivalent outside
the replaced SQL-target block.

The previous live authorization is consumed. This launcher is NONDEPLOY and must
not be used until the Owner separately approves its exact commit, ZIP and binding.

