"""Robotsuz deneme: sürüş sayfasını robot olmadan, bu bilgisayarda çalıştırır.

    python tools/robotsuz_deneme/sahte_robot.py            → http://127.0.0.1:8090 açılır
    python tools/robotsuz_deneme/sahte_robot.py --gecikme 400   → ağ kötüymüş gibi (cevaplar geç gelir)

- Sayfa: robottakiyle aynı dosya (robot/jetson/surus_koprusu/surus.html). Sayfada yaptığın her değişiklik
  yenileyince görünür.
- Görüntü: robottan kaydedilmiş 360° video (kayit/goruntu.h264) döngüde oynar; JPEG yolu için kayit/kare.jpg.
- Arduino: sahte bir Arduino, gerçek koddaki sayılarla (robot_kontrol.ino'dan okunur) rampayı, 300 ms
  güvenliğini, ölü bölgeyi ve durum satırını taklit eder. Motor akımları uydurmadır.
- Ses: sayfaya sahte_ses.js eklenir; buzzer'ın yapacağı her sesi (korna, melodiler, motor sesi, geri vites
  bipi) aynı öncelik kuralıyla tarayıcıdan çalar. Kulaklıkla dinle. Melodiler de .ino ve sarkilar.h'den okunur.
- Köprünün kuralları da aynı: 250 ms'den geç gelen hareket komutu atılır.
"""
import argparse
import json
import os
import re
import sys
import threading
import time
import webbrowser
from http.server import BaseHTTPRequestHandler, HTTPServer
from socketserver import ThreadingMixIn

BURASI = os.path.dirname(os.path.abspath(__file__))
KOK = os.path.normpath(os.path.join(BURASI, "..", ".."))
KOPRU = os.path.join(KOK, "robot", "jetson", "surus_koprusu")
ARDUINO_KODU = os.path.join(KOK, "robot", "arduino", "robot_kontrol")
sys.path.insert(0, KOPRU)
import websoket  # noqa: E402  (köprünün kendi WebSocket kodu)

PORT = 8090
ESKI_KOMUT_MS = 250


# ---------------------------------------------------------------- Arduino kodunu oku

def firmware_oku():
    """robot_kontrol.ino ve sarkilar.h'den sabitleri ve melodileri çıkarır."""
    metin = ""
    for ad in ("robot_kontrol.ino", "sarkilar.h"):
        with open(os.path.join(ARDUINO_KODU, ad), encoding="utf-8") as f:
            metin += re.sub(r"//[^\n]*", "", f.read()) + "\n"
    sabit = {ad: int(deger) for ad, deger in re.findall(r"\b([A-Z][A-Z0-9_]+)\s*=\s*(\d+)\b", metin)}
    diziler = {}
    for ad, govde in re.findall(r"const uint16_t (M_\w+)\[\] PROGMEM = \{(.*?)\};", metin, re.S):
        sayilar = [sabit[t] if t in sabit else int(t) for t in (x.strip() for x in govde.split(",")) if t]
        diziler[ad] = [sayilar[i:i + 2] for i in range(0, len(sayilar) - 1, 2)]
    sira = re.search(r"MELODILER\[\] = \{(.*?)\};", metin, re.S).group(1)
    melodiler = [diziler[ad.strip()] for ad in sira.split(",") if ad.strip()]
    return sabit, melodiler


SABIT, MELODILER = firmware_oku()


# ---------------------------------------------------------------- sahte Arduino

