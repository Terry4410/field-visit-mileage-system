#!/usr/bin/env python3
"""GET-only Control Tower observer. Status never grants execution authority."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import urllib.request
from schema import SCHEMA_PATH, errors, load_schema, strict_loads

REPO = 'Terry4410/field-visit-mileage-system'
PROTECTED = 'post-uat/v1.8.0'
BRANCH = 'work/control-tower-hub-20261016'
ISSUE = 41
LANES = {'WORK_A': 33, 'WORK_B': 38, 'WORK_C': 37, 'WORK_D': 42, 'AUDIT': 33, 'OWNER': None}
MARKER = '<!-- control-tower-handoff:v1 -->\n'
SHA = re.compile(r'[0-9a-f]{40}')
HASH = re.compile(r'[0-9a-f]{64}')
# Compatibility exports are derived from the actual schema, never a parallel enum list.
FIELDS = set(load_schema()['required'])
CODES = set(load_schema()['properties']['Completed']['items']['enum'])


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def timestamp(value):
    if value is None:
        return None
    if not isinstance(value, str) or not re.fullmatch(r'\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?(?:Z|\+00:00)', value):
        raise ValueError('INVALID_TIMESTAMP')
    dt.datetime.fromisoformat(value.replace('Z', '+00:00'))
    return value


def schema_codes(record):
    return sorted({'SCHEMA_' + ('_'.join(path[:1]).upper() + '_' if path else '') + kind
                   for path, kind in errors(record)})


def validate(record):
    if errors(record):
        raise ValueError('SCHEMA_INVALID')
    if len(set(record['CI'])) != len(record['CI']) or len(set(record['Evidence'])) != len(record['Evidence']):
        raise ValueError('DUPLICATE_REFERENCE')
    return record


class Client:
    def __init__(self, token=None):
        self.token = token
    def get(self, path):
        if not isinstance(path, str) or path.startswith('/') or '..' in path or '://' in path or '\\' in path:
            raise ValueError('UNSAFE_ENDPOINT')
        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, *args, **kwargs):
                return None
        headers = {'Accept': 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28'}
        if self.token:
            headers['Authorization'] = 'Bearer ' + self.token
        req = urllib.request.Request('https://api.github.com/repos/' + REPO + '/' + path, headers=headers, method='GET')
        with urllib.request.build_opener(NoRedirect).open(req, timeout=30) as response:
            return strict_loads(response.read(8_000_001).decode())
    def pages(self, path, key=None):
        rows = []
        for page in range(1, 101):
            data = self.get(path + ('&' if '?' in path else '?') + f'per_page=100&page={page}')
            if key:
                data = data[key]
            if not isinstance(data, list):
                raise ValueError('INVALID_PAGE')
            rows.extend(data)
            if len(data) < 100:
                return rows
        raise ValueError('PAGINATION_LIMIT')


def scan_comments(comments):
    """Never repair input. Keep safe invalid diagnostics and a lane's latest observation."""
    latest, history, frontier, seen_ids = {}, [], {lane: 0 for lane in LANES}, {}
    for comment in sorted(comments, key=lambda c: int(c['id'])):
        body = comment.get('body', '')
        if not isinstance(body, str) or not body.startswith(MARKER):
            continue
        cid = int(comment['id'])
        event_fingerprint = digest({'body_sha256': hashlib.sha256(body.encode()).hexdigest(),
                                    'trusted': comment.get('user', {}).get('login') == 'Terry4410',
                                    'created_at': timestamp(comment.get('created_at')),
                                    'updated_at': timestamp(comment.get('updated_at'))})
        if cid in seen_ids:
            if seen_ids[cid] != event_fingerprint:
                raise ValueError('COMMENT_EVENT_CONFLICT')
            continue  # duplicate delivery of the same GitHub comment is not a second publication
        seen_ids[cid] = event_fingerprint
        row = {'comment_id': cid, 'lane': None, 'sequence': None, 'schema_status': 'INVALID',
               'reasons': [], 'created_at': timestamp(comment.get('created_at')),
               'updated_at': timestamp(comment.get('updated_at'))}
        record = None
        if len(body) > 12000:
            row['reasons'].append('RECORD_TOO_LARGE')
        else:
            try:
                candidate = strict_loads(body[len(MARKER):])
                if isinstance(candidate, dict):
                    if isinstance(candidate.get('Lane'), str) and candidate['Lane'] in LANES:
                        row['lane'] = candidate['Lane']
                    sequence = candidate.get('Sequence')
                    if type(sequence) is int and 1 <= sequence <= 10**9:
                        row['sequence'] = sequence
                row['reasons'].extend(schema_codes(candidate))
                try:
                    record = validate(candidate)
                except (ValueError, TypeError):
                    if not row['reasons']:
                        row['reasons'].append('DUPLICATE_REFERENCE')
            except (ValueError, TypeError, RecursionError):
                row['reasons'].append('JSON_INVALID')
        trusted = comment.get('user', {}).get('login') == 'Terry4410'
        if not trusted:
            row['reasons'].append('UNAUTHORIZED_AUTHOR')
        lane, seq = row['lane'], row['sequence']
        # Reserve typed sequences even in invalid trusted records; never reuse historical numbers.
        if trusted and lane and seq:
            if seq == frontier[lane]:
                row['reasons'].append('DUPLICATE_SEQUENCE')
            elif seq < frontier[lane]:
                row['reasons'].append('REPLAYED_SEQUENCE')
            frontier[lane] = max(frontier[lane], seq)
        if row['updated_at'] and row['created_at'] and row['updated_at'] != row['created_at']:
            row['reasons'].append('EDITED_RECORD')
        if record and not row['reasons']:
            row['schema_status'] = 'VALID'
            row['record'] = record
        row['reasons'] = sorted(set(row['reasons']))
        history.append(row)
        if trusted and lane:
            latest[lane] = row  # invalid latest is visible; never silently fall back to an older valid handoff
    return {'latest': latest, 'history': history, 'frontier': frontier,
            'stream_fingerprint': digest(sorted(seen_ids.items()))}


