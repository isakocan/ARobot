"""Gösterim videosu (AR): konum kameradan kestirilirken sanal nesneler oyuncunun göreceği gibi çizilirse nasıl görünür (yalnızca sim).

Ana görüntü: robotun 360 derece (çift balık gözü) karesinden 100 derece yatay görüş açılı, ileriye bakan perspektif görünüm.
Dört sanal nesne dünyada SABİT yerlere konur. Her karede her nesne İKİ kez çizilir:
  1) gerçek poz ile: yalnızca ince beyaz çizgi ("olması gereken yer");
  2) sistemin kestirdiği poz ile (varyant D: yalnız kamera, harita düzeltmeli): dolu, gölgeli nesne.
Dolu nesnenin çizgiden ne kadar kaydığı = oyuncunun göreceği AR hatası.
Örtme (yalnızca dolu nesne): nesne 7 dikey dilime bölünür; kestirilen kamera konumundan dilime çekilen doğru, robotun KENDİ
haritasında (o ana kadar kestirilen pozlarla kurduğu harita) dolu bir hücreden geçiyorsa dilim gizlenir (soluk çizilir).
Bilinmeyen hücre engellemez. Karşılaştırma için aynı kararlar GERÇEK haritayla ve gerçek konumla da hesaplanır; farklıysa sayılır.

DÜRÜSTLÜK: konum ve harita kameradan; yalnızca başlangıç konumu verildi (varyant tanımları konum_slam.py'de). Hatalar süslenmez.

Kullanım (depo kökünden):
    python tools/konum_deney/gosterim_ar.py --yalniz_onizleme          # önizleme resimleri
    python tools/konum_deney/gosterim_ar.py                            # D videosu (720p + 480p) + önizleme
    python tools/konum_deney/gosterim_ar.py --varyant A --yalniz_480   # A varyantı (karşılaştırma)
    python tools/konum_deney/gosterim_ar.py --yalniz_harita D A        # yalnız harita önbelleği
Çıktı: kayitlar/sonuclar/gosterim/ (onbellek_ar_*.npz kestirilen haritaları saklar)."""
import argparse
import json
import os
import sys
import time

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
import konum_ortak as ko  # noqa: E402
import konum_slam as ks  # noqa: E402
import harita_kur as hk  # noqa: E402
from kayit import Kayit  # noqa: E402
from kamera import Kamera  # noqa: E402

AD, TUR = "ev_turu", "theta"
CIKTI = os.path.join(ko.KAYITLAR, "sonuclar", "gosterim")
ADIM = 2                                    # 2x hız: her ikinci kare
FPS = 15
KAM_H = ko.KAM_H                            # mercek yüksekliği 0,255 m
HFOV = 100.0                                # ana görüntünün yatay görüş açısı (derece)
DILIM = 7
ENYAKIN = 4.0                               # istatistik için en uzak nesne mesafesi (m)
SOLUK_ALFA = 0.15

# ---------------------------------------------------------------- yerleşim (1280x720 tuval)
W, H = 1280, 720
BASLIK_H = 52
VX, VY, VW, VH = 0, BASLIK_H, 840, 540      # ana görüntü
PX, PW = 840, 440                           # sağ panel
ALT_Y = VY + VH                             # 592: alt şerit (açıklama)
OLCEK_HARITA = 2                            # hücre başına piksel (sağ üst harita)

# ---------------------------------------------------------------- renkler (RGB), gosterim_video.py ile aynı stil
ARKA = (16, 21, 28)
PANEL = (24, 31, 41)
BASLIK = (20, 28, 36)
YAZI = (240, 244, 248)
SOLUK = (150, 162, 176)
VURGU = (255, 196, 0)
GERCEK_R = (70, 160, 255)
TAHMIN_R = (255, 140, 0)
HATA_IYI = (60, 220, 120)
HATA_KOTU = (255, 150, 40)

# ---------------------------------------------------------------- nesneler (dünya konumu, gerçek haritada serbest zemin)
NESNELER = [
    dict(ad="küp", sekil="kup", x=-1.9, y=-1.1, renk=(255, 112, 70), yaw=25.0),
    dict(ad="silindir", sekil="silindir", x=-0.35, y=0.60, renk=(40, 205, 190), yaw=0.0),
    dict(ad="koni", sekil="koni", x=1.6, y=-0.37, renk=(165, 110, 255), yaw=0.0),
    dict(ad="küre", sekil="kure", x=3.9, y=-1.5, renk=(250, 205, 40), yaw=0.0),
]
BOY = 0.30                                  # nesne boyu / çapı (m), zemine oturur

_FONT = {}


def font(boyut, kalin=False):
    a = (boyut, kalin)
    if a not in _FONT:
        yol = r"C:\Windows\Fonts\segoeuib.ttf" if kalin else r"C:\Windows\Fonts\segoeui.ttf"
        _FONT[a] = ImageFont.truetype(yol, boyut)
    return _FONT[a]


def bgr(r):
    return (int(r[2]), int(r[1]), int(r[0]))


def virgul(x, n=1):
    return ("%.*f" % (n, x)).replace(".", ",")


# ================================================================ 1) kestirilen harita (her taramadan sonraki anlık görüntüler)
class _AnlikIzgara(hk.Izgara):
    liste = None
    kayitla = False

    def tara_ekle(self, *a, **k):
        super().tara_ekle(*a, **k)
        if self.kayitla:
            _AnlikIzgara.liste.append(ks.izgara_kirp(self).copy())