class SahteArduino:
    """robot_kontrol.ino'nun motor ve durum kısmı. Ses tarayıcıda (sahte_ses.js) çalınır."""

    def __init__(self):
        self.kilit = threading.Lock()
        self.hedef = [0, 0]
        self.simdi = [0, 0]
        self.son_komut = 0.0
        self.led = self.dortlu = self.motor_sesi = False
        self.geri_bip = True
        self.melodi = 0
        self.melodi_bitis = 0.0
        threading.Thread(target=self._dongu, daemon=True).start()

    def komut(self, satir):
        p = satir.split()
        with self.kilit:
            if p[0] == "M":
                self.hedef = [max(-255, min(255, int(p[1]))), max(-255, min(255, int(p[2])))]
                self.son_komut = time.time()
            elif p[0] == "L":
                self.led, self.dortlu = p[1] == "1", False
            elif p[0] == "H":
                self.dortlu = p[1] == "1"
            elif p[0] == "E":
                self.motor_sesi = p[1] == "1"
            elif p[0] == "V":
                self.geri_bip = p[1] == "1"
            elif p[0] == "C":
                n = int(p[1])
                self.melodi = n if 1 <= n <= len(MELODILER) else 0
                self.melodi_bitis = time.time() + sum(ms for _, ms in MELODILER[n - 1]) / 1000 if self.melodi else 0

    @staticmethod
    def _yaklas(simdi, hedef):
        yavasliyor = (simdi > 0 and hedef < simdi) or (simdi < 0 and hedef > simdi)
        adim = SABIT["YAVASLAMA_ADIMI"] if yavasliyor else SABIT["HIZLANMA_ADIMI"]
        if yavasliyor and ((simdi > 0 > hedef) or (simdi < 0 < hedef)):
            hedef = 0
        if simdi < hedef:
            return min(simdi + adim, hedef)
        if simdi > hedef:
            return max(simdi - adim, hedef)
        return simdi

    def _dongu(self):
        adim = SABIT["RAMPA_ADIMI_MS"] / 1000
        while True:
            time.sleep(adim)
            with self.kilit:
                if any(self.hedef) and time.time() - self.son_komut > SABIT["KOMUT_ZAMAN_ASIMI"] / 1000:
                    self.hedef = [0, 0]
                self.simdi = [self._yaklas(s, h) for s, h in zip(self.simdi, self.hedef)]
                if self.melodi and time.time() > self.melodi_bitis:
                    self.melodi = 0

    def durum(self):
        with self.kilit:
            # Uydurma akım: düz giderken 0,1-0,2 A, hızlanırken kalkış tepesi
            ma = []
            for s, h in zip(self.simdi, self.hedef):
                kalkis = 700 if abs(h) > abs(s) else 0
                ma.append(0 if s == 0 else 90 + abs(s) // 3 + kalkis)
            return {"sol": self.simdi[0], "sag": self.simdi[1], "sol_ma": ma[0], "sag_ma": ma[1],
                    "ariza": False, "led": self.led, "melodi": self.melodi, "dortlu": self.dortlu,
                    "motor_sesi": self.motor_sesi, "geri_bip": self.geri_bip}


ARDUINO = SahteArduino()


# ---------------------------------------------------------------- kayıtlı görüntü

with open(os.path.join(BURASI, "kayit", "goruntu.h264"), "rb") as f:
    VIDEO = f.read()
with open(os.path.join(BURASI, "kayit", "kare.jpg"), "rb") as f:
    KARE = f.read()
AUD = b"\x00\x00\x00\x01\x09"
_konum = [m.start() for m in re.finditer(re.escape(AUD), VIDEO)]
KARELER = [VIDEO[a:b] for a, b in zip(_konum, _konum[1:] + [len(VIDEO)])]


def anahtar_mi(au):
    j = au.find(b"\x00\x00\x01")
    while j != -1 and j + 3 < len(au):
        tur = au[j + 3] & 0x1F
        if tur in (5, 7):
            return True
        if tur == 1:
            return False
        j = au.find(b"\x00\x00\x01", j + 3)
    return False


ANAHTAR = [i for i, au in enumerate(KARELER) if anahtar_mi(au)]
GONDERILEN = [0]


# ---------------------------------------------------------------- sunucu

class Sunucu(ThreadingMixIn, HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


class Istek(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    gecikme = 0.0

    def log_message(self, *a):
        pass

    def _yanit(self, govde, tur):
        self.send_response(200)
        self.send_header("Content-Type", tur)
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(govde)))
        self.end_headers()
        self.wfile.write(govde)

    def do_GET(self):
        yol = self.path.split("?")[0]
        if yol in ("/", "/index.html"):
            with open(os.path.join(KOPRU, "surus.html"), "rb") as f:
                sayfa = f.read()
            sayfa = sayfa.replace(b"</body>", b'<script src="/sahte_ses.js"></script>\n</body>')
            self._yanit(sayfa, "text/html; charset=utf-8")
        elif yol == "/sahte_ses.js":
            with open(os.path.join(BURASI, "sahte_ses.js"), "rb") as f:
                self._yanit(f.read(), "text/javascript; charset=utf-8")
        elif yol == "/sahte/ses.json":
            self._yanit(json.dumps({"sabit": SABIT, "melodiler": MELODILER}).encode(), "application/json")
        elif yol == "/api/kamera-ayari":
            self._yanit(b"{}", "application/json")
        elif yol == "/kare.jpg":
            self._yanit(KARE, "image/jpeg")
        elif yol == "/goruntu.mjpg":
            self._mjpeg()
        elif yol == "/goruntu-h264":
            self._h264()
        elif yol == "/kontrol":
            self._kontrol()
        else:
            self.send_error(404)

    def do_POST(self):
        uzunluk = int(self.headers.get("Content-Length") or 0)
        self.rfile.read(uzunluk)
        self._yanit(b'{"tamam": true}', "application/json")

    def _mjpeg(self):
        self.close_connection = True
        self.send_response(200)
        self.send_header("Content-Type", "multipart/x-mixed-replace; boundary=kare")
        self.end_headers()
        try:
            while True:
                self.wfile.write(b"--kare\r\nContent-Type: image/jpeg\r\nContent-Length: " +
                                 str(len(KARE)).encode() + b"\r\n\r\n" + KARE + b"\r\n")
                GONDERILEN[0] += len(KARE)
                time.sleep(1 / 15)
        except OSError:
            pass

    def _h264(self):
        ws = websoket.el_sikis(self)
        if not ws:
            return
        i = ANAHTAR[0]
        try:
            while True:
                au = KARELER[i]
                ws.ikili((b"\x01" if anahtar_mi(au) else b"\x00") + au)
                GONDERILEN[0] += len(au)
                i = i + 1 if i + 1 < len(KARELER) else ANAHTAR[0]
                time.sleep(1 / 15)
        except websoket.Kapandi:
            pass

    def _kontrol(self):
        ws = websoket.el_sikis(self)
        if not ws:
            return
        fark_min = [None]
        atilan = [0]

        def durum_gonder():
            onceki, t0 = GONDERILEN[0], time.time()
            try:
                while ws.acik:
                    time.sleep(0.1)
                    simdi = time.time()
                    kbps = int((GONDERILEN[0] - onceki) * 8 / 1000 / max(simdi - t0, 0.01))
                    onceki, t0 = GONDERILEN[0], simdi
                    ws.metin(json.dumps({"t": "durum", "arduino": True, "motor": ARDUINO.durum(), "kamera": "aktif",
                                         "fps": 15.0, "h264": True, "sinyal": -60, "veri_kbps": kbps,
                                         "atilan": atilan[0], "sahte": True}))
            except websoket.Kapandi:
                pass

        threading.Thread(target=durum_gonder, daemon=True).start()
        try:
            while True:
                tur, metin = ws.oku()
                if tur != websoket.METIN:
                    continue
                try:
                    m = json.loads(metin)
                except ValueError:
                    continue
                if self.gecikme:
                    time.sleep(self.gecikme)
                self._mesaj(ws, m, fark_min, atilan)
        except websoket.Kapandi:
            pass
        finally:
            ws.acik = False
            ARDUINO.komut("M 0 0")

    def _mesaj(self, ws, m, fark_min, atilan):
        t = m.get("t")
        if t == "ping":
            ws.metin(json.dumps({"t": "pong", "z": m.get("z")}))
        elif t == "surus":
            sol, sag = int(m.get("sol", 0)), int(m.get("sag", 0))
            if "z" in m:   # köprüdeki gibi: geç gelen hareket komutu yerine dur
                fark = time.time() * 1000 - float(m["z"])
                if fark_min[0] is None or fark < fark_min[0]:
                    fark_min[0] = fark
                if (sol or sag) and fark - fark_min[0] > ESKI_KOMUT_MS:
                    atilan[0] += 1
                    sol = sag = 0
            ARDUINO.komut("M {} {}".format(sol, sag))
        elif t == "led":
            ARDUINO.komut("L {}".format(1 if m.get("acik") else 0))
        elif t == "dortlu":
            ARDUINO.komut("H {}".format(1 if m.get("acik") else 0))
        elif t == "melodi":
            ARDUINO.komut("C {}".format(int(m.get("no", 0))))
        elif t == "motor_sesi":
            ARDUINO.komut("E {}".format(1 if m.get("acik") else 0))
        elif t == "geri_bip":
            ARDUINO.komut("V {}".format(1 if m.get("acik") else 0))


def main():
    ap = argparse.ArgumentParser(description="Sürüş sayfasını robotsuz dene")
    ap.add_argument("--gecikme", type=int, default=0, help="ms; her komuta eklenecek gecikme (kötü ağ)")
    ap.add_argument("--acma", action="store_true", help="tarayıcıyı açma")
    a = ap.parse_args()
    Istek.gecikme = a.gecikme / 1000
    adres = "http://127.0.0.1:{}/".format(PORT)
    print("Robotsuz deneme:", adres, "| {} kare, {} melodi".format(len(KARELER), len(MELODILER)), flush=True)
    if not a.acma:
        threading.Timer(0.8, webbrowser.open, args=(adres,)).start()
    Sunucu(("127.0.0.1", PORT), Istek).serve_forever()


if __name__ == "__main__":
    main()
