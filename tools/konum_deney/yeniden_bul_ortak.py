"""Yeniden bulma (kidnapped robot) deneyi: ortak yollar, veri yükleme, panorama özellik önbelleği, harita, "haritalı alan" tanımı.

Veri akışı (yalnızca sim, kamera tabanlı hat; ayarlara dokunulmaz):
  * Haritalama koşusu: theta/ev_turu. Robotun KENDİ pozları = varyant D tahmini (konum/tahmin/D_theta_ev_turu.csv), kendi haritası =
    bu pozlar + zemin taramaları (3 Hz) ile kurulan log-odds ızgarası (konum_slam.buyuk_izgara + tara_ekle).
  * Sorgu kareleri: her kayıttan taramanın bulunduğu kareler (her 5. kare = 3 Hz). Gerçek poz YALNIZCA ölçmede kullanılır.
  * Panorama özellikleri: yontem_pusula_nokta.Yontem._cikar (ORB, 3B ışınlar + 32 bayt tanımlayıcı), kare başına önbelleğe alınır.
Poz sözleşmesi: konum_ortak.py (x doğu, y kuzey, yaw kuzeyden saat yönünde derece; poz = kameranın zemin izdüşümü)."""
import json
import os
import sys
import time

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
import konum_ortak as ko  # noqa: E402

KOK = ko.KOK
KAYITLAR = os.path.join(KOK, "kayitlar")
CIKTI = os.path.join(KAYITLAR, "sonuclar", "yeniden_bul")
ORNEK = os.path.join(CIKTI, "ornek")
ONBELLEK = os.environ.get("YENIDEN_BUL_ONBELLEK") or os.path.join(
    os.environ.get("TEMP", CIKTI), "claude_yeniden_bul_onbellek")

HARITA_KAYDI = "ev_turu"          # haritalama koşusu
# kayit adi -> (klasor, tarama dosyasi, VO (komutsuz C) tahmin dosyası, D tahmin dosyası)
KAYITLAR_TUM = {}
for _ad in ["ev_turu", "salon_turu", "duz_git", "yerinde_donus_salon", "yerinde_donus_koridor", "yerinde_donus_banyo",
            "yerinde_donus_yatak_odasi", "yerinde_donus_mutfak", "yerinde_donus_calisma_odasi"]:
    KAYITLAR_TUM[_ad] = dict(
        klasor=os.path.join(KAYITLAR, "theta", _ad),
        tarama=os.path.join(KAYITLAR, "sonuclar", "harita", "tarama", "model_gorunum", "theta", _ad + ".npz"),
        vo=os.path.join(KAYITLAR, "sonuclar", "konum", "tahmin", "C_theta_%s.csv" % _ad),
        d=os.path.join(KAYITLAR, "sonuclar", "konum", "tahmin", "D_theta_%s.csv" % _ad))
for _ad in ["sinama_ters_tur", "sinama_yeni_rota", "sinama_los", "sinama_far", "sinama_duz_zemin", "sinama_hizli"]:
    KAYITLAR_TUM[_ad] = dict(
        klasor=os.path.join(KAYITLAR, "sinama", "theta", _ad),
        tarama=os.path.join(KAYITLAR, "sonuclar", "sinama", "harita", "tarama", "model_gorunum", "theta", _ad + ".npz"),
        vo=os.path.join(KAYITLAR, "sonuclar", "sinama", "konum", "tahmin", "C_theta_%s.csv" % _ad),
        d=os.path.join(KAYITLAR, "sonuclar", "sinama", "konum", "tahmin", "D_theta_%s.csv" % _ad))


def csv_poz(yol):
    """kare,t_ms,x_m,y_m,yon_derece -> dict dizileri."""
    a = np.loadtxt(yol, delimiter=",", skiprows=1, ndmin=2)
    return dict(kare=a[:, 0].astype(int), t_ms=a[:, 1], x=a[:, 2], y=a[:, 3], yaw=a[:, 4])


