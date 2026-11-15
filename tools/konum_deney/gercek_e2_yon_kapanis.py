"""M2D birikimli yon kapanisi: ESKI geometri (kamera.json, onbellek_gercek) ve KALIBRE (kalibS), LCN M2D komutsuz+komutlu; referans = ayni geometrideki dogrudan eslesme.
  python gercek_e2_yon_kapanis.py   (once: gercek_e2_kapanis.py ve gercek_e2_kapanis.py eski)"""
import json
import os
import sys

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_ortak as go  # noqa: E402
from kayit import Kayit  # noqa: E402

E2D = os.path.join(go.SONUC, "e2")
ONB = {"eski": os.path.join(go.SONUC, "onbellek_gercek"), "kalib": os.path.join(go.SONUC, "onbellek_gercek_kalibS")}
DOG = {"eski": "kapanis_dogrudan_eski.json", "kalib": "kapanis_dogrudan.json"}


def sar(x):
    return (x + 180.0) % 360.0 - 180.0


out = {}
for ad in ("olcum_ham", "3_vitessiz_donus", "5_saksi_turu", "2a_vitessiz_duz"):
    kay = Kayit(os.path.join(go.GERCEK, ad))
    t = kay.t_ms / 1000.0
    for g in ("eski", "kalib"):
        d = json.load(open(os.path.join(E2D, DOG[g])))[ad][0]
        for src in ("komutsuz", "komutlu"):
            p = os.path.join(ONB[g], "yonL_%s_%s.npz" % (src, ad))
            if not os.path.exists(p):
                print(ad, g, src, "ölçülemedi (önbellek yok)")
                continue
            y = np.load(p)["yon"]
            i = int(np.argmin(abs(t - 82.5))) if ad == "olcum_ham" else len(y) - 1
            tot = float(np.abs(np.diff(y)).sum())
            hata = float(sar((y[i] - y[0]) - d["dogrudan_yaw"]))
            out["%s_%s_%s" % (ad, g, src)] = dict(zincir=float(y[i] - y[0]), dogrudan=d["dogrudan_yaw"], ic=d["ic"], hata=hata, toplam_donus=tot)
            print("%-18s %-6s %-9s zincir(mod360) %8.1f  doğrudan %7.1f (ic %3d)  kapanış hatası %6.1f°  (toplam |dönüş| %.0f°)" % (ad, g, src, sar(y[i] - y[0]), d["dogrudan_yaw"], d["ic"], hata, tot))
json.dump(out, open(os.path.join(E2D, "yon_kapanis.json"), "w", encoding="utf-8"), indent=1)
