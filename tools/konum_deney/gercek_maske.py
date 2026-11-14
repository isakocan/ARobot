"""GERÇEK robot gövdesi maskesi (14 Kasım 2026 kayıtlarından). Mevcut kamera_maske.* dosyalarına DOKUNMAZ.
Çokgenler, 5_saksi_turu kare ortalaması (kameraya göre sabit duran gövde net, dünya bulanık) üstünde elle izlendi; mevcut maske
(masada duran kameradan çizilmişti) ile aynı biçimde. Ek: lens 2'de iki anten çubuğu (V biçimli, gövdeye bağlı, görüntüde sabit) ve
lens 1'de pilin sol kenarı ~10-30 piksel içeri (eski maske sol kenarda zemin şeridi de kapatıyordu).
Üretir: kamera_maske_gercek.png / kamera_maske_gercek.json / maske_gercek_onizleme.jpg. Kullanım: python gercek_maske.py"""
import json
import os
import sys

import cv2
import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
from kamera import Kamera  # noqa: E402
from maske_uret import maske_ciz  # noqa: E402

COKGENLER = {
    "lens1": [[492, 122], [530, 106], [560, 95], [700, 60], [700, 700], [600, 570], [540, 531], [490, 521], [455, 508],
              [438, 480], [440, 448], [462, 436], [450, 400], [443, 330], [442, 250], [458, 175]],
    "lens2": [[640, 60], [735, 98], [770, 112], [868, 225], [878, 330], [875, 400], [855, 470], [805, 525], [760, 520],
              [745, 565], [690, 565], [630, 520], [630, 60]],
    "anten_ust": [[870, 237], [885, 222], [940, 190], [1000, 140], [1060, 118], [1063, 128], [1000, 172], [940, 217], [878, 240]],
    "anten_alt": [[870, 400], [950, 405], [1030, 412], [1034, 424], [950, 423], [870, 416]],
}


def yap():
    kam = Kamera()
    m = maske_ciz(COKGENLER, kam)
    cv2.imwrite(os.path.join(KLASOR, "kamera_maske_gercek.png"), m)
    # anten polygonları lens dairesi dışına taşmaz zaten (daire içi kırpılır)
    eski = cv2.imread(os.path.join(KLASOR, "kamera_maske.png"), 0) > 0
    yeni = m > 0
    ozet = {"aciklama": "Gercek robot govde maskesi, 14 Kasim 2026 kayitlarindan elle izlendi (gercek_maske.py).",
            "cokgenler": COKGENLER,
            "piksel": {"eski": int(eski.sum()), "yeni": int(yeni.sum()), "yalniz_eski": int((eski & ~yeni).sum()),
                       "yalniz_yeni": int((~eski & yeni).sum()), "ortak": int((eski & yeni).sum()),
                       "iou_eski_yeni": round(float((eski & yeni).sum() / (eski | yeni).sum()), 3)}}
    json.dump(ozet, open(os.path.join(KLASOR, "kamera_maske_gercek.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    print(ozet["piksel"])
    return m


if __name__ == "__main__":
    yap()
