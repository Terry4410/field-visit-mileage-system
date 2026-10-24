"""Fixed approved-origin static GET reader; no authentication or redirects."""
import time
import urllib.request
from urllib.parse import urlsplit
from arm import NoRedirect
from main_probe import Stop
from main_channel import file_manifest


class FrontendReader:
    def __init__(self,origin,manifest,opener=None,clock=time.monotonic):
        url=urlsplit(origin)
        if (url.scheme!='https' or not url.hostname or url.username or url.password or
            url.port not in (None,443) or url.path or url.query or url.fragment):raise Stop()
        self.origin=origin;self.allowed=set(file_manifest(manifest));self.used=set()
        self.clock=clock;self.end=clock()+120;self.bytes=0
        self.opener=opener or urllib.request.build_opener(NoRedirect())

    def __call__(self,path):
        if path not in self.allowed or path in self.used or self.clock()>=self.end:raise Stop()
        self.used.add(path)  # Every attempt consumes its slot; never retry.
        url=self.origin+path
        request=urllib.request.Request(url,method='GET',headers={'Cache-Control':'no-cache','Accept-Encoding':'identity'})
        with self.opener.open(request,timeout=min(20,max(.01,self.end-self.clock()))) as response:
            if response.status!=200 or response.geturl()!=url:raise Stop()
            result=bytearray()
            while True:
                if self.clock()>=self.end:raise Stop()
                chunk=response.read(65536)
                self.bytes+=len(chunk)
                if len(result)+len(chunk)>8388608 or self.bytes>33554432:raise Stop()
                result.extend(chunk)
                if not chunk:break
        return bytes(result)
