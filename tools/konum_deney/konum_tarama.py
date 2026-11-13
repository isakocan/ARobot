"""Tarama -> harita eşleştirme (korelatif tarama eşleştirme, Olson 2009 tarzı; Hector SLAM gibi harita tabanlı, kare-kare değil).

Girdi: doluluk ızgarası (harita_kur.Izgara, log-odds), tahmin edilen poz (x, y, yaw), tarama (360 mesafe + güven, güvenilir menzil).
Yöntem:
  1. Izgaranın tahmin çevresindeki penceresinden DOLU hücreler (lo >= dolu_esik) alınır; en yakın dolu hücreye uzaklık dönüşümü ile
     "olabilirlik alanı" f = exp(-d^2 / 2 sigma^2) (d > d_kes için 0: haritada olmayan yere düşen nokta hiçbir adayı kayırmaz).
  2. Tarama noktaları (yalnız güvenli ve güvenilir menzil içindeki sınır noktaları; gövde yakını atılır) kamera çerçevesinde (sag, ileri).
  3. Kaba arama: yaw ± ay_yaw (1 derece adım) x konum ± ay_xy (1 hücre = 5 cm adım); skor = toplam f - öncül cezası.
     Öncül (tahmin etrafında Gaussian): koridor gibi eksen boyunca belirsiz yerlerde tahmin kazanır.
  4. İnce arama: en iyi çevresinde 1,25 cm / 0,25 derece adımla çift doğrusal aradeğer.
  5. Güven: en iyi skorun ortalama f değeri (nokta başına) ve AYIRT EDİCİLİK = en iyi skor ile "en iyiden >= ayirt_m uzak en iyi aday"
     arasındaki fark (nokta sayısına bölünmüş). Düşükse düzeltme uygulanmaz (None) ve tahmine güvenilir.
Dönüş: dict(poz=(x,y,yaw), ort_f, ayirt, n, skor) ya da None."""
import os
import sys

import numpy as np
from scipy import ndimage as ndi

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import konum_ortak as ko  # noqa: E402


class EsleParam:
    def __init__(self, **kw):
        self.dolu_esik = 0.85           # log-odds: en az ~2 net "dolu" kanıtı
        self.sigma = 0.07               # m, olabilirlik alanı genişliği
        self.d_kes = 0.25               # m
        self.ay_xy = 0.30               # m (kaba pencere yarı genişliği)
        self.ay_yaw = 4.0               # derece
        self.sigma_xy = 0.10            # m, öncül
        self.sigma_yaw = 1.5            # derece, öncül
        self.kazanc_xy = 0.15           # kabul edilen eşleşmenin konuma KARIŞMA oranı (1 = tam atla)
        self.kazanc_yaw = 0.05          # yaw için (M2D yaw'ı zaten ~1 derece doğru: haritadan yalnız yavaş düzeltme)
        self.oncul_agirlik = 0.04       # öncül cezasının nokta sayısına oranı (n * bu * (...)/2)
        self.menzil = None              # m, kullanılacak en uzak nokta (None: taramanın güvenilir menzili)
        self.guven_min = 0.2
        self.min_mesafe = 0.30          # gövde maskesi içi
        self.min_nokta = 60
        self.min_dolu_hucre = 40
        self.min_ort_f = 0.30
        self.min_ayirt = 0.0
        self.ayirt_m = 0.30
        self.gecikme_tarama = 60        # eşleştirme haritasına tarama, bu kadar tarama (3 Hz) gecikmeyle girer: yakın geçmişle (kendi kendine tutarlı) eşleşmeyi önler
        self.pencere_m = 5.0            # olabilirlik alanı penceresi yarı boyutu
        self.__dict__.update(kw)


def tarama_noktalari(mesafe, guven, menzil, p):
    """360 mesafe -> kamera çerçevesinde (sag, ileri) nokta dizisi (N,2) ve azimut."""
    mesafe = np.asarray(mesafe, float)
    guven = np.asarray(guven, float)
    m = p.menzil or menzil
    k = np.arange(360)
    ok = np.isfinite(mesafe) & (mesafe >= p.min_mesafe) & (mesafe <= m) & (guven >= p.guven_min)
    a = np.radians(k[ok])
    return np.stack([mesafe[ok] * np.sin(a), mesafe[ok] * np.cos(a)], 1)


