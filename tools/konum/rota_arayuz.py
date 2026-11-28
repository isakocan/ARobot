"""Rota sürüşü için tarayıcı arayüzü (PC, http://127.0.0.1:8765).

Sayfada: telefonla taranan zemin + planlanan rota + robotun anlık yeri ve izi (canlı), robotun 360 kamerası
(eşdikdörtgene açılmış), Başlat / büyük DUR düğmesi (boşluk tuşu da DUR), sürücünün mesajları.
Sürüşü rota_sur.py yapar (alt süreç); DUR, rota klasörüne "DUR" dosyası yazar (sürücü her 50 ms'de bakar), 2 sn'de
durmazsa süreç öldürülür (köprü paket kesilince motorları ~1 sn'de durdurur). Sürüş yokken robotun konumu, sürüş
uygulamasının yaptığı gibi UDP ile (motor 0) sorulur.
Kullanım: python rota_arayuz.py <robot_adresi> <rota_klasörü>"""
import csv, glob, io, json, math, os, socket, subprocess, sys, threading, time, urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import cv2
import numpy as np

ADR, KL = sys.argv[1:3]
BURASI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(BURASI, "..", "..", "robot", "jetson", "konum"))
import teta

ROTA = json.load(open(os.path.join(KL, "rota.json"), encoding="utf-8"))
Z = json.load(open(os.path.join(KL, "zemin.json"), encoding="utf-8"))
EVJ = json.load(open(os.path.join(KL, "ev.json"), encoding="utf-8"))
MX, MY = teta.es_dikdortgen_haritasi(960, 320, -30.0, 75.0)
D = {"surec": None, "satirlar": [], "poz": None, "iz": [], "kamera": None, "kamera_t": 0.0}
kilit = threading.Lock()


def log(s):
    with kilit:
        D["satirlar"] = (D["satirlar"] + [time.strftime("%H:%M:%S ") + s])[-200:]


def poz_sorgu():
    """Sürüş yokken robotun konumu (motor 0, sürüş uygulamasıyla aynı paket)."""
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); s.settimeout(0.3); n = 0
    while True:
        time.sleep(0.25)
        if D["surec"] is not None and D["surec"].poll() is None:
            son = sorted(glob.glob(os.path.join(KL, "surus_*.csv")))
            if son:
                try:
                    with open(son[-1], encoding="utf-8") as f: satir = f.readlines()[-1].split(",")
                    if satir[6] == "True": guncelle(float(satir[7]), float(satir[8]), float(satir[9]), satir[2])
                except (OSError, IndexError, ValueError):
                    pass
            continue
        n += 1
        try:
            s.sendto(json.dumps({"t": "d", "n": n, "sol": 0, "sag": 0, "konum": 1}).encode(), (ADR, 8081))
            k = json.loads(s.recv(65535).decode()).get("konum")
            if k and k.get("var"): guncelle(k["x"], k["z"], k["yaw"], "bekliyor")
        except (OSError, ValueError):
            pass


def guncelle(x, z, yaw, evre):
    with kilit:
        D["poz"] = dict(x=x, z=z, yaw=yaw, evre=evre, t=time.time())
        if not D["iz"] or math.hypot(D["iz"][-1][0] - x, D["iz"][-1][1] - z) > 0.03: D["iz"] = (D["iz"] + [(x, z)])[-3000:]


def kamera():
    while True:
        try:
            ham = urllib.request.urlopen("http://%s:8080/kare.jpg" % ADR, timeout=3).read()
            g = cv2.imdecode(np.frombuffer(ham, np.uint8), cv2.IMREAD_COLOR)
            pan = cv2.remap(g, MX, MY, cv2.INTER_LINEAR)
            ok, j = cv2.imencode(".jpg", pan, [cv2.IMWRITE_JPEG_QUALITY, 80])
            if ok:
                with kilit: D["kamera"] = j.tobytes(); D["kamera_t"] = time.time()
        except Exception:
            time.sleep(1.0)
        time.sleep(0.25)


