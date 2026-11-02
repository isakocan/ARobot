#!/usr/bin/env python3
"""
Sürüş köprüsü (Jetson, bs kullanıcısıyla çalışır).

Tarayıcı / telefon  <-- HTTP :8080 -->  bu program  <-- seri 115200 -->  Arduino (robot_kontrol)
                                             ^
                                   Theta S (/dev/video0, MJPEG)

- Görüntü: kamera zaten JPEG verdiği için kareler çözülmeden aktarılır (GStreamer yalnızca
  kareleri ayırır). Kamera yalnızca izleyen biri varken açılır.
- Sürüş: sayfa basılı tuş varken 100 ms'de bir /api/surus gönderir. Arduino 300 ms komut
  alamazsa motorları kendisi durdurur; bu program son komutu tekrar etmez.

Python 3.6 uyumlu, standart kütüphane + pyserial.
"""

import json
import os
import socket
import subprocess
import threading
import time
from http.server import BaseHTTPRequestHandler, HTTPServer
from socketserver import ThreadingMixIn
from urllib.parse import parse_qs, urlsplit

import serial

SERI_PORT = "/dev/ttyACM0"
KAMERA = "/dev/video0"
PORT = 8080
KLASOR = os.path.dirname(os.path.abspath(__file__))
SAYFA = os.path.join(KLASOR, "surus.html")
KAMERA_AYARI = os.path.expanduser("~/.config/robot/kamera_ayari.json")
KAMERA_BOSTA_KAPAT = 10    # sn: izleyen kalmayınca kamerayı kapat
VARSAYILAN_FPS = 15
AKIS_TAMPONU = 64 * 1024   # bayt; bir kare ~150 KB, yani ağda en fazla ~1 kare bekler


def log(*parca):
    print(time.strftime("%H:%M:%S"), *parca, flush=True)


# ---------------------------------------------------------------- Arduino

class Arduino:
    def __init__(self):
        self.kilit = threading.Lock()
        self.port = None
        self.bagli = False
        self.durum = {}            # son "D" satırı
        self.son_durum_zamani = 0
        threading.Thread(target=self._dongu, daemon=True).start()

    def gonder(self, komut):
        with self.kilit:
            if not self.port:
                return False
            try:
                self.port.write((komut + "\n").encode())
                return True
            except (serial.SerialException, OSError):
                return False

    def _dongu(self):
        while True:
            try:
                with serial.Serial(SERI_PORT, 115200, timeout=0.5) as p:
                    log("Arduino portu açıldı, HAZIR bekleniyor")
                    son = time.time() + 5
                    while time.time() < son:     # port açılınca Uno yeniden başlar
                        if p.readline().startswith(b"HAZIR"):
                            break
                    with self.kilit:
                        self.port = p
                    self.bagli = True
                    log("Arduino hazır")
                    while True:
                        satir = p.readline().decode("ascii", "replace").strip()
                        if satir.startswith("D "):
                            self._durum_isle(satir)
                        elif satir:
                            log("Arduino:", satir)
            except (serial.SerialException, OSError) as e:
                if self.bagli:
                    log("Arduino bağlantısı koptu:", e)
            with self.kilit:
                self.port = None
            self.bagli = False
            time.sleep(2)

    def _durum_isle(self, satir):
        try:
            deger = [int(x) for x in satir.split()[1:9]]
            sol, sag, sol_ma, sag_ma, ariza, led = deger[:6]
        except ValueError:
            return
        self.durum = {"sol": sol, "sag": sag, "sol_ma": sol_ma, "sag_ma": sag_ma,
                      "ariza": bool(ariza), "led": bool(led),
                      "melodi": deger[6] if len(deger) > 6 else 0,
                      "dortlu": bool(deger[7]) if len(deger) > 7 else False}
        self.son_durum_zamani = time.time()


# ---------------------------------------------------------------- kamera

