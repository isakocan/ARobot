"""M2D yaw'ının aydınlanmaya dayanıklı sürümü: yontem_pusula_nokta.Yontem'in ALT SINIFI, tek fark ORB'den önce LCN (birlesik_bev.lcn).
konum_yon_onbellek.hesapla ile aynı kayıt/çıktı biçimi (yon, phi, icerik, eslesme, kalite, durum, anahtar_degisim, ms); çıktı
kayitlar/sonuclar/birlesik/onbellek/yonL_<komutlu|komutsuz>_<kayit>.npz. Kullanım: python birlesik_yon.py gel|sin|sin2|hepsi15 [--isci N]"""
import os
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import birlesik_ortak as bo  # noqa: E402

LCN_SIGMA = 4.0          # eşdikdörtgen G=1024'te piksel
LCN_EPS = 2.0
LCN_KAZANC = 40.0


def yol(ad, komutlu):
    return os.path.join(bo.ONB, "yonL_%s_%s.npz" % ("komutlu" if komutlu else "komutsuz", ad))


def yukle(ad, komutlu):
    p = yol(ad, komutlu)
    if not os.path.exists(p):
        hesapla((ad, komutlu))
    return bo.npz(p)


def hesapla(arg):
    ad, komutlu = arg
    p = yol(ad, komutlu)
    if os.path.exists(p):
        return p
    import cv2
    cv2.setNumThreads(1)
    import yontem_pusula_nokta as ypn
    import birlesik_bev as bb
    from kayit import Kayit
    from kamera import Kamera
    s = bo.spec(ad)
    kay = Kayit(s["theta"])
    kam = Kamera.bilgiden(kay.bilgi)
    bilgi = dict(kay.bilgi)
    bilgi["_kayit_klasoru"] = kay.klasor

    class LcnM2D(ypn.Yontem):
        def _cikar(self, gri, gecerli):
            g = bb.lcn(np.clip(gri, 0, 255), LCN_SIGMA, LCN_EPS, LCN_KAZANC).astype(np.float32)
            return super()._cikar(g, gecerli)

        def _duzlem(self, a, b, merkez_der, pencere_der, N):
            r = super()._duzlem(a, b, merkez_der, pencere_der, N)
            self.son_phi = r[3]["phi"]
            self.son_ic = r[3]["icerik"]
            return r

    y = LcnM2D(bilgi, kam, kip="duzlem", komut_oncel=bool(komutlu))
    n = len(kay)
    yon = np.zeros(n)
    phi = np.full(n, np.nan)
    ic = np.zeros(n, np.int32)
    esl = np.zeros(n, np.int32)
    kal = np.zeros(n)
    dk = np.zeros(n, np.int8)
    ms = np.zeros(n)
    t0 = time.time()
    DUR = ["baslangic", "ok", "tut", "anahtar_mesafe", "anahtar_goreli", "anahtar_kalite", "yedek"]
    for i in range(n):
        t = int(kay.t_ms[i])
        y.son_phi = np.nan
        y.son_ic = 0
        a = time.perf_counter()
        r = y.guncelle(kay.resim(i), t, kay.komut(t))
        ms[i] = (time.perf_counter() - a) * 1000.0
        yon[i] = r[2]
        phi[i] = y.son_phi
        ic[i] = y.son_ic
        esl[i] = y.son_bilgi.get("eslesme", 0) if y.durum != "tut" else 0
        kal[i] = y.kalite
        dk[i] = DUR.index(y.durum)
        if i % 300 == 0:
            print("  yonL %s %s %d/%d (%.0f sn)" % ("komutlu" if komutlu else "komutsuz", ad, i, n, time.time() - t0), flush=True)
    os.makedirs(bo.ONB, exist_ok=True)
    np.savez_compressed(p, kare=kay.kare_numaralari, t_ms=kay.t_ms, yon=yon, phi=phi, icerik=ic, eslesme=esl, kalite=kal, durum=dk, ms=ms)
    return p


if __name__ == "__main__":
    import argparse
    from concurrent.futures import ProcessPoolExecutor
    ap = argparse.ArgumentParser()
    ap.add_argument("kume", choices=["gel", "sin", "sin2", "hepsi15"])
    ap.add_argument("--isci", type=int, default=6)
    ap.add_argument("--yalniz", choices=["komutlu", "komutsuz", "ikisi"], default="ikisi")
    a = ap.parse_args()
    adlar = {"gel": bo.GEL, "sin": bo.SIN, "sin2": bo.SIN2, "hepsi15": bo.GEL + bo.SIN}[a.kume]
    kips = {"komutlu": [True], "komutsuz": [False], "ikisi": [True, False]}[a.yalniz]
    isler = [(ad, k) for ad in adlar for k in kips]
    with ProcessPoolExecutor(a.isci) as ex:
        for r in ex.map(hesapla, isler):
            print("tamam", r, flush=True)
