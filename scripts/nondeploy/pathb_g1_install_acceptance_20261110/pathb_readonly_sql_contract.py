"""Pure, NONDEPLOY SQL read-only transport contract. No live entrypoint.

Constructs a pinned sqlcmd argument vector for an eventual separately authorized
executor. This module does not spawn processes, read tokens, access Azure or SQL,
or import the installer. It is intentionally not a complete live transport.
"""
from pathlib import Path
import hashlib

SERVER = 'sql-fieldvisit-jpe-pathb-uat.database.windows.net'
DATABASE = 'db-fieldvisit-pathb-uat'
SQL_SHA256 = 'e87dbd54d4740254b6f0041fe330cc4af71e51a214964c39e1001c4437075add'

class ReadonlyHold(Exception):
    pass

def sqlcmd_argv(binary, token_path, sql_bytes):
    """Validate exact SQL and paths; return arguments only, never execute."""
    if not isinstance(sql_bytes, bytes) or hashlib.sha256(sql_bytes).hexdigest() != SQL_SHA256:
        raise ReadonlyHold('SQL_HASH_HOLD')
    if not isinstance(binary, Path) or not binary.is_absolute() or binary.name != 'sqlcmd':
        raise ReadonlyHold('BINARY_PATH_HOLD')
    if not isinstance(token_path, Path) or not token_path.is_absolute() or token_path.name != 'sql-token':
        raise ReadonlyHold('TOKEN_PATH_HOLD')
    if binary == token_path:
        raise ReadonlyHold('PATH_COLLISION_HOLD')
    return [str(binary), '-S', SERVER, '-d', DATABASE, '-I', '-b', '-l', '10',
            '-G', '-P', str(token_path), '-Nm', '-h', '-1', '-W', '-w', '65535', '-s', '|']

if __name__ == '__main__':
    raise SystemExit('NONDEPLOY_NO_SQL_EXECUTION')
