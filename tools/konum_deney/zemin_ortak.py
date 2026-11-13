"""Zemin sınırı deneyi: ortak geometri, girdi hazırlama, gerçek veri okuma.

Fikir: kamera zeminden h yüksekliğinde ve zemin düzse, ufkun a kadar altında görülen zemin noktası
yatay d = h / tan(a) uzaklıktadır. Her azimut için zeminin bittiği satırı bulursak "sahte lidar" taraması elde ederiz.

Ortak çıktı (her yöntem): SinirSonucu
    mesafe  (360,)  metre, azimut k = robot önünden saat yönünde k derece. NaN = bilinmiyor
    guven   (360,)  0..1
    max_menzil      bu girdi çözünürlüğünde güvenilir en uzak mesafe (m)  (bkz. guvenilir_menzil)

Girdi (Girdi): eşdikdörtgen panorama (BGR), geçersiz piksel maskesi (gövde / kapsam dışı), kamera yüksekliği.
  sim:   1536x768 doğrudan.  theta: çift balık gözü -> kamera.py ile 1280x640 eşdikdörtgen + maske.
Eşdikdörtgen sözleşmesi (kamera.py): orta sütun = ön, sütunlar saat yönünde artar, ufuk orta satır."""
import os
os.environ.setdefault("LOKY_MAX_CPU_COUNT", "4")
import sys
from dataclasses import dataclass

import cv2
import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
KOK = os.path.abspath(os.path.join(KLASOR, "..", ".."))
sys.path.insert(0, KLASOR)
from kamera import Kamera  # noqa: E402
from kayit import Kayit  # noqa: E402

KAYITLAR = os.path.join(KOK, "kayitlar")
CIKTI = os.path.join(KOK, "kayitlar", "sonuclar", "harita")
KAYIT_ADLARI = ["yerinde_donus_salon", "yerinde_donus_koridor", "yerinde_donus_yatak_odasi", "yerinde_donus_banyo",
                "yerinde_donus_mutfak", "yerinde_donus_calisma_odasi", "duz_git", "salon_turu", "ev_turu"]
N_AZ = 360
THETA_ESD = (1280, 640)          # theta karesinin eşdikdörtgene çevrildiği boyut (kamera.py ön ayarı)
HUCRE = 0.05


# ----------------------------------------------------------------------------- geometri
def satir_aci(v_kenar, yukseklik):
    """Piksel KENARI koordinatı v_kenar (0 = üst kenar, H = alt kenar) -> ufkun altındaki açı (derece, aşağı +)."""
    return np.asarray(v_kenar, float) / yukseklik * 180.0 - 90.0


def aci_satir(aci_derece, yukseklik):
    return (np.asarray(aci_derece, float) + 90.0) / 180.0 * yukseklik


def aci_mesafe(aci_derece, h):
    """Ufkun a derece altındaki zemin noktasının yatay uzaklığı: d = h / tan(a). a <= 0 -> inf."""
    a = np.radians(np.asarray(aci_derece, float))
    with np.errstate(divide="ignore"):
        d = np.where(a > 1e-9, h / np.tan(np.maximum(a, 1e-9)), np.inf)
    return d


def mesafe_aci(d, h):
    return np.degrees(np.arctan2(h, np.asarray(d, float)))


def satir_mesafe(v_kenar, yukseklik, h):
    return aci_mesafe(satir_aci(v_kenar, yukseklik), h)


def mesafe_satir(d, yukseklik, h):
    """Yatay uzaklık d (m) -> piksel kenarı satır koordinatı."""
    return aci_satir(mesafe_aci(d, h), yukseklik)


def piksel_hatasi(d, yukseklik, h, aci_piksel=None):
    """Bir pikselin (satır) mesafede yarattığı hata (m): dd = (h^2 + d^2)/h * delta,  delta = piksel açısı (rad).
    aci_piksel verilmezse 180/yukseklik derece (eşdikdörtgen satır adımı)."""
    delta = np.radians(180.0 / yukseklik if aci_piksel is None else aci_piksel)
    d = np.asarray(d, float)
    return (h * h + d * d) / h * delta


def guvenilir_menzil(yukseklik, h, hata_siniri=0.20, aci_piksel=None):
    """Bir pikselin hatası hata_siniri'ni (m) geçmeyen en uzak mesafe."""
    delta = np.radians(180.0 / yukseklik if aci_piksel is None else aci_piksel)
    v = hata_siniri * h / delta - h * h
    return float(np.sqrt(max(v, 0.0)))


# ----------------------------------------------------------------------------- girdi
@dataclass
class Girdi:
    pan: np.ndarray          # BGR eşdikdörtgen
    gecersiz: np.ndarray     # bool, True = gövde/kapsam dışı
    h: float                 # kamera yüksekliği (m)
    tur: str                 # "sim" | "theta"

    @property
    def yuk(self):
        return self.pan.shape[0]

    @property
    def gen(self):
        return self.pan.shape[1]


