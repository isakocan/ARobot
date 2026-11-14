"""Aydınlanmaya dayanıklı zemin öteleme (BEV): konum_bev.py'nin AYNI sınıfları, tek fark BEV görüntüsüne YEREL KONTRAST NORMALİZASYONU (LCN).

Neden: Önceki BEV ham gri BEV üstünde ORB çalıştırıyordu. Loş ışıkta zemin dokusunun kontrastı gürültü seviyesine iner (ORB eşiği geçilmez:
ok oranı %0), farda ise robota bağlı parlak leke ve kenarı özellik olarak seçilir (robotla birlikte hareket ettiği için "öteleme yok" oyu
verir: gerçek hareketin ~%30'u). LCN: g' = (g - G_s*g) / sqrt(G_s*(g - G_s*g)^2 + eps^2) * kazanc + 128. Yavaş değişen aydınlanma (leke,
gölge geçişi, genel parlaklık) çıkarılır, yerel kontrast birim ölçeğe getirilir; eps gürültü tabanı (gri seviye), doku yoksa gürültüyü şişirmez.
Çıktı: kayitlar/sonuclar/birlesik/onbellek/bevL_<kayit>.npz (kare, d (n,2), ok, ic, ms)."""
import os
import sys
import time

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import birlesik_ortak as bo  # noqa: E402
import konum_bev as kb  # noqa: E402


class LcnParam:
    sigma = 5.0          # piksel (2 cm/piksel -> 10 cm): yavaş aydınlanma ölçeği
    eps = 2.0            # gri seviye: gürültü tabanı
    kazanc = 40.0
    fast_esik = 6


def lcn(b, sigma=LcnParam.sigma, eps=LcnParam.eps, kazanc=LcnParam.kazanc):
    b = b.astype(np.float32)
    d = b - cv2.GaussianBlur(b, (0, 0), sigma)
    v = cv2.GaussianBlur(d * d, (0, 0), sigma)
    return np.clip(d / np.sqrt(v + eps * eps) * kazanc + 128.0, 0, 255).astype(np.uint8)


class LcnTablo(kb.BevTablo):
    def __init__(self, *a, lcn_par=None, **k):
        super().__init__(*a, **k)
        self.par = lcn_par or LcnParam

    def uret(self, kare_bgr):
        return lcn(super().uret(kare_bgr), self.par.sigma, self.par.eps, self.par.kazanc)


def bev_yolu(ad, etiket="L"):
    return os.path.join(bo.ONB, "bev%s_%s.npz" % (etiket, ad))


def yaw_oku(ad, yaw_kaynak):
    """yaw_kaynak: komutlu | komutsuz (ham M2D) | Lkomutlu | Lkomutsuz (LCN M2D, birlesik_yon.py)."""
    s = bo.spec(ad)
    if yaw_kaynak.startswith("L"):
        import birlesik_yon as by
        return by.yukle(ad, yaw_kaynak == "Lkomutlu")
    return bo.npz(bo.yon_yolu(s, yaw_kaynak == "komutlu"))


def bev_lcn(ad, par=None, etiket="L", yeniden=False, ilerleme=True, yaw_kaynak="komutlu"):
    """Kare başına BEV artımı (yaw = M2D komutlu, nedensel tarama maskesiyle). Önbellekli."""
    par = par or LcnParam
    p = bev_yolu(ad, etiket)
    if os.path.exists(p) and not yeniden:
        return bo.npz(p)
    cv2.setNumThreads(1)
    from kayit import Kayit
    from kamera import Kamera
    s = bo.spec(ad)
    yz = yaw_oku(ad, yaw_kaynak)
    kay = Kayit(s["theta"])
    kam = Kamera.bilgiden(kay.bilgi)
    tab = LcnTablo("theta", kam, lcn_par=par, h=float(kay.bilgi.get("kamera_yuksekligi_m", 0.255)))   # yükseklik kayıttan (gerçek robot ~0,17 m)
    oz = kb.BevOtelemeci(tab)
    oz.orb = cv2.ORB_create(nfeatures=500, scaleFactor=1.2, nlevels=3, edgeThreshold=13, fastThreshold=par.fast_esik, patchSize=21)
    tz = bo.npz(s["tar"])
    tk = tz["kare"]
    n = len(kay)
    D = np.full((n, 2), np.nan)
    OK = np.zeros(n, bool)
    IC = np.zeros(n, np.int32)
    MS = np.zeros(n)
    t0 = time.time()
    for i in range(n):
        j = max(int(np.searchsorted(tk, i, side="right")) - 1, 0)
        em = kb.tarama_maskesi(tab, tz["mesafe"][j], tz["guven"][j], 0.25)
        ds, di, ok, ic = oz.guncelle(kay.resim(i), float(yz["yon"][i]), em)
        D[i] = (ds, di)
        OK[i] = ok
        IC[i] = ic
        MS[i] = oz.son_ms
        if ilerleme and i % 300 == 0:
            print("  bevL %s %d/%d (%.0f sn)" % (ad, i, n, time.time() - t0), flush=True)
    os.makedirs(bo.ONB, exist_ok=True)
    z = {"kare": kay.kare_numaralari, "d": D, "ok": OK, "ic": IC, "ms": MS}
    np.savez_compressed(p, **z)
    return z


def _is(arg):
    ad, etiket, yk = arg
    bev_lcn(ad, etiket=etiket, yaw_kaynak=yk)
    return ad


if __name__ == "__main__":
    import argparse
    from concurrent.futures import ProcessPoolExecutor
    ap = argparse.ArgumentParser()
    ap.add_argument("kume", choices=["gel", "sin", "sin2", "hepsi15"])
    ap.add_argument("--isci", type=int, default=6)
    ap.add_argument("--yeniden", action="store_true")
    ap.add_argument("--yaw", default="komutlu", choices=["komutlu", "komutsuz", "Lkomutlu", "Lkomutsuz"])
    ap.add_argument("--etiket", default=None, help="önbellek etiketi (varsayılan: komutlu->L, diğerleri->L<yaw>)")
    a = ap.parse_args()
    etiket = a.etiket or ("L" if a.yaw == "komutlu" else "L" + a.yaw)
    adlar = {"gel": bo.GEL, "sin": bo.SIN, "sin2": bo.SIN2, "hepsi15": bo.GEL + bo.SIN}[a.kume]
    if a.yeniden:
        for ad in adlar:
            p = bev_yolu(ad, etiket)
            if os.path.exists(p):
                os.remove(p)
    with ProcessPoolExecutor(a.isci) as ex:
        for r in ex.map(_is, [(ad, etiket, a.yaw) for ad in adlar]):
            print("tamam", r, flush=True)
