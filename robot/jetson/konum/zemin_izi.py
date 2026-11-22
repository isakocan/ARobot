"""Zeminden hareket ölçümü (Jetson ve PC; Python 3.6, numpy + OpenCV).

360 karenin alt yarısından robotun çevresindeki zeminin kuşbakışı görüntüsü çıkarılır (kamera yerden YUKSEKLIK m,
1 piksel = 1 cm). Zemindeki doku noktaları (halı, derz, leke) ardışık iki karede izlenir (Lucas-Kanade), noktaların
ortak hareketinden (RANSAC ile dönme + kayma) robotun o adımdaki ileri / yan hareketi metre olarak, dönüşü derece
olarak bulunur. Kamera yüksekliği bilindiği için ölçek doğrudan metredir (tek kamerada ölçek sorunu yok).

21 Kasım kaydında (halı): düz gidişte 0,2 sn'de 6-8 cm, dönüş pusulayla 1 derece içinde; çok hızlı dönüşte
(> ~20 derece/adım) nokta kaybolur, o zaman sonuç verilmez (pusula + motor kullanılır).
Robotun kendi gövdesi (kamera direği, yazı) zemin_maske.png ile dışarıda tutulur (kayıtta zamanla değişmeyen pikseller).
"""
import math
import os

import cv2
import numpy as np

import teta

YUKSEKLIK = 0.17      # m: mercek yerden (CLAUDE.md / hafıza: ~17 cm)
R = 1.0               # m: kuşbakışı görüntünün yarı genişliği
N = 200               # piksel (1 cm)
PX = N / (2 * R)
EN_AZ_NOKTA = 15


class ZeminIzi:
    def __init__(self):
        v, u = np.mgrid[0:N, 0:N]
        x = (u + 0.5) / N * 2 * R - R
        z = R - (v + 0.5) / N * 2 * R
        d = np.stack([x, -np.full_like(x, YUKSEKLIK), z], -1).reshape(-1, 3)
        d /= np.linalg.norm(d, axis=1, keepdims=True)
        p = teta.yon_pikseller(d)
        self.mx = p[:, 0].reshape(N, N).copy()
        self.my = p[:, 1].reshape(N, N).copy()
        r = np.hypot(x, z)
        mk = cv2.imread(os.path.join(os.path.dirname(os.path.abspath(__file__)), "zemin_maske.png"), cv2.IMREAD_GRAYSCALE)
        self.maske = ((mk > 127) if mk is not None and mk.shape == (N, N) else (r > 0.3)) & (r < 0.9)
        self.maske = self.maske.astype(np.uint8)
        self.onceki = None
        self.son = None            # hata ayıklama: (görüntü, noktalar, yeni yerler, iç nokta maskesi)

    def kus(self, ham_gri):
        return cv2.remap(ham_gri, self.mx, self.my, cv2.INTER_LINEAR)

    def guncelle(self, ham_gri):
        """-> (ileri_m, sag_m, don_derece (sağa +), iç nokta sayısı) ya da None (ilk kare / yeterli nokta yok)."""
        b = self.kus(ham_gri)
        a, self.onceki = self.onceki, b
        if a is None:
            return None
        pts = cv2.goodFeaturesToTrack(a, 300, 0.01, 5, mask=self.maske)
        if pts is None or len(pts) < EN_AZ_NOKTA:
            self.son = (b, None, None, None)
            return None
        q, st, _ = cv2.calcOpticalFlowPyrLK(a, b, pts, None, winSize=(21, 21), maxLevel=3)
        ok = st.ravel() == 1
        if ok.sum() < EN_AZ_NOKTA:
            self.son = (b, pts, None, None)
            return None
        M, ic = cv2.estimateAffinePartial2D(pts[ok], q[ok], method=cv2.RANSAC, ransacReprojThreshold=1.5)
        if M is None:
            self.son = (b, pts[ok], q[ok], None)
            return None
        n = int(ic.sum())
        self.son = (b, pts[ok], q[ok], ic.ravel().astype(bool))
        if n < EN_AZ_NOKTA:
            return None
        # görüntüde zemin saat yönünün tersine dönerse robot sağa dönmüştür
        don = -math.degrees(math.atan2(M[1, 0], M[0, 0]))
        c = np.array([N / 2 - 0.5, N / 2 - 0.5])
        sx, sy = M[:, :2] @ c + M[:, 2] - c
        return float(sy) / PX, -float(sx) / PX, float(don), n

    def hata_jpeg(self):
        """Dev modu için: kuşbakışı zemin + izlenen noktalar (yeşil: ortak harekete uyan, kırmızı: uymayan)."""
        if self.son is None:
            return None
        b, p, q, ic = self.son
        g = cv2.cvtColor(b, cv2.COLOR_GRAY2BGR)
        g[self.maske == 0] //= 3
        if p is not None and q is not None:
            for i in range(len(p)):
                iyi = ic is not None and ic[i]
                x0, y0 = p[i].ravel(); x1, y1 = q[i].ravel()
                renk = (60, 220, 90) if iyi else (60, 60, 230)
                cv2.line(g, (int(x0), int(y0)), (int(x1), int(y1)), renk, 1)
                cv2.circle(g, (int(x1), int(y1)), 2, renk, -1)
        g = cv2.resize(g, (N * 2, N * 2), interpolation=cv2.INTER_NEAREST)
        ok, j = cv2.imencode(".jpg", g, [cv2.IMWRITE_JPEG_QUALITY, 70])
        return j.tobytes() if ok else None