_MASKE = {}


def theta_maskesi(kam, esd=THETA_ESD, genisletme_px=2):
    """kamera_maske.png (balık gözü, 255 = gövde) -> eşdikdörtgen geçersiz maske (+kapsam dışı)."""
    anahtar = (esd, genisletme_px)
    if anahtar not in _MASKE:
        m = cv2.imread(os.path.join(KLASOR, "kamera_maske.png"), cv2.IMREAD_GRAYSCALE)
        me, kapsam = kam.balikgozu_to_esdikdortgen(m, esd[0], esd[1])
        g = (me > 127) | (~kapsam)
        if genisletme_px:
            g = cv2.dilate(g.astype(np.uint8), np.ones((2 * genisletme_px + 1,) * 2, np.uint8)) > 0
        _MASKE[anahtar] = g
    return _MASKE[anahtar]


class KayitGirdisi:
    """Bir kaydın sim ve theta girdilerini kare sırasına göre üretir."""

    def __init__(self, ad, tur):
        self.ad, self.tur = ad, tur
        self.kay = Kayit(os.path.join(KAYITLAR, tur, ad))
        self.h = float(self.kay.bilgi["kamera_yuksekligi_m"])
        self.kam = Kamera.bilgiden(self.kay.bilgi) if tur == "theta" else None
        self.n = self.kay.kare_sayisi

    def girdi(self, sira):
        im = self.kay.resim(sira)
        if self.tur == "sim":
            g = np.zeros(im.shape[:2], bool)
            return Girdi(im, g, self.h, "sim")
        e, _ = self.kam.balikgozu_to_esdikdortgen(im, *THETA_ESD)
        return Girdi(e, theta_maskesi(self.kam), self.h, "theta")


# ----------------------------------------------------------------------------- polar (azimut x satır) gösterim
def azimut_sutunlari(resim, n_az=N_AZ):
    """Eşdikdörtgen (H,G[,3]) -> (H,n_az[,3]) float32; sütun k = robot azimutu k derece (0 = ön, saat yönü),
    her sütun o azimut etrafındaki 360/n_az derecelik bandın alan ortalaması."""
    G = resim.shape[1]
    kay = 0.5 * G / 360.0          # bantlar tam sayı azimut merkezli olsun (yarım derece kaydır)
    M = np.float32([[1, 0, kay], [0, 1, 0]])
    r = cv2.warpAffine(resim.astype(np.float32), M, (G, resim.shape[0]), flags=cv2.INTER_LINEAR,
                       borderMode=cv2.BORDER_WRAP)
    r = cv2.resize(r, (n_az, resim.shape[0]), interpolation=cv2.INTER_AREA)
    return np.roll(r, -n_az // 2, axis=1)      # indeks j -> az = j-180  =>  k = az mod 360


def satirdan_mesafe(v_kenar, girdi_yuk, h, d_ust=60.0):
    return np.minimum(satir_mesafe(v_kenar, girdi_yuk, h), d_ust)


# ----------------------------------------------------------------------------- gerçek veri
def _csv_oku_sayisal(yol):
    a = np.loadtxt(yol, delimiter=",", ndmin=2)
    return a[:, 0].astype(int), a[:, 1:361]


def gercek_mesafe(ad, tur="gorunen_zemin", tur_dizin="sim"):
    """gercek_mesafe_<tur>.csv -> (kare_no, (N,360)). tur='gorunen_zemin' yoksa None (çağıran yedek seçer)."""
    yol = os.path.join(KAYITLAR, tur_dizin, ad, "gercek_mesafe_%s.csv" % tur)
    if not os.path.exists(yol):
        return None
    k, a = _csv_oku_sayisal(yol)
    return k, a


def gercek_tanimli_mi(ad):
    import json
    with open(os.path.join(KAYITLAR, "sim", ad, "bilgi.json"), encoding="utf-8") as f:
        return "gercek_tanimlari" in json.load(f)


def gercek_zemin_dosyasi(ad):
    """(tur_adi, kare_no, mesafe) -- gorunen_zemin varsa o, yoksa eski zemin (kutu) ve uyarı için ad 'zemin(KUTU)'."""
    r = gercek_mesafe(ad, "gorunen_zemin")
    if r is not None:
        return "gorunen_zemin", r[0], r[1]
    r = gercek_mesafe(ad, "zemin")
    return "zemin(KUTU)", r[0], r[1]


@dataclass
class SinirSonucu:
    mesafe: np.ndarray
    guven: np.ndarray
    max_menzil: float
    ek: dict = None


def poz_oku(ad):
    gk = Kayit(os.path.join(KAYITLAR, "sim", ad)).gercek_konum()
    return gk
