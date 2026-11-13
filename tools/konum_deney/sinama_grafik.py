"""Sinama: kayıt başına zaman serisi grafiği (konum hatası A/C/D, yaw hatası, BEV ok/ic, M2D içerik). Yalnızca okur."""
import os, sys
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sinama_calistir as sc, konum_ortak as ko
from kayit import Kayit
KON = os.path.join(sc.SONUC, "konum"); CIK = os.path.join(sc.SONUC, "analiz"); os.makedirs(CIK, exist_ok=True)
for ad in sc.ADLAR:
    g = Kayit(os.path.join(sc.SINAMA, "theta", ad)).gercek_konum(); n = len(g["x_m"]); t = np.arange(n) / 15
    fig, ax = plt.subplots(4, 1, figsize=(11, 9), sharex=True)
    for v, c in (("A", "tab:blue"), ("C", "tab:red"), ("D", "tab:green")):
        P = np.load(os.path.join(KON, "log_%s_%s.npz" % (v, ad)))["P"]
        # hizali: baslangic dogru verildigi icin dogrudan
        ax[0].plot(t, np.hypot(P[:, 0] - g["x_m"], P[:, 1] - g["y_m"]), c, label=v)
        ax[1].plot(t, ko.sar(P[:, 2] - g["yon_derece"]), c, label=v)
    ax[0].set_ylabel("konum hatasi m"); ax[0].legend(); ax[1].set_ylabel("yaw hatasi derece")
    bz = np.load(os.path.join(KON, "onbellek", "bevT_komutsuz_theta_%s.npz" % ad)); yz = np.load(os.path.join(KON, "onbellek", "yon_komutsuz_theta_%s.npz" % ad))
    ax[2].plot(t, bz["ic"], "k", lw=0.8, label="BEV ic"); ax[2].plot(t, np.where(bz["ok"], 0, -3), "r.", ms=2, label="BEV ok=False")
    ax[2].set_ylabel("BEV ic"); ax[2].legend()
    ax[3].plot(t, yz["icerik"], "m", lw=0.8); ax[3].set_ylabel("M2D icerik"); ax[3].set_xlabel("sn")
    fig.suptitle(ad); fig.tight_layout(); fig.savefig(os.path.join(CIK, "zaman_%s.png" % ad), dpi=80); plt.close(fig)
print("tamam")
