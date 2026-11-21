"""Theta S çift balık gözü modeli (Jetson ve PC; Python 3.6, numpy + OpenCV).

Kamera USB'den 1280x720 verir: sol çember ön lens, sağ çember arka lens; çemberler üstteki 640 satırda,
merkezler (320,320) ve (960,320), yarıçap 320 piksel, her lens 203 derece (eşit uzaklık: r = θ / (FOV/2) * 320).
Kamera robota yan yatık takılı. Model, tools/kayit_videoya.py'nin ffmpeg v360 çıktısıyla piksel piksel
karşılaştırılarak bulundu (14 Kasım kaydı, 3 kare, korelasyon 0,948).

Robot koordinatı: x sağ, y yukarı, z ileri (metre). Telefon (Unity) ile aynı el yönü.
"""
import math

import cv2
import numpy as np

GEN, YUK = 1280, 720
CEMBER_Y = 320.0
ON_X, ARKA_X = 320.0, 960.0
YARICAP = 320.0
FOV = math.radians(203.0)


def yon_pikseller(d):
    """Robot koordinatındaki birim ışınlar (N,3) -> ham karedeki (u, v) piksel (N,2) float32.

    Örtüşme bölgesinde ışının eksene daha yakın olduğu lens seçilir."""
    d = np.asarray(d, dtype=np.float64)
    cx, cy, cz = d[:, 1], -d[:, 0], d[:, 2]          # robot -> kamera
    on = cz >= 0
    th = np.where(on, np.arccos(np.clip(cz, -1, 1)), np.arccos(np.clip(-cz, -1, 1)))
    ph = np.arctan2(cy, cx)
    r = th / (FOV / 2) * YARICAP
    u = np.where(on, ON_X - r * np.cos(ph), ARKA_X + r * np.cos(ph))
    v = CEMBER_Y + r * np.sin(ph)
    return np.stack([u, v], -1).astype(np.float32)


def es_dikdortgen_haritasi(gen, yuk, enlem_alt=-90.0, enlem_ust=90.0):
    """Eş dikdörtgen (boylam -180..180, robotun önü ortada; enlem aralığı) için cv2.remap haritaları."""
    v, u = np.mgrid[0:yuk, 0:gen]
    lon = ((u + 0.5) / gen - 0.5) * 2 * math.pi
    lat = math.radians(enlem_ust) - (v + 0.5) / yuk * math.radians(enlem_ust - enlem_alt)
    d = np.stack([np.cos(lat) * np.sin(lon), np.sin(lat), np.cos(lat) * np.cos(lon)], -1).reshape(-1, 3)
    p = yon_pikseller(d)
    return p[:, 0].reshape(yuk, gen).copy(), p[:, 1].reshape(yuk, gen).copy()


class SanalKamera:
    """Robotun etrafına bakan sanal iğne deliği kamera (ArUco aramak için): yaw (derece, sağa +), yatay görüş, boyut.

    CV koordinatı: x sağ, y aşağı, z ileri. ray_robot = a*sag + b*asagi + ileri (a, b normalleştirilmiş)."""

    def __init__(self, yaw_derece, hfov_derece=80.0, gen=400, yuk=300, pitch_derece=0.0):
        self.yaw = math.radians(yaw_derece)
        self.gen, self.yuk = gen, yuk
        self.f = (gen / 2.0) / math.tan(math.radians(hfov_derece) / 2)
        self.cx, self.cy = gen / 2.0, yuk / 2.0
        p = math.radians(pitch_derece)
        ileri = np.array([math.sin(self.yaw) * math.cos(p), math.sin(p), math.cos(self.yaw) * math.cos(p)])
        sag = np.array([math.cos(self.yaw), 0.0, -math.sin(self.yaw)])
        asagi = np.cross(ileri, sag)     # sağ el çarpımı: ileri x sağ = aşağı (y aşağı CV)
        if asagi[1] > 0:
            asagi = -asagi
        self.R = np.stack([sag, asagi, ileri], 1)          # sütunlar: CV x, y, z -> robot
        v, u = np.mgrid[0:yuk, 0:gen]
        a = (u + 0.5 - self.cx) / self.f
        b = (v + 0.5 - self.cy) / self.f
        isin = np.stack([a, b, np.ones_like(a)], -1).reshape(-1, 3) @ self.R.T
        isin /= np.linalg.norm(isin, axis=1, keepdims=True)
        p2 = yon_pikseller(isin)
        self.mx = p2[:, 0].reshape(yuk, gen).copy()
        self.my = p2[:, 1].reshape(yuk, gen).copy()

    def goruntu(self, ham_gri):
        return cv2.remap(ham_gri, self.mx, self.my, cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0)

    def robota(self, a, b):
        """Normalleştirilmiş görüntü (a, b) -> robot koordinatında yön (birim değil)."""
        return self.R @ np.array([a, b, 1.0])


class Pusula:
    """Görsel pusula: ufuk bandının (enlem -5..+30 derece, robot gövdesinin üstü) 360 derece eş dikdörtgen şeridi
    kareden kareye faz korelasyonuyla kaydırılır; kayma = dönüş. Ölçek: 2 piksel / derece."""

    PIKSEL_DERECE = 2.0

    def __init__(self):
        self.gen = int(360 * self.PIKSEL_DERECE)
        self.yuk = 72
        self.mx, self.my = es_dikdortgen_haritasi(self.gen, self.yuk, -5.0, 31.0)
        self.onceki = None
        self.pencere = cv2.createHanningWindow((self.gen, self.yuk), cv2.CV_32F)
        # boylamda pencere istemiyoruz (şerit dairesel): yalnız dikey kenarları yumuşat
        dikey = np.hanning(self.yuk).astype(np.float32)[:, None]
        self.pencere = np.repeat(dikey, self.gen, axis=1)

    def serit(self, ham_gri):
        s = cv2.remap(ham_gri, self.mx, self.my, cv2.INTER_LINEAR).astype(np.float32)
        s = cv2.GaussianBlur(s, (0, 0), 1.2)
        return s - s.mean()

    def guncelle(self, ham_gri):
        """Dönüş (derece, sağa dönüş +) ve güven (0..1). İlk karede (0, 0)."""
        s = self.serit(ham_gri)
        if self.onceki is None:
            self.onceki = s
            return 0.0, 0.0
        (dx, dy), guven = cv2.phaseCorrelate(self.onceki, s, self.pencere)
        self.onceki = s
        # sağa dönünce sahne görüntüde sola kayar (boylam azalır): dönüş = -dx
        return -dx / self.PIKSEL_DERECE, float(guven)