def harita_anliklari(varyant, yeniden=False):
    """Kosucu'yu (konum_slam.py) DEĞİŞTİRMEDEN çalıştırır; haritanın her taramadan sonraki gerçek-harita pencereli görüntüsünü saklar.
    Dönüş: dict(P (n,3) kestirilen poz, kare (m,) tarama sonrası kare sırası, harita (m,H,W) uint8 0/255/128)."""
    yol = os.path.join(CIKTI, "onbellek_ar_%s.npz" % varyant)
    if os.path.exists(yol) and not yeniden:
        z = np.load(yol)
        return {k: z[k] for k in z.files}
    k = ks.Kosucu(AD, TUR, varyant)
    orijinal = ks.buyuk_izgara
    ilk = []

    def fabrika(klasor, kenar_m=10.0):
        iz = orijinal(klasor, kenar_m)
        iz.__class__ = _AnlikIzgara
        iz.kayitla = len(ilk) == 0                     # ilk oluşturulan = asıl harita
        ilk.append(iz)
        return iz
    _AnlikIzgara.liste = []
    ks.buyuk_izgara = fabrika
    t0 = time.time()
    try:
        P, iz, log = k.kos(lambda i, n: print("  harita %s %d/%d (%.0f sn)" % (varyant, i, n, time.time() - t0), flush=True))
    finally:
        ks.buyuk_izgara = orijinal
    kare = [i for i in range(k.n) if int(k.kay.kare_numaralari[i]) in k.tar_sira]
    harita = np.stack(_AnlikIzgara.liste)
    assert len(kare) == len(harita), (len(kare), len(harita))
    z = {"P": P, "kare": np.array(kare), "harita": harita}
    os.makedirs(CIKTI, exist_ok=True)
    np.savez_compressed(yol, **z)
    return z


# ================================================================ 2) nesne geometrisi (x doğu, y kuzey, z yukarı; zemine oturur)
def _dondur(v, yaw_derece):
    a = np.radians(yaw_derece)
    c, s = np.cos(a), np.sin(a)
    R = np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])
    return v @ R.T


def _yuzey(pts, dis_yon):
    """Çokgen -> (köşeler, birim dış normal); köşe sırası kullanılmaz, normal verilen dış yöne göre."""
    pts = np.asarray(pts, float)
    n = np.cross(pts[1] - pts[0], pts[2] - pts[0])
    n = n / max(np.linalg.norm(n), 1e-12)
    if np.dot(n, dis_yon) < 0:
        n = -n
    return pts, n


def mesh_yap(sekil, yaw):
    """Dışbükey nesne yüzeyleri (alt yüz yok), yerel çerçevede yaw ile döndürülmüş: [(köşeler (N,3), normal (3,)), ...]."""
    y = []
    if sekil == "kup":
        h, z = BOY / 2.0, BOY
        K = np.array([[-h, -h, 0], [h, -h, 0], [h, h, 0], [-h, h, 0], [-h, -h, z], [h, -h, z], [h, h, z], [-h, h, z]])
        for idx, dis in (((4, 5, 6, 7), (0, 0, 1)), ((0, 1, 5, 4), (0, -1, 0)), ((1, 2, 6, 5), (1, 0, 0)),
                         ((2, 3, 7, 6), (0, 1, 0)), ((3, 0, 4, 7), (-1, 0, 0))):
            y.append(_yuzey(K[list(idx)], np.array(dis, float)))
    elif sekil == "silindir":
        r, z, n = BOY / 2.0, BOY, 20
        a = np.linspace(0, 2 * np.pi, n, endpoint=False)
        alt = np.stack([r * np.cos(a), r * np.sin(a), np.zeros(n)], 1)
        ust = alt + np.array([0, 0, z])
        for i in range(n):
            j = (i + 1) % n
            am = 0.5 * (a[i] + a[j]) if j else 0.5 * (a[i] + 2 * np.pi)
            y.append(_yuzey([alt[i], alt[j], ust[j], ust[i]], np.array([np.cos(am), np.sin(am), 0.0])))
        y.append(_yuzey(ust, np.array([0, 0, 1.0])))
    elif sekil == "koni":
        r, z, n = 0.16, 0.34, 20
        a = np.linspace(0, 2 * np.pi, n, endpoint=False)
        alt = np.stack([r * np.cos(a), r * np.sin(a), np.zeros(n)], 1)
        tepe = np.array([0, 0, z])
        for i in range(n):
            j = (i + 1) % n
            am = 0.5 * (a[i] + a[j]) if j else 0.5 * (a[i] + 2 * np.pi)
            y.append(_yuzey([alt[i], alt[j], tepe], np.array([np.cos(am) * z, np.sin(am) * z, r])))
    elif sekil == "kure":
        r, nlat, nlon = BOY / 2.0, 9, 20
        c0 = np.array([0, 0, r])
        enl = np.linspace(-np.pi / 2, np.pi / 2, nlat + 1)
        boy = np.linspace(0, 2 * np.pi, nlon, endpoint=False)

        def nok(e, b):
            return c0 + r * np.array([np.cos(e) * np.cos(b), np.cos(e) * np.sin(b), np.sin(e)])
        for i in range(nlat):
            for j in range(nlon):
                j2 = (j + 1) % nlon
                p = [nok(enl[i], boy[j]), nok(enl[i], boy[j2]), nok(enl[i + 1], boy[j2]), nok(enl[i + 1], boy[j])]
                # kutuplarda iki köşe çakışır: yinelenen köşeyi at
                q = [p[0]]
                for v in p[1:]:
                    if np.linalg.norm(v - q[-1]) > 1e-9:
                        q.append(v)
                if len(q) > 1 and np.linalg.norm(q[0] - q[-1]) < 1e-9:
                    q.pop()
                if len(q) < 3:
                    continue
                orta = np.mean(q, 0)
                y.append(_yuzey(q, orta - c0))
    return [(_dondur(v, yaw), _dondur(n[None], yaw)[0]) for v, n in y]


def mesh_dunya(nes):
    m = mesh_yap(nes["sekil"], nes["yaw"])
    t = np.array([nes["x"], nes["y"], 0.0])
    return [(v + t, n) for v, n in m]


L_ISIK = np.array([-0.60, -0.30, 0.74])
L_ISIK = L_ISIK / np.linalg.norm(L_ISIK)
TON = (1.12, 0.84, 0.60)                    # açık, orta, koyu: düz gölgeleme, 3 ton


def ton_renk(renk_rgb, n):
    nl = float(np.dot(n, L_ISIK))
    t = TON[0] if nl > 0.50 else (TON[1] if nl > -0.30 else TON[2])
    c = np.clip(np.array(renk_rgb, float) * t, 0, 255)
    return (int(c[2]), int(c[1]), int(c[0]))


