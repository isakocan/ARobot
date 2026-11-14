"""Zemin sınırı taramalarından METRİK öteleme: iki taramanın (3 Hz) olabilirlik alanı eşleştirmesi, zemin dokusu GEREKMEZ.

Fikir: zemin taraması (360 azimutta, görülebilir zeminin bittiği yerin mesafesi) duvar/mobilya ayaklarının ZEMİNLE KESİŞİM çizgisidir; aydınlık
koşuluna ve zemin dokusuna bağlı olmayan (SegFormer loş ışıkta da çalışır) bir şekil verir. Önceki taramanın noktalarından olabilirlik alanı
(en yakın noktaya uzaklık -> exp(-d^2/2s^2)) kurulur; şimdiki tarama yaw farkı (M2D) kadar döndürülür ve (sag, ileri) öteleme ızgarasında
(1 cm adım, +-pencere) taranır: skor(t) = toplam alan değeri. Konum belirsizliği SOFT-ARGMAX ile skor yüzeyinden çıkar: koridorda
eksen boyunca yüzey düzdür -> o yönün varyansı büyük, dar yönde küçük (kaynak kaynaştırmada doğal ağırlık).
Dönüş: t (önceki tarama çerçevesinde (sag, ileri) m), C (2x2 kovaryans), n nokta, ic = alanda değeri > 0,5 olan nokta sayısı, skor.
Sözleşme konum_bev.py ile aynı: p_onceki = R(delta) p_simdi + t, R = [[cos, sin], [-sin, cos]], delta = yaw_simdi - yaw_onceki (derece)."""
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))


def rot(delta_der):
    c, s = np.cos(np.radians(delta_der)), np.sin(np.radians(delta_der))
    return np.array([[c, s], [-s, c]])


class EsleParam:
    def __init__(self, **kw):
        self.res = 0.02              # m, alan hücresi
        self.sigma = 0.06            # m
        self.d_kes = 0.25            # m
        self.pencere = 0.30          # m, arama yarı genişliği (merkez çevresinde)
        self.adim = 0.01             # m
        self.min_nokta = 60
        self.min_mesafe = 0.30       # gövde yakını atılır
        self.guven_min = 0.2
        self.T = 2.0                 # soft-argmax sıcaklığı (skor birimi = nokta sayısı)
        self.kov_taban = 0.012       # m, kovaryans tabanı (std)
        self.kov_olcek = 1.0         # kalibrasyon çarpanı (varyansa)
        self.__dict__.update(kw)


def noktalar(mesafe, guven, menzil, p):
    k = np.arange(360)
    m = np.asarray(mesafe, float)
    ok = np.isfinite(m) & (m >= p.min_mesafe) & (m <= menzil) & (np.asarray(guven, float) >= p.guven_min)
    a = np.radians(k[ok])
    return np.stack([m[ok] * np.sin(a), m[ok] * np.cos(a)], 1)


class Alan:
    """Bir taramanın olabilirlik alanı (kareli ızgara, merkez = kamera)."""

    def __init__(self, pts, menzil, p):
        self.p = p
        self.R = menzil + p.pencere + 0.35
        n = int(2 * self.R / p.res)
        self.n = n
        im = np.full((n, n), 255, np.uint8)
        ix = np.clip(((pts[:, 0] + self.R) / p.res).astype(int), 0, n - 1)
        iy = np.clip(((self.R - pts[:, 1]) / p.res).astype(int), 0, n - 1)
        im[iy, ix] = 0
        d = cv2.distanceTransform(im, cv2.DIST_L2, 5) * p.res
        self.f = np.where(d < p.d_kes, np.exp(-0.5 * (d / p.sigma) ** 2), 0.0).astype(np.float32)
        self.pts = pts

    def ornek(self, x, y):
        ix = np.rint((x + self.R) / self.p.res - 0.5).astype(np.int64)
        iy = np.rint((self.R - y) / self.p.res - 0.5).astype(np.int64)
        ok = (ix >= 0) & (ix < self.n) & (iy >= 0) & (iy < self.n)
        return np.where(ok, self.f[np.clip(iy, 0, self.n - 1), np.clip(ix, 0, self.n - 1)], 0.0)


def esle(alan_onceki, pts_simdi, delta_der, merkez=(0.0, 0.0), p=None, pencere=None):
    """Şimdiki tarama noktalarını (sag, ileri) önceki taramanın alanına eşler. merkez: aramanın merkezi (önceki çerçevede öteleme tahmini)."""
    p = p or alan_onceki.p
    pen = p.pencere if pencere is None else pencere
    if len(pts_simdi) < p.min_nokta or len(alan_onceki.pts) < p.min_nokta:
        return None
    q = pts_simdi @ rot(delta_der).T                                   # R p_simdi
    ofs = np.arange(-pen, pen + 1e-9, p.adim)
    tx = merkez[0] + ofs
    ty = merkez[1] + ofs
    S = np.empty((len(ty), len(tx)), np.float32)
    for j, y0 in enumerate(ty):
        X = q[None, :, 0] + tx[:, None]
        Y = q[None, :, 1] + y0
        S[j] = alan_onceki.ornek(X, Y).sum(1)
    j, i = np.unravel_index(int(np.argmax(S)), S.shape)
    smax = float(S[j, i])
    w = np.exp((S - smax) / p.T)
    w /= w.sum()
    TX, TY = np.meshgrid(tx, ty)
    mx, my = float((w * TX).sum()), float((w * TY).sum())
    dx, dy = TX - mx, TY - my
    C = np.array([[(w * dx * dx).sum(), (w * dx * dy).sum()], [(w * dx * dy).sum(), (w * dy * dy).sum()]]) * p.kov_olcek
    C = C + np.eye(2) * p.kov_taban ** 2
    ic = int((alan_onceki.ornek(q[:, 0] + tx[i], q[:, 1] + ty[j]) > 0.5).sum())
    return {"t": np.array([float(tx[i]), float(ty[j])]), "tsoft": np.array([mx, my]), "C": C, "n": len(pts_simdi), "ic": ic,
            "skor": smax, "kenarda": bool(i in (0, len(tx) - 1) or j in (0, len(ty) - 1))}
