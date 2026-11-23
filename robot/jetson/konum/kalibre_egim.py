"""Kamera eğimi kalibrasyonu (PC ya da robot): konum kayıtlarındaki işaret gözlemlerinden kameranın robota göre eğimini bulur.

python kalibre_egim.py <kayit> [<kayit> ...] [--yaz]

Yöntem: duvardaki işaretin merkezi kamera düzleminde sabit yükseklikte olmalı. Kamera eğikse işaretin ölçülen yüksekliği
bakış yönüyle değişir: y = h + z * tan(ileri) + x * tan(yan). Her kayda ayrı h (işaret yeri farklı olabilir), ortak eğim.
Bulunan eğim şimdiki teta.EGIM üstüne eklenir (şimdiki model zaten düzeltilmiş ölçüyor). --yaz: ~/.config/robot/kamera_egimi.json.
"""
import json
import math
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import isaret  # noqa: E402
import teta  # noqa: E402


def main():
    arg = [a for a in sys.argv[1:] if a != "--yaz"]
    satir = []
    for i, K in enumerate(arg):
        R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl"))]
        for r in R[::3]:
            g = cv2.imread(os.path.join(K, r["jpg"]), cv2.IMREAD_GRAYSCALE)
            if g is None:
                continue
            for m in isaret.bul(g):
                if m["mesafe"] < 1.6 and not m["belirsiz"]:
                    satir.append((i, m["x"], m["z"], m["y"]))
    if len(satir) < 20:
        print("yetersiz gözlem:", len(satir)); return
    A = np.zeros((len(satir), len(arg) + 2)); y = np.zeros(len(satir))
    for j, (i, x, z, yy) in enumerate(satir):
        A[j, i] = 1; A[j, -2] = z; A[j, -1] = x; y[j] = yy
    c = np.linalg.lstsq(A, y, rcond=None)[0]
    ileri, yan = math.degrees(math.atan(c[-2])), math.degrees(math.atan(c[-1]))
    print("gözlem %d, artık %.1f cm (eğimsiz %.1f cm)" % (len(satir), np.std(y - A @ c) * 100,
                                                     np.std(y - A[:, :-2] @ np.linalg.lstsq(A[:, :-2], y, rcond=None)[0]) * 100))
    print("işaret yüksekliği (kameradan):", " ".join("%.3f" % v for v in c[:-2]))
    print("kalan eğim: ileri %.2f, yan %.2f derece; şimdiki %.2f / %.2f -> yeni %.2f / %.2f" % (
        ileri, yan, teta.EGIM_ILERI, teta.EGIM_YAN, teta.EGIM_ILERI + ileri, teta.EGIM_YAN + yan))
    if "--yaz" in sys.argv:
        os.makedirs(os.path.dirname(teta.EGIM_DOSYASI), exist_ok=True)
        with open(teta.EGIM_DOSYASI, "w") as f:
            json.dump({"ileri": round(teta.EGIM_ILERI + ileri, 2), "yan": round(teta.EGIM_YAN + yan, 2)}, f)
        print("yazıldı:", teta.EGIM_DOSYASI)


if __name__ == "__main__":
    main()
