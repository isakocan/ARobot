"""
En küçük WebSocket (RFC 6455) sunucu tarafı; Python 3.6 standart kütüphanesiyle.

Neden: HTTP'de her komut yeni bir istek (başlıklarıyla ~500 bayt, ayrı yanıt beklenir).
WebSocket'te bağlantı bir kez açılır, iki yöne de küçük mesajlar beklemeden gider.
Tarayıcılar kendi mesajlarını parçalamadan gönderdiği için parçalı çerçeve desteklenmez.
"""

import base64
import hashlib
import struct
import threading

_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"

METIN, IKILI, KAPAT, PING, PONG = 0x1, 0x2, 0x8, 0x9, 0xA


class Kapandi(Exception):
    pass


def el_sikis(istek):
    """BaseHTTPRequestHandler içinden çağrılır; olmazsa None döner (400 gönderilmiş olur)."""
    anahtar = istek.headers.get("Sec-WebSocket-Key")
    if not anahtar or "websocket" not in istek.headers.get("Upgrade", "").lower():
        istek.send_error(400)
        return None
    kabul = base64.b64encode(hashlib.sha1((anahtar + _GUID).encode()).digest()).decode()
    istek.send_response(101)
    istek.send_header("Upgrade", "websocket")
    istek.send_header("Connection", "Upgrade")
    istek.send_header("Sec-WebSocket-Accept", kabul)
    istek.end_headers()
    istek.wfile.flush()
    istek.close_connection = True
    return WebSoket(istek.connection, istek.rfile)


class WebSoket:
    def __init__(self, soket, okuyucu):
        self.soket = soket
        self.okuyucu = okuyucu
        self.yazma_kilidi = threading.Lock()
        self.acik = True

    def _oku_tam(self, n):
        veri = self.okuyucu.read(n)
        if len(veri) < n:
            raise Kapandi()
        return veri

    def oku(self):
        """Sıradaki veri mesajı: (METIN, str) ya da (IKILI, bytes). Kapanınca Kapandi."""
        while True:
            b1, b2 = self._oku_tam(2)
            kod = b1 & 0x0F
            uzunluk = b2 & 0x7F
            if uzunluk == 126:
                uzunluk = struct.unpack(">H", self._oku_tam(2))[0]
            elif uzunluk == 127:
                uzunluk = struct.unpack(">Q", self._oku_tam(8))[0]
            maske = self._oku_tam(4) if b2 & 0x80 else None
            veri = self._oku_tam(uzunluk) if uzunluk else b""
            if maske:
                veri = bytes(b ^ maske[i & 3] for i, b in enumerate(veri))
            if kod == KAPAT:
                self.kapat()
                raise Kapandi()
            if kod == PING:
                self._gonder(PONG, veri)
                continue
            if kod == METIN:
                return METIN, veri.decode("utf-8", "replace")
            if kod == IKILI:
                return IKILI, veri
            # PONG ve bilinmeyenler yok sayılır

    def _gonder(self, kod, veri):
        n = len(veri)
        if n < 126:
            baslik = struct.pack(">BB", 0x80 | kod, n)
        elif n < 65536:
            baslik = struct.pack(">BBH", 0x80 | kod, 126, n)
        else:
            baslik = struct.pack(">BBQ", 0x80 | kod, 127, n)
        with self.yazma_kilidi:
            if not self.acik:
                raise Kapandi()
            try:
                self.soket.sendall(baslik + veri)
            except OSError:
                self.acik = False
                raise Kapandi()

    def metin(self, s):
        self._gonder(METIN, s.encode("utf-8"))

    def ikili(self, b):
        self._gonder(IKILI, b)

    def kapat(self):
        if self.acik:
            try:
                self._gonder(KAPAT, b"")
            except Kapandi:
                pass
        self.acik = False
