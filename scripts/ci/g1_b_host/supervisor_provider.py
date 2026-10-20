"""Test-only child against loopback; never a live host."""
import hashlib, os, pathlib, sys, tempfile
from supervisor import supervise
try:
    assert os.environ.get('GITHUB_ACTIONS') == 'true'
    assert os.environ.get('GITHUB_REPOSITORY') == 'Terry4410/field-visit-mileage-system'
    assert os.environ.get('G1_TEST_ONLY') == '1'
    with tempfile.TemporaryDirectory() as tmp:
        p=pathlib.Path(tmp)
        worker=pathlib.Path(__file__).with_name('provider_worker.ps1').resolve()
        os.environ['G1_WORKER_STARTED']=str(p/'started')
        os.environ['G1_WORKER_LATER']=str(p/'later')
        sha=hashlib.sha256(worker.read_bytes()).hexdigest()
        code=supervise(['/usr/bin/pwsh',str(worker)], reviewed_worker_sha256=sha, result_path=str(p/'result'),deadline=8)
        assert code == 'ADAPTER_DEADLINE_STOP'
        assert (p/'started').read_text() == 'STARTED'
        assert not (p/'later').exists()
    print('SupervisorRealProvider=PASS; LaterStage=NOT_RUN; ProductionAdapterCollector=NOT_TESTED;')
except Exception:
    print('SupervisorRealProvider=STOP; Details=SUPPRESSED;');sys.exit(1)
