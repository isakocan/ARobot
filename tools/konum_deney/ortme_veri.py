"""Örtme (gizleme) deneyi için ortak veri: gerçek poz, kestirilen poz (varyant A / D), gerçek harita, robotun kendi haritası anlıkları,
canlı zemin taramaları (taze: her gösterilen kare; gerçekçi: her 5. kare + gecikme). Yalnızca sim; yeni dosya, mevcut dosyalara dokunmaz.

Kullanım (başka modüllerden):   v = ortme_veri.Veri("D")"""
import json
import os
import sys

import cv2
import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
import konum_ortak as ko  # noqa: E402
import gosterim_ar as ga  # noqa: E402

AD, TUR = ga.AD, ga.TUR
CIKTI = ga.CIKTI
ADIM = ga.ADIM
ZEMIN_NPZ = os.path.join(CIKTI, "onbellek_zemin_ev_turu_809.npz")          # her 2. kare (taze tarama)
TARAMA_NPZ = os.path.join(ko.KAYITLAR, "sonuclar", "harita", "tarama", "model_gorunum", TUR, AD + ".npz")   # her 5. kare (3 Hz)
GUVEN_MIN = 0.2                                # harita_kur.Izgara.tara_ekle ile aynı güven eşiği
GECIKME_KARE = 3                               # tarama işleme gecikmesi: 3 kare = 0,2 sn (15 Hz kayıt)
TARAMA_ARALIK = 5                              # 5 kare = 3 Hz
FPS_KAYIT = 15.0


class Veri:
    def __init__(self, varyant="D"):
        self.varyant = varyant
        klasor = os.path.join(ko.KAYITLAR, "sim", AD)
        gk = np.loadtxt(os.path.join(ko.KAYITLAR, TUR, AD, "gercek_konum.csv"), delimiter=",", skiprows=1)
        self.n = len(gk)
        self.t_sn = gk[:, 1] / 1000.0
        self.Pt = np.stack([gk[:, 2], gk[:, 3], gk[:, 4]], 1)
        z = ga.harita_anliklari(varyant)           # önbellekten
        self.Pe = z["P"]
        self.snap_kare, self.snap = z["kare"], z["harita"]
        self.T = cv2.imread(os.path.join(klasor, "gercek_harita.png"), cv2.IMREAD_GRAYSCALE)
        with open(os.path.join(klasor, "gercek_harita.json"), encoding="utf-8") as f:
            hj = json.load(f)
        self.hj = hj
        self.h = float(hj.get("hucre_m", 0.05))
        self.x0, self.y0 = float(hj["x0_m"]), float(hj["y0_m"])
        self.Hh, self.Wg = int(hj["yukseklik"]), int(hj["genislik"])
        self.hm = ga.Harita(self.x0, self.y0, self.Hh, self.Wg, self.h)
        sn = hj["ev_sinirlari_m"]
        self.c0 = int(round((sn["x_min"] - self.x0) / self.h))
        self.c1 = int(round((sn["x_max"] - self.x0) / self.h))
        self.r0 = int(round(self.Hh - (sn["y_max"] - self.y0) / self.h))
        self.r1 = int(round(self.Hh - (sn["y_min"] - self.y0) / self.h))
        # serbest zemin ve duvara uzaklık (m)
        serbest = (self.T == 255).astype(np.uint8)
        self.uzak = cv2.distanceTransform(serbest, cv2.DIST_L2, 5) * self.h       # engele (dolu ya da ev dışı) uzaklık
        self.kareler = np.arange(0, self.n, ADIM)
        # canlı taramalar
        zz = np.load(ZEMIN_NPZ)
        self.taze = {int(k): (zz["mesafe"][j], zz["guven"][j]) for j, k in enumerate(zz["kare"])}
        tt = np.load(TARAMA_NPZ)
        self.menzil = float(tt["max_menzil"])
        self.seyrek = {int(k): (tt["mesafe"][j], tt["guven"][j]) for j, k in enumerate(tt["kare"])}
        self.seyrek_kareler = np.array(sorted(self.seyrek))

    # ---------------------------------------------------------------- yardımcılar
    def harita_at(self, i):
        j = int(np.searchsorted(self.snap_kare, i, side="right")) - 1
        return self.snap[j] if j >= 0 else None

    def hucre(self, x, y):
        c = np.floor((np.asarray(x) - self.x0) / self.h).astype(int)
        r = (self.Hh - 1 - np.floor((np.asarray(y) - self.y0) / self.h)).astype(int)
        return r, c

    def serbest_mi(self, x, y, pay=0.0):
        """Gerçek haritada (x,y) serbest ve engele en az pay (m) uzak mı."""
        r, c = self.hucre(x, y)
        ok = (r >= 0) & (r < self.Hh) & (c >= 0) & (c < self.Wg)
        out = np.zeros(np.shape(x), bool)
        rr, cc = np.clip(r, 0, self.Hh - 1), np.clip(c, 0, self.Wg - 1)
        out = ok & (self.T[rr, cc] == 255) & (self.uzak[rr, cc] >= pay)
        return out

    def tarama_taze(self, i):
        """Kare i (çift) için o karenin kendi taraması: (mesafe(360), güven(360), tarama_kare)."""
        i = int(i) - int(i) % 2
        m, g = self.taze[i]
        return m, g, i

    def tarama_gercekci(self, i):
        """Kare i için 3 Hz, gecikmeli tarama: tarama kare k (5'in katı) ancak k + GECIKME_KARE <= i olduğunda elde. Yoksa None."""
        j = np.searchsorted(self.seyrek_kareler, i - GECIKME_KARE, side="right") - 1
        if j < 0:
            return None
        k = int(self.seyrek_kareler[j])
        m, g = self.seyrek[k]
        return m, g, k
