"""Tüm kayıtların tarama karelerini tek tek (tek kare) sorgu olarak hafızaya sorar; ham özellikleri önbelleğe yazar.
Kullanım: python yeniden_bul_sorgu_calistir.py [--isci 4] [--kayit ad ...]   Çıktı: <onbellek>/sorgu_<ad>.pkl, ilerleme.log"""
import argparse
import os
import pickle
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import yeniden_bul_ortak as yo  # noqa: E402
import yeniden_bul_cekirdek as yc  # noqa: E402

HARIC_SN = 3.0


def yigin(ad, bas, bit, genel=True):
    import cv2
    cv2.setNumThreads(1)
    H = yc.hafiza_kur()
    dep = yo.OzDeposu(ad)
    tar = yo.tarama_oku(ad)
    g = yo.gercek_oku(ad)
    gsira = {int(k): i for i, k in enumerate(g["kare"])}
    cik = []
    for j in range(bas, min(bit, len(tar["kare"]))):
        k = int(tar["kare"][j])
        i = gsira[k]
        haric = None
        if ad == yo.HARITA_KAYDI:
            haric = np.abs(H.kf_t - g["t_ms"][i]) <= HARIC_SN * 1000.0
        t = time.perf_counter()
        r = yc.sorgula(H, dep.al(k), (tar["mesafe"][j], tar["guven"][j], tar["menzil"]), haric=haric, genel=genel)
        r["toplam_ms"] = (time.perf_counter() - t) * 1000
        r["kare"] = k
        r["t_ms"] = float(g["t_ms"][i])
        r["gercek"] = (float(g["x"][i]), float(g["y"][i]), float(g["yaw"][i]))
        r["haric_n"] = int(haric.sum()) if haric is not None else 0
        cik.append(r)
    return ad, bas, cik


def ana():
    ap = argparse.ArgumentParser()
    ap.add_argument("--isci", type=int, default=4)
    ap.add_argument("--genel", type=int, default=1)
    ap.add_argument("--kayit", nargs="*", default=list(yo.KAYITLAR_TUM))
    a = ap.parse_args()
    yc.hafiza_kur()
    isler = []
    for ad in a.kayit:
        if os.path.exists(os.path.join(yo.ONBELLEK, "sorgu_%s.pkl" % ad)):
            continue
        n = len(yo.tarama_oku(ad)["kare"])
        for b in range(0, n, 40):
            isler.append((ad, b, b + 40))
    isler.sort(key=lambda t: -(t[2] - t[1]))
    from concurrent.futures import ProcessPoolExecutor
    log = open(os.path.join(yo.ONBELLEK, "ilerleme.log"), "a", encoding="utf-8")
    sonuc = {}
    t0 = time.time()
    with ProcessPoolExecutor(a.isci) as ex:
        fut = [ex.submit(yigin, *t, bool(a.genel)) for t in isler]
        for n, f in enumerate(fut):
            ad, b, cik = f.result()
            sonuc.setdefault(ad, {})[b] = cik
            m = "%d/%d %s %d tamam (%.0f sn)" % (n + 1, len(fut), ad, b, time.time() - t0)
            print(m, flush=True)
            log.write(m + "\n")
            log.flush()
    for ad, d in sonuc.items():
        liste = [r for b in sorted(d) for r in d[b]]
        with open(os.path.join(yo.ONBELLEK, "sorgu_%s.pkl" % ad), "wb") as f:
            pickle.dump(liste, f)
    print("bitti", time.time() - t0)


if __name__ == "__main__":
    ana()
