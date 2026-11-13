"""Robot gövdesi maskesini (kamera_maske.json + kamera_maske.png) üretir ve önizleme çizer. YAKLAŞIKTIR.

Nasıl türetildi: gerçek robottan alınan, kamera masada sabitken çekilmiş karelerde (tools/robotsuz_deneme/kayit/kare.jpg
ve 10 karelik sıra) gövdenin (siyah üst plaka, pil) iki lens dairesindeki görünür sınırı elle izlendi; çokgen köşeleri
balık gözü piksel koordinatındadır. Kareler arasında pilin duruşu biraz farklı (kenar ~25 piksel oynuyor), bu yüzden
sınırın belirsizliği yaklaşık +-15-25 pikseldir. Kamera masada olduğu için masanın siyah yüzeyi gövdeden ayrılamadı;
yalnızca belirgin plaka/pil kenarı izlendi.

Kullanım:  python tools/konum_deney/maske_uret.py [gercek_kare.jpg] [onizleme.png]"""
import json
import os
import sys

import cv2
import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kamera import Kamera, KLASOR  # noqa: E402

COKGENLER = {
    "lens1": [[500, 88], [470, 120], [445, 165], [437, 250], [447, 340], [455, 420], [480, 470], [530, 512],
              [600, 570], [700, 700], [700, 60]],
    "lens2": [[640, 60], [740, 98], [775, 104], [840, 160], [868, 230], [880, 300], [876, 400], [862, 455],
              [830, 505], [790, 542], [755, 572], [690, 565], [630, 520], [630, 60]],
}
GOVDE_RENGI_BGR = [28, 26, 26]


def maske_ciz(cokgenler, kam, kaydir=(0, 0)):
    """255 = gövde (daire içinde). kaydir: (dx, dy) piksel, belirsizlik denemesi için."""
    m = np.zeros((kam.Y, kam.G), np.uint8)
    for ad, p in cokgenler.items():
        p = np.array(p, np.float32) + np.array(kaydir, np.float32)
        cv2.fillPoly(m, [np.round(p).astype(np.int32)], 255)
    daire = np.zeros_like(m)
    for L in kam.lens:
        cv2.circle(daire, (int(round(L["merkez_x"])), int(round(L["merkez_y"]))), int(kam.daire_r), 255, -1)
    return cv2.bitwise_and(m, daire)


def yukseklik_profili(maske, kam, genislik=1440, yukseklik=720):
    """Maskeyi eşdikdörtgene çevirir; her azimut için gövdenin başladığı (ufuktan aşağı) ilk açıyı verir (derece).
    Yoksa None. Azimut: ileriden saat yönünde."""
    e, _ = kam.balikgozu_to_esdikdortgen(maske, genislik, yukseklik)
    e = e > 127
    ufuk = yukseklik // 2
    sonuc = {}
    for az in range(-180, 180, 15):
        u = int((az + 180) / 360 * genislik)
        sutun = e[ufuk:, u]
        if sutun.any():
            sonuc[az] = round(float(np.argmax(sutun)) / yukseklik * 180.0, 1)
        else:
            sonuc[az] = None
    return sonuc


def main():
    kam = Kamera()
    maske = maske_ciz(COKGENLER, kam)
    cv2.imwrite(os.path.join(KLASOR, "kamera_maske.png"), maske)
    daire_alan = np.pi * kam.daire_r ** 2
    oran = []
    for L in kam.lens:
        d = np.zeros_like(maske)
        cv2.circle(d, (int(round(L["merkez_x"])), int(round(L["merkez_y"]))), int(kam.daire_r), 255, -1)
        oran.append(round(100.0 * np.count_nonzero(maske & d) / np.count_nonzero(d), 1))
    bilgi = {
        "aciklama": "YAKLASIK gövde maskesi. cokgenler: balik gozu piksel koordinati (x, y). Lens daire disi zaten karanlik.",
        "turetme": "tools/robotsuz_deneme/kayit/kare.jpg ve masada sabit kameradan 10 kare uzerinde plaka/pil kenarinin elle izlenmesi (maske_uret.py). Belirsizlik ~ +-15-25 piksel.",
        "cokgenler": COKGENLER,
        "govde_rengi_bgr": GOVDE_RENGI_BGR,
        "daire_kapsami_yuzde": {"lens1": oran[0], "lens2": oran[1]},
    }
    with open(os.path.join(KLASOR, "kamera_maske.json"), "w", encoding="utf-8") as f:
        json.dump(bilgi, f, indent=1, ensure_ascii=False)
    print("daire kapsamı % (lens1, lens2):", oran)
    # belirsizlik: çokgeni sağa/sola 20 piksel kaydırıp ufuktan başlama açısına bak
    for ad, k in (("asıl", (0, 0)), ("içe -20px", (-20, 0)), ("dışa +20px", (20, 0))):
        # lens 2'de gövde görüntünün sol tarafında: içe/dışa yönü ters
        c = {a: [[x + (k[0] if a == "lens1" else -k[0]), y] for x, y in p] for a, p in COKGENLER.items()}
        pr = yukseklik_profili(maske_ciz(c, kam), kam)
        print(ad, "azimut -> gövde başlangıç çökme açısı:", pr)
    gercek = sys.argv[1] if len(sys.argv) > 1 else os.path.join(KLASOR, "..", "robotsuz_deneme", "kayit", "kare.jpg")
    cikis = sys.argv[2] if len(sys.argv) > 2 else os.path.join(KLASOR, "maske_onizleme.jpg")
    im = cv2.imread(gercek)
    if im is not None:
        for p in COKGENLER.values():
            cv2.polylines(im, [np.array(p, np.int32)], True, (0, 255, 255), 2)
        g = np.zeros_like(im)
        g[maske > 0] = (0, 0, 255)
        im = cv2.addWeighted(im, 1.0, g, 0.25, 0)
        cv2.imwrite(cikis, im, [cv2.IMWRITE_JPEG_QUALITY, 85])
        print("önizleme:", cikis)


if __name__ == "__main__":
    main()