# ================================================================ 3) kamera (perspektif görünüm) ve izdüşüm
FP = (VW / 2.0) / np.tan(np.radians(HFOV / 2.0))      # odak uzaklığı (piksel)
CX, CY = (VW - 1) / 2.0, (VH - 1) / 2.0
FN = 0.05                                             # yakın düzlem (m)


def kam_koord(poz, P):
    """Dünya noktaları (N,3) -> kamera koordinatı (sağ, yukarı, ileri); kamera = (poz x,y, yükseklik KAM_H), yön kuzeyden saat yönünde."""
    y = np.radians(poz[2])
    sn, cs = np.sin(y), np.cos(y)
    P = np.atleast_2d(np.asarray(P, float))
    dx, dy = P[:, 0] - poz[0], P[:, 1] - poz[1]
    return np.stack([dx * cs - dy * sn, P[:, 2] - KAM_H, dx * sn + dy * cs], 1)


def piksel(c):
    f = np.maximum(c[:, 2], 1e-6)
    return np.stack([CX + FP * c[:, 0] / f, CY - FP * c[:, 1] / f], 1)


def yakin_kirp(c):
    """Çokgeni (N,3) kamera koordinatında f >= FN düzlemine kırp (Sutherland-Hodgman)."""
    out = []
    n = len(c)
    for i in range(n):
        a, b = c[i], c[(i + 1) % n]
        ia, ib = a[2] >= FN, b[2] >= FN
        if ia:
            out.append(a)
        if ia != ib:
            t = (FN - a[2]) / (b[2] - a[2])
            out.append(a + t * (b - a))
    return np.array(out) if len(out) >= 3 else None


def perspektif_tablolari(kam):
    """Ana görüntünün her pikseli için çift balık gözü tablosu (sabit: robot çerçevesinde ileri bakış, ufuk düz)."""
    jj, ii = np.mgrid[0:VH, 0:VW].astype(np.float64)
    d = np.stack([(ii - CX) / FP, -(jj - CY) / FP, np.ones_like(ii)], -1)
    d /= np.linalg.norm(d, axis=-1, keepdims=True)
    tab = []
    for k in (0, 1):
        u, v, _ = kam.robot_to_lens(d, k)
        tab.append((u.astype(np.float32), v.astype(np.float32)))
    w1, w2 = kam.agirliklar(d)
    toplam = np.maximum(w1 + w2, 1e-4)
    return tab, (w1 / toplam).astype(np.float32)[..., None], (w2 / toplam).astype(np.float32)[..., None]


def perspektif(resim, tabl):
    tab, k1, k2 = tabl
    a = cv2.remap(resim, tab[0][0], tab[0][1], cv2.INTER_CUBIC, borderMode=cv2.BORDER_CONSTANT)
    if float(k2.max()) <= 0.0:
        return a
    b = cv2.remap(resim, tab[1][0], tab[1][1], cv2.INTER_CUBIC, borderMode=cv2.BORDER_CONSTANT)
    return np.clip(a.astype(np.float32) * k1 + b.astype(np.float32) * k2 + 0.5, 0, 255).astype(np.uint8)


# ================================================================ 4) örtme: haritada görüş hattı
class Harita:
    """Gerçek harita ızgarasıyla aynı örgü. M: uint8, 0 = dolu, 255 = boş, 128 = bilinmiyor (engellemez)."""
    def __init__(self, x0, y0, Hh, W_, h):
        self.x0, self.y0, self.Hh, self.W, self.h = x0, y0, Hh, W_, h

    def engelli(self, M, px, py, qx, qy):
        """(px,py) kameradan (qx,qy) noktalarına (K,) doğru dolu hücreden geçiyor mu? Başlangıçtan 0,12 m ve uçtan 0,08 m atlanır."""
        qx, qy = np.atleast_1d(np.asarray(qx, float)), np.atleast_1d(np.asarray(qy, float))
        d = np.hypot(qx - px, qy - py)
        t0, t1 = 0.12, d - 0.08
        N = max(8, int(np.ceil(max(float(np.max(d)) - 0.2, 0.1) / 0.02)))
        a = np.linspace(0.0, 1.0, N)[None, :]
        t = t0 + (np.maximum(t1, t0)[:, None] - t0) * a
        u = t / np.maximum(d, 1e-9)[:, None]
        x = px + (qx - px)[:, None] * u
        y = py + (qy - py)[:, None] * u
        c = np.floor((x - self.x0) / self.h).astype(np.int64)
        r = (self.Hh - 1 - np.floor((y - self.y0) / self.h)).astype(np.int64)
        ic = (r >= 0) & (r < self.Hh) & (c >= 0) & (c < self.W)
        dolu = np.zeros(x.shape, bool)
        dolu[ic] = M[r[ic], c[ic]] == 0
        return dolu.any(1) & (t1 > t0)


def dilim_noktalari(poz, nes):
    """Nesne için DILIM dikey dilimin orta (z=boy/2) noktaları ve sınır noktaları; kameradan bakışa dik, sağa doğru artar."""
    R = BOY / 2.0
    d = np.array([nes["x"] - poz[0], nes["y"] - poz[1]])
    n = max(float(np.hypot(*d)), 1e-6)
    sag = np.array([d[1], -d[0]]) / n
    lb = -R + np.arange(DILIM + 1) * (2 * R / DILIM)
    lm = 0.5 * (lb[:-1] + lb[1:])
    sinir = np.stack([nes["x"] + sag[0] * lb, nes["y"] + sag[1] * lb, np.full(DILIM + 1, BOY / 2.0)], 1)
    orta = np.stack([nes["x"] + sag[0] * lm, nes["y"] + sag[1] * lm, np.full(DILIM, BOY / 2.0)], 1)
    return orta, sinir


def sinir_u(poz, nes):
    """Dilim sınırlarının ekran x koordinatı (DILIM+1,), arkada kalan noktalar yakın düzleme sıkıştırılır."""
    _, s = dilim_noktalari(poz, nes)
    c = kam_koord(poz, s)
    c[:, 2] = np.maximum(c[:, 2], FN)
    return piksel(c)[:, 0]


