"""Görsel pusuladan (M2D, komutsuz) ölçülen dönüş hızı ile motor farkının ilişkisi: derece/sn başına (sol-sag)/510.
Yer gerçeği yok: bu yalnız görsel pusulanın ölçeği + motor-komut tutarlılığı. M2D ölçek hatası kadar belirsiz (bantla/yerinde 360° ile doğrulanmalı).
  python gercek_donus_kazanc.py [maske=eski]"""
import os
import sys

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_ortak as go  # noqa: E402
from kayit import Kayit  # noqa: E402


def ana(maske="eski"):
    for ad in ["3_vitessiz_donus", "4_parke_vitessiz", "5_saksi_turu", "6c_karanlik_surus", "2a_vitessiz_duz"]:
        k = Kayit(os.path.join(go.GERCEK, ad))
        t = k.t_ms / 1000.0
        z = np.load(os.path.join(go.onbellek(maske), "yon_komutsuz_gercek_%s.npz" % ad))
        y = np.degrees(np.unwrap(np.radians(z["yon"])))
        kom = np.array([k.komut(int(x)) for x in k.t_ms])
        # 0,5 sn pencerelerle hız: komut ve görsel
        pen = 8
        best = None
        for lag in range(0, 9):     # motor -> görüntü gecikmesi (kare)
            dy = (y[pen + lag:] - y[lag:-pen]) / (t[pen + lag:] - t[lag:-pen])
            fark = (kom[pen:len(t) - lag, 0] - kom[pen:len(t) - lag, 1])
            fark0 = (kom[:len(t) - pen - lag, 0] - kom[:len(t) - pen - lag, 1])
            ort = 0.5 * (fark + fark0)[:len(dy)] / 510.0       # tam fark = 1 (sol=255, sag=-255)
            m = np.abs(ort) > 0.05
            if m.sum() < 20:
                continue
            kaz = float(np.sum(dy[m] * ort[m]) / np.sum(ort[m] ** 2))
            r = float(np.corrcoef(dy[m], ort[m])[0, 1])
            if best is None or r > best[2]:
                best = (lag, kaz, r, int(m.sum()))
        yer = (np.abs(kom[:, 0] + kom[:, 1]) < 60) & (np.abs(kom[:, 0] - kom[:, 1]) > 250)
        print("%-18s en iyi gecikme %d kare (%.0f ms): kazanç %.0f derece/sn @ tam fark (sol=255,sag=-255) [r=%.2f, n=%d]; yerinde dönüş karesi %d" % (
            ad, best[0], best[0] * 1000 / 15, best[1], best[2], best[3], yer.sum()) if best else (ad, "yetersiz"))


if __name__ == "__main__":
    ana(sys.argv[1] if len(sys.argv) > 1 else "eski")