def tarama_oku(ad):
    z = np.load(KAYITLAR_TUM[ad]["tarama"])
    return dict(kare=z["kare"].astype(int), mesafe=z["mesafe"].astype(np.float32), guven=z["guven"].astype(np.float32),
                menzil=float(z["max_menzil"]))


def gercek_oku(ad):
    return csv_poz(os.path.join(KAYITLAR_TUM[ad]["klasor"], "gercek_konum.csv"))


def vo_oku(ad):
    return csv_poz(KAYITLAR_TUM[ad]["vo"])


# ----------------------------------------------------------------------------- panorama özellikleri
_Y = {}


def pusula_yontemi(ad=HARITA_KAYDI):
    """Donmuş M2D çözücü (varsayılan parametreler, komutsuz). Tek örnek; _cikar ve _duzlem buradan kullanılır."""
    if "y" not in _Y:
        import yontem_pusula_nokta as yp
        with open(os.path.join(KAYITLAR_TUM[ad]["klasor"], "bilgi.json"), encoding="utf-8") as f:
            bilgi = json.load(f)
        _Y["y"] = yp.Yontem(bilgi, None, komut_oncel=False)
    return _Y["y"]


def oz_cikar(kare_bgr):
    """BGR ham kare -> (ışınlar (N,3) float32, tanımlayıcı (N,32) uint8)."""
    y = pusula_yontemi()
    gri, gec = y.don(kare_bgr)
    r, d = y._cikar(gri, gec)
    return r.astype(np.float32), d


def oz_onbellek_yol(ad):
    os.makedirs(ONBELLEK, exist_ok=True)
    return os.path.join(ONBELLEK, "oz_%s.npz" % ad)


def oz_hesapla(ad, ilerleme=None):
    """Tarama karelerinin hepsi için panorama özelliklerini hesapla ve önbelleğe yaz."""
    from kayit import Kayit
    yol = oz_onbellek_yol(ad)
    if os.path.exists(yol):
        return yol
    kay = Kayit(KAYITLAR_TUM[ad]["klasor"])
    tar = tarama_oku(ad)
    kareler, sayi, R, D, ms = [], [], [], [], []
    for j, k in enumerate(tar["kare"]):
        a = time.perf_counter()
        im = kay.resim(int(k))
        r, d = oz_cikar(im)
        ms.append((time.perf_counter() - a) * 1000.0)
        kareler.append(int(k))
        sayi.append(len(r))
        R.append(r)
        D.append(d)
        if ilerleme and j % 50 == 0:
            ilerleme(ad, j, len(tar["kare"]))
    np.savez(yol, kare=np.array(kareler), sayi=np.array(sayi), R=np.concatenate(R), D=np.concatenate(D), ms=np.array(ms))
    return yol


class OzDeposu:
    """Bir kaydın kare -> (R, D) önbelleği."""

    def __init__(self, ad):
        z = np.load(oz_onbellek_yol(ad))
        self.kare = z["kare"]
        self.sira = {int(k): i for i, k in enumerate(self.kare)}
        ofs = np.concatenate([[0], np.cumsum(z["sayi"])])
        self._R, self._D, self._o = z["R"], z["D"], ofs
        self.ms = z["ms"]

    def al(self, kare):
        i = self.sira[int(kare)]
        a, b = self._o[i], self._o[i + 1]
        return self._R[a:b], self._D[a:b]


# ----------------------------------------------------------------------------- robotun kendi haritası
def robot_haritasi():
    """Haritalama koşusunun KENDİ haritası: D pozları + taramalar. Dönüş: (Izgara (büyük, log-odds), kirp bilgisi)."""
    import harita_kur as hk
    import konum_slam as ks
    a = HARITA_KAYDI
    tar = tarama_oku(a)
    d = csv_poz(KAYITLAR_TUM[a]["d"])
    sira = {int(k): i for i, k in enumerate(d["kare"])}
    iz = ks.buyuk_izgara(os.path.join(KAYITLAR, "sim", a), kenar_m=2.0)
    for j, k in enumerate(tar["kare"]):
        i = sira[int(k)]
        iz.tara_ekle(d["x"][i], d["y"][i], d["yaw"][i], tar["mesafe"][j], tar["guven"][j], tar["menzil"], guven_min=0.2)
    return iz


