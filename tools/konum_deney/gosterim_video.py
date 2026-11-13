"""Gösterim videosu: kamera tabanlı yön kestirimi ve harita kurmayı uzman olmayan biri için GÖRÜNÜR yapar (yalnızca sim).

Üstte robotun 360 derece panoraması: yeşil = modelin "zemin" dediği bölge, parlak çizgi = zemin sınırı, noktalar = M2D yönteminin
anahtar kareyle eşleştirdiği ORB noktaları (çizgi: noktanın anahtar karedeki yerinden şimdiki yerine). Altta: kurulan doluluk haritası
(sol), gerçek ev planı (orta), canlı ölçümler ve yön hatası grafiği (sağ).

DÜRÜSTLÜK: haritada robotun konumu VE yönü simden hazır alınır; kameradan yalnızca zemin sınırı gelir. Yön tahmini (M2D)
yalnızca sağdaki ölçümde ve turuncu okta görünür; harita kurmada kullanılmaz. Yön tahmini yalnızca dönüşü ölçer, başlangıç yönü
gerçek değerden verilir (olc.hizala ile aynı kural). Sonuçlar yumuşatılmaz ya da süslenmez; yöntemler ne verdiyse o çizilir.

Kullanım (depo kökünden):
    python tools/konum_deney/gosterim_video.py                     # iki video + önizleme resimleri
    python tools/konum_deney/gosterim_video.py --yalniz_onizleme    # yalnızca 4'er önizleme resmi
    python tools/konum_deney/gosterim_video.py --kayit ev_turu      # tek kayıt
Çıktı: kayitlar/sonuclar/gosterim/ (önbellek_*.pkl/npz hesapları saklar; --yeniden ile silinir)."""
import argparse
import os
import pickle
import sys
import time

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
import zemin_ortak as zo  # noqa: E402
import olc  # noqa: E402
import harita_kur as hk  # noqa: E402
import yontem_pusula_nokta as ypn  # noqa: E402
from kayit import Kayit  # noqa: E402
from kamera import Kamera  # noqa: E402

CIKTI = os.path.join(zo.KAYITLAR, "sonuclar", "gosterim")
TARAMA = os.path.join(zo.CIKTI, "tarama", "model_gorunum", "theta")
TAHMIN_CSV = os.path.join(zo.KAYITLAR, "sonuclar", "pusula", "tahmin", "M2D")

# ---------------------------------------------------------------- yerleşim (piksel)
W, H = 1920, 1080
BASLIK_Y, BASLIK_H = 0, 64
PANO_Y, PANO_H = 64, 380
ALT_Y = PANO_Y + PANO_H                     # 444
EL_UST, EL_ALT = 65.0, -45.0                # panoramada gösterilen yükseklik açısı aralığı (derece)
HARITA_X, HARITA_G = 6, 660                 # harita panelleri: sol, orta
PANEL1, PANEL2, PANEL3 = (0, 672), (672, 1344), (1344, 1920)
OLCEK = 3                                   # hücre başına piksel (tam sayı: net pikseller)
HARITA_Y = ALT_Y + 62
FPS = 15

# ---------------------------------------------------------------- renkler (RGB)
ARKA = (16, 21, 28)
PANEL = (24, 31, 41)
YAZI = (240, 244, 248)
SOLUK = (150, 162, 176)
VURGU = (255, 196, 0)
GERCEK_R = (70, 160, 255)                   # gerçek yön
TAHMIN_R = (255, 140, 0)                    # kameradan tahmin edilen yön
IYI = (0, 230, 255)                         # içerik (doğru eşleşme)
KOTU = (255, 90, 110)                       # ayıklanan eşleşme
ZEMIN_R = (40, 220, 110)
SINIR_R = (170, 255, 0)
TARAMA_R = (255, 60, 200)
HATA_IYI = (60, 220, 120)
HATA_KOTU = (255, 150, 40)

_FONT = {}


def font(boyut, kalin=False):
    a = (boyut, kalin)
    if a not in _FONT:
        yol = r"C:\Windows\Fonts\segoeuib.ttf" if kalin else r"C:\Windows\Fonts\segoeui.ttf"
        _FONT[a] = ImageFont.truetype(yol, boyut)
    return _FONT[a]


def bgr(r):
    return (int(r[2]), int(r[1]), int(r[0]))


def sar(a):
    return float(olc.sar(a))


def virgul(x, n=1):
    return ("%.*f" % (n, x)).replace(".", ",")