def ci_runs(client, sha):
    data = client.get(f'actions/runs?head_sha={sha}&per_page=100')
    if data.get('total_count', 0) > 100:
        raise ValueError('RUN_LIMIT')
    result = []
    for run in data['workflow_runs']:
        if run['head_sha'] != sha or run.get('repository', {}).get('full_name') != REPO:
            raise ValueError('RUN_PROVENANCE_MISMATCH')
        status = 'PASS' if run.get('status') == 'completed' and run.get('conclusion') == 'success' else (
                 'PENDING' if run.get('status') != 'completed' else 'FAIL')
        result.append({'id': int(run['id']), 'attempt': int(run.get('run_attempt', 1)), 'status': status,
                       'event': run['event'] if run.get('event') in ('push', 'pull_request', 'workflow_dispatch', 'schedule') else 'OTHER',
                       'updated_at': timestamp(run.get('updated_at'))})
    return sorted(result, key=lambda r: r['id'])


def source_for(client, lane):
    number = LANES[lane]
    if number is None:
        sha = client.get('git/ref/heads/' + PROTECTED)['object']['sha']
        return {'sha': sha, 'kind': 'PROTECTED', 'pr': None, 'state': 'READ_ONLY'}
    raw = client.get(f'pulls/{number}')
    sha = raw['head']['sha']
    if raw['head']['repo']['full_name'] != REPO:
        raise ValueError('UNTRUSTED_SOURCE')
    return {'sha': sha, 'kind': 'PR', 'pr': number,
            'state': 'MERGED_HISTORICAL' if raw['merged'] else ('OPEN' if raw['state'] == 'open' else 'CLOSED'),
            'updated_at': timestamp(raw.get('updated_at')), 'merged_at': timestamp(raw.get('merged_at'))}


def verify_evidence(client, record, source):
    reasons, artifacts = [], []
    if record['SHA'] != source['sha']:
        reasons.append('STALE_SHA')
    by_id = {run['id']: run for run in source['ci']}
    if not record['CI'] or any(r not in by_id or by_id[r]['status'] != 'PASS' for r in record['CI']):
        reasons.append('CI_MISMATCH_OR_NOT_SUCCESS')
    if not record['Evidence']:
        reasons.append('MISSING_EVIDENCE')
    if reasons:
        return reasons, artifacts
    for run_id in record['CI']:
        for artifact in client.pages(f'actions/runs/{run_id}/artifacts', key='artifacts'):
            value = artifact.get('digest', '')
            if (artifact.get('expired') is not False or artifact.get('workflow_run', {}).get('head_sha') != record['SHA']
                    or artifact.get('workflow_run', {}).get('id') != run_id
                    or not isinstance(value, str) or not value.startswith('sha256:') or not HASH.fullmatch(value[7:])):
                continue
            artifacts.append({'id': int(artifact['id']), 'run_id': run_id, 'sha256': value[7:],
                              'created_at': timestamp(artifact.get('created_at'))})
    known = {a['sha256'] for a in artifacts}
    if any(h not in known for h in record['Evidence']):
        reasons.append('ARTIFACT_DIGEST_MISMATCH_OR_UNVERIFIED')
    return reasons, [a for a in artifacts if a['sha256'] in record['Evidence']]


