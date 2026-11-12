#!/usr/bin/env python3
"""G1 exact-instance App SSH tunnel session (NOT a live CLI).

Subclass uses the preexisting, well-scoped stage/PTY/secret protocol but swaps
its management transport for the *official* pinned App Service WebSocket/SSH
remote connection. Retrieves the ephemeral SSH credential only from the
approved Azure CLI tunnel process; never includes it in source, args, env or
published output. No Azure operations on import.
"""
import json
import os
from pathlib import Path
import pty
import re
import select
import signal
import socket
import subprocess
import tempfile
import termios
import time

from p3_kudu_transport import KuduExecSession, TransportHold, MAX_OUTPUT_BYTES
from p3_ssh_transport_contract import (APP, RG, ssh_interactive_args,
                                       pinned_host_fingerprint, check_owner_account)


def owner_subscription_id():
    """Read-only exact Owner subscription verification, never a guessed default."""
    try:
        r=subprocess.run(['az','account','show','-o','json','--only-show-errors'],
                         stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,
                         timeout=20,check=True)
        record=json.loads(r.stdout)
        check_owner_account(record)
        subscription=str(record['id'])
        if re.fullmatch(r'[0-9a-fA-F-]{36}',subscription) is None:
            raise ValueError()
        return subscription
    except Exception:
        raise TransportHold('SSH_OWNER_CONTEXT_HOLD') from None


def ssh_tunnel_args(instance, port, subscription):
    if (not isinstance(instance,str) or
        re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]{7,255}',instance) is None
        or type(port) is not int or port<1024 or port>65535
        or not isinstance(subscription,str) or
        re.fullmatch(r'[0-9a-fA-F-]{36}',subscription) is None):
        raise TransportHold('INSTANCE_ID_HOLD')
    return ['az','webapp','create-remote-connection','-g',RG,'-n',APP,
            '--subscription',subscription,'--instance',instance,'--port',str(port)]


def validate_tunnel_host_fingerprint(observed):
    """Fail closed, emitting only a fixed code; never accept an unknown host."""
    if not observed:
        raise TransportHold('SSH_HOST_KEY_NOT_OBSERVED_HOLD')
    try:
        pinned_host_fingerprint(observed)
    except Exception:
        raise TransportHold('SSH_HOST_KEY_MISMATCH_HOLD') from None


