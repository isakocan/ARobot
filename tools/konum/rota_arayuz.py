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


def harmanli_lut(gen=960, yuk=320, alt=-30.0, ust=75.0, bindirme=6.0):
    """Her eşdikdörtgen piksel için iki merceğin ham karedeki yeri (u1, v1, u2, v2; 0..1) ve 1. merceğin ağırlığı.
    Mercekler ~190 derece görür: 90 +- bindirme derecelik kuşakta iki görüntü yumuşakça karıştırılır (dikiş çizgisi
    kaybolur). Model teta.yon_pikseller'in aynısı, yalnız mercek seçmek yerine ikisi de hesaplanır."""
    v, u = np.mgrid[0:yuk, 0:gen]
    lon = ((u + 0.5) / gen - 0.5) * 2 * math.pi
    lat = math.radians(ust) - (v + 0.5) / yuk * math.radians(ust - alt)
    d = np.stack([np.cos(lat) * np.sin(lon), np.sin(lat), np.cos(lat) * np.cos(lon)], -1).reshape(-1, 3) @ teta.EGIM.T
    c = np.stack([-d[:, 1], d[:, 0], d[:, 2]], -1)
    c2 = np.stack([-c[:, 0], c[:, 1], -c[:, 2]], -1) @ teta.LENS2_DON.T
    def piksel(k, L):
        th = np.arccos(np.clip(k[:, 2], -1, 1)); n = np.maximum(np.hypot(k[:, 0], k[:, 1]), 1e-12)
        return L[0] + k[:, 0] / n * th * L[2], L[1] - k[:, 1] / n * th * L[2]
    u1, v1 = piksel(c, teta.LENS1); u2, v2 = piksel(c2, teta.LENS2)
    th1 = np.degrees(np.arccos(np.clip(c[:, 2], -1, 1)))               # 1. merceğin eksenine açı
    w = np.clip((90.0 + bindirme - th1) / (2 * bindirme), 0, 1)
    w = w * w * (3 - 2 * w)                                            # yumuşak geçiş
    lut = np.stack([u1 / 1280.0, v1 / 720.0, u2 / 1280.0, v2 / 720.0], -1).reshape(yuk, gen, 4).astype(np.float32)
    return lut, w.reshape(yuk, gen).astype(np.float32)


LUT4, LUTW = harmanli_lut()
D = {"surec": None, "satirlar": [], "poz": None, "iz": [], "kamera": None, "kamera_t": 0.0}
kilit = threading.Lock()


def log(s):
    with kilit:
        D["satirlar"] = (D["satirlar"] + [time.strftime("%H:%M:%S ") + s])[-200:]


def poz_sorgu():
    """Sürüş yokken robotun konumu (motor 0, sürüş uygulamasıyla aynı paket)."""
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); s.settimeout(0.3); n = 0
    while True:
        time.sleep(0.1)
        if D["surec"] is not None and D["surec"].poll() is None:
            try:
                c_ = json.load(open(os.path.join(KL, "canli.json"), encoding="utf-8"))
                if c_["t"] > (D["poz"] or {}).get("t", 0): guncelle(c_["x"], c_["z"], c_["yaw"], c_["evre"], c_["t"])
            except (OSError, ValueError, KeyError):
                pass
            continue
        n += 1
        try:
            s.sendto(json.dumps({"t": "d", "n": n, "sol": 0, "sag": 0, "konum": 1}).encode(), (ADR, 8081))
            k = json.loads(s.recv(65535).decode()).get("konum")
            if k and k.get("var"): guncelle(k["x"], k["z"], k["yaw"], "bekliyor")
            time.sleep(0.1)
        except (OSError, ValueError):
            pass


def guncelle(x, z, yaw, evre, t=None):
    yaw = (yaw + 180.0) % 360.0 - 180.0
    with kilit:
        D["poz"] = dict(x=x, z=z, yaw=yaw, evre=evre, t=t or time.time())
        if not D["iz"] or math.hypot(D["iz"][-1][0] - x, D["iz"][-1][1] - z) > 0.03: D["iz"] = (D["iz"] + [(x, z)])[-3000:]


def kamera():
    while True:
        if time.time() - D.get("jpeg_istek", 0) > 3.0:     # tarayıcı H.264 kullanıyorsa JPEG çekme (ağı yorma)
            time.sleep(0.2); continue
        try:
            ham = urllib.request.urlopen("http://%s:8080/kucuk.jpg" % ADR, timeout=3).read()   # yarım boy (640x360)
            g = cv2.imdecode(np.frombuffer(ham, np.uint8), cv2.IMREAD_COLOR)
            pan = cv2.remap(g, MX * (g.shape[1] / 1280.0), MY * (g.shape[0] / 720.0), cv2.INTER_LINEAR)
            ok, j = cv2.imencode(".jpg", pan, [cv2.IMWRITE_JPEG_QUALITY, 80])
            if ok:
                with kilit: D["kamera"] = j.tobytes(); D["kamera_t"] = time.time()
        except Exception:
            time.sleep(1.0)
        time.sleep(0.05)


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
            self._gonder(200, "text/html; charset=utf-8", open(os.path.join(BURASI, "rota_arayuz.html"), encoding="utf-8").read().encode())
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
        elif self.path == "/lut":
            # eşdikdörtgen piksel -> çift balık gözü kare (0..1) koordinatı, float32 [y][x][2]: tarayıcı WebGL ile açar
            self._gonder(200, "application/octet-stream", LUT4.tobytes() + LUTW.tobytes())
        elif self.path == "/ayar":
            self._gonder(200, "application/json", json.dumps({"robot": ADR, "gen": int(MX.shape[1]), "yuk": int(MX.shape[0])}).encode())
        elif self.path.startswith("/kamera.jpg"):
            D["jpeg_istek"] = time.time()
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