def collect(client):
    sources, cached = {}, {}
    for lane in LANES:
        pr = LANES[lane]
        if pr not in cached:
            source = source_for(client, lane)
            if not SHA.fullmatch(source['sha']):
                raise ValueError('INVALID_SOURCE_SHA')
            source['ci'] = ci_runs(client, source['sha'])
            commit = client.get('commits/' + source['sha'])
            source['commit_at'] = timestamp(commit['commit']['committer']['date'])
            cached[pr] = source
        sources[lane] = cached[pr]
    stream = scan_comments(client.pages(f'issues/{ISSUE}/comments'))
    lanes = {}
    for lane, source in sources.items():
        observation = stream['latest'].get(lane)
        reasons, artifacts = [], []
        if not observation:
            reasons.append('MISSING_HANDOFF')
            state = 'MISSING'
        elif observation['schema_status'] != 'VALID':
            reasons.extend(observation['reasons'])
            state = 'INVALID'
        else:
            reasons, artifacts = verify_evidence(client, observation['record'], source)
            state = 'INVALID' if reasons else 'VALIDATED'
        lanes[lane] = {'gate': 'HOLD' if reasons else 'REPORTED_' + observation['record']['Status'],
                       'handoff_status': state, 'reasons': reasons, 'handoff': observation,
                       'artifacts': artifacts, 'source': source}
    for lane in ('WORK_A', 'WORK_B', 'WORK_C', 'WORK_D', 'OWNER'):
        if source_for(client, lane)['sha'] != sources[lane]['sha']:
            raise ValueError('SOURCE_MOVED_RETRY')
    # Comment changes during collection are also reconciled; no silently mixed stream.
    if scan_comments(client.pages(f'issues/{ISSUE}/comments'))['stream_fingerprint'] != stream['stream_fingerprint']:
        raise ValueError('HANDOFF_STREAM_MOVED_RETRY')
    return {'version': 2, 'prs': {str(n): s for n, s in cached.items() if n is not None},
            'protected': sources['OWNER']['sha'], 'lanes': lanes, 'records': stream['history'],
            'frontier': stream['frontier'], 'schema_sha256': hashlib.sha256(SCHEMA_PATH.read_bytes()).hexdigest(),
            'rejected_records': sum(x['schema_status'] == 'INVALID' for x in stream['history']),
            'execution_authority': 'HOLD'}


def compare(current, previous):
    def signature(snapshot, lane):
        entry = snapshot.get('lanes', {}).get(lane, {})
        handoff = entry.get('handoff') or {}
        record = handoff.get('record', {})
        source = entry.get('source', {})
        pr = LANES[lane]
        sha = source.get('sha') or (snapshot.get('protected') if pr is None else snapshot.get('prs', {}).get(str(pr), {}).get('sha'))
        ci = source.get('ci', snapshot.get('prs', {}).get(str(pr), {}).get('ci', []))
        return digest({'source': sha, 'source_state': source.get('state'),
                       'ci': [{k: x.get(k) for k in ('id', 'attempt', 'status')} for x in ci],
                       'evidence': sorted(record.get('Evidence', [])), 'status': record.get('Status'),
                       'completed': sorted(record.get('Completed', [])), 'blockers': sorted(record.get('Blockers', [])),
                       'next_delta': sorted(record.get('NextDelta', [])), 'authorization': record.get('OwnerAuthorization'),
                       'gate': entry.get('gate'), 'validation': entry.get('handoff_status'), 'reasons': entry.get('reasons', [])})
    changes = []
    for lane in LANES:
        before, after = signature(previous, lane), signature(current, lane)
        if before != after:
            changes.append({'lane': lane, 'event_id': digest([lane, before, after]), 'reason': 'SOURCE_OR_ACCEPTANCE_CHANGED'})
    return changes


