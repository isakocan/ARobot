"""Robotun hafızası: anahtar kareler (Python 3.6, numpy + OpenCV).

Robot konumundan eminken (yakında işaret görüyorken) her ARALIK metrede bir, ufuk bandının 360 derece kenar şeridini
(teta.Pusula.serit, küçültülmüş) konum ve yönüyle saklar. Sonra işaret görünmezken şimdiki şeridi saklananlarla
karşılaştırır: şerit 360 derece olduğu için robot hangi yöne bakarsa baksın eşleşir, döngüsel kayma da yön farkını
verir (hafızadaki karenin yönü + kayma = şimdiki yön). Konum en iyi eşleşen karelerin ağırlıklı ortalamasıdır
(kare aralığı kadar doğruluk); yön ise aynı yere yakın olunca dereceler mertebesinde doğrudur.

Hafıza evin işaret listesine bağlıdır (ev değişirse başka klasör): ~/robot/konum_hafiza/<ev_kodu>.npz
"""
import hashlib
import json
import math
import os
import threading

import cv2
import numpy as np

ARALIK = 0.30          # m: yeni anahtar kare için en yakın kareye uzaklık
GEN, YUK = 360, 24     # küçültülmüş şerit (1 derece / piksel)
EN_COK = 600


def ev_kodu(ev):
    """ev: id -> (x, z, aci). İşaretler cm/derece düzeyinde aynıysa aynı kod."""
    s = json.dumps(sorted([int(i), round(p[0], 1), round(p[1], 1), round(math.degrees(p[2]))] for i, p in ev.items()))
    return hashlib.sha1(s.encode()).hexdigest()[:10]


class Hafiza:
    def __init__(self, klasor=None):
        self.klasor = klasor
        self.kilit = threading.Lock()
        self.dosya = None
        self._bos()

    def _bos(self):
        self.poz = np.zeros((0, 3), np.float32)               # x, z, yaw (radyan)
        self.F = np.zeros((0, YUK, GEN // 2 + 1), np.complex64)
        self.degisti = False

    def __len__(self):
        return len(self.poz)

    # ------------------------------------------------------------ dosya
    def ev_ayarla(self, ev):
        if not self.klasor or not ev:
            return
        d = os.path.join(self.klasor, ev_kodu(ev) + ".npz")
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
                    self.F = z["F"].astype(np.complex64)
                    print("[konum] hafiza yuklendi: %d anahtar kare (%s)" % (len(self.poz), os.path.basename(d)))
                except Exception as e:
                    print("[konum] hafiza okunamadi:", repr(e))
                    self._bos()

    def kaydet(self):
        with self.kilit:
            if not self.dosya or not self.degisti:
                return
            os.makedirs(os.path.dirname(self.dosya), exist_ok=True)
            gecici = self.dosya + ".tmp.npz"
            np.savez(gecici, poz=self.poz, F=self.F)
            os.replace(gecici, self.dosya)
            self.degisti = False

    # ------------------------------------------------------------ öznitelik
    @staticmethod
    def ozellik(serit):
        """teta.Pusula.serit çıktısı (72x720 kenar şeridi) -> satır satır FFT (YUK x GEN/2+1), birim enerjili."""
        k = cv2.resize(serit, (GEN, YUK), interpolation=cv2.INTER_AREA).astype(np.float32)
        k -= k.mean(1, keepdims=True)
        n = float(np.sqrt((k * k).sum())) + 1e-6
        return np.fft.rfft(k / n, axis=1).astype(np.complex64)

    # ------------------------------------------------------------ ekleme
    def ekle(self, F, x, z, yaw):
        with self.kilit:
            if len(self.poz):
                d = np.hypot(self.poz[:, 0] - x, self.poz[:, 1] - z)
                if d.min() < ARALIK:
                    return False
            if len(self.poz) >= EN_COK:
                return False
            self.poz = np.vstack([self.poz, np.array([[x, z, yaw]], np.float32)])
            self.F = np.concatenate([self.F, F[None]], 0)
            self.degisti = True
            return True

    # ------------------------------------------------------------ eşleştirme
    def eslestir(self, F, merkez=None, yaricap=None):
        """-> [(benzerlik 0..1, x, z, yaw_simdi)] iyiden kötüye. merkez/yaricap verilirse yalnız o çevredeki kareler."""
        with self.kilit:
            if not len(self.poz):
                return []
            sec = np.arange(len(self.poz))
            if merkez is not None:
                d = np.hypot(self.poz[:, 0] - merkez[0], self.poz[:, 1] - merkez[1])
                sec = sec[d < yaricap]
                if not len(sec):
                    return []
            # her kare için döngüsel çapraz korelasyon: sum_satır IFFT(F_kare * conj(F_simdi))
            c = np.fft.irfft((self.F[sec] * np.conj(F)[None]).sum(1), n=GEN, axis=1)
            k = c.argmax(1)
            tepe = c[np.arange(len(sec)), k]
            y0 = c[np.arange(len(sec)), (k - 1) % GEN]; y2 = c[np.arange(len(sec)), (k + 1) % GEN]
            pay = y0 - 2 * tepe + y2
            ince = np.where(np.abs(pay) > 1e-9, 0.5 * (y0 - y2) / np.where(np.abs(pay) > 1e-9, pay, 1.0), 0.0)
            kay = k + ince
            kay = np.where(kay > GEN / 2, kay - GEN, kay)           # derece: kareden şimdiye sağa dönüş +
            out = []
            for j in np.argsort(-tepe)[:8]:
                p = self.poz[sec[j]]
                out.append((float(tepe[j]), float(p[0]), float(p[1]), float(p[2] + math.radians(kay[j]))))
            return out
