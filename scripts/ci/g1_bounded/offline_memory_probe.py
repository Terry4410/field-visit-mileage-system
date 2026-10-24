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
from main_probe import Stop, run_memory_probe, read_line, digest


def main():
    print('MainContainerMemoryStage=FD_SELF_TEST;',flush=True)
    binary=Path(sys.argv[1]).read_bytes();runtime=Path(sys.argv[2])
    fd=os.memfd_create('g1-offline-selftest',flags=0)
    try:
        os.write(fd,binary);os.lseek(fd,0,os.SEEK_SET)
        r=subprocess.run(['dotnet','exec','--runtimeconfig',str(runtime),f'/proc/self/fd/{fd}','--self-test'],
            pass_fds=(fd,),capture_output=True,timeout=30,check=False)
        if r.returncode!=0:
            # This invocation has no input/credentials and only --self-test.
            # Report known host categories, never arbitrary provider text.
            error=r.stderr.decode('utf-8','replace').lower()
            category=('HOST_REQUIRES_DLL_EXTENSION' if '.dll' in error and 'extension' in error else
                      'HOST_REJECTED_APPLICATION_PATH' if 'application' in error or 'exist' in error else
                      'HOST_SELFTEST_FAILED')
            print('MainContainerMemoryHost='+category+'; Exit='+str(r.returncode)+'; StderrSha256='+digest(r.stderr)+';',flush=True)
            raise Stop()
        if json.loads(r.stdout).get('sqlConnections')!=0:raise Stop()
    finally:os.close(fd)
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
    except Exception:
        print('MainContainerMemoryContract=HOLD; Details=SUPPRESSED; Azure=NOT_ACCESSED;')
        sys.exit(1)