class Kamera:
    """Son kareyi tutar; izleyen varken GStreamer'ı çalıştırır."""

    def __init__(self):
        self.kosul = threading.Condition()
        self.kare = None
        self.sayac = 0
        self.izleyen = 0
        self.son_izleyen = 0
        self.durum = "kapali"      # kapali | yok | baslatiliyor | aktif
        self.fps = 0.0
        threading.Thread(target=self._dongu, daemon=True).start()

    def izle(self, artis):
        with self.kosul:
            self.izleyen += artis
            self.son_izleyen = time.time()
            self.kosul.notify_all()

    def _dongu(self):
        while True:
            with self.kosul:
                while self.izleyen == 0:
                    self.durum = "kapali"
                    self.kosul.wait()
            if not os.path.exists(KAMERA):
                self.durum = "yok"
                time.sleep(2)
                continue
            self.durum = "baslatiliyor"
            self._yayinla()
            time.sleep(1)

    def _yayinla(self):
        komut = ["gst-launch-1.0", "-q", "v4l2src", "device=" + KAMERA, "!",
                 "image/jpeg,width=1280,height=720", "!",
                 "multipartmux", "boundary=kare", "!", "fdsink", "fd=1"]
        log("kamera açılıyor")
        p = subprocess.Popen(komut, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, bufsize=0)
        akis = p.stdout
        sayac_bas, zaman_bas = 0, time.time()
        try:
            while True:
                # izleyen kalmadıysa bir süre sonra kapat
                if self.izleyen == 0 and time.time() - self.son_izleyen > KAMERA_BOSTA_KAPAT:
                    break
                uzunluk = None
                while True:                       # parça başlıkları
                    satir = akis.readline()
                    if not satir:
                        return
                    satir = satir.strip()
                    if satir.lower().startswith(b"content-length:"):
                        uzunluk = int(satir.split(b":")[1])
                    elif not satir and uzunluk is not None:
                        break
                veri = b""
                while len(veri) < uzunluk:
                    parca = akis.read(uzunluk - len(veri))
                    if not parca:
                        return
                    veri += parca
                with self.kosul:
                    self.kare = veri
                    self.sayac += 1
                    self.durum = "aktif"
                    self.kosul.notify_all()
                sayac_bas += 1
                if time.time() - zaman_bas >= 2:
                    self.fps = sayac_bas / (time.time() - zaman_bas)
                    sayac_bas, zaman_bas = 0, time.time()
        finally:
            p.kill()
            p.wait()
            self.fps = 0.0
            log("kamera kapandı")

    def sonraki_kare(self, son_sayac, sure=2.0):
        with self.kosul:
            self.kosul.wait_for(lambda: self.kare is not None and self.sayac != son_sayac, timeout=sure)
            return self.sayac, self.kare


ARDUINO = Arduino()
KAM = Kamera()


# ---------------------------------------------------------------- web sunucusu

