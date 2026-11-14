"""Eklenen parçaların kare başına süresi (bu PC, tek çekirdek, ms). Jetson Nano süresi DOĞRULANAMADI.
Kullanım: python birlesik_sure.py   (kayit: duz_git; önbellekteki M2D/BEV süreleriyle birlikte yazar)"""
import os
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cv2  # noqa: E402
cv2.setNumThreads(1)
import birlesik_ortak as bo  # noqa: E402
import birlesik_bev as bb  # noqa: E402
import birlesik_degerlendir as bd  # noqa: E402


def olc(f, n=200):
    f()
    t = []
    for _ in range(n):
        a = time.perf_counter()
        f()
        t.append((time.perf_counter() - a) * 1000.0)
    return float(np.mean(t)), float(np.percentile(t, 95))


if __name__ == "__main__":
    g = bd.Girdi("duz_git")
    bev = (np.random.rand(145, 145) * 255).astype(np.uint8)
    esd = (np.random.rand(512, 1024) * 255).astype(np.float32)
    print("LCN (BEV 145x145)        ort %.2f ms  p95 %.2f ms" % olc(lambda: bb.lcn(bev)))
    import birlesik_yon as by
    print("LCN (M2D 1024x512)       ort %.2f ms  p95 %.2f ms" % olc(lambda: bb.lcn(np.clip(esd, 0, 255), by.LCN_SIGMA, by.LCN_EPS, by.LCN_KAZANC), 60))
    P, seri, ms = bd.kos_E(g)
    print("E.adim (kaynaştırma)     ort %.3f ms  p95 %.3f ms" % (ms.mean(), np.percentile(ms, 95)))
    yz = bo.npz(bo.yon_yolu(g.s, True))
    yl = by.yukle("duz_git", True)
    print("M2D ham (önbellek)       ort %.1f ms  p95 %.1f ms ; M2D+LCN (önbellek) ort %.1f ms p95 %.1f ms" % (
        yz["ms"].mean(), np.percentile(yz["ms"], 95), yl["ms"].mean(), np.percentile(yl["ms"], 95)))
    for nm, e in (("BEV ham (K)", bo.npz(bo.bev_yolu(g.s))), ("BEV LCN", bo.npz(bb.bev_yolu("duz_git", "LLkomutlu")))):
        print("%-24s ort %.1f ms  p95 %.1f ms" % (nm, e["ms"].mean(), np.percentile(e["ms"], 95)))
