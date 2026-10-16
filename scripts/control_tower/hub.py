#!/usr/bin/env python3
"""Read-only Control Tower collector. No GitHub mutation or execution authority."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import urllib.error
import urllib.request

REPO = 'Terry4410/field-visit-mileage-system'
PROTECTED = 'post-uat/v1.8.0'
ISSUE = 41
LANES = {'WORK_A': 33, 'WORK_B': 38, 'WORK_C': 37, 'AUDIT': 33, 'OWNER': None}
CODES = {'SCHEMA_FIDELITY', 'CI_PENDING', 'CI_FAILED', 'SOURCE_CHANGED', 'EVIDENCE_CHANGED',
         'OWNER_REVIEW', 'NONDEPLOY_VALIDATION', 'IMPLEMENTATION', 'INDEPENDENT_AUDIT',
         'DEPLOYMENT_PROVENANCE', 'ROLLBACK_EVIDENCE', 'BUSINESS_UAT', 'R3_HOLD', 'NO_CHANGE'}
FIELDS = {'Version', 'Lane', 'SHA', 'CI', 'Status', 'Completed', 'Blockers', 'NextDelta',
          'OwnerAuthorization', 'Evidence', 'Sequence'}
MARKER = '<!-- control-tower-handoff:v1 -->\n'
SHA = re.compile(r'[0-9a-f]{40}')
HASH = re.compile(r'[0-9a-f]{64}')

def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()

def validate(record):
    """Deliberately reject all arbitrary text, URLs, identities and executable authority."""
    if not isinstance(record, dict) or set(record) != FIELDS:
        raise ValueError('INVALID_FIELDS')
    if type(record['Version']) is not int or record['Version'] != 1 or record['Lane'] not in LANES:
        raise ValueError('INVALID_VERSION_OR_LANE')
    if not isinstance(record['SHA'], str) or not SHA.fullmatch(record['SHA']):
        raise ValueError('INVALID_SHA')
    if type(record['Sequence']) is not int or not 1 <= record['Sequence'] <= 10**9:
        raise ValueError('INVALID_SEQUENCE')
    if record['Status'] not in ('PASS', 'FAIL', 'HOLD', 'IN_PROGRESS'):
        raise ValueError('INVALID_STATUS')
    if record['OwnerAuthorization'] != 'NONDEPLOY_ONLY':
        raise ValueError('AUTHORITY_DENIED')
    for key in ('Completed', 'Blockers', 'NextDelta'):
        values = record[key]
        if not isinstance(values, list) or len(values) > 16 or any(not isinstance(x, str) or x not in CODES for x in values):
            raise ValueError('INVALID_CODES')
    ci = record['CI']
    if not isinstance(ci, list) or len(ci) > 16 or any(type(x) is not int or not 1 <= x <= 10**15 for x in ci):
        raise ValueError('INVALID_CI')
    evidence = record['Evidence']
    if not isinstance(evidence, list) or len(evidence) > 32 or any(not isinstance(x, str) or not HASH.fullmatch(x) for x in evidence):
        raise ValueError('INVALID_EVIDENCE')
    if record['Status'] == 'PASS' and (not ci or not evidence or record['Blockers']):
        raise ValueError('PASS_WITHOUT_EVIDENCE')
    return record

class Client:
    def __init__(self, token=None):
        self.token = token
    def get(self, path):
        # Fixed host, no redirects, no arbitrary URLs or methods.
        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, *args, **kwargs):
                return None
        headers = {'Accept': 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28'}
        if self.token:
            headers['Authorization'] = 'Bearer ' + self.token
        req = urllib.request.Request('https://api.github.com/repos/' + REPO + '/' + path, headers=headers)
        with urllib.request.build_opener(NoRedirect).open(req, timeout=30) as response:
            return json.load(response)
    def pages(self, path):
        rows = []
        for page in range(1, 101):
            data = self.get(path + ('&' if '?' in path else '?') + f'per_page=100&page={page}')
            if not isinstance(data, list):
                raise ValueError('INVALID_PAGE')
            rows.extend(data)
            if len(data) < 100:
                return rows
        raise ValueError('PAGINATION_LIMIT')

def collect(client):
    prs = {}
    for number in (33, 37, 38):
        raw = client.get(f'pulls/{number}')
        sha = raw['head']['sha']
        if not SHA.fullmatch(sha) or raw['head']['repo']['full_name'] != REPO:
            raise ValueError('UNTRUSTED_SOURCE')
        # Do not copy PR bodies, titles, branch names, logs or actor data.
        runs = client.get(f'actions/runs?head_sha={sha}&per_page=100')
        if runs.get('total_count', 0) > 100:
            raise ValueError('RUN_LIMIT')
        safe_runs = []
        for run in runs['workflow_runs']:
            if run['head_sha'] != sha:
                raise ValueError('RUN_SHA_MISMATCH')
            # Fixed status vocabulary; do not emit arbitrary provider strings.
            conclusion = run.get('conclusion')
            status = 'PASS' if conclusion == 'success' else ('PENDING' if conclusion is None else 'FAIL')
            safe_runs.append({'id': int(run['id']), 'attempt': int(run.get('run_attempt', 1)), 'status': status})
        safe_runs.sort(key=lambda r: r['id'])
        prs[str(number)] = {'sha': sha, 'state': 'OPEN' if raw['state'] == 'open' else 'CLOSED',
                            'merged': raw['merged'] is True, 'ci': safe_runs}
    protected = client.get('git/ref/heads/' + PROTECTED)['object']['sha']
    if not SHA.fullmatch(protected):
        raise ValueError('INVALID_PROTECTED_SHA')
    comments = client.pages(f'issues/{ISSUE}/comments')
    records, rejected, seen = {}, 0, set()
    for comment in sorted(comments, key=lambda c: int(c['id'])):
        body = comment.get('body', '')
        if not body.startswith(MARKER):
            continue
        # Comment author check is ingestion policy only, never an authorization grant.
        if comment.get('user', {}).get('login') != 'Terry4410' or len(body) > 12000:
            rejected += 1
            continue
        try:
            record = validate(json.loads(body[len(MARKER):]))
        except (ValueError, TypeError, KeyError):
            rejected += 1
            continue
        key = digest(record)
        if key in seen:
            continue
        seen.add(key)
        lane = record['Lane']
        old = records.get(lane)
        # Equal sequence with different payload is a conflict: fail closed.
        if old and record['Sequence'] <= old['record']['Sequence']:
            if record['Sequence'] == old['record']['Sequence']:
                old['conflict'] = True
            continue
        records[lane] = {'record': record, 'comment_id': int(comment['id']), 'conflict': False}
    lanes = {}
    for lane, pr in LANES.items():
        entry = records.get(lane)
        expected = protected if pr is None else prs[str(pr)]['sha']
        reasons = []
        if not entry:
            reasons.append('MISSING_HANDOFF')
        else:
            record = entry['record']
            if entry['conflict']:
                reasons.append('SEQUENCE_CONFLICT')
            if record['SHA'] != expected:
                reasons.append('STALE_SHA')
            live = [] if pr is None else prs[str(pr)]['ci']
            by_id = {x['id']: x for x in live}
            if not record['CI'] or any(x not in by_id or by_id[x]['status'] != 'PASS' for x in record['CI']):
                reasons.append('CI_UNVERIFIED')
            if not record['Evidence']:
                reasons.append('MISSING_EVIDENCE')
        lanes[lane] = {'gate': 'HOLD' if reasons else 'REPORTED_' + entry['record']['Status'],
                       'reasons': reasons, 'handoff': entry}
    # Re-read refs after collection. Never mix moving source snapshots silently.
    for number in (33, 37, 38):
        if client.get(f'pulls/{number}')['head']['sha'] != prs[str(number)]['sha']:
            raise ValueError('SOURCE_MOVED_RETRY')
    if client.get('git/ref/heads/' + PROTECTED)['object']['sha'] != protected:
        raise ValueError('SOURCE_MOVED_RETRY')
    return {'version': 1, 'prs': prs, 'protected': protected, 'lanes': lanes,
            'rejected_records': rejected, 'execution_authority': 'HOLD'}

def compare(current, previous):
    # Only exact source and acceptance evidence changes trigger audit queue entries.
    changes = []
    for lane in LANES:
        def signature(snapshot):
            entry = snapshot.get('lanes', {}).get(lane, {}).get('handoff')
            record = entry['record'] if entry else {}
            pr = LANES[lane]
            source = snapshot.get('protected') if pr is None else snapshot.get('prs', {}).get(str(pr), {}).get('sha')
            return digest({'source': source, 'evidence': record.get('Evidence', []),
                           'ci': snapshot.get('prs', {}).get(str(pr), {}).get('ci', []),
                           'status': record.get('Status'), 'completed': record.get('Completed', []),
                           'blockers': record.get('Blockers', []), 'gate': snapshot.get('lanes', {}).get(lane, {}).get('gate')})
        if signature(current) != signature(previous):
            changes.append({'lane': lane, 'event_id': digest([lane, signature(previous), signature(current)]),
                            'reason': 'SOURCE_OR_ACCEPTANCE_CHANGED'})
    return changes

def write_report(snapshot, previous, output):
    output.mkdir(parents=True, exist_ok=True)
    changes = compare(snapshot, previous)
    payload = {'snapshot': snapshot, 'fingerprint': digest(snapshot), 'audit_queue': changes,
               'collected_at': dt.datetime.now(dt.timezone.utc).isoformat(),
               'baseline_mode': 'PRIOR_SUCCESS' if previous else 'BOOTSTRAP'}
    (output / 'status.json').write_text(json.dumps(payload, indent=2) + '\n')
    lines = ['# Control Tower Hub', '', 'Execution authority: HOLD. Handoffs are claims; evidence is not release approval.', '',
             '| Lane | Source SHA | Handoff gate |', '| --- | --- | --- |']
    for lane, pr in LANES.items():
        source = snapshot['protected'] if pr is None else snapshot['prs'][str(pr)]['sha']
        lines.append(f"| {lane} | `{source}` | {snapshot['lanes'][lane]['gate']} |")
    lines += ['', f'Audit delta events: {len(changes)}. Rejected records: {snapshot["rejected_records"]}.',
              '', 'See status.json for fixed-code blockers and exact CI run IDs. No automatic ChatGPT wake-up.']
    (output / 'dashboard.md').write_text('\n'.join(lines) + '\n')
    return payload

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=Path('hub-output'))
    parser.add_argument('--previous', type=Path)
    parser.add_argument('--validate', type=Path)
    args = parser.parse_args()
    try:
        if args.validate:
            validate(json.loads(args.validate.read_text()))
            print('HANDOFF_VALID')
            return
        previous = {}
        if args.previous:
            baseline = json.loads(args.previous.read_text())
            previous = baseline['snapshot']
            if baseline['fingerprint'] != digest(previous) or previous.get('execution_authority') != 'HOLD':
                raise ValueError('INVALID_BASELINE')
        payload = write_report(collect(Client(os.environ.get('GH_TOKEN'))), previous, args.output)
        summary = os.environ.get('GITHUB_STEP_SUMMARY')
        if summary:
            with open(summary, 'a') as stream:
                stream.write((args.output / 'dashboard.md').read_text())
        print('HUB_READONLY_COMPLETE ' + payload['fingerprint'])
    except Exception:
        # Never expose raw HTTP/provider exceptions, tokens or raw inputs.
        print('HUB_COLLECTION_FAILED_HOLD')
        raise SystemExit(1)

if __name__ == '__main__':
    main()
