"""Kayıt (çift balık gözü, kayit.py biçimi) -> eşdikdörtgen kare dizisi, bizim kalibre modelimizle (kamera_gercek.json).
Çıktı: kareler yerçekimine hizalı (kalibre montaj: yaw/pitch/roll telafili), robot ilerisi görüntü ortasında, 1280x640 (derece başına 3,56 px, yükseltme YOK).
  <cikti>/000000.jpg ...   (sıra numarası; stella_vslam run_image_slam bu sıralamayla okur)
  <cikti>/zamanlar.csv     sira,kare,t_ms (özgün kare zamanları korunur)
  <cikti>/maske.png        stella_vslam maskesi: 255 = kullan, 0 = kullanma (robot gövdesi ve robotla birlikte hareket eden her şey)
Kullanım: python ekdortgen_donustur.py <kayit_klasoru> <cikti_klasoru> [--bas N] [--bitis N] [--adim N] [--is 6]
Bu dosya sınıf olarak da kullanılır: Donusturucu(...).kare(jpeg_bayt) -> eşdikdörtgen BGR."""
import argparse
import csv
import os
import sys
from multiprocessing import Pool

import cv2
import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
KONUM = os.path.abspath(os.path.join(KLASOR, "..", "konum_deney"))
sys.path.insert(0, KONUM)
from kayit import Kayit  # noqa: E402
from gercek_kalib_model import KameraGercek  # noqa: E402
import json  # noqa: E402

G, Y = 1280, 640
AYAR = os.path.join(KONUM, "kamera_gercek.json")
MASKE_BALIKGOZU = os.path.join(KONUM, "kamera_maske_gercek_sabit.png")   # 255 = gövde (balık gözü pikselleri)


class Donusturucu:
    def __init__(self, g=G, y=Y, ayar=AYAR, maske_yolu=MASKE_BALIKGOZU, f_olcek=1.0, m1=(0.0, 0.0), m2=(0.0, 0.0), enterpolasyon=cv2.INTER_LINEAR,
                 maske_modu="taban", clahe=None):
        """f_olcek: iki merceğin f'sine çarpan (piksel/radyan). m1, m2: mercek merkezi kayması (dx, dy piksel). enterpolasyon: remap yöntemi.
        maske_modu: 'taban' (blok şeklindeki sabit maske + 2 px güvenlik) | 'dar' (yalnız büyük gövde parçası, sabit yansıma yamaları ve güvenlik payı yok).
        clahe: None | (sınır, kutu) örn (2.0, 8): L kanalına yerel kontrast."""
        with open(ayar, encoding="utf-8") as f:
            aj = json.load(f)
        aj["lens1"]["f"] *= f_olcek
        aj["lens2"]["f"] *= f_olcek
        aj["lens1"]["merkez_x"] += m1[0]; aj["lens1"]["merkez_y"] += m1[1]
        aj["lens2"]["merkez_x"] += m2[0]; aj["lens2"]["merkez_y"] += m2[1]
        self.kam = KameraGercek(aj)
        self.interp = enterpolasyon
        self.clahe = cv2.createCLAHE(clipLimit=clahe[0], tileGridSize=(clahe[1], clahe[1])) if clahe else None
        self.g, self.y = g, y
        tab, w1, w2 = self.kam._esd_tablo(g, y)
        self.tab = tab
        toplam = np.maximum(w1 + w2, 1e-4)
        self.k1 = (w1 / toplam)[..., None]
        self.k2 = (w2 / toplam)[..., None]
        self.kapsam = (w1 + w2) > 0
        k1 = (w1 / toplam)
        self.sade1 = (k1 >= 0.9999).astype(np.uint8)          # yalnız lens 1 (hızlı yol)
        self.bant = np.where((k1 > 0.0001) & (k1 < 0.9999))    # karışım bandı (az piksel): yalnız orada kayan nokta
        self.kb1 = k1[self.bant][:, None].astype(np.float32)
        # maske: aynı tablolarla balık gözü maskesi -> eşdikdörtgen (en yakın komşu), her lens kendi ağırlığıyla
        bm = cv2.imread(maske_yolu, cv2.IMREAD_GRAYSCALE)
        ma = cv2.remap(bm, tab[0][0], tab[0][1], cv2.INTER_NEAREST, borderMode=cv2.BORDER_CONSTANT, borderValue=255).astype(np.float32) / 255
        mb = cv2.remap(bm, tab[1][0], tab[1][1], cv2.INTER_NEAREST, borderMode=cv2.BORDER_CONSTANT, borderValue=255).astype(np.float32) / 255
        gov = ma * self.k1[..., 0] + mb * self.k2[..., 0]
        kullan = (gov < 0.25) & self.kapsam          # karışım bandında her iki lens da gövdeyi görmüyorsa kullan; temkinli: %25 üstü gövde = maskele
        if maske_modu == "dar":
            gov_m = ((~kullan) | (gov >= 0.25)).astype(np.uint8)
            n, lab, st, _ = cv2.connectedComponentsWithStats(gov_m, connectivity=8)
            buyuk = np.zeros_like(gov_m)
            for i in range(1, n):
                if st[i, cv2.CC_STAT_AREA] > 0.03 * g * y:
                    buyuk[lab == i] = 1
            kullan = (buyuk == 0) & self.kapsam
            kullan = kullan.astype(np.uint8)
        else:
            kullan = cv2.erode(kullan.astype(np.uint8), np.ones((5, 5), np.uint8))      # kenarda 2 px güvenlik
        self.maske = (kullan * 255).astype(np.uint8)

    def kare(self, jpeg_bayt_ya_da_bgr):
        im = jpeg_bayt_ya_da_bgr
        if isinstance(im, (bytes, bytearray)):
            im = cv2.imdecode(np.frombuffer(im, np.uint8), cv2.IMREAD_COLOR)
        a = cv2.remap(im, self.tab[0][0], self.tab[0][1], self.interp, borderMode=cv2.BORDER_CONSTANT)
        b = cv2.remap(im, self.tab[1][0], self.tab[1][1], self.interp, borderMode=cv2.BORDER_CONSTANT)
        self._b0 = b.copy() if len(self.bant[0]) else b
        cv2.copyTo(a, self.sade1, b)                          # b: lens 1 baskın yerlerde a, öbür yerlerde b
        bb = self.bant
        if len(bb[0]):
            b[bb] = np.clip(a[bb].astype(np.float32) * self.kb1 + self._b0[bb].astype(np.float32) * (1 - self.kb1) + 0.5, 0, 255).astype(np.uint8)
        if self.clahe is not None:
            lab = cv2.cvtColor(b, cv2.COLOR_BGR2LAB)
            lab[..., 0] = self.clahe.apply(lab[..., 0])
            b = cv2.cvtColor(lab, cv2.COLOR_LAB2BGR)
        return b


