"""Hash preservation only; not live SQL or authorization proof."""
import hashlib,json,pathlib,subprocess,sys
root=pathlib.Path(__file__).resolve().parents[3]
prefix='docs/release/g1-minimal-preflight/'
try:
    baseline='c12451700b619897afe0e4c5dc9ae2862e259d2f'
    files=subprocess.check_output(['git','ls-tree','-r','--name-only',baseline,'--',prefix],cwd=root,text=True).splitlines()
    assert files
    for name in files:
        assert subprocess.check_output(['git','show',baseline+':'+name],cwd=root)==(root/name).read_bytes()
    folder=root/prefix
    assert hashlib.sha256((folder/'manifest.json').read_bytes()).hexdigest()=='7742024b9bb790acef0821d0dd10361dd284f4a44311d8045464d1a7f1db686f'
    manifest=json.loads((folder/'manifest.json').read_text())
    for name,digest in manifest['files'].items():
        assert pathlib.Path(name).name==name
        assert hashlib.sha256((folder/name).read_bytes()).hexdigest()==digest
    collector=(folder/'collect.ps1').read_text()
    assert collector.count('$ApplicationConnection.Open()')==1
    assert '$cmd.CommandTimeout=30' in collector and "$b['ConnectRetryCount']=0" in collector
    print('AcceptedInputs=PASS; CollectorOpenCallSites=1; LiveProof=UNKNOWN')
except Exception:
    print('AcceptedInputs=STOP; Details=Suppressed');sys.exit(1)
