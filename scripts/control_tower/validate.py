#!/usr/bin/env python3
"""Run isolated tests and publish a fixed-field, exact-source test manifest."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import unittest


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
    dirty = subprocess.check_output(['git', 'status', '--porcelain', '--', 'scripts/control_tower', 'docs/control-tower', '.github/workflows/control-tower-hub.yml'], text=True)
    if dirty:
        raise SystemExit('VALIDATION_DIRTY_SOURCE_HOLD')
    expected = os.environ.get('HUB_TEST_SHA', sha)
    if not re.fullmatch('[0-9a-f]{40}', expected) or sha != expected:
        raise SystemExit('VALIDATION_SOURCE_HOLD')
    tests = unittest.defaultTestLoader.discover('scripts/control_tower', pattern='test_*.py')
    result = unittest.TextTestRunner(verbosity=2).run(tests)
    args.output.mkdir(parents=True, exist_ok=True)
    schema = Path('docs/control-tower/handoff.schema.json')
    report = {'version': 1, 'source_sha': sha, 'schema_sha256': hashlib.sha256(schema.read_bytes()).hexdigest(),
              'tests_run': result.testsRun, 'failures': len(result.failures), 'errors': len(result.errors),
              'result': 'PASS' if result.wasSuccessful() else 'FAIL', 'execution_authority': 'HOLD'}
    (args.output / 'validation.json').write_text(json.dumps(report, indent=2) + '\n')
    raise SystemExit(0 if result.wasSuccessful() else 1)

if __name__ == '__main__':
    main()