_D = {}


def _is(args):
    kayit_yol, cikti, idxler, kalite = args
    if "d" not in _D:
        _D["d"] = Donusturucu()
        _D["k"] = Kayit(kayit_yol)
    d, k = _D["d"], _D["k"]
    for s, i in idxler:
        e = d.kare(k.jpeg_bayt(i))
        cv2.imwrite(os.path.join(cikti, "%06d.jpg" % s), e, [cv2.IMWRITE_JPEG_QUALITY, kalite])
    return len(idxler)


def donustur(kayit_yol, cikti, bas=0, bitis=None, adim=1, is_sayisi=6, kalite=95):
    os.makedirs(cikti, exist_ok=True)
    k = Kayit(kayit_yol)
    n = len(k)
    bitis = n if bitis is None else min(bitis, n)
    idx = list(range(bas, bitis, adim))
    with open(os.path.join(cikti, "zamanlar.csv"), "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["sira", "kare", "t_ms"])
        for s, i in enumerate(idx):
            w.writerow([s, int(k.kare_numaralari[i]), int(k.t_ms[i])])
    d = Donusturucu()
    cv2.imwrite(os.path.join(cikti, "maske.png"), d.maske)
    isler = list(enumerate(idx))
    parca = [isler[j::is_sayisi * 8] for j in range(is_sayisi * 8)]
    with Pool(is_sayisi) as p:
        topl = sum(p.map(_is, [(kayit_yol, cikti, pc, kalite) for pc in parca if pc]))
    return topl


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("kayit")
    ap.add_argument("cikti")
    ap.add_argument("--bas", type=int, default=0)
    ap.add_argument("--bitis", type=int, default=None)
    ap.add_argument("--adim", type=int, default=1)
    ap.add_argument("--is", dest="is_sayisi", type=int, default=6)
    a = ap.parse_args()
    print(donustur(a.kayit, a.cikti, a.bas, a.bitis, a.adim, a.is_sayisi), "kare yazıldı")