def write_report(snapshot, previous, output):
    output.mkdir(parents=True, exist_ok=True)
    payload = {'snapshot': snapshot, 'fingerprint': digest(snapshot), 'audit_queue': compare(snapshot, previous),
               'collected_at': dt.datetime.now(dt.timezone.utc).isoformat(),
               'baseline_mode': 'PRIOR_SUCCESS' if previous else 'BOOTSTRAP',
               'collector_sha': os.environ.get('GITHUB_SHA') if SHA.fullmatch(os.environ.get('GITHUB_SHA', '')) else None,
               'collector_run': int(os.environ['GITHUB_RUN_ID']) if os.environ.get('GITHUB_RUN_ID', '').isdigit() else None}
    (output / 'status.json').write_text(json.dumps(payload, indent=2) + '\n')
    lines = ['# Control Tower Hub', '', 'Execution authority: HOLD. VALIDATED means schema + source/CI/artifact metadata binding, never release approval.', '',
             '| Lane | Exact SHA | CI (run:state) | Handoff | Completed | Blockers | Next Delta | Owner authorization |',
             '| --- | --- | --- | --- | --- | --- | --- | --- |']
    provenance = []
    for lane, entry in snapshot['lanes'].items():
        row = entry.get('handoff') or {}
        record = row.get('record', {}) if entry['handoff_status'] == 'VALIDATED' else {}
        ci = ', '.join(str(x['id']) + ':' + x['status'] for x in entry['source']['ci']) or 'NONE'
        lines.append('| ' + ' | '.join([lane, '`' + entry['source']['sha'] + '`', ci,
                     entry['handoff_status'] + '/' + entry['gate'], ', '.join(record.get('Completed', [])) or 'NONE',
                     ', '.join(record.get('Blockers', []) + entry['reasons']) or 'NONE',
                     ', '.join(record.get('NextDelta', [])) or 'NONE', record.get('OwnerAuthorization', 'UNVERIFIED')]) + ' |')
        provenance += ['', f"{lane} provenance: {entry['source']['kind']}; state={entry['source']['state']}; commit_at={entry['source']['commit_at']}; comment_id={row.get('comment_id')}; comment_at={row.get('created_at')}", '']
    lines += provenance
    invalid = [x for x in snapshot['records'] if x['schema_status'] == 'INVALID']
    lines += ['', '## Rejected historical records (original comments retained)', '', '| Comment ID | Lane | State | Reasons |', '| --- | --- | --- | --- |']
    for row in invalid:
        lines.append(f"| {row['comment_id']} | {row['lane'] or 'UNKNOWN'} | INVALID/HOLD | {', '.join(row['reasons'])} |")
    lines += ['', f"Collected: {payload['collected_at']}; baseline: {payload['baseline_mode']}; delta events: {len(payload['audit_queue'])}.",
              '', 'JSON includes source timestamps, artifact IDs/digests, schema hash and collector provenance. No automatic ChatGPT wake-up.']
    (output / 'dashboard.md').write_text('\n'.join(lines) + '\n')
    return payload


def guard_event(environment=None):
    environment = os.environ if environment is None else environment
    if environment.get('GITHUB_ACTIONS') != 'true':
        return
    if (environment.get('GITHUB_REPOSITORY') != REPO
            or environment.get('GITHUB_EVENT_NAME') not in ('push', 'workflow_dispatch')
            or environment.get('GITHUB_REF') != 'refs/heads/' + BRANCH
            or not SHA.fullmatch(environment.get('GITHUB_SHA', ''))):
        raise ValueError('UNAUTHORIZED_COLLECTOR_TRIGGER')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=Path('hub-output'))
    parser.add_argument('--previous', type=Path)
    parser.add_argument('--validate', type=Path)
    parser.add_argument('--guard-event', action='store_true')
    args = parser.parse_args()
    try:
        if args.validate:
            validate(strict_loads(args.validate.read_text()))
            print('SCHEMA_VALID_ONLY')
            return
        guard_event()
        if args.guard_event:
            print('COLLECTOR_TRIGGER_ALLOWED')
            return
        previous = {}
        if args.previous:
            baseline = strict_loads(args.previous.read_text())
            previous = baseline['snapshot']
            if baseline['fingerprint'] != digest(previous) or previous.get('execution_authority') != 'HOLD':
                raise ValueError('INVALID_BASELINE')
        payload = write_report(collect(Client(os.environ.get('GH_TOKEN'))), previous, args.output)
        if os.environ.get('GITHUB_STEP_SUMMARY'):
            with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as stream:
                stream.write((args.output / 'dashboard.md').read_text())
        print('HUB_READONLY_COMPLETE ' + payload['fingerprint'])
    except Exception:
        print('HUB_COLLECTION_FAILED_HOLD')
        raise SystemExit(1)

if __name__ == '__main__':
    main()