# ================================================================ 5) her kare için geometri (piksel çizmeden)
def geri_izdusum_yer(poz, u, v):
    """Piksel (u,v) ışınını poz ile zemine (z=0) indir -> dünya (x,y) ya da None."""
    s = (u - CX) / FP
    yk = -(v - CY) / FP
    if yk >= -1e-6:
        return None
    t = -KAM_H / yk                                    # ileri bileşen t, sağ bileşen s*t
    ileri, sag = t, s * t
    y = np.radians(poz[2])
    return poz[0] + sag * np.cos(y) + ileri * np.sin(y), poz[1] - sag * np.sin(y) + ileri * np.cos(y)


def geometri(pe, pt, Me, Mt, hm):
    """Bir kare: her nesne için tahmin (pe) ve gerçek (pt) pozla ölçüler, kayma ve dilim örtme kararları."""
    bilgi = []
    for nes in NESNELER:
        G = np.array([[nes["x"], nes["y"], 0.0]])
        Cg = np.array([[nes["x"], nes["y"], BOY / 2.0]])
        ce, ct = kam_koord(pe, Cg)[0], kam_koord(pt, Cg)[0]
        ge, gt = kam_koord(pe, G)[0], kam_koord(pt, G)[0]
        pce, pct = piksel(ce[None])[0], piksel(ct[None])[0]
        b = {"mesafe": float(np.hypot(nes["x"] - pt[0], nes["y"] - pt[1])), "ileri_e": float(ce[2])}
        b["gorus_e"] = bool(ce[2] > 0.1 and 0 <= pce[0] < VW and 0 <= pce[1] < VH)
        b["gorus_t"] = bool(ct[2] > 0.1 and 0 <= pct[0] < VW and 0 <= pct[1] < VH)
        oe, _ = dilim_noktalari(pe, nes)
        ot, _ = dilim_noktalari(pt, nes)
        b["gizli_e"] = hm.engelli(Me, pe[0], pe[1], oe[:, 0], oe[:, 1]) if Me is not None else np.zeros(DILIM, bool)
        b["gizli_t"] = hm.engelli(Mt, pt[0], pt[1], ot[:, 0], ot[:, 1])
        ce_d = kam_koord(pe, oe)
        pe_d = piksel(np.where(ce_d[:, 2:3] > FN, ce_d, ce_d * 0 + np.array([0, 0, FN])))
        b["dilim_ekranda"] = (ce_d[:, 2] > 0.1) & (pe_d[:, 0] >= 0) & (pe_d[:, 0] < VW)
        b["kayma_px"] = b["kayma_yuzde"] = b["kayma_cm"] = float("nan")
        if gt[2] > FN and ge[2] > FN:
            pg_e, pg_t = piksel(ge[None])[0], piksel(gt[None])[0]
            b["kayma_px"] = float(np.hypot(*(pg_e - pg_t)))
            b["kayma_yuzde"] = b["kayma_px"] / VW * 100.0
            yer = geri_izdusum_yer(pt, pg_e[0], pg_e[1])
            if yer is not None:
                b["kayma_cm"] = float(np.hypot(yer[0] - nes["x"], yer[1] - nes["y"]) * 100.0)
        b["cizilen"] = bool(b["gorus_e"] and b["mesafe"] < ENYAKIN and not np.all(b["gizli_e"]))
        bilgi.append(b)
    return bilgi


