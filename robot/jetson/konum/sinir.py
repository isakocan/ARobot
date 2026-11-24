"""Zemin sınırı: robotun 360 karesinden "zemin nerede bitiyor" kanıtı ve telefon haritasından beklenen sınır
(Jetson ve PC; Python 3.6, numpy + OpenCV).

Fikir (25 Kasım): kamera yerden 17 cm ve zemin düz, bu yüzden zemindeki bir noktanın ufkun ne kadar altında
göründüğü uzaklığını verir (r = h / tan(açı)). 360 kare tek seferde her yönü görür; bu, LIDAR'ın mesafe halkası
gibi kullanılır. Telefonun taradığı 2B haritada (açık / engel hücreleri) aynı yönlere ışın atılınca "robot şurada
olsaydı her yönde zemin kaç metrede biterdi" bulunur. Görüntü bu beklentiyle ne kadar uyuşuyorsa o konum / yön o kadar
olasıdır. Robotun gezmediği yerler için de beklenti hazır: anahtar kare toplamaya gerek yok.

Kutupsal kuşbakışı: sütun = robota göre yön (0 = ön, saat yönü, 1 derece), satır = zemindeki uzaklık (logaritmik,
R0..R1 m). Zemin kanıtı: robotun yakınındaki (0,3-0,7 m) zemine renk + doku benzerliği, -1..1. Robotun kendi
gövdesi (direk, kablo, taban) sinir_govde.png ile dışarıda (kayıtta zamanla değişmeyen pikseller).
"""
import json
import math
import os

import cv2
import numpy as np

YUK = 0.17          # m: mercek yerden
R0, R1 = 0.20, 3.0  # m: kutupsal görüntünün uzaklık aralığı
NB = 360            # yön (1 derece)
NR = 160            # uzaklık satırı
OTE = 1.35          # sınırın "ötesi" penceresi: sınırdan bu oran uzağa kadar zemin olmamalı
KIRP = -2.0         # sütun puanının alt sınırı: taşınmış sandalye gibi tek tük uyuşmazlık bütün puanı batırmasın
BILINMEYEN_W = 0.5  # haritada taranmamış yerde biten sütunun ağırlığı (engelde biten = 1)

_BURASI = os.path.dirname(os.path.abspath(__file__))


def uzakliklar():
    return R0 * (R1 / R0) ** (np.arange(NR) / (NR - 1.0))


def satir(r):
    """Uzaklık (m) -> kutupsal görüntü satırı (float)."""
    return np.log(np.clip(r, R0, R1) / R0) / np.log(R1 / R0) * (NR - 1)


def kutupsal_harita(teta):
    r = uzakliklar()
    b = np.radians(np.arange(NB) + 0.5)
    rr, bb = np.meshgrid(r, b, indexing="ij")
    d = np.stack([rr * np.sin(bb), -np.full_like(rr, YUK), rr * np.cos(bb)], -1).reshape(-1, 3)
    d /= np.linalg.norm(d, axis=1, keepdims=True)
    p = teta.yon_pikseller(d)
    return p[:, 0].reshape(NR, NB).copy(), p[:, 1].reshape(NR, NB).copy()


def govde_maskesi():
    """True = robotun kendi gövdesi (kullanma). Dosya yoksa yalnız en yakın satırlar."""
    m = cv2.imread(os.path.join(_BURASI, "sinir_govde.png"), cv2.IMREAD_GRAYSCALE)
    if m is not None and m.shape == (NR, NB):
        return m > 127
    g = np.zeros((NR, NB), bool); g[:int(satir(0.3))] = True
    return g


class Harita:
    """Telefonun ev haritası (zemin.json, ev koordinatı, 5 cm hücre): 0 bilinmiyor, 1 açık, 2 engel."""

    V = 0.05

    def __init__(self, zemin_json):
        z = json.load(open(zemin_json)) if isinstance(zemin_json, str) else zemin_json
        a = np.array(z["acik"], int).reshape(-1, 2); e = np.array(z["engel"], int).reshape(-1, 2)
        s = np.array(z.get("silinen", []), int).reshape(-1, 2)
        allc = np.vstack([a, e]) if len(e) else a
        self.o = allc.min(0) - 2
        W, H = allc.max(0) - self.o + 3
        self.g = np.zeros((H, W), np.uint8)
        if len(e):
            self.g[e[:, 1] - self.o[1], e[:, 0] - self.o[0]] = 2
        self.g[a[:, 1] - self.o[1], a[:, 0] - self.o[0]] = 1
        if len(s):
            ok = (s[:, 0] >= self.o[0]) & (s[:, 1] >= self.o[1]) & (s[:, 0] - self.o[0] < W) & (s[:, 1] - self.o[1] < H)
            s = s[ok]; self.g[s[:, 1] - self.o[1], s[:, 0] - self.o[0]] = 0
        self._profil = {}

    def hucre(self, x, z):
        i = np.floor(np.asarray(x, float) / self.V).astype(int) - self.o[0]
        j = np.floor(np.asarray(z, float) / self.V).astype(int) - self.o[1]
        ok = (i >= 0) & (j >= 0) & (i < self.g.shape[1]) & (j < self.g.shape[0])
        out = np.zeros(np.shape(i), np.uint8)
        out[ok] = self.g[j[ok], i[ok]]
        return out

    def acik_merkezler(self):
        j, i = np.nonzero(self.g == 1)
        return np.stack([(i + self.o[0] + 0.5) * self.V, (j + self.o[1] + 0.5) * self.V], 1)

    def profil(self, x, z, adim=0.025):
        """Dünya yönlerinde (0 = +z, saat yönü, 1 derece) beklenen sınır: (satır [NB] int, engel mi [NB]).
        Bilinmeyen hücre, açık görüldükten sonra sınır sayılır (taranmamış yer, ama zemin bilinmiyor: yarım ağırlık).
        Hücre merkezine yuvarlanıp önbelleklenir."""
        ci, cj = int(math.floor(x / self.V)), int(math.floor(z / self.V))
        k = (ci, cj)
        if k in self._profil:
            return self._profil[k]
        x, z = (ci + 0.5) * self.V, (cj + 0.5) * self.V
        b = np.radians(np.arange(NB) + 0.5)
        r = np.arange(adim, R1 + adim, adim)
        h = self.hucre(x + r[None, :] * np.sin(b)[:, None], z + r[None, :] * np.cos(b)[:, None])
        acik_once = np.maximum.accumulate(h == 1, axis=1)
        onceki = np.zeros_like(acik_once); onceki[:, 1:] = acik_once[:, :-1]
        dur = (h == 2) | ((h == 0) & onceki)
        var = dur.any(1); kk = np.argmax(dur, 1)
        out = np.where(var, r[kk], R1)
        eng = var & (h[np.arange(NB), kk] == 2)
        sonuc = (np.clip(np.round(satir(out)).astype(int), 0, NR), eng)
        if len(self._profil) < 200000:
            self._profil[k] = sonuc
        return sonuc


