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
import uygulama_harita
UYG = uygulama_harita.Harita(os.path.join(KL, "zemin.json"), olcek=80)      # tarama uygulamasının harita görünümü
UYG_PNG = cv2.imencode(".png", UYG.resim)[1].tobytes()
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
D = {"surec": None, "satirlar": [], "poz": None, "iz": [], "kamera": None, "kamera_t": 0.0,
     "jiro": None, "jiz": [], "jiro_durum": "bağlanıyor"}
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
                if c_["t"] > (D["poz"] or {}).get("t", 0): guncelle(c_["x"], c_["z"], c_["yaw"], c_["evre"], c_["t"], c_.get("gorulen"))
            except (OSError, ValueError, KeyError):
                pass
            continue
        n += 1
        try:
            s.sendto(json.dumps({"t": "d", "n": n, "sol": 0, "sag": 0, "konum": 1}).encode(), (ADR, 8081))
            k = json.loads(s.recv(65535).decode()).get("konum")
            if k and k.get("var"): guncelle(k["x"], k["z"], k["yaw"], "bekliyor", gorulen=[g[0] for g in k.get("gorulen") or []])
            time.sleep(0.1)
        except (OSError, ValueError):
            pass


def guncelle(x, z, yaw, evre, t=None, gorulen=None):
    yaw = (yaw + 180.0) % 360.0 - 180.0
    J.kamera(x, z, yaw)
    with kilit:
        D["poz"] = dict(x=x, z=z, yaw=yaw, evre=evre, t=t or time.time(), gorulen=gorulen or [])
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
    D["surec"] = p; D["iz"] = []; D["jiz"] = []; J.surus = True; J.ayri_bas = None
    if not J.canli(): log("UYARI: jiroskop verisi gelmiyor (%s); hakem kapalı" % D["jiro_durum"])
    def oku():
        for satir in io.TextIOWrapper(p.stdout, encoding="utf-8", errors="replace"):
            log(satir.rstrip())
        log("sürücü kapandı (çıkış %s)" % p.wait()); J.surus = False
    threading.Thread(target=oku, daemon=True).start()
    log("BAŞLAT (hız %s, turlar %s)" % (env["HIZ"], "açık" if env["TUR"] == "1" else "kapalı")); return "başladı"


# ------------------------------------------------------------------------------------------------------------------
# Telefonun jiroskobu (robotun üstünde, tarama uygulaması jiroskop kaydı açık), kablosuz adb ile canlı.
# İkinci robot simgesi: yönü jiroskoptan (sürüş başında kameraya hizalanır, sonra serbest), yeri kameranın ileri
# gidişiyle o yönde ilerletilir. Hakem: sürüşte kamera yönü jiroskoptan HAKEM_DER'den fazla, HAKEM_SN boyunca
# ayrılırsa sürüş durdurulur.
ADB = os.environ.get("ADB", r"C:\platform-toolsdb.exe")
JIRO_CIHAZ = os.environ.get("JIRO_CIHAZ", "172.20.243.17:5555")
JIRO_KLASOR = "/sdcard/Android/data/com.ekip.artarama/files/olcum"
HAKEM_DER, HAKEM_SN = float(os.environ.get("HAKEM_DER", "35")), float(os.environ.get("HAKEM_SN", "2.0"))


def da(a): return (a + 180.0) % 360.0 - 180.0


