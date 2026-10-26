#!/usr/bin/env python3
"""Two-phase connector publication: GET-only prepare/check/verify; never POST.

The supported GitHub connector appends the exact prepared comment between check and verify.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
from hub import Client, ISSUE, MARKER, SCHEMA_PATH, ci_runs, digest, scan_comments, source_for, validate, verify_evidence
from schema import strict_loads


def body_for(record):
    return MARKER + json.dumps(record, sort_keys=True, separators=(',', ':'))


def schema_hash():
    return hashlib.sha256(SCHEMA_PATH.read_bytes()).hexdigest()


def prepare(client, record):
    validate(record)
    stream = scan_comments(client.pages(f'issues/{ISSUE}/comments'))
    if record['Sequence'] <= stream['frontier'][record['Lane']]:
        raise ValueError('SEQUENCE_NOT_MONOTONIC')
    source = source_for(client, record['Lane'])
    source['ci'] = ci_runs(client, source['sha'])
    reasons, artifacts = verify_evidence(client, record, source)
    if reasons:
        raise ValueError('SOURCE_CI_EVIDENCE_HOLD')
    if source_for(client, record['Lane'])['sha'] != source['sha']:
        raise ValueError('SOURCE_MOVED')
    if scan_comments(client.pages(f'issues/{ISSUE}/comments'))['stream_fingerprint'] != stream['stream_fingerprint']:
        raise ValueError('STREAM_MOVED')
    return {'version': 1, 'issue': ISSUE, 'schema_sha256': schema_hash(), 'record': record,
            'stream_fingerprint': stream['stream_fingerprint'], 'artifacts': artifacts,
            'body': body_for(record), 'body_sha256': hashlib.sha256(body_for(record).encode()).hexdigest()}


def check(client, plan):
    if plan.get('version') != 1 or plan.get('issue') != ISSUE or plan.get('schema_sha256') != schema_hash():
        raise ValueError('PLAN_SCHEMA_MISMATCH')
    fresh = prepare(client, plan['record'])
    if fresh != plan:
        raise ValueError('PLAN_STALE')
    return fresh


def verify(client, plan, comment_id):
    if type(comment_id) is not int or comment_id <= 0 or plan.get('version') != 1 or plan.get('issue') != ISSUE or plan.get('schema_sha256') != schema_hash():
        raise ValueError('READBACK_PLAN_MISMATCH')
    record = validate(plan['record'])
    if plan['body'] != body_for(record) or plan['body_sha256'] != hashlib.sha256(plan['body'].encode()).hexdigest():
        raise ValueError('READBACK_BODY_MISMATCH')
    comment = client.get(f'issues/comments/{comment_id}')
    if (int(comment['id']) != comment_id or comment.get('issue_url') != 'https://api.github.com/repos/Terry4410/field-visit-mileage-system/issues/41'
            or comment.get('user', {}).get('login') != 'Terry4410' or comment.get('body') != plan['body']):
        raise ValueError('READBACK_MISMATCH')
    validate(strict_loads(comment['body'][len(MARKER):]))
    stream = scan_comments(client.pages(f'issues/{ISSUE}/comments'))
    row = stream['latest'].get(record['Lane'])
    if not row or row['schema_status'] != 'VALID' or row['comment_id'] != comment_id or row.get('record') != record:
        raise ValueError('READBACK_CONFLICT_OR_SUPERSEDED')
    source = source_for(client, record['Lane'])
    source['ci'] = ci_runs(client, source['sha'])
    reasons, artifacts = verify_evidence(client, record, source)
    if reasons:
        raise ValueError('READBACK_SOURCE_EVIDENCE_HOLD')
    if source_for(client, record['Lane'])['sha'] != record['SHA']:
        raise ValueError('READBACK_SOURCE_MOVED')
    if scan_comments(client.pages(f'issues/{ISSUE}/comments'))['stream_fingerprint'] != stream['stream_fingerprint']:
        raise ValueError('READBACK_STREAM_MOVED')
    return {'result': 'HANDOFF_VALID', 'comment_id': comment_id, 'lane': record['Lane'], 'sha': record['SHA'],
            'sequence': record['Sequence'], 'ci': record['CI'], 'schema_sha256': schema_hash(),
            'body_sha256': plan['body_sha256'], 'artifacts': artifacts, 'execution_authority': 'HOLD'}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('operation', choices=('prepare', 'check', 'verify'))
    parser.add_argument('--payload', type=Path)
    parser.add_argument('--plan', type=Path, required=True)
    parser.add_argument('--comment-id', type=int)
    parser.add_argument('--receipt', type=Path)
    args = parser.parse_args()
    try:
        client = Client(os.environ.get('GH_TOKEN'))
        if args.operation == 'prepare':
            result = prepare(client, strict_loads(args.payload.read_text()))
            args.plan.write_text(json.dumps(result, indent=2) + '\n')
            print('PAYLOAD_VALIDATED_NOT_PUBLISHED')
        elif args.operation == 'check':
            check(client, strict_loads(args.plan.read_text()))
            print('PUBLICATION_READY_NOT_PUBLISHED')
        else:
            result = verify(client, strict_loads(args.plan.read_text()), args.comment_id)
            if args.receipt:
                args.receipt.write_text(json.dumps(result, indent=2) + '\n')
            print('HANDOFF_VALID ' + str(result['comment_id']))
    except Exception:
        print('HANDOFF_PUBLISH_HOLD')
        raise SystemExit(1)

if __name__ == '__main__':
    main()
