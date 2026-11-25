"""Nokta eşleşmeli anahtar kareler (Python 3.6, numpy + OpenCV): görüntüdeki belirgin noktalarla (ORB) yer tanıma.

Neden (25 Kasım ölçümü, tools/konum/hafiza_olc.py ve orb_hafiza_dene.py): eski hafıza (hafiza.py) 360 derece ufuk
şeridinin kaba silüetini saklıyordu; aynı gün işe yarıyor, ama farklı günde (sandalyeler, ışık, insanlar değişince)
doğru yer ile yanlış yerin benzerliği aynı çıkıyordu (yön hatası 60-100 derece). ORB noktaları + geometrik doğrulamayla
farklı günler arası 10 çiftin 8'inde yer 0,4 m içinde %78-98, yön ortanca 3-10 derece.

Görüntü: ham çift balık gözünden eş dikdörtgen panorama (boylam -180..180, robotun önü ortada; enlem ALT..UST,
derece başına PX piksel). Robotun kendi gövdesi (anten, direk; günden güne yeri değişiyor) teta.DinamikMaske ile
ayıklanır: robot hareket ederken değişmeyen pikseller.

Eşleşme: ORB tanımlayıcıları, Lowe oran testi, geometrik doğrulama. Düz zeminde robot yalnız döner / kayar: doğru
eşleşmelerin hepsi aşağı yukarı aynı boylam kaymasını (= yön farkı; yakındaki nesnelerin paralaksı için TOL derece pay)
ve küçük enlem farkını verir. O kaymaya uyan eşleşme sayısı = puan, kaymanın ortancası = yön farkı.
Yer: en iyi eşleşen anahtar karelerin yeri (puanla ağırlıklı); doğruluk anahtar kare aralığı mertebesinde.

Hafıza evin işaret listesine bağlıdır: ~/robot/konum_hafiza/<ev_kodu>_orb.npz
"""
import math
import os
import threading

import cv2
import numpy as np

import hafiza
import teta

PX, ALT, UST = 4, -25.0, 65.0          # panorama: 4 piksel / derece, enlem -25..65 (altı robotun gövdesi ve yakın zemin)
GEN, YUK = 360 * PX, int((UST - ALT) * PX)
ARALIK = 0.15                          # m: yeni anahtar kare için en yakın kareye uzaklık
EN_COK = 800
NOKTA = 1000                           # kare başına ORB noktası
ORAN = 0.8                             # Lowe oran testi
TOL = 6.0                              # derece: boylam kayması uyum payı (paralaks)
ENLEM_TOL = 4.0


def _da(a):
    return (a + 180.0) % 360.0 - 180.0


