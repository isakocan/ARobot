"""Panorama ornekleri (gercek kareler, kamera.json geometrisi) + govde maskesi bindirmesi + dikis buyutmesi. Cikti: kayitlar/sonuclar/gercek/panorama_*.jpg"""
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_ortak as go  # noqa: E402
from kamera import Kamera  # noqa: E402
from kayit import Kayit  # noqa: E402


def yaz(im, s, org, boy=0.6, renk=(255, 255, 255)):
    cv2.putText(im, s, org, cv2.FONT_HERSHEY_SIMPLEX, boy, (0, 0, 0), 3, cv2.LINE_AA)
    cv2.putText(im, s, org, cv2.FONT_HERSHEY_SIMPLEX, boy, renk, 1, cv2.LINE_AA)


kam = Kamera()
G, Y = 1280, 640
me = cv2.imread(os.path.join(go.KLASOR, "kamera_maske.png"), 0)
mg = cv2.imread(os.path.join(go.KLASOR, "kamera_maske_gercek.png"), 0)
pe, kap = kam.balikgozu_to_esdikdortgen(me, G, Y)
pg, _ = kam.balikgozu_to_esdikdortgen(mg, G, Y)
ORN = [("2a_vitessiz_duz", 300, "hali"), ("4_parke_vitessiz", 250, "parke"), ("5_saksi_turu", 1200, "parke, esyalar"),
       ("6a_far_isik_acik", 150, "far acik"), ("6b_far_karanlik", 150, "karanlik"), ("6c_karanlik_surus", 500, "karanlik surus")]
satir = []
dikis = []
for ad, i, et in ORN:
    k = Kayit(os.path.join(go.GERCEK, ad))
    im = k.resim(min(i, len(k) - 1))
    e, _ = kam.balikgozu_to_esdikdortgen(im, G, Y)
    o = e.copy()
    for m, renk in ((pe, (0, 0, 255)), (pg, (0, 255, 0))):
        c, _ = cv2.findContours((m > 127).astype(np.uint8), cv2.RETR_LIST, cv2.CHAIN_APPROX_NONE)
        cv2.drawContours(o, c, -1, renk, 1)
    for x in (G // 4, 3 * G // 4):
        cv2.line(o, (x, 0), (x, Y), (255, 255, 0), 1)
    cv2.line(o, (G // 2, 0), (G // 2, 20), (255, 255, 255), 2)
    yaz(o, "%s kare %d (%s)" % (ad, i, et), (8, 24), 0.7)
    satir.append(cv2.resize(o, (960, 480)))
    # dikis buyutme: az -90 (sutun 320) ve +90 (sutun 960), yukseklik ufuk civari
    a = e[150:430, 320 - 80:320 + 80]
    b = e[150:430, 960 - 80:960 + 80]
    d = np.hstack([a, b])
    d = cv2.resize(d, None, fx=2, fy=2, interpolation=cv2.INTER_CUBIC)
    yaz(d, "%s: sol dikis (az=-90) | sag dikis (az=+90)" % ad, (6, 20), 0.55)
    dikis.append(d)
cv2.imwrite(os.path.join(go.SONUC, "panorama_ornekleri.jpg"), np.vstack(satir[:4]), [cv2.IMWRITE_JPEG_QUALITY, 86])
cv2.imwrite(os.path.join(go.SONUC, "panorama_ornekleri_karanlik.jpg"), np.vstack(satir[4:] + satir[3:4]), [cv2.IMWRITE_JPEG_QUALITY, 86])
cv2.imwrite(os.path.join(go.SONUC, "panorama_dikis.jpg"), np.vstack(dikis[:3]), [cv2.IMWRITE_JPEG_QUALITY, 90])
print("tamam")