def sure_yazi(ms):
    s = ms / 1000.0
    return "%d:%04.1f" % (int(s // 60), s % 60)


# ================================================================ 1) yön yöntemi (M2D) ve eşleşmeler
class IzliYontem(ypn.Yontem):
    """M2D'yi DEĞİŞTİRMEDEN çalıştırır; yalnızca _duzlem sonucunda kullanılan eşleşmeleri ve içerik maskesini kaydeder."""

    def _duzlem(self, a, b, merkez_der, pencere_der, N):
        d, kal, tamam, bilgi = super()._duzlem(a, b, merkez_der, pencere_der, N)
        esik = np.sin(np.radians(self.esik))
        ic = self._epi_hata(a, b, d, bilgi["phi"])[0, 0] < esik
        self.izli = (a.copy(), b.copy(), ic.copy())
        return d, kal, tamam, bilgi


def _azel(r):
    az = np.degrees(np.arctan2(r[:, 0], r[:, 2]))
    el = np.degrees(np.arcsin(np.clip(r[:, 1], -1, 1)))
    return np.stack([az, el], 1).astype(np.float32)


def yon_hesapla(ad, yeniden=False):
    yol = os.path.join(CIKTI, "onbellek_yon_%s.pkl" % ad)
    if os.path.exists(yol) and not yeniden:
        with open(yol, "rb") as f:
            return pickle.load(f)
    import cv2 as _cv
    _cv.setNumThreads(1)
    kay = Kayit(os.path.join(zo.KAYITLAR, "theta", ad))
    kam = Kamera.bilgiden(kay.bilgi)
    bilgi = dict(kay.bilgi)
    bilgi["_kayit_klasoru"] = kay.klasor
    y = IzliYontem(bilgi, kam, kip="duzlem")           # pusula_degerlendir.py "M2D" ile aynı kurulum
    kayitlar = []
    t0 = time.time()
    for i in range(len(kay)):
        t = int(kay.t_ms[i])
        y.izli = None
        r = y.guncelle(kay.resim(i), t, kay.komut(t))
        k = {"yon": float(r[2]), "durum": y.durum, "degisim": int(y.anahtar_degisim), "kalite": float(y.kalite), "izli": None}
        if y.izli is not None and y.durum != "tut":
            a, b, ic = y.izli
            k["izli"] = (_azel(a), _azel(b), ic)
        kayitlar.append(k)
        if i % 200 == 0:
            print("  yön %s %d/%d (%.0f sn)" % (ad, i, len(kay), time.time() - t0), flush=True)
    os.makedirs(CIKTI, exist_ok=True)
    with open(yol, "wb") as f:
        pickle.dump(kayitlar, f)
    return kayitlar


# ================================================================ 2) zemin bölütleme (her gösterilen kare)
def zemin_hesapla(ad, kareler, yeniden=False):
    """Gösterilecek her kare için: zemin sınırı satırı r (360), mesafe, güven ve zemin olasılığı (alt 160 satırın 80x640 küçük hali)."""
    yol = os.path.join(CIKTI, "onbellek_zemin_%s_%d.npz" % (ad, len(kareler)))
    if os.path.exists(yol) and not yeniden:
        z = np.load(yol)
        if np.array_equal(z["kare"], kareler):
            return {k: z[k] for k in z.files}
    import zemin_model as zm
    b = zm.Bolutleyici()
    yakala = {}
    asil = zo.azimut_sutunlari

    def sarici(resim, n_az=zo.N_AZ):           # ilk çağrı = (H2, G) zemin olasılığı: çizim için yakala
        if "p" not in yakala:
            yakala["p"] = np.array(resim, np.float32)
        return asil(resim, n_az)
    zo.azimut_sutunlari = sarici
    kg = zo.KayitGirdisi(ad, "theta")
    n = len(kareler)
    R = np.zeros((n, 360), np.int32)
    M = np.zeros((n, 360), np.float32)
    G = np.zeros((n, 360), np.float32)
    P = np.zeros((n, 80, 640), np.uint8)
    t0 = time.time()
    try:
        for j, i in enumerate(kareler):
            yakala.clear()
            g = kg.girdi(int(i))
            Pk, gec = b.zemin_olasiligi(g)
            s = zm.p_den_sinir(Pk, gec, g, "kesim")
            R[j] = s.ek["r"]
            M[j] = s.mesafe
            G[j] = s.guven
            P[j] = cv2.resize(np.clip(yakala["p"][:160] * 255.0, 0, 255).astype(np.uint8), (640, 80), interpolation=cv2.INTER_AREA)
            if j % 100 == 0:
                print("  zemin %s %d/%d (%.0f sn)" % (ad, j, n, time.time() - t0), flush=True)
    finally:
        zo.azimut_sutunlari = asil
    z = {"kare": np.asarray(kareler), "r": R, "mesafe": M, "guven": G, "P": P, "max_menzil": np.float64(s.max_menzil)}
    os.makedirs(CIKTI, exist_ok=True)
    np.savez_compressed(yol, **z)
    return z


# ================================================================ 3) çizim
class Cizici:
    def __init__(self, ad, adim, yon_kay, zemin, hizli_yazi):
        self.ad, self.adim = ad, adim
        self.kay = Kayit(os.path.join(zo.KAYITLAR, "theta", ad))
        self.kam = Kamera.bilgiden(self.kay.bilgi)
        self.gk = self.kay.gercek_konum()
        self.n = len(self.kay)
        self.kareler = np.arange(0, self.n, adim)
        self.yon_kay = yon_kay
        self.zemin = zemin
        self.hizli_yazi = hizli_yazi
        self.gercek_harita = self.kay.harita().resim
        with open(os.path.join(zo.KAYITLAR, "sim", ad, "gercek_harita.json"), encoding="utf-8") as f:
            import json
            self.hj = json.load(f)
        self.iz = hk.Izgara.gercekten(os.path.join(zo.KAYITLAR, "sim", ad))
        sinir = self.hj["ev_sinirlari_m"]
        r0, c0 = self.iz.hucre_idx(np.array(sinir["x_min"] + 1e-6), np.array(sinir["y_max"] - 1e-6))
        r1, c1 = self.iz.hucre_idx(np.array(sinir["x_max"] - 1e-6), np.array(sinir["y_min"] + 1e-6))
        self.r0, self.r1, self.c0, self.c1 = int(r0), int(r1) + 1, int(c0), int(c1) + 1
        # kayıtlı model taramaları (her 5. kare), harita bunlarla kurulur (harita_calistir.py ile aynı kural)
        z = np.load(os.path.join(TARAMA, ad + ".npz"))
        self.tar = {k: z[k] for k in z.files}
        self.tar_menzil = float(z["max_menzil"])
        self.tar_sira = 0
        # gerçek konum / yön, tahmin edilen yön (başlangıçta gerçeğe oturtulmuş: olc.hizala ile aynı)
        self.t_ms = self.kay.t_ms
        self.gyon = self.gk["yon_derece"]
        yon = np.array([k["yon"] for k in yon_kay])
        self.tyon_ham = yon
        self.ofset = sar(self.gyon[0] - yon[0])
        self.tyon = (yon + self.ofset) % 360.0
        self.hata = olc.sar(self.tyon - self.gyon)
        self.yol = np.r_[0.0, np.cumsum(np.hypot(np.diff(self.gk["x_m"]), np.diff(self.gk["y_m"])))]
        self.max_hata = np.maximum.accumulate(np.abs(self.hata))
        self.sure_ms = int(self.t_ms[-1])
        self.govde = zo.theta_maskesi(self.kam)
        self.zemin_sira = {int(k): j for j, k in enumerate(zemin["kare"])}
        self.son_degisim_kare = -10 ** 9
        self.onceki_degisim = 0
        self.degisim_cikis = {}                 # çıkış kare sırası -> son anahtar değişim çıkış sırası
        self._cikis = -1
        self.durum_img = None
        self.flas_kalan = 0

    # ---- durum ilerlet (çizimden bağımsız, sırayla çağrılmalı)
    def ilerlet(self, i):
        self._cikis += 1
        while self.tar_sira < len(self.tar["kare"]) and self.tar["kare"][self.tar_sira] <= i:
            j = self.tar_sira
            k = int(self.tar["kare"][j])
            self.iz.tara_ekle(self.gk["x_m"][k], self.gk["y_m"][k], self.gk["yon_derece"][k], self.tar["mesafe"][j],
                              self.tar["guven"][j], self.tar_menzil, guven_min=0.2)
            self.tar_sira += 1
        d = self.yon_kay[i]["degisim"]
        if d > self.onceki_degisim:
            self.flas_kalan = 12
        self.onceki_degisim = d
        flas = self.flas_kalan
        self.flas_kalan = max(0, self.flas_kalan - 1)
        return flas

    # ---- koordinat dönüşümleri
    def dunya_piksel(self, x, y):
        px = ((x - self.iz.x0) / self.iz.h - self.c0) * OLCEK
        py = ((self.iz.Hh - (y - self.iz.y0) / self.iz.h) - self.r0) * OLCEK
        return px, py

    @staticmethod
    def pano_xy(az, el):
        return (np.asarray(az) + 180.0) / 360.0 * W, (EL_UST - np.asarray(el)) / (EL_UST - EL_ALT) * PANO_H

    # ---- panorama
    def pano(self, i, yazi):
        im = self.kay.resim(i)
        e, _ = self.kam.balikgozu_to_esdikdortgen(im, 1280, 640)
        v0 = int(round((90.0 - EL_UST) / 180.0 * 640))
        v1 = int(round((90.0 - EL_ALT) / 180.0 * 640))
        p = cv2.resize(e[v0:v1], (W, PANO_H), interpolation=cv2.INTER_LINEAR)
        zj = self.zemin_sira.get(i)
        # zemin bölgesi (yeşil) ve sınır
        y_alt0 = int(round((EL_UST - 0.0) / (EL_UST - EL_ALT) * PANO_H))      # ufuk çizgisinin satırı
        if zj is not None:
            Pm = cv2.resize(self.zemin["P"][zj], (W, PANO_H - y_alt0), interpolation=cv2.INTER_LINEAR)
            govde = cv2.resize(self.govde[320:int(round((90.0 - EL_ALT) / 180.0 * 640))].astype(np.uint8), (W, PANO_H - y_alt0),
                               interpolation=cv2.INTER_NEAREST) > 0
            m = ((Pm > 127) & ~govde)[..., None]
            bolge = p[y_alt0:]
            yesil = np.array([ZEMIN_R[2], ZEMIN_R[1], ZEMIN_R[0]], np.float32)
            p[y_alt0:] = np.where(m, bolge * 0.62 + yesil * 0.38, bolge).astype(np.uint8)
            # sınır çizgisi
            k = np.arange(360)
            v = 320 + self.zemin["r"][zj].astype(np.float32)             # panorama satırı (piksel kenarı)
            el = 90.0 - v / 640.0 * 180.0
            y = (EL_UST - el) / (EL_UST - EL_ALT) * PANO_H
            x = ((k + 0.5) / 360.0 * W + W / 2) % W
            o = np.argsort(x)
            pts = np.stack([x[o], y[o]], 1)
            kes = np.where(np.diff(pts[:, 0]) > 40)[0] + 1
            for parca in np.split(pts, kes):
                if len(parca) > 1:
                    pp = parca.astype(np.int32).reshape(-1, 1, 2)
                    cv2.polylines(p, [pp], False, (0, 0, 0), 8, cv2.LINE_AA)
                    cv2.polylines(p, [pp], False, bgr(SINIR_R), 4, cv2.LINE_AA)
        # eşleşmeler
        ky = self.yon_kay[i]
        n_es = n_ic = 0
        if ky["izli"] is not None:
            a, b, ic = ky["izli"]
            ax, ay = self.pano_xy(a[:, 0], a[:, 1])
            bx, by = self.pano_xy(b[:, 0], b[:, 1])
            n_es, n_ic = len(ic), int(ic.sum())
            kat = p.copy()
            for q in np.where(~ic)[0]:
                if abs(bx[q] - ax[q]) < W / 2:
                    cv2.line(kat, (int(ax[q]), int(ay[q])), (int(bx[q]), int(by[q])), bgr(KOTU), 1, cv2.LINE_AA)
                cv2.circle(kat, (int(bx[q]), int(by[q])), 3, bgr(KOTU), -1, cv2.LINE_AA)
            p = cv2.addWeighted(kat, 0.45, p, 0.55, 0)
            for q in np.where(ic)[0]:
                if abs(bx[q] - ax[q]) < W / 2:
                    cv2.line(p, (int(ax[q]), int(ay[q])), (int(bx[q]), int(by[q])), (0, 0, 0), 3, cv2.LINE_AA)
                    cv2.line(p, (int(ax[q]), int(ay[q])), (int(bx[q]), int(by[q])), bgr(IYI), 1, cv2.LINE_AA)
            for q in np.where(ic)[0]:
                cv2.circle(p, (int(bx[q]), int(by[q])), 5, (0, 0, 0), -1, cv2.LINE_AA)
                cv2.circle(p, (int(bx[q]), int(by[q])), 3, bgr(IYI), -1, cv2.LINE_AA)
        return p, n_es, n_ic

    # ---- harita
    def harita_resmi(self):
        E = self.iz.durum()[self.r0:self.r1, self.c0:self.c1]
        lut = np.zeros((256, 3), np.uint8)
        lut[:] = (128, 124, 120)                    # RGB bilinmiyor = gri
        lut[255] = (238, 236, 228)                  # boş = açık
        lut[0] = (28, 30, 36)                       # dolu = koyu
        rgb = lut[E]
        return cv2.resize(rgb, (E.shape[1] * OLCEK, E.shape[0] * OLCEK), interpolation=cv2.INTER_NEAREST)

    def gercek_resmi(self):
        T = self.gercek_harita[self.r0:self.r1, self.c0:self.c1]
        lut = np.zeros((256, 3), np.uint8)
        lut[:] = (128, 124, 120)
        lut[255] = (238, 236, 228)
        lut[0] = (28, 30, 36)
        rgb = lut[T]
        return cv2.resize(rgb, (T.shape[1] * OLCEK, T.shape[0] * OLCEK), interpolation=cv2.INTER_NEAREST)

    def okla(self, img, p0, yon_der, uzun, renk, kal, ucboyu=14):
        s, c = np.sin(np.radians(yon_der)), np.cos(np.radians(yon_der))
        uc = (p0[0] + s * uzun, p0[1] - c * uzun)
        cv2.line(img, (int(p0[0]), int(p0[1])), (int(uc[0]), int(uc[1])), (0, 0, 0), kal + 3, cv2.LINE_AA)
        sol = (uc[0] - s * ucboyu - c * ucboyu * 0.55, uc[1] + c * ucboyu - s * ucboyu * 0.55)
        sag = (uc[0] - s * ucboyu + c * ucboyu * 0.55, uc[1] + c * ucboyu + s * ucboyu * 0.55)
        cv2.fillPoly(img, [np.array([uc, sol, sag], np.int32)], (0, 0, 0), cv2.LINE_AA)
        cv2.line(img, (int(p0[0]), int(p0[1])), (int(uc[0]), int(uc[1])), bgr(renk), kal, cv2.LINE_AA)
        cv2.fillPoly(img, [np.array([uc, sol, sag], np.int32)], bgr(renk), cv2.LINE_AA)

    def robot_ucgeni(self, img, p0, yon_der, boy=15):
        s, c = np.sin(np.radians(yon_der)), np.cos(np.radians(yon_der))
        on = (p0[0] + s * boy, p0[1] - c * boy)
        a = (p0[0] - s * boy * 0.7 - c * boy * 0.7, p0[1] + c * boy * 0.7 - s * boy * 0.7)
        b = (p0[0] - s * boy * 0.7 + c * boy * 0.7, p0[1] + c * boy * 0.7 + s * boy * 0.7)
        pts = np.array([on, a, b], np.int32)
        cv2.fillPoly(img, [pts], (255, 255, 255), cv2.LINE_AA)
        cv2.polylines(img, [pts], True, (0, 0, 0), 2, cv2.LINE_AA)

    def harita_ciz(self, i, tarama_j):
        hm = self.harita_resmi()
        gm = self.gercek_resmi()
        gx, gy = self.gk["x_m"], self.gk["y_m"]
        iz_x, iz_y = self.dunya_piksel(gx[:i + 1], gy[:i + 1])
        yol = np.stack([iz_x, iz_y], 1).astype(np.int32).reshape(-1, 1, 2)
        xr, yr = self.dunya_piksel(gx[i], gy[i])
        p0 = (xr, yr)
        gy_der = self.gyon[i]
        ty_der = self.tyon[i]
        for im, sol in ((hm, True), (gm, False)):
            if len(yol) > 1:
                cv2.polylines(im, [yol], False, (255, 255, 255), 4, cv2.LINE_AA)
                cv2.polylines(im, [yol], False, bgr((210, 30, 30)), 2, cv2.LINE_AA)
        # güvenilir menzil halkası ve canlı tarama (yalnızca sol harita)
        cv2.circle(hm, (int(xr), int(yr)), int(self.tar_menzil / self.iz.h * OLCEK), (120, 120, 120), 1, cv2.LINE_AA)
        if tarama_j is not None:
            k = np.arange(360)
            d = self.zemin["mesafe"][tarama_j]
            ok = np.isfinite(d) & (d < self.tar_menzil) & (self.zemin["guven"][tarama_j] >= 0.2)
            teta = np.radians(gy_der + k[ok])
            px = xr + np.sin(teta) * d[ok] / self.iz.h * OLCEK
            py = yr - np.cos(teta) * d[ok] / self.iz.h * OLCEK
            for x_, y_ in zip(px, py):
                cv2.circle(hm, (int(x_), int(y_)), 2, bgr(TARAMA_R), -1, cv2.LINE_AA)
        # robot ve iki yön oku (yalnızca sol harita); gerçek harita: yalnızca robot
        self.okla(hm, p0, ty_der, 100, TAHMIN_R, 4)
        self.okla(hm, p0, gy_der, 72, GERCEK_R, 6)
        self.robot_ucgeni(hm, p0, gy_der)
        self.robot_ucgeni(gm, p0, gy_der)
        # ölçek çubuğu (1 m)
        for im in (hm, gm):
            l = int(1.0 / self.iz.h * OLCEK)
            yb = im.shape[0] - 14
            cv2.line(im, (14, yb), (14 + l, yb), (0, 0, 0), 5, cv2.LINE_AA)
            cv2.line(im, (14, yb), (14 + l, yb), (255, 255, 255), 2, cv2.LINE_AA)
        return hm, gm

    # ---- ölçüm paneli grafiği
    def grafik(self, canvas, x0, y0, w, h, i, yazi):
        pencere = 30.0 if self.sure_ms > 40000 else self.sure_ms / 1000.0 + 0.5
        t_now = self.t_ms[i] / 1000.0
        ba = max(0.0, t_now - pencere) if t_now > pencere else 0.0
        ymax = 4.0
        cv2.rectangle(canvas, (x0, y0), (x0 + w, y0 + h), (34, 42, 54), -1)

        def yy(e):
            return int(y0 + h / 2 - np.clip(e, -ymax * 1.1, ymax * 1.1) / ymax * (h / 2 - 6))
        kat = canvas.copy()
        cv2.rectangle(kat, (x0, yy(2.0)), (x0 + w, yy(-2.0)), bgr(HATA_IYI), -1)
        canvas[:] = cv2.addWeighted(kat, 0.22, canvas, 0.78, 0)
        cv2.line(canvas, (x0, y0 + h // 2), (x0 + w, y0 + h // 2), (90, 100, 112), 1)
        for e in (2.0, -2.0):
            cv2.line(canvas, (x0, yy(e)), (x0 + w, yy(e)), bgr(HATA_IYI), 1, cv2.LINE_AA)
        t_all = self.t_ms[:i + 1] / 1000.0
        sec = t_all >= ba
        xs = x0 + (t_all[sec] - ba) / pencere * w
        ys = np.array([yy(e) for e in self.hata[:i + 1][sec]])
        pts = np.stack([xs, ys], 1).astype(np.int32).reshape(-1, 1, 2)
        if len(pts) > 1:
            cv2.polylines(canvas, [pts], False, bgr(TAHMIN_R), 3, cv2.LINE_AA)
        cv2.circle(canvas, (int(xs[-1]), int(ys[-1])), 6, (255, 255, 255), -1, cv2.LINE_AA)
        yazi.append((x0 + w + 6, yy(2.0), "+2°", 20, SOLUK, False, "lm"))
        yazi.append((x0 + w + 6, yy(-2.0), "-2°", 20, SOLUK, False, "lm"))
        yazi.append((x0 + 8, y0 + 4, "yön hatası - zaman (son %d sn, yeşil bant ±2°)" % int(round(pencere)), 20, SOLUK, False, "la"))

    # ---- kare
    def ciz(self, i, flas, cikis_sirasi):
        cv = np.zeros((H, W, 3), np.uint8)
        cv[:] = bgr(ARKA)
        yazi = []                         # (x, y, metin, boyut, renk, kalin, anchor)
        # başlık
        cv[BASLIK_Y:BASLIK_H] = (36, 28, 20)
        yazi.append((24, 32, "Simülasyon · gerçek kamera benzetimi", 36, YAZI, True, "lm"))
        yazi.append((W - 24, 32, "kayıt: " + self.ad, 30, SOLUK, False, "rm"))
        hiz = self.adim
        rozet = "%dx hız" % hiz if hiz > 1 else "gerçek zaman"
        rx = W - 24 - int(font(30).getlength("kayıt: " + self.ad)) - 28
        yazi.append((rx, 32, rozet, 30, VURGU, True, "rm"))
        # panorama
        pano, n_es, n_ic = self.pano(i, yazi)
        cv[PANO_Y:PANO_Y + PANO_H] = pano
        ov = cv[PANO_Y:PANO_Y + PANO_H]
        kat = ov.copy()
        cv2.rectangle(kat, (0, 0), (W, 54), (10, 12, 16), -1)
        cv2.rectangle(kat, (0, PANO_H - 40), (W, PANO_H), (10, 12, 16), -1)
        ov[:] = cv2.addWeighted(kat, 0.72, ov, 0.28, 0)
        yazi.append((18, PANO_Y + 27, "Robotun gördüğü (360° panorama)", 34, YAZI, True, "lm"))
        # açıklama (sağ üst): renkli parçalar sağdan sola
        xs = W - 18
        for metin, renk in reversed([("yeşil: zemin  ·  ", ZEMIN_R), ("parlak çizgi: zemin sınırı  ·  ", SINIR_R),
                                     ("mavi: eşleşen nokta (anahtar kare → şimdi)  ·  ", IYI), ("kırmızı: ayıklanan", KOTU)]):
            yazi.append((xs, PANO_Y + 27, metin, 22, renk, True, "rm"))
            xs -= int(font(22, True).getlength(metin))
        # yön etiketleri
        for x, ad_ in ((W // 2, "ÖN"), (W * 3 // 4 + 200, "SAĞ"), (W // 4 - 200, "SOL"), (60, "ARKA"), (W - 60, "ARKA")):
            yazi.append((x, PANO_Y + PANO_H - 20, ad_, 24, SOLUK, True, "mm"))
        ky = self.yon_kay[i]
        durum = ky["durum"]
        if durum == "tut":
            es_yazi = "robot durgun, açı korunuyor"
        elif n_es:
            es_yazi = "eşleşme %d · doğru %d" % (n_es, n_ic)
        else:
            es_yazi = ""
        yazi.append((W // 2 - 80, PANO_Y + PANO_H - 20, es_yazi, 24, IYI, True, "rm"))
        yazi.append((W // 2 + 80, PANO_Y + PANO_H - 20, "anahtar kare değişimi: %d" % ky["degisim"], 24, VURGU, True, "lm"))
        if flas > 0:
            a = min(1.0, flas / 6.0)
            kat = cv.copy()
            cv2.rectangle(kat, (0, PANO_Y), (W, PANO_Y + PANO_H), bgr(VURGU), 14)
            cv2.rectangle(kat, (W // 2 - 230, PANO_Y + 62), (W // 2 + 230, PANO_Y + 124), (10, 12, 16), -1)
            cv[:] = cv2.addWeighted(kat, 0.9 * a, cv, 1 - 0.9 * a, 0)
            yazi.append((W // 2, PANO_Y + 93, "yeni anahtar kare", 44, VURGU, True, "mm"))
        # paneller
        for (x0, x1) in (PANEL1, PANEL2, PANEL3):
            cv[ALT_Y:H, x0:x1] = bgr(PANEL)
        # haritalar
        zj = self.zemin_sira.get(i)
        if zj is None:                    # en yakın önceki hesaplanmış kare
            zj = self.zemin_sira[int(self.kareler[self.kareler <= i][-1])]
        hm, gm = self.harita_ciz(i, zj)
        hy, hx = hm.shape[:2]
        cv[HARITA_Y:HARITA_Y + hy, HARITA_X:HARITA_X + hx] = hm
        cv[HARITA_Y:HARITA_Y + hy, PANEL2[0] + HARITA_X:PANEL2[0] + HARITA_X + hx] = gm
        yazi.append((16, ALT_Y + 20, "Kurulan harita (canlı)", 32, YAZI, True, "lm"))
        yazi.append((16, ALT_Y + 47, "Harita: zemin sınırı kameradan, robotun konumu simden hazır alındı", 21, VURGU, False, "lm"))
        yazi.append((PANEL2[0] + 16, ALT_Y + 20, "Gerçek ev planı", 32, YAZI, True, "lm"))
        yazi.append((PANEL2[0] + 16, ALT_Y + 47, "karşılaştırma için (robot bunu bilmiyor)", 21, SOLUK, False, "lm"))
        # harita içi etiketler
        for x0 in (HARITA_X, PANEL2[0] + HARITA_X):
            yazi.append((x0 + hx - 10, HARITA_Y + 18, "K ↑", 26, (30, 30, 36), True, "rm"))
        yazi.append((HARITA_X + 12, HARITA_Y + 18, "● canlı tarama (zemin sınırı)", 20, TARAMA_R, True, "lm"))
        yazi.append((HARITA_X + 14, HARITA_Y + hy - 34, "1 m", 20, (30, 30, 36), True, "lm"))
        # harita lejantı (alt)
        ly = HARITA_Y + hy + 3
        yazi.append((HARITA_X + 4, ly + 12, "▬ gerçek yön", 22, GERCEK_R, True, "lm"))
        yazi.append((HARITA_X + 190, ly + 12, "▬ kameradan tahmin edilen yön", 22, TAHMIN_R, True, "lm"))
        yazi.append((PANEL2[0] + HARITA_X + 4, ly + 12, "kırmızı çizgi: robotun gittiği yol   gri: bilinmiyor", 20, SOLUK, False, "lm"))
        # ölçüm paneli
        x0 = PANEL3[0] + 20
        yazi.append((x0, ALT_Y + 20, "Canlı ölçümler", 32, YAZI, True, "lm"))
        yazi.append((x0, ALT_Y + 47, "yön tahmini kameradan, harita kurmada kullanılmıyor", 21, VURGU, False, "lm"))
        t = self.t_ms[i]
        sat1 = ALT_Y + 80
        yazi.append((x0, sat1, "SÜRE", 20, SOLUK, True, "la"))
        yazi.append((x0, sat1 + 22, "%s / %s" % (sure_yazi(t), sure_yazi(self.sure_ms)), 34, YAZI, True, "la"))
        yazi.append((x0 + 290, sat1, "GİDİLEN YOL", 20, SOLUK, True, "la"))
        yazi.append((x0 + 290, sat1 + 22, "%s m" % virgul(self.yol[i], 1), 34, YAZI, True, "la"))
        sat2 = sat1 + 74
        yazi.append((x0, sat2, "GERÇEK YÖN", 20, GERCEK_R, True, "la"))
        yazi.append((x0, sat2 + 22, "%s°" % virgul(self.gyon[i] % 360.0, 1), 34, YAZI, True, "la"))
        yazi.append((x0 + 290, sat2, "KAMERADAN YÖN", 20, TAHMIN_R, True, "la"))
        yazi.append((x0 + 290, sat2 + 22, "%s°" % virgul(self.tyon[i] % 360.0, 1), 34, YAZI, True, "la"))
        sat3 = sat2 + 78
        e = self.hata[i]
        yazi.append((x0, sat3, "YÖN HATASI", 20, SOLUK, True, "la"))
        yazi.append((x0, sat3 + 18, "%s°" % (("+" if e >= 0 else "-") + virgul(abs(e), 1)), 96, HATA_IYI if abs(e) <= 2.0 else HATA_KOTU, True, "la"))
        yazi.append((x0 + 290, sat3 + 10, "EN BÜYÜK HATA", 20, SOLUK, True, "la"))
        yazi.append((x0 + 290, sat3 + 32, "%s°" % virgul(self.max_hata[i], 1), 44, YAZI, True, "la"))
        gy0 = sat3 + 140
        gh = min(190, H - gy0 - 62)
        gyaz = []
        self.grafik(cv[gy0:gy0 + gh, x0:x0 + 500], 0, 0, 500, gh, i, gyaz)       # görünüm üstüne çizer; yazılar kaydırılır
        for (gx, gy_, m, b, r, k, an) in gyaz:
            yazi.append((x0 + gx, gy0 + gy_, m, b, r, k, an))
        yazi.append((x0, H - 40, "tahmin başlangıçta gerçek yöne oturtuldu,", 20, SOLUK, False, "lm"))
        yazi.append((x0, H - 16, "kamera yalnızca dönüşü ölçer, başlangıç yönünü bilmez", 20, SOLUK, False, "lm"))
        # metinleri tek geçişte çiz
        pil = Image.fromarray(cv[:, :, ::-1])
        d = ImageDraw.Draw(pil)
        for (x, y, m, b, r, k, an) in yazi:
            kont = (PANO_Y <= y < ALT_Y) or (HARITA_Y <= y < HARITA_Y + 540 and x < PANEL3[0])        # görüntü üstündeki yazılar: kontur
            if kont:
                koyu = sum(r) < 300
                d.text((x, y), m, font=font(b, k), fill=r, anchor=an, stroke_width=3 if koyu else 2,
                       stroke_fill=(255, 255, 255) if koyu else (0, 0, 0))
            else:
                d.text((x, y), m, font=font(b, k), fill=r, anchor=an)
        return np.asarray(pil)[:, :, ::-1].copy()


def cikis_kareleri(ad):
    adim = 2 if ad == "ev_turu" else 1
    return adim


def onizleme_secimi(c):
    """Gösterilecek kareler (c.kareler) içinden 4 önizleme: erken, kapı aralığı (en dar yer), orta, son."""
    kn = c.kareler
    x, y = c.gk["x_m"][kn], c.gk["y_m"][kn]
    n = len(kn)
    ilk = int(kn[min(n - 1, max(2, n // 20))])
    orta = int(kn[n // 2])
    son = int(kn[-1])
    kapi = int(kn[int(n * 0.4)])
    tur = zo.gercek_mesafe(c.ad, "zemin", "sim")
    if c.ad == "ev_turu" and tur is not None:
        kare_no, mes = tur
        sat = {int(k): j for j, k in enumerate(kare_no)}
        en = None
        for q, k in enumerate(kn):
            j = sat.get(int(k))
            if j is None or q < 30 or q > n - 20:
                continue
            hareket = np.hypot(x[q] - x[max(0, q - 8)], y[q] - y[max(0, q - 8)])
            if hareket < 0.15:
                continue
            ac = c.gyon[k]
            sol = mes[j][np.arange(-110, -70) % 360].min()
            sag = mes[j][np.arange(70, 110) % 360].min()
            deg = max(sol, sag)
            if abs(((ac % 90) + 45) % 90 - 45) > 12:      # eksene hizalı gidişte kapı daha belirgin
                continue
            if en is None or deg < en[0]:
                en = (deg, int(k))
        if en is not None:
            kapi = en[1]
    return [("erken", ilk), ("kapi", kapi), ("orta", orta), ("son", son)]


def calistir(ad, onizleme=True, video=True, yeniden=False, olcek=1.0):
    os.makedirs(CIKTI, exist_ok=True)
    t0 = time.time()
    adim = cikis_kareleri(ad)
    yon_kay = yon_hesapla(ad, yeniden)
    kay = Kayit(os.path.join(zo.KAYITLAR, "theta", ad))
    kareler = np.arange(0, len(kay), adim)
    zemin = zemin_hesapla(ad, kareler, yeniden)
    c = Cizici(ad, adim, yon_kay, zemin, None)
    # ---- kayıtlı tahmin CSV'si ile karşılaştırma
    import csv
    with open(os.path.join(TAHMIN_CSV, "theta_%s.csv" % ad), newline="", encoding="utf-8") as f:
        satir = list(csv.DictReader(f))
    csv_hata = np.array([float(s["hata_derece"]) for s in satir])
    csv_yon = np.array([float(s["yon_derece"]) for s in satir])
    fark = np.abs(olc.sar(c.hata - csv_hata))
    print("%s: M2D hatası, kayıtlı CSV'ye göre en çok fark %.4g derece, ortalama fark %.4g (kayıtlı en çok |hata| %.2f, benim %.2f)" % (
        ad, fark.max(), fark.mean(), np.abs(csv_hata).max(), np.abs(c.hata).max()))
    # ---- kayıtlı tarama ile yeniden hesaplanan tarama farkı (her 5. kare)
    ortak = np.intersect1d(c.tar["kare"], zemin["kare"], return_indices=True)
    if len(ortak[0]):
        a = c.tar["mesafe"][ortak[1]]
        b = zemin["mesafe"][ortak[2]]
        ok = np.isfinite(a) & np.isfinite(b)
        print("%s: tarama farkı (kayıtlı - şimdi) %d ortak kare, aynı değerli oran %.3f, ort |fark| %.4g m" % (
            ad, len(ortak[0]), float((np.abs(a - b)[ok] < 1e-4).mean()), float(np.abs(a - b)[ok].mean())))
    secim = onizleme_secimi(c)
    print("önizleme kareleri:", secim)
    # ---- durum ilerletirken önizleme + video
    yaz = None
    if video:
        yol = os.path.join(CIKTI, "%s.mp4" % ad)
        yaz = cv2.VideoWriter(yol, cv2.VideoWriter_fourcc(*"mp4v"), FPS, (W, H))
        if not yaz.isOpened():
            raise RuntimeError("VideoWriter açılamadı")
    sec_say = {k: ad_ for ad_, k in secim}
    cikis = 0
    for i in c.kareler:
        flas = c.ilerlet(int(i))
        i = int(i)
        gerek_onizleme = onizleme and i in sec_say
        if gerek_onizleme or video:
            kare = c.ciz(i, flas, cikis)
            if gerek_onizleme:
                cv2.imwrite(os.path.join(CIKTI, "onizleme_%s_%s.png" % (ad, sec_say[i])), kare)
            if yaz is not None:
                yaz.write(kare)
        cikis += 1
        if video and cikis % 100 == 0:
            print("  çizim %s %d/%d (%.0f sn)" % (ad, cikis, len(c.kareler), time.time() - t0), flush=True)
    if yaz is not None:
        yaz.release()
        yol = os.path.join(CIKTI, "%s.mp4" % ad)
        v = cv2.VideoCapture(yol)
        sayi = int(v.get(cv2.CAP_PROP_FRAME_COUNT))
        ok, _ = v.read()
        v.release()
        print("%s: %s  %.1f MB  kare %d  açılıyor: %s  süre %.1f sn" % (ad, yol, os.path.getsize(yol) / 1e6, sayi, ok, time.time() - t0))
    return c


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--kayit", nargs="*", default=["yerinde_donus_salon", "ev_turu"])
    ap.add_argument("--yalniz_onizleme", action="store_true")
    ap.add_argument("--yeniden", action="store_true", help="önbellekteki yön ve zemin hesaplarını yeniden yap")
    a = ap.parse_args()
    for ad in a.kayit:
        calistir(ad, onizleme=True, video=not a.yalniz_onizleme, yeniden=a.yeniden)