def olabilirlik_alani(iz, merkez_xy, p):
    """Pencere içindeki alan. Dönüş: (alan float32 (H,W), x0, y_ust, h): hücre (r,c) merkezi = (x0+(c+.5)h, y_ust-(r+.5)h)."""
    h = iz.h
    cx, cy = merkez_xy
    r0, c0 = iz.hucre_idx(np.array(cx - p.pencere_m), np.array(cy + p.pencere_m))
    r1, c1 = iz.hucre_idx(np.array(cx + p.pencere_m), np.array(cy - p.pencere_m))
    r0, c0 = int(max(r0, 0)), int(max(c0, 0))
    r1, c1 = int(min(r1 + 1, iz.Hh)), int(min(c1 + 1, iz.W))
    if r1 <= r0 or c1 <= c0:
        return None
    lo = iz.lo[r0:r1, c0:c1]
    dolu = lo >= p.dolu_esik
    n_dolu = int(dolu.sum())
    if n_dolu < p.min_dolu_hucre:
        return None
    d = ndi.distance_transform_edt(~dolu) * h
    f = np.where(d < p.d_kes, np.exp(-0.5 * (d / p.sigma) ** 2), 0.0).astype(np.float32)
    x0 = iz.x0 + c0 * h
    y_ust = iz.y0 + (iz.Hh - r0) * h
    return f, x0, y_ust, h, n_dolu


def _bilin(f, c, r):
    """Hücre merkezi koordinatlı (c, r) için çift doğrusal örnek (sınır dışı 0)."""
    H, W = f.shape
    c0 = np.floor(c).astype(np.int64)
    r0 = np.floor(r).astype(np.int64)
    fc = c - c0
    fr = r - r0
    out = np.zeros(c.shape, np.float32)
    for dr, dc, w in ((0, 0, (1 - fr) * (1 - fc)), (0, 1, (1 - fr) * fc), (1, 0, fr * (1 - fc)), (1, 1, fr * fc)):
        rr, cc = r0 + dr, c0 + dc
        ic = (rr >= 0) & (rr < H) & (cc >= 0) & (cc < W)
        out += np.where(ic, f[np.clip(rr, 0, H - 1), np.clip(cc, 0, W - 1)], 0.0) * w
    return out