def baslat(ayar):
    if D["surec"] is not None and D["surec"].poll() is None: return "zaten sürüyor"
    try: os.remove(os.path.join(KL, "DUR"))
    except OSError: pass
    env = dict(os.environ, PYTHONIOENCODING="utf-8", PYTHONUNBUFFERED="1", TUR="1" if ayar.get("tur", True) else "0", HIZ=str(int(ayar.get("hiz", 130))))
    p = subprocess.Popen([sys.executable, os.path.join(BURASI, "rota_sur.py"), ADR, KL], env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL)
    D["surec"] = p; D["iz"] = []
    def oku():
        for satir in io.TextIOWrapper(p.stdout, encoding="utf-8", errors="replace"):
            log(satir.rstrip())
        log("sürücü kapandı (çıkış %s)" % p.wait())
    threading.Thread(target=oku, daemon=True).start()
    log("BAŞLAT (hız %s, turlar %s)" % (env["HIZ"], "açık" if env["TUR"] == "1" else "kapalı")); return "başladı"


def durdur():
    open(os.path.join(KL, "DUR"), "w").close(); log("DUR basıldı")
    p = D["surec"]
    def bekle_oldur():
        if p is None: return
        b = time.time()
        while p.poll() is None and time.time() - b < 2.0: time.sleep(0.05)
        if p.poll() is None: p.kill(); log("sürücü 2 sn'de durmadı, öldürüldü (robot ~1 sn'de kendisi durur)")
    threading.Thread(target=bekle_oldur, daemon=True).start()
    return "duruyor"


SAYFA = open(os.path.join(BURASI, "rota_arayuz.html"), encoding="utf-8").read()


class Isleyici(BaseHTTPRequestHandler):
    def log_message(self, *a): pass

    def _gonder(self, kod, tur, veri):
        self.send_response(kod); self.send_header("Content-Type", tur); self.send_header("Cache-Control", "no-store")
        self.end_headers(); self.wfile.write(veri)

    def do_GET(self):
        if self.path == "/":
            self._gonder(200, "text/html; charset=utf-8", SAYFA.encode())
        elif self.path == "/harita":
            isaretler = [e if isinstance(e, dict) else dict(id=e[0], x=e[1], z=e[2], aci=e[3]) for e in EVJ["isaretler"]]
            self._gonder(200, "application/json", json.dumps(dict(hucre=Z.get("hucre", 0.05), acik=Z["acik"], engel=Z.get("engel", []),
                         rota=ROTA["noktalar"], pay=ROTA.get("pay"), isaretler=isaretler)).encode())
        elif self.path == "/durum":
            with kilit:
                c = D["surec"]
                v = dict(poz=D["poz"], iz=D["iz"][-1500:], satirlar=D["satirlar"][-40:], suruyor=c is not None and c.poll() is None,
                         kamera_yas=round(time.time() - D["kamera_t"], 1) if D["kamera_t"] else None)
            self._gonder(200, "application/json", json.dumps(v).encode())
        elif self.path.startswith("/kamera.jpg"):
            with kilit: j = D["kamera"]
            if j: self._gonder(200, "image/jpeg", j)
            else: self._gonder(503, "text/plain", b"kamera yok")
        else:
            self._gonder(404, "text/plain", b"yok")

    def do_POST(self):
        n = int(self.headers.get("Content-Length") or 0); ayar = json.loads(self.rfile.read(n) or b"{}")
        if self.path == "/baslat": self._gonder(200, "application/json", json.dumps({"sonuc": baslat(ayar)}).encode())
        elif self.path == "/dur": self._gonder(200, "application/json", json.dumps({"sonuc": durdur()}).encode())
        else: self._gonder(404, "text/plain", b"yok")


threading.Thread(target=poz_sorgu, daemon=True).start()
threading.Thread(target=kamera, daemon=True).start()
log("arayüz hazır, robot %s" % ADR)
ThreadingHTTPServer(("127.0.0.1", int(os.environ.get("PORT", "8765"))), Isleyici).serve_forever()
