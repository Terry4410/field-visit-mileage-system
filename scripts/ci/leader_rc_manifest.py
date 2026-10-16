"""Create one exact-source candidate preflight manifest; never authorizes release."""
import hashlib,json,os,re,subprocess
from pathlib import Path
BRANCH='refs/heads/work/leader-uat010-minimal-rc-20261016'
PROTECTED='7f0cf0cb55467916fb0f57239301a8787fd5cb69'

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def build_manifest(root,source,ref,head,evidence,gates):
    if not re.fullmatch(r'[0-9a-f]{40}',source) or source!=head or ref!=BRANCH:raise ValueError('EXACT_SOURCE_OR_BRANCH_MISMATCH')
    if evidence.get('contract')!='leader-uat-preflight-evidence-v1' or evidence.get('protectedBase')!=PROTECTED:raise ValueError('EVIDENCE_CONTRACT_MISMATCH')
    if not gates or any(v not in ('HOLD','NOT_AUTHORIZED') for v in gates.values()):raise ValueError('NONDEPLOY_GATE_RELAXATION_DENIED')
    if evidence['api']['activeSourceSha'] is not None or evidence['rollback']['apiArchiveVerified'] or evidence['rollback']['pagesArchiveVerified']:raise ValueError('UNREVIEWED_PROVENANCE_PROMOTION_DENIED')
    if evidence['decision']['release']!='HOLD' or evidence['decision']['ownerExecutionApproval']!='NOT_AUTHORIZED':raise ValueError('UNAUTHORIZED_RELEASE_STATE')
    art=root/'artifacts';files={}
    for kind in ['api','frontend']:
        folder=art/kind
        if not folder.is_dir():raise ValueError('ARTIFACT_DIRECTORY_MISSING')
        stamp=folder/'source-sha.txt'
        if not stamp.is_file() or stamp.read_text().strip()!=source:raise ValueError('ARTIFACT_STAMP_MISMATCH')
        for p in folder.rglob('*'):
            if p.is_symlink():raise ValueError('ARTIFACT_SYMLINK_DENIED')
            if p.is_file():files[str(p.relative_to(art))]=sha(p)
    return {'contract':'leader-uat-preflight-manifest-v2','sourceSha':source,'branch':ref,'protectedBase':'b1a507c90c577fb2c0cdd6a98ef33481d3a5f790',
      'originalProtectedBase':'d691fba430ce80dd2048f84863b4628e1e6713d8','implementationSha':evidence['implementationSha'],
      'scope':'PARTIAL_LEADER_DRAFT_WITH_VISITOR_COMPATIBILITY','stage':'CANDIDATE_PACKAGE_BEFORE_FINAL_BROWSER_SQL_TESTS','migrationRequired':False,
      'observedPagesSourceSha':evidence['pages']['sourceSha'],'pagesProvenance':'LIVE_STAMP_OBSERVED_ARCHIVE_UNAVAILABLE',
      'deployedApiSource':'UNVERIFIED','historicalApiDeploymentSourceSha':evidence['api']['historicalSourceSha'],
      'schemaEvidence':'PROTECTED_EF_MODEL_ONLY','actualUatParity':'HOLD','referenceChain':'HOLD_MISSING_APPROVED_150_BASELINE',
      'existingVisitorCompatibility':'SOURCE_CONTRACT_RESTORED_RUNTIME_EVIDENCE_REQUIRED','rollbackArtifact':'HOLD_PRIOR_BINARY_PAIR_UNVERIFIED',
      'deployment':'NOT_AUTHORIZED','releaseGates':gates,'evidenceSnapshot':evidence,'files':files,
      'evidenceSnapshotSha256':sha(root/'docs/release/leader-uat010-rc/evidence/existing-uat-observations.json'),
      'releaseGatesSha256':sha(root/'docs/release/leader-uat010-rc/release-gates.json')}

if __name__=='__main__':
    root=Path('.')
    evidence=json.loads((root/'docs/release/leader-uat010-rc/evidence/existing-uat-observations.json').read_text())
    gates=json.loads((root/'docs/release/leader-uat010-rc/release-gates.json').read_text())
    head=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
    manifest=build_manifest(root,os.environ['GITHUB_SHA'],os.environ['GITHUB_REF'],head,evidence,gates)
    (root/'artifacts/release-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    print('EXACT_SHA_PREFLIGHT_MANIFEST_CREATED deployment=NOT_AUTHORIZED actualUatParity=HOLD visitorCompatibility=HOLD')