# ================================================================ 6) çizim
class Cizici:
    def __init__(self, varyant):
        self.varyant = varyant
        self.kay = Kayit(os.path.join(ko.KAYITLAR, TUR, AD))
        self.kam = Kamera.bilgiden(self.kay.bilgi)
        self.gk = self.kay.gercek_konum()
        self.n = len(self.kay)
        self.Pt = np.stack([self.gk["x_m"], self.gk["y_m"], self.gk["yon_derece"]], 1)
        z = harita_anliklari(varyant)
        self.Pe = z["P"]
        self.snap_kare = z["kare"]
        self.snap = z["harita"]
        klasor = os.path.join(ko.KAYITLAR, "sim", AD)
        self.T = cv2.imread(os.path.join(klasor, "gercek_harita.png"), cv2.IMREAD_GRAYSCALE)
        with open(os.path.join(klasor, "gercek_harita.json"), encoding="utf-8") as f:
            hj = json.load(f)
        self.hj = hj
        self.h = float(hj.get("hucre_m", 0.05))
        self.x0, self.y0 = float(hj["x0_m"]), float(hj["y0_m"])
        self.Hh, self.Wg = int(hj["yukseklik"]), int(hj["genislik"])
        self.hm = Harita(self.x0, self.y0, self.Hh, self.Wg, self.h)
        sn = hj["ev_sinirlari_m"]
        self.c0 = int(round((sn["x_min"] - self.x0) / self.h))
        self.c1 = int(round((sn["x_max"] - self.x0) / self.h))
        self.r0 = int(round(self.Hh - (sn["y_max"] - self.y0) / self.h))
        self.r1 = int(round(self.Hh - (sn["y_min"] - self.y0) / self.h))
        self.tabl = perspektif_tablolari(self.kam)
        self.mesh = [mesh_dunya(n) for n in NESNELER]
        self.kareler = np.arange(0, self.n, ADIM)
        self.geo = [geometri(self.Pe[i], self.Pt[i], self.harita_at(i), self.T, self.hm) for i in self.kareler]
        self._istatistik_hazirla()

    def harita_at(self, i):
        j = int(np.searchsorted(self.snap_kare, i, side="right")) - 1
        return self.snap[j] if j >= 0 else None

    # ---- istatistik (gösterilen karelerde)
    def _istatistik_hazirla(self):
        self.yuzde = []
        self.cm = []
        for g in self.geo:
            self.yuzde.append([b["kayma_yuzde"] for b in g if b["cizilen"] and np.isfinite(b["kayma_yuzde"])])
            self.cm.append([b["kayma_cm"] for b in g if b["cizilen"] and np.isfinite(b["kayma_cm"])])
        self.kum_orta = np.full(len(self.geo), np.nan)
        self.kum_max = np.full(len(self.geo), np.nan)
        havuz = []
        for q in range(len(self.geo)):
            havuz += self.yuzde[q]
            if havuz:
                self.kum_orta[q] = float(np.median(havuz))
                self.kum_max[q] = float(np.max(havuz))

    def ozet(self):
        """Tüm gösterilen kareler: kayma (ekran % ve yerde cm) ortanca / %90 / en çok ve örtme uyuşmazlığı."""
        yz = np.array([v for l in self.yuzde for v in l])
        cm = np.array([v for l in self.cm for v in l])
        px = np.array([b["kayma_px"] for g in self.geo for b in g if b["cizilen"] and np.isfinite(b["kayma_px"])])
        o = {"cizilen_ornek": int(len(yz)), "cizilen_kare": int(sum(1 for l in self.yuzde if l))}
        for ad_, d in (("yuzde", yz), ("cm", cm), ("px", px)):
            o[ad_] = {"ortanca": float(np.median(d)), "p90": float(np.percentile(d, 90)), "max": float(d.max())} if len(d) else None
        # örtme uyuşmazlığı: ekranda olan dilimler; yakın (<4 m) ve tüm mesafeler
        for ad_, lim in (("yakin", ENYAKIN), ("tum", 1e9)):
            top = fark = yanlis_goster = yanlis_gizle = 0
            for g in self.geo:
                for b in g:
                    if not b["gorus_e"] or b["mesafe"] >= lim:
                        continue
                    m = b["dilim_ekranda"]
                    top += int(m.sum())
                    fark += int((m & (b["gizli_e"] != b["gizli_t"])).sum())
                    yanlis_goster += int((m & ~b["gizli_e"] & b["gizli_t"]).sum())
                    yanlis_gizle += int((m & b["gizli_e"] & ~b["gizli_t"]).sum())
            o["orte_" + ad_] = {"dilim_karari": top, "farkli": fark, "yanlis_goster": yanlis_goster, "yanlis_gizle": yanlis_gizle,
                                "yuzde_farkli": 100.0 * fark / max(top, 1)}
        return o

    # ---- nesne çizimi
    def dolu_ciz(self, frame, pe, k, b):
        nes, mesh = NESNELER[k], self.mesh[k]
        kam = np.array([pe[0], pe[1], KAM_H])
        polys = []
        for V, n in mesh:
            if np.dot(n, kam - V.mean(0)) <= 0:
                continue
            c = yakin_kirp(kam_koord(pe, V))
            if c is None:
                continue
            polys.append((piksel(c), ton_renk(nes["renk"], n), float(c[:, 2].mean())))
        a = np.linspace(0, 2 * np.pi, 40, endpoint=False)
        gv = np.stack([nes["x"] + 0.22 * np.cos(a), nes["y"] + 0.22 * np.sin(a), np.zeros(40)], 1)
        cs = yakin_kirp(kam_koord(pe, gv))
        sp = piksel(cs) if cs is not None else None
        tum = [p[0] for p in polys] + ([sp] if sp is not None else [])
        if not tum:
            return
        tum = np.concatenate(tum)
        tum = np.clip(tum, -5000, 5000)
        sigma = 1.0
        if sp is not None:
            ge = np.clip(sp, -5000, 5000)
            sigma = max(1.0, 0.10 * min(np.ptp(ge[:, 0]), np.ptp(ge[:, 1])))
        pad = int(3 * sigma) + 3
        x0, y0 = int(np.floor(tum[:, 0].min())) - pad, int(np.floor(tum[:, 1].min())) - pad
        x1, y1 = int(np.ceil(tum[:, 0].max())) + pad + 1, int(np.ceil(tum[:, 1].max())) + pad + 1
        x0, y0, x1, y1 = max(0, x0), max(0, y0), min(VW, x1), min(VH, y1)
        if x1 <= x0 or y1 <= y0:
            return
        cw, ch, S = x1 - x0, y1 - y0, 3

        def buyuk(p):
            q = (np.clip(p, -5000, 5000) - [x0, y0] + 0.5) * S - 0.5
            return np.round(q * 16).astype(np.int32)
        # gölge
        sh = np.zeros((ch, cw), np.float32)
        if sp is not None:
            bm = np.zeros((ch * S, cw * S), np.uint8)
            cv2.fillPoly(bm, [buyuk(sp)], 255, shift=4)
            sh = cv2.resize(bm, (cw, ch), interpolation=cv2.INTER_AREA).astype(np.float32) / 255.0
            sh = cv2.GaussianBlur(sh, (0, 0), sigma) * 0.50
        # gövde (uzaktan yakına)
        bc = np.zeros((ch * S, cw * S, 3), np.uint8)
        bm = np.zeros((ch * S, cw * S), np.uint8)
        for p, renk, _ in sorted(polys, key=lambda t: -t[2]):
            cv2.fillPoly(bc, [buyuk(p)], renk, shift=4)
            cv2.fillPoly(bm, [buyuk(p)], 255, shift=4)
        prem = cv2.resize(bc, (cw, ch), interpolation=cv2.INTER_AREA).astype(np.float32)
        alfa = (cv2.resize(bm, (cw, ch), interpolation=cv2.INTER_AREA).astype(np.float32) / 255.0)[..., None]
        # sütun başına dilim görünürlüğü
        ub = sinir_u(pe, nes)
        idx = np.searchsorted(ub[1:DILIM], np.arange(x0, x1))
        cA = np.where(b["gizli_e"][idx], SOLUK_ALFA, 1.0).astype(np.float32)[None, :, None]
        crop = frame[y0:y1, x0:x1].astype(np.float32)
        crop = crop * (1.0 - sh[..., None] * cA)
        crop = crop * (1.0 - alfa * cA) + prem * cA
        frame[y0:y1, x0:x1] = np.clip(crop + 0.5, 0, 255).astype(np.uint8)

    def cizgi_noktalari(self, pt, k):
        pts = []
        for V, n in self.mesh[k]:
            c = yakin_kirp(kam_koord(pt, V))
            if c is not None:
                pts.append(piksel(c))
        if not pts:
            return None
        return cv2.convexHull(np.clip(np.concatenate(pts), -5000, 5000).astype(np.float32))[:, 0, :]

    def cizgiler_ciz(self, frame, pt, geo):
        halo = np.zeros((VH, VW), np.uint8)
        cizgi = np.zeros((VH, VW), np.uint8)
        for k in range(len(NESNELER)):
            hull = self.cizgi_noktalari(pt, k)
            if hull is None:
                continue
            a = 0.55 if np.all(geo[k]["gizli_t"]) else 1.0
            q = np.round(hull * 16).astype(np.int32).reshape(-1, 1, 2)
            cv2.polylines(halo, [q], True, int(255 * a), 4, cv2.LINE_AA, shift=4)
            cv2.polylines(cizgi, [q], True, int(255 * a), 2, cv2.LINE_AA, shift=4)
        f = frame.astype(np.float32)
        f *= (1.0 - 0.40 * halo.astype(np.float32) / 255.0)[..., None]
        m = (cizgi.astype(np.float32) / 255.0)[..., None]
        f = f * (1.0 - m) + 255.0 * m
        frame[:] = np.clip(f + 0.5, 0, 255).astype(np.uint8)

    # ---- sağ üst harita
    def harita_paneli(self, i, pe, pt, k_i):
        M = self.harita_at(i)
        S = OLCEK_HARITA
        if M is None:
            M = np.full((self.Hh, self.Wg), 128, np.uint8)
        E = M[self.r0:self.r1, self.c0:self.c1]
        lut = np.zeros((256, 3), np.uint8)
        lut[:] = (128, 124, 120)
        lut[255] = (238, 236, 228)
        lut[0] = (28, 30, 36)
        im = cv2.resize(lut[E], (E.shape[1] * S, E.shape[0] * S), interpolation=cv2.INTER_NEAREST)   # RGB
        im = np.ascontiguousarray(im[:, :, ::-1])                                                    # BGR

        def dp(x, y):
            return (((np.asarray(x) - self.x0) / self.h - self.c0) * S, ((self.Hh - (np.asarray(y) - self.y0) / self.h) - self.r0) * S)
        # görüş alanı kaması (gerçek yön)
        px_, py_ = dp(pt[0], pt[1])
        kama = [(px_, py_)]
        for dg in np.linspace(-HFOV / 2, HFOV / 2, 13):
            t = np.radians(pt[2] + dg)
            kama.append((px_ + np.sin(t) * 2.0 / self.h * S, py_ - np.cos(t) * 2.0 / self.h * S))
        kat = im.copy()
        cv2.fillPoly(kat, [np.array(kama, np.int32)], (255, 255, 255), cv2.LINE_AA)
        im = cv2.addWeighted(kat, 0.28, im, 0.72, 0)
        for P, renk, kal in ((self.Pe, TAHMIN_R, 2), (self.Pt, GERCEK_R, 1)):
            ux, uy = dp(P[:k_i + 1, 0], P[:k_i + 1, 1])
            pts = np.stack([ux, uy], 1).astype(np.int32).reshape(-1, 1, 2)
            if len(pts) > 1:
                cv2.polylines(im, [pts], False, bgr(renk), kal, cv2.LINE_AA)
        # nesneler
        for nes in NESNELER:
            ox, oy = dp(nes["x"], nes["y"])
            cv2.circle(im, (int(ox), int(oy)), 7, (0, 0, 0), -1, cv2.LINE_AA)
            cv2.circle(im, (int(ox), int(oy)), 5, bgr(nes["renk"]), -1, cv2.LINE_AA)
        # robot (gerçek) ve sistemin sandığı yer (turuncu halka)
        ex, ey = dp(pe[0], pe[1])
        cv2.circle(im, (int(ex), int(ey)), 6, (0, 0, 0), 2, cv2.LINE_AA)
        cv2.circle(im, (int(ex), int(ey)), 5, bgr(TAHMIN_R), 2, cv2.LINE_AA)
        t = np.radians(pt[2])
        s_, c_ = np.sin(t), np.cos(t)
        boy = 11
        tri = np.array([(px_ + s_ * boy, py_ - c_ * boy),
                        (px_ - s_ * boy * 0.7 - c_ * boy * 0.7, py_ + c_ * boy * 0.7 - s_ * boy * 0.7),
                        (px_ - s_ * boy * 0.7 + c_ * boy * 0.7, py_ + c_ * boy * 0.7 + s_ * boy * 0.7)], np.int32)
        cv2.fillPoly(im, [tri], (255, 255, 255), cv2.LINE_AA)
        cv2.polylines(im, [tri], True, (0, 0, 0), 2, cv2.LINE_AA)
        return im

    # ---- kare
    def ciz(self, q, baslik_modu):
        i = int(self.kareler[q])
        pe, pt = self.Pe[i], self.Pt[i]
        geo = self.geo[q]
        gor = perspektif(self.kay.resim(i), self.tabl)
        for k in sorted(range(len(NESNELER)), key=lambda k: -np.hypot(NESNELER[k]["x"] - pe[0], NESNELER[k]["y"] - pe[1])):
            self.dolu_ciz(gor, pe, k, geo[k])
        self.cizgiler_ciz(gor, pt, geo)
        cv = np.zeros((H, W, 3), np.uint8)
        cv[:] = bgr(ARKA)
        cv[:BASLIK_H] = bgr(BASLIK)
        cv[VY:VY + VH, VX:VX + VW] = gor
        cv[VY + VH:H, :PX] = bgr(PANEL)
        cv[VY:H, PX:] = bgr(PANEL)
        hm = self.harita_paneli(i, pe, pt, i)
        cv[VY:VY + hm.shape[0], PX:PX + hm.shape[1]] = hm
        yazi = []                                   # (x, y, metin, boyut, renk, kalin, anchor, kontur)
        # başlık
        yazi.append((20, BASLIK_H // 2, baslik_modu["baslik"], 34, YAZI, True, "lm", False))
        yazi.append((W - 20, BASLIK_H // 2, "%dx hız" % ADIM, 34, VURGU, True, "rm", False))
        yazi.append((16, VY + 24, "oyuncunun ekranı", 26, YAZI, True, "lm", True))
        yazi.append((PX + 10, VY + 20, "robotun kendi haritası", 24, (30, 30, 36), True, "lm", False))
        yazi.append((PX + PW - 10, VY + 20, "▬ gerçek yol", 22, GERCEK_R, True, "rm", True))
        yazi.append((PX + PW - 10, VY + 44, "▬ kestirilen yol", 22, TAHMIN_R, True, "rm", True))
        # açıklama (alt sol)
        ly = ALT_Y + 20
        cv2.rectangle(cv, (20, ly - 11), (56, ly + 11), (255, 255, 255), 2, cv2.LINE_AA)
        r0 = NESNELER[0]["renk"]
        cv2.rectangle(cv, (20, ly + 30 - 11), (56, ly + 30 + 11), bgr(r0), -1)
        kat = cv.copy()
        cv2.rectangle(kat, (20, ly + 60 - 11), (56, ly + 60 + 11), bgr(r0), -1)
        a = SOLUK_ALFA
        yy0, yy1 = ly + 60 - 12, ly + 60 + 12
        cv[yy0:yy1, 19:58] = (kat[yy0:yy1, 19:58].astype(np.float32) * a + cv[yy0:yy1, 19:58].astype(np.float32) * (1 - a)).astype(np.uint8)
        yazi.append((70, ly, "beyaz çizgi: olması gereken yer", 26, YAZI, False, "lm", False))
        yazi.append((70, ly + 30, "dolu: sistemin çizdiği", 26, YAZI, False, "lm", False))
        yazi.append((70, ly + 60, "soluk: haritaya göre gizlendi", 26, YAZI, False, "lm", False))
        yazi.append((20, H - 15, baslik_modu["durustluk"], 24, VURGU, True, "lm", False))
        # okuma paneli
        x0 = PX + 18
        yo = VY + hm.shape[0] + 14
        en = None
        for k, b in enumerate(geo):
            if b["cizilen"] and (en is None or b["mesafe"] < geo[en]["mesafe"]):
                en = k
        yazi.append((x0, yo + 4, "EN YAKIN NESNE", 21, SOLUK, True, "lm", False))
        if en is None:
            yazi.append((x0, yo + 38, "görünür nesne yok", 32, SOLUK, True, "lm", False))
            v_yuzde = v_px = v_cm = "–"
            renk_k = SOLUK
        else:
            b = geo[en]
            cv2.circle(cv, (x0 + 12, yo + 38), 12, (0, 0, 0), -1, cv2.LINE_AA)
            cv2.circle(cv, (x0 + 12, yo + 38), 10, bgr(NESNELER[en]["renk"]), -1, cv2.LINE_AA)
            yazi.append((x0 + 34, yo + 38, "%s · %s m" % (NESNELER[en]["ad"], virgul(b["mesafe"], 1)), 32, YAZI, True, "lm", False))
            v_yuzde = "%%%s" % virgul(b["kayma_yuzde"], 1)
            v_px = "%d px" % round(b["kayma_px"])
            v_cm = "%d cm" % round(b["kayma_cm"]) if np.isfinite(b["kayma_cm"]) else "–"
            renk_k = HATA_IYI if b["kayma_yuzde"] <= 2.0 else HATA_KOTU
        yazi.append((x0, yo + 84, "EKRANDA KAYMA", 21, SOLUK, True, "lm", False))
        yazi.append((x0 + 230, yo + 84, "YERDE KAYMA", 21, SOLUK, True, "lm", False))
        yazi.append((x0, yo + 122, v_yuzde, 44, renk_k, True, "lm", False))
        yazi.append((x0 + 230, yo + 122, v_cm, 44, renk_k, True, "lm", False))
        yazi.append((x0, yo + 158, v_px + (" · ekran genişliği" if en is not None else ""), 22, SOLUK, False, "lm", False))
        yazi.append((x0, yo + 194, "ŞİMDİYE DEK · 4 m İÇİ · ekran %", 21, SOLUK, True, "lm", False))
        if np.isfinite(self.kum_orta[q]):
            yazi.append((x0, yo + 226, "ortanca %s · en çok %s" % (virgul(self.kum_orta[q], 1), virgul(self.kum_max[q], 1)), 32, YAZI, True, "lm", False))
        else:
            yazi.append((x0, yo + 226, "ölçüm yok", 32, SOLUK, True, "lm", False))
        pil = Image.fromarray(cv[:, :, ::-1])
        d = ImageDraw.Draw(pil)
        for (x, y, m, bs, r, kl, an, kont) in yazi:
            if kont:
                koyu = sum(r) < 300
                d.text((x, y), m, font=font(bs, kl), fill=r, anchor=an, stroke_width=3 if koyu else 2,
                       stroke_fill=(255, 255, 255) if koyu else (0, 0, 0))
            else:
                d.text((x, y), m, font=font(bs, kl), fill=r, anchor=an)
        return np.asarray(pil)[:, :, ::-1].copy()

    # ---- önizleme için kare seçimi
    def onizleme_secimi(self):
        G = self.geo
        sec = {}
        # yakın: en yakın görünür nesne 0,8-1,3 m
        en = None
        for q, g in enumerate(G):
            for k, b in enumerate(g):
                if b["cizilen"] and not b["gizli_e"].any() and not b["gizli_t"].any() and 0.8 <= b["mesafe"] <= 1.3:
                    sk = abs(b["mesafe"] - 1.0) + (0.0 if b["kayma_yuzde"] > 1.0 else 0.5)
                    if en is None or sk < en[0]:
                        en = (sk, q)
        if en:
            sec["yakin"] = en[1]
        # kapı aralığı: silindir (nesne 1) bir kısmı görünür, bir kısmı örtülü; kamera koridorda
        en = None
        for q, g in enumerate(G):
            b = g[1]
            ic = int(b["gizli_e"].sum())
            if b["cizilen"] and 2 <= ic <= 5 and self.Pt[self.kareler[q]][1] < 0.45:
                sk = abs(b["mesafe"] - 2.0)
                if en is None or sk < en[0]:
                    en = (sk, q)
        if en:
            sec["kapi"] = en[1]
        # duvar arkası: bir nesne ekranda, tüm dilimler gizli (tahmin ve gerçek haritaya göre), 4 m içinde
        en = None
        for q, g in enumerate(G):
            for k, b in enumerate(g):
                if b["gorus_e"] and b["mesafe"] < ENYAKIN and b["gizli_e"].all() and b["gizli_t"].all():
                    sk = abs(b["mesafe"] - 2.5)
                    if en is None or sk < en[0]:
                        en = (sk, q)
        if en:
            sec["duvar_arkasi"] = en[1]
        # en kötü kayma
        en = None
        for q, g in enumerate(G):
            for b in g:
                if b["cizilen"] and np.isfinite(b["kayma_yuzde"]) and (en is None or b["kayma_yuzde"] > en[0]):
                    en = (b["kayma_yuzde"], q)
        if en:
            sec["en_kotu"] = en[1]
        # örtme hatası: (a) harita yanlışlıkla gizliyor, (b) harita yanlışlıkla gösteriyor; nesne 1-3 m, çok dilim
        for ad_, ist in (("orte_yanlis_gizli", lambda b: b["gizli_e"] & ~b["gizli_t"]), ("orte_yanlis_gorunur", lambda b: ~b["gizli_e"] & b["gizli_t"])):
            en = None
            for q, g in enumerate(G):
                for b in g:
                    if b["gorus_e"] and 1.0 < b["mesafe"] < 3.0:
                        f = int((b["dilim_ekranda"] & ist(b)).sum())
                        sk = f - 0.3 * b["mesafe"]
                        if f >= 5 and (en is None or sk > en[0]):
                            en = (sk, q)
            if en:
                sec[ad_] = en[1]
        return sec


def baslik_bilgisi(varyant):
    if varyant == "D":
        return {"baslik": "Sanal nesneler · konum tamamen kameradan (sim)",
                "durustluk": "Konum ve harita kameradan; yalnızca başlangıç konumu verildi"}
    return {"baslik": "Sanal nesneler · yön kameradan, hız motor komutundan (sim)",
            "durustluk": "Yön kameradan, hız motor komutundan; harita bu konumlarla kuruldu; başlangıç konumu verildi"}


def calistir(varyant, onizleme, v720, v480, sadece_onizleme, kare_sinir=None):
    t0 = time.time()
    c = Cizici(varyant)
    print("geometri hazır: %d gösterilecek kare (%.0f sn)" % (len(c.kareler), time.time() - t0), flush=True)
    o = c.ozet()
    print(json.dumps(o, ensure_ascii=False, indent=1), flush=True)
    with open(os.path.join(CIKTI, "ar_ozet_%s.json" % varyant), "w", encoding="utf-8") as f:
        json.dump(o, f, ensure_ascii=False, indent=1)
    bm = baslik_bilgisi(varyant)
    sec = c.onizleme_secimi() if onizleme else {}
    print("önizleme kareleri:", {k: int(c.kareler[q]) for k, q in sec.items()}, flush=True)
    for ad, q in sec.items():
        cv2.imwrite(os.path.join(CIKTI, "onizleme_ar_%s.png" % ad), c.ciz(q, bm))
    if sadece_onizleme:
        return c
    y720 = y480 = None
    if v720:
        y720 = cv2.VideoWriter(os.path.join(CIKTI, "ar_ev_turu_%s.mp4" % varyant), cv2.VideoWriter_fourcc(*"mp4v"), FPS, (W, H))
    if v480:
        y480 = cv2.VideoWriter(os.path.join(CIKTI, "ar_ev_turu_%s_480.mp4" % varyant), cv2.VideoWriter_fourcc(*"mp4v"), FPS, (854, 480))
    aralik = range(len(c.kareler)) if kare_sinir is None else range(*kare_sinir)
    for q in aralik:
        kare = c.ciz(q, bm)
        if y720 is not None:
            y720.write(kare)
        if y480 is not None:
            y480.write(cv2.resize(kare, (854, 480), interpolation=cv2.INTER_AREA))
        if q % 50 == 0:
            print("  çizim %s %d/%d (%.0f sn)" % (varyant, q, len(c.kareler), time.time() - t0), flush=True)
    for y in (y720, y480):
        if y is not None:
            y.release()
    for ek in ([("", W, H)] if v720 else []) + ([("_480", 854, 480)] if v480 else []):
        yol = os.path.join(CIKTI, "ar_ev_turu_%s%s.mp4" % (varyant, ek[0]))
        v = cv2.VideoCapture(yol)
        sayi = int(v.get(cv2.CAP_PROP_FRAME_COUNT))
        ok, kk = v.read()
        v.release()
        print("%s  %.2f MiB  kare %d  açılıyor: %s  boyut %s" % (yol, os.path.getsize(yol) / 2 ** 20, sayi, ok, None if kk is None else kk.shape))
    print("bitti (%.0f sn)" % (time.time() - t0))
    return c


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--varyant", default="D", choices=["A", "B", "C", "D"])
    ap.add_argument("--yalniz_onizleme", action="store_true")
    ap.add_argument("--yalniz_480", action="store_true", help="720p kopyayı yazma")
    ap.add_argument("--yalniz_harita", nargs="*", default=None, help="yalnız kestirilen harita önbelleğini üret (varyantlar)")
    ap.add_argument("--onizleme_yok", action="store_true")
    ap.add_argument("--kareler", nargs=2, type=int, default=None, help="hata ayıklama: gösterilen kare aralığı")
    a = ap.parse_args()
    os.makedirs(CIKTI, exist_ok=True)
    if a.yalniz_harita is not None:
        for v in a.yalniz_harita:
            z = harita_anliklari(v)
            csv_yol = ks.tahmin_yol(v, TUR, AD)
            cc = np.loadtxt(csv_yol, delimiter=",", skiprows=1)
            fark = np.hypot(cc[:, 2] - z["P"][:, 0], cc[:, 3] - z["P"][:, 1])
            print(v, "CSV ile fark: en çok %.4f m, yaw en çok %.3f derece, anlık sayısı %d" % (
                fark.max(), np.abs(ko.sar(cc[:, 4] - z["P"][:, 2])).max(), len(z["kare"])))
        sys.exit(0)
    calistir(a.varyant, not a.onizleme_yok, not a.yalniz_480, True, a.yalniz_onizleme, a.kareler)
