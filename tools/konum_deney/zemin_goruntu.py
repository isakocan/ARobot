"""Zemin sınırı üstüne bindirme görüntüleri (önbellekteki taramalardan): sınır panoramaya çizilir, gerçek başka renkte.
Kırmızı = tahmin (güven >= 0,2), turuncu = tahmin (düşük güven), yeşil = gerçek (görünen zemin sınırı).
    python tools/konum_deney/zemin_goruntu.py [--yontem klasik model_gorunum] [--tur sim theta]
Çıktı: kayitlar/sonuclar/harita/ustuste/<kayit>_kare<N>.png (her satır bir yöntem x girdi)."""
import argparse
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zemin_ortak as zo  # noqa: E402
import zemin_degerlendir as zd  # noqa: E402

ODALAR = ["yerinde_donus_salon", "yerinde_donus_koridor", "yerinde_donus_yatak_odasi", "yerinde_donus_banyo",
          "yerinde_donus_mutfak", "yerinde_donus_calisma_odasi"]


def ciz(g, mesafe, guven, gercek, etiket):
    im = g.pan.copy()
    H, G = im.shape[:2]
    v0 = max(H // 2 - 40, 0)
    for k in range(360):
        az = k if k < 180 else k - 360
        u = int(round((az + 180) / 360 * G - 0.5)) % G
        for d, renk in ((gercek[k], (0, 255, 0)), (mesafe[k], (0, 0, 255) if guven[k] >= 0.2 else (0, 150, 255))):
            if np.isfinite(d):
                v = int(round(float(zo.mesafe_satir(min(d, 60), H, g.h))))
                if v0 <= v < H:
                    cv2.circle(im, (u, v), 2, renk, -1)
    im = im[v0:int(H * 0.82)]
    im = cv2.resize(im, (1536, int(im.shape[0] * 1536 / G)))
    cv2.putText(im, etiket, (8, 20), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 3, cv2.LINE_AA)
    cv2.putText(im, etiket, (8, 20), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 0), 1, cv2.LINE_AA)
    return im


def ana():
    ap = argparse.ArgumentParser()
    ap.add_argument("--yontem", nargs="*", default=["klasik", "model_gorunum"])
    ap.add_argument("--tur", nargs="*", default=["sim", "theta"])
    ap.add_argument("--kayit", nargs="*", default=ODALAR)
    ap.add_argument("--kareler", nargs="*", type=int, default=[25, 75])
    a = ap.parse_args()
    cikis = os.path.join(zo.CIKTI, "ustuste")
    os.makedirs(cikis, exist_ok=True)
    for ad in a.kayit:
        ts, kn, tr = zo.gercek_zemin_dosyasi(ad)
        for kare in a.kareler:
            satirlar = []
            for tur in a.tur:
                kg = zo.KayitGirdisi(ad, tur)
                g = kg.girdi(kare)
                for y in a.yontem:
                    yol = zd.tarama_yolu(y, tur, ad)
                    if not os.path.exists(yol):
                        continue
                    z = zd.tarama_yukle(y, tur, ad)
                    if kare not in z["kare"]:
                        continue
                    j = list(z["kare"]).index(kare)
                    satirlar.append(ciz(g, z["mesafe"][j], z["guven"][j], tr[list(kn).index(kare)], "%s | %s | %s kare %d" % (y, tur, ad, kare)))
            if satirlar:
                cv2.imwrite(os.path.join(cikis, "%s_kare%d.png" % (ad, kare)), np.vstack(satirlar))
                print("yazıldı", ad, kare)


if __name__ == "__main__":
    ana()
