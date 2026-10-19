"""Emit GitHub mask commands only; never print identifiers as ordinary log text.

Inputs MUST arrive through workflow_call secrets so runner pre-step env rendering
is already masked. A normal env mapping from vars is intentionally unsupported.
"""
import os
import re
import sys

GUID = re.compile(r'[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\Z')
REPOSITORY = 'Terry4410/field-visit-mileage-system'
REF = 'refs/heads/post-uat/v1.8.0'
PARENT = '.github/workflows/azure-sql-uat-schema-fidelity-readonly.yml'
PRIVATE = '.github/workflows/r3-private-uat-execution.yml'

def escape_command(value):
    return value.replace('%','%25').replace('\r','%0D').replace('\n','%0A')

def reviewed_masks(identifiers, repository=REPOSITORY, ref=REF):
    if len(identifiers) != 3 or any(not GUID.fullmatch(v) for v in identifiers):
        raise ValueError('invalid identifiers')
    if repository != REPOSITORY or ref != REF:
        raise ValueError('wrong source')
    masks = []
    for value in identifiers:
        masks.extend((value, value.lower(), value.upper()))
    masks.extend((
        'https://'+'token.actions.githubusercontent.com',
        'api://'+'AzureADTokenExchange',
        'repo:'+repository+':environment:uat',
        repository+'/'+PARENT+'@'+ref,
        repository+'/'+PRIVATE+'@'+ref,
    ))
    return list(dict.fromkeys(masks))

def environment_masks(environment):
    if environment.get('RUNNER_DEBUG') == '1' or environment.get('GITHUB_ACTIONS') != 'true':
        raise ValueError('unsafe runner')
    ids = [environment.get(k,'') for k in ('R3_CLIENT_ID','R3_TENANT_ID','R3_SUBSCRIPTION_ID')]
    return reviewed_masks(ids, environment.get('GITHUB_REPOSITORY',''), environment.get('GITHUB_REF',''))

def emit_masks(values, stream):
    # A GitHub command channel, not an ordinary-value log. Runner consumes these.
    for value in values:
        stream.write('::add-mask::'+escape_command(value)+'\n')

def main():
    try:
        masks = environment_masks(os.environ)
        emit_masks(masks, sys.stdout)
        print('PASS: reviewed login output masks registered.')
    except Exception:
        print('STOP: login privacy prerequisites rejected; details suppressed.',file=sys.stderr)
        return 1
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