class AppSshTunnel:
    def __init__(self,instance,deadline):
        self.instance=instance
        self.deadline=deadline
        self.proc=None
        self.port=None
        self.password=None
        self.known_hosts_dir=None
        self.known_hosts=None

    def start(self):
        try:
            # Reserve a local candidate port only; the pinned Azure CLI
            # subprocess owns it after this binding probe exits.
            with socket.socket(socket.AF_INET,socket.SOCK_STREAM) as sock:
                sock.bind(('127.0.0.1',0));port=sock.getsockname()[1]
            self.port=port
            argv=ssh_tunnel_args(self.instance,port,owner_subscription_id())
            self.proc=subprocess.Popen(argv,stdout=subprocess.PIPE,stderr=subprocess.PIPE,
                                       start_new_session=True)
            for s in (self.proc.stdout,self.proc.stderr):os.set_blocking(s.fileno(),False)
            captured=b'';end=min(self.deadline,time.monotonic()+60)
            while time.monotonic()<end:
                if self.proc.poll() is not None:raise TransportHold('SSH_TUNNEL_EXIT_HOLD')
                for stream in select.select([self.proc.stdout,self.proc.stderr],[],[],.2)[0]:
                    captured=(captured+(stream.read(4096) or b''))[-32768:]
                match=re.search(rb'username:\s*root\s*,\s*password:\s*([^\s}\r\n]{1,128})',captured,re.I)
                if match and self.password is None:self.password=bytearray(match.group(1))
                if self.password:
                    try:
                        with socket.create_connection(('127.0.0.1',port),timeout=.2):pass
                        break
                    except OSError:pass
            else:raise TransportHold('SSH_TUNNEL_TIMEOUT_HOLD')
            self.known_hosts_dir=tempfile.TemporaryDirectory(prefix='fv-g1-hostkey-')
            self.known_hosts=Path(self.known_hosts_dir.name)/'known_hosts'
            try:
                scan=subprocess.run(['ssh-keyscan','-p',str(port),'-t','ed25519','127.0.0.1'],
                                    stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,timeout=20,check=True)
            except (subprocess.TimeoutExpired, subprocess.CalledProcessError, OSError):
                raise TransportHold('SSH_HOST_KEY_NOT_OBSERVED_HOLD') from None
            if not scan.stdout or len(scan.stdout)>4096:
                raise TransportHold('SSH_HOST_KEY_NOT_OBSERVED_HOLD')
            fd=os.open(self.known_hosts,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
            with os.fdopen(fd,'wb') as f:f.write(scan.stdout)
            fingerprint=subprocess.run(['ssh-keygen','-lf',str(self.known_hosts)],
                                       capture_output=True,timeout=20,check=True)
            validate_tunnel_host_fingerprint(fingerprint.stdout)
            return self
        except TransportHold:
            # Any failure after Popen may leave the Azure tunnel alive.
            # Closing here is required even if the caller never receives self.
            self.close()
            raise
        except Exception:
            self.close()
            raise TransportHold('SSH_TUNNEL_SETUP_HOLD') from None

    def close(self):
        if self.password is not None:
            for i in range(len(self.password)):self.password[i]=0
            self.password=None
        if self.proc is not None and self.proc.poll() is None:
            try:
                os.killpg(self.proc.pid,signal.SIGTERM)
                self.proc.wait(timeout=5)
            except Exception:
                try:os.killpg(self.proc.pid,signal.SIGKILL)
                except Exception:pass
        if self.known_hosts_dir is not None:self.known_hosts_dir.cleanup()
        self.known_hosts_dir=None


class SshExecSession(KuduExecSession):
    """Adapter interface matches existing bounded Parent protocol exactly."""
    def __init__(self,instance,*,deadline):
        super().__init__(instance,deadline=deadline)
        self.tunnel=None

    def start(self):
        self.state.begin()
        try:
            self.tunnel=AppSshTunnel(self.instance,self.deadline).start()
            argv=ssh_interactive_args(self.tunnel.port,self.tunnel.known_hosts)
            master,slave=pty.openpty()
            self.fd=master  # Superclass closes master even if PTY/SSH setup fails.
            try:
                attrs=termios.tcgetattr(slave)
                # SSH token handoff must not be truncated at PTY canonical 4096B.
                attrs[3]&=~(termios.ECHO|termios.ICANON)
                attrs[6][termios.VMIN]=1
                attrs[6][termios.VTIME]=0
                termios.tcsetattr(slave,termios.TCSANOW,attrs)
                self.proc=subprocess.Popen(argv,stdin=slave,stdout=slave,stderr=slave,
                                           start_new_session=True)
            finally:
                os.close(slave)
            self.audit.append('PROCESS_START')
        except TransportHold:
            self.close()
            raise
        except Exception:
            self.close()
            raise TransportHold('SSH_SESSION_SPAWN_HOLD') from None

    def _await(self,pattern,seconds):
        rx=re.compile(pattern,re.I)
        end=min(self.deadline,self._clock()+seconds)
        while self._clock()<end:
            if rx.search(self.buffer):return
            ready=select.select([self.fd],[],[],.2)[0]
            if ready:
                chunk=os.read(self.fd,65536)
                if not chunk:raise TransportHold('KUDU_CHANNEL_CLOSED')
                self.buffer=(self.buffer+chunk)[-MAX_OUTPUT_BYTES:]
            elif self.proc is None or self.proc.poll() is not None:
                raise TransportHold('KUDU_CHANNEL_CLOSED')
        raise TransportHold('KUDU_CHANNEL_TIMEOUT')

    def await_cli_shell_banner(self,seconds=45):
        try:
            self._await(rb'password:\s*$',seconds)
            if not self.tunnel or not self.tunnel.password:
                raise TransportHold('KUDU_CHANNEL_CLOSED')
            self._write_all(bytes(self.tunnel.password)+b'\n')
            # No authentication value is ever intentionally printed.
            self._await(rb'root@[^\r\n]*#[ ]*$',seconds)
            self.buffer=b''
            if self.tunnel.password:
                for i in range(len(self.tunnel.password)):self.tunnel.password[i]=0
                self.tunnel.password=None
            self.audit.append('CLI_SHELL_BANNER_READY')
        except TransportHold:raise
        except Exception:raise TransportHold('KUDU_CHANNEL_CLOSED') from None

    def establish_safe_channel(self):
        """Disable remote echo and PTY canonical buffering before any token."""
        from p3_kudu_transport import require
        require(self.state.state=='CHANNEL','PROTOCOL_STATE_HOLD')
        boundary=len(self.buffer)
        self.send_public(b"stty -echo -icanon min 1 time 0\n",'ECHO_DISABLE_SENT')
        self.send_public(b"umask 077\nprintf '\n%s\n' FV_KUDU_CHANNEL_READY\n",
                         'ECHO_PROBE_SENT')
        self.expect_fixed(b'FV_KUDU_CHANNEL_READY',30,'ECHO_READY',after=boundary)

    def close(self):
        try:super().close()
        finally:
            if self.tunnel is not None:self.tunnel.close()
            self.tunnel=None