class Sunucu(ThreadingMixIn, HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


class Istek(BaseHTTPRequestHandler):
    # Bağlantı açık kalsın: sürüş komutları her seferinde yeni TCP bağlantısı kurmasın
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def setup(self):
        super().setup()
        # Küçük paketler (sürüş komutu yanıtı) bekletilmeden gitsin
        self.connection.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)

    def _yanit(self, govde, tur, kod=200):
        self.send_response(kod)
        self.send_header("Content-Type", tur)
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(govde)))
        self.end_headers()
        self.wfile.write(govde)

    def _json(self, veri, kod=200):
        self._yanit(json.dumps(veri, ensure_ascii=False).encode("utf-8"), "application/json; charset=utf-8", kod)

    def _govde(self):
        uzunluk = int(self.headers.get("Content-Length") or 0)
        try:
            return json.loads(self.rfile.read(uzunluk).decode("utf-8") or "{}")
        except ValueError:
            return {}

    def do_GET(self):
        adres = urlsplit(self.path)
        if adres.path in ("/", "/index.html"):
            with open(SAYFA, "rb") as f:
                self._yanit(f.read(), "text/html; charset=utf-8")
        elif adres.path == "/goruntu.mjpg":
            fps = float(parse_qs(adres.query).get("fps", [VARSAYILAN_FPS])[0])
            self._goruntu_akisi(max(1.0, min(fps, 30.0)))
        elif adres.path == "/kare.jpg":
            KAM.izle(+1)
            try:
                _, kare = KAM.sonraki_kare(-1, sure=8)
            finally:
                KAM.izle(-1)
            if kare:
                self._yanit(kare, "image/jpeg")
            else:
                self._json({"hata": "Kamera görüntü vermiyor."}, 503)
        elif adres.path == "/api/durum":
            taze = time.time() - ARDUINO.son_durum_zamani < 1
            self._json({
                "arduino": ARDUINO.bagli and taze,
                "motor": ARDUINO.durum if taze else None,
                "kamera": KAM.durum,
                "fps": round(KAM.fps, 1),
            })
        elif adres.path == "/api/kamera-ayari":
            try:
                with open(KAMERA_AYARI) as f:
                    self._json(json.load(f))
            except (OSError, ValueError):
                self._json({})
        else:
            self.send_error(404)

    def _goruntu_akisi(self, fps):
        # Gönderme tamponu küçük: ağ yavaşsa kareler kuyrukta birikip gecikme büyümesin,
        # yazma bekler ve sonra en yeni kare gönderilir (aradakiler atlanır).
        self.connection.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, AKIS_TAMPONU)
        self.close_connection = True
        self.send_response(200)
        self.send_header("Content-Type", "multipart/x-mixed-replace; boundary=kare")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        KAM.izle(+1)
        sayac, aralik, son = -1, 1.0 / fps, 0.0
        try:
            while True:
                bekle = aralik - (time.time() - son)
                if bekle > 0:
                    time.sleep(bekle)
                sayac, kare = KAM.sonraki_kare(sayac)
                if not kare:
                    continue
                son = time.time()
                self.wfile.write(b"--kare\r\nContent-Type: image/jpeg\r\nContent-Length: " +
                                 str(len(kare)).encode() + b"\r\n\r\n" + kare + b"\r\n")
        except (BrokenPipeError, ConnectionResetError, OSError):
            pass
        finally:
            KAM.izle(-1)

    def do_POST(self):
        yol = urlsplit(self.path).path
        veri = self._govde()
        if yol == "/api/surus":
            try:
                sol = max(-255, min(255, int(veri.get("sol", 0))))
                sag = max(-255, min(255, int(veri.get("sag", 0))))
            except (TypeError, ValueError):
                self._json({"hata": "sol/sag sayı olmalı"}, 400)
                return
            self._json({"tamam": ARDUINO.gonder("M {} {}".format(sol, sag))})
        elif yol == "/api/dur":
            self._json({"tamam": ARDUINO.gonder("S")})
        elif yol == "/api/led":
            self._json({"tamam": ARDUINO.gonder("L {}".format(1 if veri.get("acik") else 0))})
        elif yol == "/api/korna":
            hz = int(veri.get("hz", 880))
            ms = int(veri.get("ms", 200))
            self._json({"tamam": ARDUINO.gonder("B {} {}".format(hz, ms))})
        elif yol == "/api/melodi":
            self._json({"tamam": ARDUINO.gonder("C {}".format(int(veri.get("no", 0))))})
        elif yol == "/api/selektor":
            self._json({"tamam": ARDUINO.gonder("F")})
        elif yol == "/api/dortlu":
            self._json({"tamam": ARDUINO.gonder("H {}".format(1 if veri.get("acik") else 0))})
        elif yol == "/api/kamera-ayari":
            os.makedirs(os.path.dirname(KAMERA_AYARI), exist_ok=True)
            with open(KAMERA_AYARI, "w") as f:
                json.dump(veri, f)
            self._json({"tamam": True})
        else:
            self.send_error(404)


def main():
    log("Sürüş köprüsü hazır, port", PORT)
    Sunucu(("0.0.0.0", PORT), Istek).serve_forever()


if __name__ == "__main__":
    main()
