"""Zemin sınırı taramalarından tarama-tarama konum artımının doğruluğu (birlesik_tarama.py), 15 geliştirme kaydında. Konum kaynağı olarak
neden BIRAKILDIĞININ kanıtı. Her ardışık tarama çifti (5 kare = 1/3 sn) için: arama merkezi = komut tahmini, yaw farkı = M2D (komutlu),
sonuç = skor yüzeyinin soft-argmax'ı; ölçüt gerçek artıma göre. 'komut' sütunu: aynı aralıkta salt komut tahmininin hatası.
Kullanım: python birlesik_tarama_olc.py   (çıktı: kayitlar/sonuclar/birlesik/tarama_deneyi.txt)"""
import os
import sys
import warnings

import numpy as np

warnings.filterwarnings("ignore")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import birlesik_ortak as bo  # noqa: E402
import birlesik_tarama as bt  # noqa: E402
import konum_ortak as ko  # noqa: E402
from kayit import Kayit  # noqa: E402

if __name__ == "__main__":
    p = bt.EsleParam()
    satir = ["kayit | cift | tarama err ort/%90 (cm) | KOMUT err ort/%90 (cm) | hareket yonunde ort. yanlilik (cm) / ort. adim (cm) | kapsama (iceride oran)"]
    for ad in bo.GEL[6:] + bo.SIN:
        s = bo.spec(ad)
        kay = Kayit(s["theta"])
        gk = kay.gercek_konum()
        yz = bo.npz(bo.yon_yolu(s, True))
        tz = bo.npz(s["tar"])
        menz = float(tz["max_menzil"])
        t = kay.t_ms
        vcmd = np.array([ko.komut_hizlari(kay.bilgi, *kay.komut(int(tt)))[0] for tt in t])
        dt = np.r_[0, np.diff(t) / 1000]
        ks = list(tz["kare"])
        E, C, A, G = [], [], [], []
        for a, b in zip(ks[:-1], ks[1:]):
            if b - a != 5:
                continue
            gp = (gk["x_m"][a], gk["y_m"][a], gk["yon_derece"][a])
            gc = (gk["x_m"][b], gk["y_m"][b], gk["yon_derece"][b])
            g = np.array(ko.dunya_to_robot(gp, gc[0], gc[1])).ravel()
            if np.hypot(*g) < 0.03:
                continue
            poz = (0, 0, 0.0)
            for i in range(a + 1, b + 1):
                poz = ko.merkez_ilerle(poz, vcmd[i] * dt[i], float(ko.sar(yz["yon"][i] - yz["yon"][i - 1])))
            tp = np.array(ko.dunya_to_robot((0, 0, 0.0), poz[0], poz[1])).ravel()
            delta = float(ko.sar(yz["yon"][b] - yz["yon"][a]))
            ja, jb = ks.index(a), ks.index(b)
            A_ = bt.Alan(bt.noktalar(tz["mesafe"][ja], tz["guven"][ja], menz, p), menz, p)
            r = bt.esle(A_, bt.noktalar(tz["mesafe"][jb], tz["guven"][jb], menz, p), delta, tp, p)
            if r is None:
                continue
            e = r["tsoft"] - g
            E.append(np.hypot(*e))
            C.append(np.hypot(*(tp - g)))
            A.append(e @ (g / np.hypot(*g)))
            G.append(np.hypot(*g))
        E, C, A, G = map(np.array, (E, C, A, G))
        satir.append("%-18s | %3d | %.1f / %.1f | %.1f / %.1f | %+.1f / %.1f" % (
            ad, len(E), 100 * E.mean(), 100 * np.percentile(E, 90), 100 * C.mean(), 100 * np.percentile(C, 90), 100 * A.mean(), 100 * G.mean()))
        print(satir[-1], flush=True)
    os.makedirs(os.path.join(bo.CIKTI), exist_ok=True)
    with open(os.path.join(bo.CIKTI, "tarama_deneyi.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(satir) + "\n")
