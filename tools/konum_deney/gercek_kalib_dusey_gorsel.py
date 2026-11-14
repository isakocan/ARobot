"""Duseylik gorseli: ayni kareden once (kamera.json) ve sonra (kamera_gercek.json) ufuk dogrultulu iğne deligi gorunumleri + LSD ile bulunan duseye yakin cizgiler (yesil) ve ideal dusey kilavuz (sari)."""
import json
import os
import sys

import cv2
import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_kalib_model as km  # noqa: E402
import gercek_kalib_dusey as kd  # noqa: E402
import gercek_ortak as go  # noqa: E402
from kayit import Kayit  # noqa: E402

if __name__ == "__main__":
    kay = Kayit(os.path.join(go.GERCEK, "olcum_ham"))
    gri = cv2.cvtColor(kay.resim(30), cv2.COLOR_BGR2GRAY).astype(np.float32)
    govde = cv2.imread(os.path.join(go.KLASOR, "kamera_maske_gercek.png"), cv2.IMREAD_GRAYSCALE)
    kam_o = km.kamera()
    kam_s = km.KameraGercek(json.load(open(os.path.join(go.KLASOR, "kamera_gercek.json"), encoding="utf-8")))
    lsd = cv2.createLineSegmentDetector(cv2.LSD_REFINE_STD)
    d = json.load(open(os.path.join(km.KALIB, "dusey.json"), encoding="utf-8"))
    for az in (330, 30, 270):
        satir = []
        for kam, etiket in ((kam_o, "ONCE: kamera.json"), (kam_s, "SONRA: kamera_gercek.json")):
            g, gm = kd.gorunum(gri, govde, kd.gorunum_haritasi(kam, az))
            g = cv2.GaussianBlur(g.astype(np.uint8), (0, 0), 0.8)
            im = cv2.cvtColor(g, cv2.COLOR_GRAY2BGR)
            seg = lsd.detect(g)[0]
            if seg is not None:
                for x1, y1, x2, y2 in seg[:, 0, :]:
                    if np.hypot(x2 - x1, y2 - y1) < 50:
                        continue
                    ust, alt = ((x1, y1), (x2, y2)) if y1 < y2 else ((x2, y2), (x1, y1))
                    sap = np.degrees(np.arctan2(ust[0] - alt[0], alt[1] - ust[1]))
                    if abs(sap) <= 20 and not gm[int(np.clip((y1 + y2) / 2, 0, kd.Hh - 1)), int(np.clip((x1 + x2) / 2, 0, kd.W - 1))]:
                        cv2.line(im, (int(x1), int(y1)), (int(x2), int(y2)), (0, 255, 0), 2, cv2.LINE_AA)
            for x in range(80, kd.W, 160):
                cv2.line(im, (x, 0), (x, kd.Hh), (0, 220, 255), 1)
            cv2.putText(im, "%s  (gorunum azimutu %d)" % (etiket, az), (8, 22), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 0, 255), 2, cv2.LINE_AA)
            satir.append(im)
        cv2.imwrite(os.path.join(km.KALIB, "dusey_gorunum_%d.jpg" % az), np.hstack(satir), [cv2.IMWRITE_JPEG_QUALITY, 88])
    print("tamam")