class OrbHafiza:
    def __init__(self, klasor=None):
        self.klasor = klasor
        self.kilit = threading.Lock()
        self.dosya = None
        self.mx, self.my = teta.es_dikdortgen_haritasi(GEN, YUK, ALT, UST)
        # gövde maskesi küçük panoramada (1 piksel / derece) her karede güncellenir
        self.kmx, self.kmy = teta.es_dikdortgen_haritasi(360, int(UST - ALT), ALT, UST)
        self.govde = teta.DinamikMaske((int(UST - ALT), 360), None, en_az=12)
        self.gorus = None                       # balık gözünün dışı (siyah) maskesi, ilk karede
        self.orb = cv2.ORB_create(nfeatures=NOKTA, scaleFactor=1.2, nlevels=6, edgeThreshold=15, patchSize=21)
        self.bf = cv2.BFMatcher(cv2.NORM_HAMMING)
        self._bos()

    def _bos(self):
        self.poz = np.zeros((0, 3), np.float32)               # x, z, yaw (radyan)
        self.kareler = []                                     # [(tanımlayıcı uint8 Nx32, boylam N, enlem N)]
        self.degisti = False

    def __len__(self):
        return len(self.poz)

    # ------------------------------------------------------------ dosya
    def ev_ayarla(self, ev):
        if not self.klasor or not ev:
            return
        d = os.path.join(self.klasor, hafiza.ev_kodu(ev) + "_orb.npz")
        if d == self.dosya:
            return
        self.kaydet()
        with self.kilit:
            self.dosya = d
            self._bos()
            if os.path.exists(d):
                try:
                    z = np.load(d)
                    self.poz = z["poz"].astype(np.float32)
                    sinir = np.concatenate([[0], np.cumsum(z["adet"])]).astype(int)
                    self.kareler = [(z["d"][a:b].copy(), z["lon"][a:b].copy(), z["lat"][a:b].copy()) for a, b in zip(sinir[:-1], sinir[1:])]
                    print("[konum] ORB hafiza yuklendi: %d anahtar kare (%s)" % (len(self.poz), os.path.basename(d)))
                except Exception as e:
                    print("[konum] ORB hafiza okunamadi:", repr(e))
                    self._bos()

    def kaydet(self):
        with self.kilit:
            if not self.dosya or not self.degisti or not len(self.poz):
                return
            os.makedirs(os.path.dirname(self.dosya), exist_ok=True)
            gecici = self.dosya + ".tmp.npz"
            np.savez(gecici, poz=self.poz, adet=np.array([len(k[0]) for k in self.kareler]),
                     d=np.concatenate([k[0] for k in self.kareler]), lon=np.concatenate([k[1] for k in self.kareler]),
                     lat=np.concatenate([k[2] for k in self.kareler]))
            os.replace(gecici, self.dosya)
            self.degisti = False

    # ------------------------------------------------------------ görüntü
    def gozle(self, ham_gri, hareketli):
        """Her karede çağrılır (ucuz): gövde maskesini günceller."""
        k = cv2.remap(ham_gri, self.kmx, self.kmy, cv2.INTER_AREA).astype(np.float32)
        self.govde.guncelle(k, hareketli)

    def ozellik(self, ham_gri):
        """-> (tanımlayıcılar, boylam derece, enlem derece) ya da None. Pahalı: yalnız gerekince."""
        p = cv2.remap(ham_gri, self.mx, self.my, cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0)
        if self.gorus is None:
            beyaz = cv2.remap(np.full(ham_gri.shape, 255, np.uint8), self.mx, self.my, cv2.INTER_NEAREST, borderMode=cv2.BORDER_CONSTANT, borderValue=0)
            self.gorus = cv2.erode((beyaz > 0).astype(np.uint8), np.ones((9, 9), np.uint8))
        g = self.govde.kullan().astype(np.uint8)
        g = cv2.erode(g, np.ones((3, 3), np.uint8))           # gövdenin çevresinde de pay
        m = cv2.resize(g, (GEN, YUK), interpolation=cv2.INTER_NEAREST) * self.gorus * 255
        kp, d = self.orb.detectAndCompute(p, m)
        if d is None or len(kp) < 20:
            return None
        lon = np.array([(k.pt[0] / GEN - 0.5) * 360.0 for k in kp], np.float32)
        lat = np.array([UST - k.pt[1] / PX for k in kp], np.float32)
        return d, lon, lat

    # ------------------------------------------------------------ ekleme
    def bos_yer(self, x, z):
        """Buraya yeni anahtar kare eklenebilir mi (özellik çıkarmadan önce sorulur)."""
        with self.kilit:
            if len(self.poz) >= EN_COK:
                return False
            return not len(self.poz) or float(np.hypot(self.poz[:, 0] - x, self.poz[:, 1] - z).min()) >= ARALIK

    def ekle(self, oz, x, z, yaw):
        if oz is None:
            return False
        with self.kilit:
            self.poz = np.vstack([self.poz, np.array([[x, z, yaw]], np.float32)])
            self.kareler.append(oz)
            self.degisti = True
            return True

    # ------------------------------------------------------------ eşleştirme
    def _bir(self, q, k):
        """-> (uyan eşleşme sayısı, kayma derece: anahtar kareden şimdiye sağa dönüş +)."""
        mm = self.bf.knnMatch(q[0], k[0], k=2)
        iyi = [p[0] for p in mm if len(p) == 2 and p[0].distance < ORAN * p[1].distance]
        if len(iyi) < 6:
            return 0, 0.0
        qi = np.array([a.queryIdx for a in iyi]); ki = np.array([a.trainIdx for a in iyi])
        dl = _da(q[1][qi] - k[1][ki]); db = q[2][qi] - k[2][ki]
        ok = np.abs(db) < ENLEM_TOL
        dl = dl[ok]
        if len(dl) < 6:
            return 0, 0.0
        # 1B RANSAC: her eşleşmenin kayması aday; 2 derecelik ızgarada yeterli
        aday = np.round(dl / 2.0) * 2.0
        en, kay = 0, 0.0
        for c in np.unique(aday):
            u = np.abs(_da(dl - c)) < TOL
            n = int(u.sum())
            if n > en:
                en, kay = n, float(np.median(dl[u]))
        return en, -kay

    def eslestir(self, oz, merkez=None, yaricap=None, en_cok=8):
        """-> [(puan, x, z, yaw_simdi), ...] iyiden kötüye. merkez/yaricap: yalnız o çevredeki en yakın en_cok kare."""
        if oz is None:
            return []
        with self.kilit:
            if not len(self.poz):
                return []
            sec = np.arange(len(self.poz))
            if merkez is not None:
                d = np.hypot(self.poz[:, 0] - merkez[0], self.poz[:, 1] - merkez[1])
                sec = sec[d < yaricap]
                sec = sec[np.argsort(d[sec])][:en_cok]
            out = []
            for j in sec:
                n, kay = self._bir(oz, self.kareler[j])
                if n:
                    p = self.poz[j]
                    out.append((n, float(p[0]), float(p[1]), float(p[2] + math.radians(kay))))
        out.sort(key=lambda a: -a[0])
        return out
