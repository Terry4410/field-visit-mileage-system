"""Runner-only executable test: memory FD + real deployed-driver constructor.

Requires a synthetic /home/site/wwwroot fixture created on the GitHub runner.
Never sends GO, uses invalid synthetic credentials and fixed fake-only packet.
No Azure environment, OIDC permission, application startup or SQL Open.
"""
import json
import os
from pathlib import Path
import subprocess
import sys
from main_probe import Stop, run_memory_probe, read_line, digest, memory_process


def main():
    print('MainContainerMemoryStage=NATIVE_HOST_BYTES_SELF_TEST;',flush=True)
    binary=Path(sys.argv[1]).read_bytes();runtime=Path(sys.argv[2])
    process=memory_process(binary,runtime,'G1SelfTest')
    try:
        output,_=process.communicate(timeout=30)
        parsed=json.loads(output)
        if process.returncode!=0:
            stage=parsed.get('stage')
            raise Stop(stage if stage in {'HOST_LIBRARY','HOST_INITIALIZE','HOST_DELEGATES','HOST_LOAD_BYTES','HOST_ENTRY'} else 'SELFTEST_REJECTED')
        if parsed.get('sqlConnections')!=0 or parsed.get('contract')!='PASS':raise Stop()
    finally:
        if process.poll() is None:process.kill()
        process.wait(timeout=5)
    print('MainContainerMemoryStage=PRIVATE_DRIVER_PREPARE;',flush=True)
    drivers={p.name:digest(p.read_bytes()) for p in Path('/home/site/wwwroot').glob('*.dll')}
    packet=dict(nonce='a'*64,driverHashes=drivers,connectionString='Server=tcp:sql-fieldvisit-jpe-uat.database.windows.net,1433;Database=db-fieldvisit-uat;User ID=synthetic-not-a-user;Password=synthetic-not-a-password;Encrypt=True;TrustServerCertificate=False',identityEndpoint='',identityHeader='')
    read,write=os.pipe();os.close(write)
    with os.fdopen(read,'rb',buffering=0) as no_go:
        runner=run_memory_probe(binary,runtime,packet,no_go,'a'*64)
        try:
            prepared=next(runner)
            if prepared.get('authentication')!='API_SQL_LOGIN' or prepared.get('sqlConnections')!=0:raise Stop()
            try:next(runner)
            except Stop:pass
            else:raise Stop()
        finally:runner.close()
    print('MainContainerMemoryContract=PASS; RealSqlClientConstructor=PASS; SqlOpenCalls=0; Azure=NOT_ACCESSED;')


if __name__=='__main__':
    try:main()
    except Exception as error:
        stage=str(error) if isinstance(error,Stop) and str(error) in {'INPUT','DRIVER','CONSENT','HOST_LIBRARY','HOST_INITIALIZE','HOST_DELEGATES','HOST_LOAD_BYTES','HOST_ENTRY','SELFTEST_REJECTED','PREPARE_REJECTED'} else 'SUPPRESSED'
        print('MainContainerMemoryContract=HOLD; Stage='+stage+'; Details=SUPPRESSED; Azure=NOT_ACCESSED;')
        sys.exit(1)
