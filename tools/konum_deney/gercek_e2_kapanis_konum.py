"""Kapanis konumu siniri: olcum_ham'da t=0,5 ile 82,5/100/133 karelerinin ZEMIN DOKUSU (LCN BEV, kalibre geometri + sabit maske) ile DOGRUDAN eslesmesi:
iki kare arasi robot ote lemesi (kamera cercevesi, sag/ileri m) ve eslesme sayisi. Goreli yaw = dogrudan M2D (-0,7 derece) kullanilir.
  python gercek_e2_kapanis_konum.py -> e2/kapanis_konum.json ; 5_saksi_turu ilk-son kare icin de (goreli yaw -7,9)."""
import json
import os
import sys

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_e2_hazirla as hz  # noqa: E402


def ana():
    bo, ko, zo = hz.kur()
    import birlesik_bev as bb
    import konum_bev as kb
    from kayit import Kayit
    from kamera import Kamera
    out = {}
    isler = {"olcum_ham": [(0.5, 82.5, -0.74), (0.5, 100.0, -0.79), (0.5, 133.0, -0.80)], "5_saksi_turu": [("ilk", "son", -7.93)]}
    for ad, cift in isler.items():
        kay = Kayit(os.path.join(hz.ok.go.GERCEK, ad))
        kam = Kamera.bilgiden(kay.bilgi)
        tab = bb.LcnTablo("theta", kam, lcn_par=bb.LcnParam, h=float(kay.bilgi.get("kamera_yuksekligi_m", 0.255)))
        t = kay.t_ms / 1000.0
        out[ad] = []
        for a, b, dy in cift:
            ia = 0 if a == "ilk" else int(np.argmin(abs(t - a)))
            ib = len(kay) - 1 if b == "son" else int(np.argmin(abs(t - b)))
            res = []
            for kar in range(0, 7):       # komsu kareler (hareketsiz robot: ayni yer) tekrar -> yayilim
                oz = kb.BevOtelemeci(tab)
                oz.guncelle(kay.resim(max(ia + kar, 0)), 0.0)
                ds, di, ok, ic = oz.guncelle(kay.resim(min(ib + kar, len(kay) - 1)), dy)
                res.append((float(ds), float(di), bool(ok), int(ic)))
            iyi = [r for r in res if r[2] and r[3] >= 8]
            if iyi:
                A = np.array([[r[0], r[1]] for r in iyi])
                m = np.median(A, 0)
                out[ad].append(dict(kare_a=ia, kare_b=ib, n=len(iyi), ds=float(m[0]), di=float(m[1]), uzaklik=float(np.hypot(*m)), yayilim=float(np.ptp(np.hypot(A[:, 0], A[:, 1]))), ic=[r[3] for r in res]))
            else:
                out[ad].append(dict(kare_a=ia, kare_b=ib, n=0, not_="eslesme yok (olculemedi)", ic=[r[3] for r in res]))
            print(ad, a, b, out[ad][-1], flush=True)
    json.dump(out, open(os.path.join(hz.ok.go.SONUC, "e2", "kapanis_konum.json"), "w", encoding="utf-8"), indent=1)


if __name__ == "__main__":
    ana()