def gercek_harita():
    """gercek_harita.png (0 dolu, 255 boş, 128 dışarı) + HaritaIzgara (dünya <-> piksel)."""
    import cv2
    from kayit import HaritaIzgara
    kl = KAYITLAR_TUM[HARITA_KAYDI]["klasor"]
    im = cv2.imread(os.path.join(kl, "gercek_harita.png"), cv2.IMREAD_GRAYSCALE)
    with open(os.path.join(kl, "gercek_harita.json"), encoding="utf-8") as f:
        b = json.load(f)
    return HaritaIzgara(im, b)


def haritali_alan(iz, yol_xy, menzil=2.0, adim_der=2.0, yol_adim=3):
    """'Haritalı alan' (yeniden üretilebilir tanım): robotun KENDİ haritasında 'boş' bilinen (lo <= -0.4) hücrelerden, haritalama
    yolunun herhangi bir noktasından görüş hattıyla (arada bilinen boş olmayan hücre yok) menzil (2 m) içinde erişilenler.
    Dönüş: bool dizi iz.lo şeklinde."""
    bos = iz.lo <= -0.4
    cikt = np.zeros(bos.shape, bool)
    h = iz.h
    t = np.arange(0.0, menzil + 1e-9, h * 0.5)
    aci = np.radians(np.arange(0, 360, adim_der))
    sx, sy = np.sin(aci), np.cos(aci)
    for (x, y) in yol_xy[::yol_adim]:
        px = x + sx[:, None] * t[None]
        py = y + sy[:, None] * t[None]
        r, c = iz.hucre_idx(px, py)
        ok = (r >= 0) & (r < iz.Hh) & (c >= 0) & (c < iz.W)
        rr, cc = np.clip(r, 0, iz.Hh - 1), np.clip(c, 0, iz.W - 1)
        b = bos[rr, cc] & ok
        # ilk boş-olmayan hücreden sonrasını kes
        durdu = np.cumsum(~b, axis=1) > 0
        gorunen = b & ~durdu
        cikt[rr[gorunen], cc[gorunen]] = True
    return cikt


def alan_sinifi(iz, alan, x, y, yakin_m=1.0):
    """(x, y) noktaları için sınıf: 0 = haritalı alan içinde, 1 = alanın yakınında (<= yakin_m), 2 = uzak (haritalı alandan > yakin_m).
    Mesafe, haritalı alan hücrelerine en yakın hücre merkezine."""
    from scipy import ndimage as ndi
    d = ndi.distance_transform_edt(~alan) * iz.h
    r, c = iz.hucre_idx(np.asarray(x), np.asarray(y))
    r = np.clip(r, 0, iz.Hh - 1)
    c = np.clip(c, 0, iz.W - 1)
    dd = d[r, c]
    return np.where(dd <= 0.5 * iz.h, 0, np.where(dd <= yakin_m, 1, 2)), dd


def aci_fark(a, b):
    return (np.asarray(a, float) - np.asarray(b, float) + 180.0) % 360.0 - 180.0


# ----------------------------------------------------------------------------- oda adları (HouseLayout.cs ile aynı dikdörtgenler)
ODALAR = [("KORIDOR", -5.5, -0.7, 11.0, 1.4), ("YATAK ODASI", -5.5, 0.7, 3.8, 3.5), ("BANYO", -1.7, 0.7, 2.7, 3.5),
          ("MUTFAK", 1.0, 0.7, 4.5, 3.5), ("SALON", -5.5, -4.7, 7.3, 4.0), ("CALISMA ODASI", 1.8, -4.7, 3.7, 4.0)]


def oda_adi(x, y):
    for ad, x0, y0, w, h in ODALAR:
        if x0 <= x <= x0 + w and y0 <= y <= y0 + h:
            return ad
    return "DISARI"
