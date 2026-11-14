"""Gerçek karelerde ön/arka mercek DİKİŞ denetimi ve (isteğe bağlı) merkez kalibrasyonu.
Her lensin eşdikdörtgen izdüşümü (yalnız o lens) ayrı alınır; iki lensin çakıştığı dar bantta (teta 90±~7 derece) iki izdüşüm arasındaki
yerel kayma (faz korelasyonu) ölçülür. Kayma ~0 ise dikiş düzgündür. Sim'de bu sıfırdı (aynı model). Çıktı: ölçüm sayıları.
Kullanım: python gercek_dikis.py [kamera.json yolu]"""
import json
import os
import sys

import cv2
import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
from kayit import Kayit  # noqa: E402
from kamera import Kamera  # noqa: E402

KOK = os.path.abspath(os.path.join(KLASOR, "..", ".."))
G, Y = 1920, 960      # yoğun örnekleme (derece başına 5,3 piksel)


def tekli(kam, im):
    tab, w1, w2 = kam._esd_tablo(G, Y)
    a = cv2.remap(im, tab[0][0], tab[0][1], cv2.INTER_LINEAR)
    b = cv2.remap(im, tab[1][0], tab[1][1], cv2.INTER_LINEAR)
    return a, b, (w1 > 0) & (w2 > 0)


def kayma_olc(kam, imler, el_aralik=(-45, 45)):
    """Her dikiş (az=-90 sol, +90 sağ) için pencere pencere kayma. Dönüş: sözlük {dikis: dizi (n,3) [yukseklik_derece, dx_piksel, dy_piksel, yanıt]}."""
    sonuc = {"sol": [], "sag": []}
    for im in imler:
        a, b, ov = tekli(kam, im)
        ga = cv2.cvtColor(a, cv2.COLOR_BGR2GRAY).astype(np.float32)
        gb = cv2.cvtColor(b, cv2.COLOR_BGR2GRAY).astype(np.float32)
        for ad, az in (("sol", -90.0), ("sag", 90.0)):
            u0 = int((az + 180) / 360 * G)
            for el in range(el_aralik[0], el_aralik[1], 15):
                v0 = int((90 - el) / 180 * Y)
                ys = slice(v0 - 40, v0 + 40)
                xs = slice(u0 - 40, u0 + 40)
                if v0 - 40 < 0 or v0 + 40 > Y:
                    continue
                m = ov[ys, xs]
                if m.mean() < 0.15:
                    continue
                pa, pb = ga[ys, xs] * m, gb[ys, xs] * m
                if pa.std() < 4 or pb.std() < 4:
                    continue
                win = cv2.createHanningWindow((80, 80), cv2.CV_32F)
                (dx, dy), r = cv2.phaseCorrelate(pa, pb, win)
                sonuc[ad].append((el, dx, dy, r))
    return {k: np.array(v) for k, v in sonuc.items()}


if __name__ == "__main__":
    yol = sys.argv[1] if len(sys.argv) > 1 else None
    kam = Kamera(yol) if yol else Kamera()
    ads = ["2a_vitessiz_duz", "4_parke_vitessiz", "5_saksi_turu", "6a_far_isik_acik"]
    imler = []
    for ad in ads:
        k = Kayit(os.path.join(KOK, "kayitlar", "gercek", ad))
        for i in np.linspace(5, len(k) - 5, 8).astype(int):
            imler.append(k.resim(i))
    s = kayma_olc(kam, imler)
    for ad, v in s.items():
        v = v[v[:, 3] > 0.05]
        print("dikis %s: n=%d  dx medyan %.2f px (%.2f derece), dy medyan %.2f px, |dx| ort %.2f, dx std %.2f" % (
            ad, len(v), np.median(v[:, 1]), np.median(v[:, 1]) / (G / 360), np.median(v[:, 2]), np.abs(v[:, 1]).mean(), v[:, 1].std()))
