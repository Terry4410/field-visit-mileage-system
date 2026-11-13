"""Pure NONDEPLOY parser for the fixed preflight.sql row; no SQL execution.

Never returns raw SID, identity, connection text, or SQL output.
An empty result, ambiguous rows, or a partial schema fails closed.
"""
from dataclasses import dataclass

EXPECTED_DB = 'db-fieldvisit-pathb-uat'
EXPECTED_SERVER = 'sql-fieldvisit-jpe-pathb-uat'
COUNTS = (7, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21)
EXPECTED_COLUMNS = 22

@dataclass(frozen=True)
class Receipt:
    sql_target: str
    connection: str
    schema: str
    next_action: str

class ReceiptHold(Exception):
    pass

def parse_preflight(raw):
    if not isinstance(raw, bytes) or len(raw) > 8192:
        raise ReceiptHold('RECEIPT_SHAPE_HOLD')
    try:
        value = raw.decode('utf-8', 'strict').strip()
        lines = [line for line in value.splitlines() if line.strip()]
        if len(lines) != 1:
            raise ValueError('rows')
        parts = [p.strip() for p in lines[0].split('|')]
        if len(parts) != EXPECTED_COLUMNS or parts[0] != 'FV_SQL':
            raise ValueError('columns')
        if (parts[2] != EXPECTED_DB or
                parts[1].lower().split('.')[0] != EXPECTED_SERVER or
                parts[3] != '5'):
            raise ValueError('target')
        if not parts[4] or not all(c in '0123456789abcdefABCDEF' for c in parts[4]):
            raise ValueError('sid-shape')
        if parts[8].upper() != 'TRUE' or parts[10].upper() not in ('TCP',):
            raise ValueError('connection')
        if parts[5] not in ('0', '1') or parts[6] not in ('0', '1'):
            raise ValueError('permissions')
        counts = [int(parts[i]) for i in COUNTS]
        if any(x < 0 or x > 1000000000 for x in counts):
            raise ValueError('counts')
        if all(x == 0 for x in counts):
            return Receipt('PASS', 'PASS', 'EMPTY_CANDIDATE', 'SEPARATE_INSTALL_AUTH_REQUIRED')
        return Receipt('PASS', 'PASS', 'NONEMPTY_OR_PARTIAL', 'HOLD_MANUAL_RECONCILIATION')
    except (ValueError, UnicodeError, IndexError, TypeError) as exc:
        raise ReceiptHold('RECEIPT_UNTRUSTED_HOLD') from exc

if __name__ == '__main__':
    raise SystemExit('NONDEPLOY_NO_SQL_EXECUTION')