def eslestir(iz, poz, mesafe, guven, menzil, p=None, ay_xy=None, ay_yaw=None, alan=None):
    """Tek tarama için en iyi poz. Dönüş dict ya da None (harita henüz yetersiz / güven düşük)."""
    p = p or EsleParam()
    ay_xy = p.ay_xy if ay_xy is None else ay_xy
    ay_yaw = p.ay_yaw if ay_yaw is None else ay_yaw
    nok = tarama_noktalari(mesafe, guven, menzil, p)
    if len(nok) < p.min_nokta:
        return {"durum": "az_nokta", "n": len(nok)}
    A = alan or olabilirlik_alani(iz, (poz[0], poz[1]), p)
    if A is None:
        return {"durum": "harita_yok", "n": len(nok)}
    f, fx0, fy_ust, h, n_dolu = A
    n = len(nok)
    # --- kaba arama
    nxy = int(round(ay_xy / h))
    dr = np.arange(-nxy, nxy + 1)
    dyaws = np.arange(-ay_yaw, ay_yaw + 1e-9, 1.0)
    # dünya noktaları her yaw adayı için (T, N)
    yaw_a = np.radians(poz[2] + dyaws)[:, None]
    wx = poz[0] + nok[None, :, 0] * np.cos(yaw_a) + nok[None, :, 1] * np.sin(yaw_a)
    wy = poz[1] - nok[None, :, 0] * np.sin(yaw_a) + nok[None, :, 1] * np.cos(yaw_a)
    cc = (wx - fx0) / h - 0.5              # alan sütun koordinatı (hücre merkezi)
    rr = (fy_ust - wy) / h - 0.5
    ci = np.rint(cc).astype(np.int64)
    ri = np.rint(rr).astype(np.int64)
    H, W = f.shape
    # kaydırmalar: x doğuya +dc sütun, y kuzeye +dy satır AZALIR
    T = len(dyaws)
    skor = np.empty((T, len(dr), len(dr)), np.float32)       # [yaw, dy_idx (kuzey), dx_idx (doğu)]
    for iy, dy in enumerate(dr):
        r2 = ri[:, None, :] - dy                              # (T,1,N)
        c2 = ci[:, None, :] + dr[None, :, None]               # (T,Dx,N)
        ic = (r2 >= 0) & (r2 < H) & (c2 >= 0) & (c2 < W)
        v = f[np.clip(r2, 0, H - 1), np.clip(c2, 0, W - 1)] * ic
        skor[:, iy, :] = v.sum(2)
    dx_m = dr * h
    dy_m = dr * h
    oncul = 0.5 * p.oncul_agirlik * n * ((dx_m[None, None, :] ** 2 + dy_m[None, :, None] ** 2) / p.sigma_xy ** 2
                                          + (dyaws[:, None, None] / p.sigma_yaw) ** 2)
    top = skor - oncul
    k = np.unravel_index(int(np.argmax(top)), top.shape)
    en_iyi = float(top[k])
    # ayırt edicilik (kaba ölçekte): en iyiden ayirt_m'den uzak konumdaki en iyi aday
    DX, DY = np.meshgrid(dx_m, dy_m)
    uzak = (DX - dx_m[k[2]]) ** 2 + (DY - dy_m[k[1]]) ** 2 > p.ayirt_m ** 2
    if uzak.any():
        ikinci = float(np.max(np.where(uzak[None], top, -1e9)))
    else:
        ikinci = en_iyi - 1e-3
    # --- ince arama
    d0 = np.array([dx_m[k[2]], dy_m[k[1]], dyaws[k[0]]])
    fx = d0[0] + np.arange(-0.0375, 0.0375 + 1e-9, 0.0125)
    fy = d0[1] + np.arange(-0.0375, 0.0375 + 1e-9, 0.0125)
    fyaw = d0[2] + np.arange(-1.0, 1.0 + 1e-9, 0.25)
    ya = np.radians(poz[2] + fyaw)[:, None]
    wx = poz[0] + nok[None, :, 0] * np.cos(ya) + nok[None, :, 1] * np.sin(ya)          # (T,N)
    wy = poz[1] - nok[None, :, 0] * np.sin(ya) + nok[None, :, 1] * np.cos(ya)
    en2, en2_k = -1e18, None
    for ix, dx in enumerate(fx):
        for iy, dy in enumerate(fy):
            c = (wx + dx - fx0) / h - 0.5
            r = (fy_ust - (wy + dy)) / h - 0.5
            v = _bilin(f, c, r).sum(1)                                                   # (T,)
            pen = 0.5 * p.oncul_agirlik * n * ((dx ** 2 + dy ** 2) / p.sigma_xy ** 2 + (fyaw / p.sigma_yaw) ** 2)
            s = v - pen
            j = int(np.argmax(s))
            if s[j] > en2:
                en2, en2_k = float(s[j]), (dx, dy, float(fyaw[j]), float(v[j]))
    dx, dy, dyaw, vf = en2_k
    return {"durum": "ok", "poz": (poz[0] + dx, poz[1] + dy, (poz[2] + dyaw) % 360.0), "d": (dx, dy, dyaw), "ort_f": vf / n,
            "ayirt": (en_iyi - ikinci) / n, "n": n, "dolu_hucre": n_dolu}


def karistir(poz, sonuc, p):
    """Kabul edilen eşleşmeyi tahmine kazançla karıştırır (EKF'in sabit-kazanç hali): poz + K (eşleşme - poz)."""
    dx, dy, dyaw = sonuc["d"]
    return (poz[0] + p.kazanc_xy * dx, poz[1] + p.kazanc_xy * dy, (poz[2] + p.kazanc_yaw * dyaw) % 360.0)


def kabul(sonuc, p):
    return sonuc is not None and sonuc.get("durum") == "ok" and sonuc["ort_f"] >= p.min_ort_f and sonuc["ayirt"] >= p.min_ayirt