class Sinir:
    """Kare -> puan tablosu; konum / yön adaylarının puanı."""

    def __init__(self, teta):
        self.mx, self.my = kutupsal_harita(teta)
        self.govde = govde_maskesi()
        r = uzakliklar()
        self.ref = (~self.govde) & ((r > 0.3) & (r < 0.7))[:, None]
        self.k0 = int(math.ceil(satir(0.25)))
        self.ote = int(round(math.log(OTE) / math.log(R1 / R0) * (NR - 1)))
        self.G = None
        self.son_kutup = None

    def kutup(self, bgr):
        return cv2.remap(bgr, self.mx, self.my, cv2.INTER_LINEAR)

    def kanit(self, p):
        """Kutupsal BGR -> zemin kanıtı [-1, 1]; gövde 0."""
        lab = cv2.cvtColor(p, cv2.COLOR_BGR2LAB).astype(np.float32)
        L = lab[..., 0]
        m = cv2.blur(L, (5, 5)); t = np.sqrt(np.maximum(cv2.blur(L * L, (5, 5)) - m * m, 0))
        F = np.dstack([cv2.blur(lab, (5, 5)), t])
        X = F[self.ref]
        med = np.median(X, 0); mad = np.median(np.abs(X - med), 0) * 1.4826 + np.array([2, 1.5, 1.5, 1.5], np.float32)
        d = (((F - med) / mad) ** 2).sum(-1)
        e = 2 * np.exp(-d / 8) - 1
        e[self.govde] = 0
        return e

    def hazirla(self, bgr):
        """Kareden puan tablosu G[k, b]: robot yönü b'de sınır k. satırdaysa sütun puanı
        = (yakın kısım zemin mi, ortalama kanıt) - (sınırın hemen ötesi zemin mi)."""
        p = self.kutup(bgr); self.son_kutup = p
        e = self.kanit(p)
        C = np.vstack([np.zeros((1, NB)), np.cumsum(e, 0)])
        W = np.vstack([np.zeros((1, NB)), np.cumsum(e != 0, 0)])
        k = np.arange(NR + 1); k0 = self.k0
        k2 = np.minimum(NR, k + self.ote)
        ic = (C[k] - C[k0]) / np.maximum(W[k] - W[k0], 1)
        dis = (C[k2] - C[k]) / np.maximum(W[k2] - W[k], 1)
        self.G = (np.where(k[:, None] > k0, ic, 0) - np.where((k2 > k)[:, None], dis, 0)).astype(np.float32)
        self.gecerli = W[NR] > 10          # sütunda yeterince gövde dışı piksel var
        return self.G

    def puan(self, H, x, z, yaw):
        """Adaylar (dizi) için ortalama sütun puanı (-2..2). yaw radyan."""
        x = np.atleast_1d(x); z = np.atleast_1d(z); yaw = np.atleast_1d(yaw)
        b = np.arange(NB)
        out = np.empty(len(x), np.float32)
        gec = self.gecerli
        for n in range(len(x)):
            kr, eng = H.profil(x[n], z[n])
            s = int(round(math.degrees(yaw[n]))) % NB
            idx = (b + s) % NB                      # robot yönü b -> dünya yönü yaw + b
            w = np.where(eng[idx], 1.0, BILINMEYEN_W) * gec
            out[n] = (np.maximum(self.G[kr[idx], b], KIRP) * w).sum() / max(w.sum(), 1.0)
        return out

    def yon_puanlari(self, H, x, z):
        """Tek konum için 360 yönün hepsi (1 derece): [NB]."""
        kr, eng = H.profil(x, z)
        b = np.arange(NB)
        idx = (b[None, :] + b[:, None]) % NB
        w = np.where(eng[idx], 1.0, BILINMEYEN_W) * self.gecerli[None, :]
        return (np.maximum(self.G[kr[idx], b[None, :]], KIRP) * w).sum(1) / np.maximum(w.sum(1), 1.0)