class Jiro:
    def __init__(self):
        self.yaw = 0.0; self.ofset = None; self.son_t = None; self.son_gelis = 0.0
        self.bias = 0.0; self.sakin = []          # duruşta dönüş hızı örnekleri (sapma)
        self.x = self.z = None; self.kam = None; self.surus = False; self.ayri_bas = None

    def ornek(self, t, w, g):
        n = math.sqrt(sum(v * v for v in g))
        if n < 0.5: return
        r = sum(w[i] * g[i] for i in range(3)) / n        # robotun yaw hızı, sağa + (rad/sn; 28 Kas kaydıyla bulundu)
        if not self.surus and abs(w[0]) + abs(w[1]) + abs(w[2]) < 0.03:
            self.sakin = (self.sakin + [r])[-300:]; self.bias = sorted(self.sakin)[len(self.sakin) // 2]
        if self.son_t is not None and 0 < t - self.son_t < 0.5:
            self.yaw += math.degrees((r - self.bias) * (t - self.son_t))
        self.son_t = t; self.son_gelis = time.time()

    def kamera(self, x, z, yaw):
        # sürüş yokken iki simge çakışık (hizalama); sürüşte jiroskop simgesi kameranın ileri gidişiyle ilerler
        if not self.surus or self.ofset is None or self.x is None:
            self.ofset = da(yaw - self.yaw); self.x, self.z = x, z
        elif self.kam is not None:
            kx, kz, ky = self.kam
            d = (x - kx) * math.sin(math.radians(ky)) + (z - kz) * math.cos(math.radians(ky))
            jy = math.radians(self.yon())
            self.x += d * math.sin(jy); self.z += d * math.cos(jy)
        self.kam = (x, z, yaw)
        with kilit:
            if not D["jiz"] or math.hypot(D["jiz"][-1][0] - self.x, D["jiz"][-1][1] - self.z) > 0.03:
                D["jiz"] = (D["jiz"] + [(self.x, self.z)])[-3000:]
        if self.surus and self.canli():
            fark = abs(da(yaw - self.yon()))
            if fark > HAKEM_DER:
                if self.ayri_bas is None: self.ayri_bas = time.time()
                elif time.time() - self.ayri_bas > HAKEM_SN and not os.path.exists(os.path.join(KL, "DUR")):
                    log("HAKEM: kamera yönü jiroskoptan %.0f° ayrıldı (%.1f sn) -> DUR" % (fark, HAKEM_SN)); durdur()
            else:
                self.ayri_bas = None

    def yon(self): return da(self.yaw + (self.ofset or 0.0))
    def canli(self): return time.time() - self.son_gelis < 1.0

    def durum(self):
        if self.x is None: return None
        return dict(x=self.x, z=self.z, yaw=self.yon(), canli=self.canli(),
                    fark=round(da(self.kam[2] - self.yon()), 1) if self.kam else None)


J = Jiro()


def jiro_oku():
    """Telefondaki en yeni jiroskop kaydını kablosuz adb ile canlı izler (kayıt yenilenirse yenisine geçer)."""
    while True:
        try:
            ls = subprocess.run([ADB, "-s", JIRO_CIHAZ, "shell", "ls -t %s | head -1" % JIRO_KLASOR], capture_output=True, text=True, timeout=8)
            ad = ls.stdout.strip()
            if not ad.startswith("jiro_"):
                D["jiro_durum"] = "telefonda kayıt yok (%s)" % (ls.stderr.strip() or "jiroskop kaydını başlat")[:80]; time.sleep(3); continue
            D["jiro_durum"] = "okunuyor: " + ad
            p = subprocess.Popen([ADB, "-s", JIRO_CIHAZ, "shell", "tail -n 0 -f %s/%s" % (JIRO_KLASOR, ad)],
                                 stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, bufsize=1)
            bakis = time.time()
            for satir in p.stdout:
                v = satir.strip().split(",")
                if len(v) >= 8:
                    try: J.ornek(float(v[1]), [float(v[2]), float(v[3]), float(v[4])], [float(v[5]), float(v[6]), float(v[7])])
                    except ValueError: pass
                if time.time() - bakis > 5:                       # daha yeni kayıt açıldı mı
                    bakis = time.time()
                    y = subprocess.run([ADB, "-s", JIRO_CIHAZ, "shell", "ls -t %s | head -1" % JIRO_KLASOR], capture_output=True, text=True, timeout=8).stdout.strip()
                    if y.startswith("jiro_") and y != ad: break
            p.kill()
        except Exception as e:
            D["jiro_durum"] = "hata: %s" % str(e)[:80]
        time.sleep(1)


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
            self._gonder(200, "application/json", json.dumps(dict(kutu=UYG.kutu(), rota=ROTA["noktalar"], pay=ROTA.get("pay"),
                         isaretler=isaretler)).encode())
        elif self.path == "/harita.png":
            self._gonder(200, "image/png", UYG_PNG)
        elif self.path == "/durum":
            with kilit:
                c = D["surec"]
                v = dict(poz=D["poz"], iz=D["iz"][-1500:], satirlar=D["satirlar"][-40:], suruyor=c is not None and c.poll() is None,
                         kamera_yas=round(time.time() - D["kamera_t"], 1) if D["kamera_t"] else None,
                         jiro=J.durum(), jiz=D["jiz"][-1500:], jiro_durum=D["jiro_durum"])
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
threading.Thread(target=jiro_oku, daemon=True).start()
threading.Thread(target=kamera, daemon=True).start()
log("arayüz hazır, robot %s" % ADR)
ThreadingHTTPServer(("127.0.0.1", int(os.environ.get("PORT", "8765"))), Isleyici).serve_forever()
