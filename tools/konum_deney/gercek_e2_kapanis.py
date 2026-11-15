"""Kalibre geometri (kalibS) ile DOGRUDAN M2D eslesmeleri (kare i <-> kare j, +-180 derece pencere, komutsuz, yontem_pusula_nokta.Yontem aynen).
  python gercek_e2_kapanis.py  -> kayitlar/sonuclar/gercek/e2/kapanis_dogrudan.json
olcum_ham: t=0,5 <-> 82,5 / 100 / 133 ; 5_saksi_turu: ilk <-> son ; 3_vitessiz_donus: ilk <-> son ; 2a: ilk <-> son."""
import json
import os
import sys

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_e2_hazirla as hz  # noqa: E402

ISLER = {"olcum_ham": [(0.5, 82.5), (0.5, 100.0), (0.5, 133.0)], "5_saksi_turu": [("ilk", "son")], "3_vitessiz_donus": [("ilk", "son")], "2a_vitessiz_duz": [("ilk", "son")]}


def ana():
    eski = len(sys.argv) > 1 and sys.argv[1] == "eski"
    if eski:
        import gercek_ortak as go
        go.kur("gercek")
    else:
        hz.kur()
    import yontem_pusula_nokta as ypn
    from kayit import Kayit
    from kamera import Kamera
    out = {}
    for ad, cift in ISLER.items():
        kay = Kayit(os.path.join(hz.ok.go.GERCEK, ad))
        kam = Kamera.bilgiden(kay.bilgi)
        y = ypn.Yontem(dict(kay.bilgi), kam, kip="duzlem", komut_oncel=False)
        t = kay.t_ms / 1000.0

        def idx(x):
            return 0 if x == "ilk" else (len(kay) - 1 if x == "son" else int(np.argmin(abs(t - x))))

        def oz(i):
            gri, gec = y.don(kay.resim(i))
            return y._cikar(gri, gec)
        out[ad] = []
        for a, b in cift:
            ia, ib = idx(a), idx(b)
            dy, kal, tamam, bl = y._esle(oz(ib), oz(ia), 0.0, 180.0)
            out[ad].append(dict(kare_a=ia, kare_b=ib, t_a=float(t[ia]), t_b=float(t[ib]), dogrudan_yaw=float(dy), ic=int(bl.get("icerik", 0)), kalite=float(kal), tamam=bool(tamam)))
            print(ad, ia, ib, "doğrudan yaw %.2f ic %d kalite %.2f" % (dy, bl.get("icerik", 0), kal), flush=True)
    json.dump(out, open(os.path.join(hz.ok.go.SONUC, "e2", "kapanis_dogrudan_eski.json" if eski else "kapanis_dogrudan.json"), "w", encoding="utf-8"), indent=1)


if __name__ == "__main__":
    ana()
