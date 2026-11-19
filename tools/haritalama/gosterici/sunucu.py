"""Bu klasörü yalnızca bu bilgisayara (127.0.0.1) sunar. Hazır http.server video ileri sarmayı (Range) desteklemiyor, bu destekler."""
import os
import re
import sys
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer


class Isleyici(SimpleHTTPRequestHandler):
    def send_head(self):
        aralik = self.headers.get("Range")
        yol = self.translate_path(self.path)
        if not aralik or not os.path.isfile(yol):
            return super().send_head()
        m = re.match(r"bytes=(\d*)-(\d*)", aralik)
        boy = os.path.getsize(yol)
        bas = int(m.group(1)) if m.group(1) else max(0, boy - int(m.group(2)))
        son = int(m.group(2)) if m.group(1) and m.group(2) else boy - 1
        son = min(son, boy - 1)
        f = open(yol, "rb")
        f.seek(bas)
        self.send_response(206)
        self.send_header("Content-Type", self.guess_type(yol))
        self.send_header("Content-Range", f"bytes {bas}-{son}/{boy}")
        self.send_header("Content-Length", str(son - bas + 1))
        self.send_header("Accept-Ranges", "bytes")
        self.end_headers()
        self._kalan = son - bas + 1
        return f

    def copyfile(self, kaynak, hedef):
        kalan = getattr(self, "_kalan", None)
        if kalan is None:
            return super().copyfile(kaynak, hedef)
        while kalan > 0:
            b = kaynak.read(min(1 << 16, kalan))
            if not b:
                break
            try:
                hedef.write(b)
            except (ConnectionResetError, BrokenPipeError):
                break
            kalan -= len(b)
        self._kalan = None

    def end_headers(self):
        self.send_header("Cache-Control", "no-cache")
        super().end_headers()

    def log_message(self, *a):
        pass


if __name__ == "__main__":
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8765
    print(f"http://127.0.0.1:{port}/  (kapatmak için bu pencereyi kapat)")
    ThreadingHTTPServer(("127.0.0.1", port), Isleyici).serve_forever()
