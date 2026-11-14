"""Birlesik kestirici E (parametreleri AYNEN; nominal 0,55 m/sn ve 197 derece/sn sim varsayimi DUZELTILMEDI) onceki (kos_gercek) ve kalibre geometri (kos_gercek_kalib) sonuclari.
  python gercek_kalib_e.py [etiket=kalib]   -> kalib/E_once_sonra.json, kalib/E_olcum_ham.png
olcum_ham: t=14,4 sn konumu (gercek 2,00 m), tur sonu (82,4 sn) baslangica uzaklik (gercek ~0), B kullanimi (hareketli), yaw'in komuta dusme orani.
5_saksi_turu: baslangic-bitis uzakligi (gercek ~0, ilk/son kare ayni yer), yol uzunlugu."""
import json
import os
import sys

import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_ortak as go  # noqa: E402
import gercek_kalib_model as km  # noqa: E402

plt.rcParams["font.family"] = "DejaVu Sans"


def yukle(klasor, ad):
    p = os.path.join(go.SONUC, klasor, ad + ".npz")
    return np.load(p) if os.path.exists(p) else None


def ozet(z, ad):
    P, t, kom = z["P"], z["t_ms"] / 1000.0, z["komut"]
    hareket = (np.abs(kom[:, 0]) + np.abs(kom[:, 1])) > 60
    d = lambda ts: float(np.hypot(*P[int(np.argmin(abs(t - ts))), :2]))
    adim = np.hypot(np.diff(P[:, 0]), np.diff(P[:, 1]))
    o = dict(yol_toplam=float(adim.sum()), bev_kullan_hareketli=float(z["bev_kullan"][hareket].mean()), yaw_komut=float(z["yaw_komut"].mean()), kare_red=float(z["kare_red"].mean()),
             bitis_baslangic_uzaklik=float(np.hypot(*P[-1, :2])), bitis_yon=float(P[-1, 2] % 360))
    if ad == "olcum_ham":
        for ts in (8.8, 14.4, 22.8, 82.4, 100.0, 133.0):
            o["uzaklik_t%.1f" % ts] = d(ts)
        o["maks_uzaklik_0_14_4"] = float(np.hypot(P[t <= 14.4, 0], P[t <= 14.4, 1]).max())
        o["yon_t82.4"] = float(P[int(np.argmin(abs(t - 82.4))), 2] % 360)
    return o


def ana(etiket="kalib"):
    sonuc = {}
    for ad in ("olcum_ham", "5_saksi_turu"):
        a, b = yukle("kos_gercek", ad), yukle("kos_gercek_" + etiket, ad)
        if a is None or b is None:
            continue
        sonuc[ad] = dict(once=ozet(a, ad), sonra=ozet(b, ad))
        for k in sonuc[ad]["once"]:
            print("%-12s %-26s once %8.3f sonra %8.3f" % (ad, k, sonuc[ad]["once"][k], sonuc[ad]["sonra"][k]))
    json.dump(sonuc, open(os.path.join(km.KALIB, "E_once_sonra.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    for ad in sonuc:
        a, b = yukle("kos_gercek", ad), yukle("kos_gercek_" + etiket, ad)
        fig, ax = plt.subplots(1, 3, figsize=(16, 5.2))
        for P, t, r, c in ((a["P"], a["t_ms"] / 1000, "önce (kamera.json)", "tab:red"), (b["P"], b["t_ms"] / 1000, "sonra (kamera_gercek.json)", "tab:green")):
            ax[0].plot(P[:, 0], P[:, 1], color=c, lw=1, label=r)
            ax[1].plot(t, np.hypot(P[:, 0], P[:, 1]), color=c, label=r)
            ax[2].plot(t, P[:, 2] % 360, color=c, lw=0.8, label=r)
        ax[0].plot(0, 0, "ko")
        ax[0].set_aspect("equal")
        ax[0].grid(alpha=0.3)
        ax[0].legend(fontsize=8)
        ax[0].set_title("%s: E yolu (x, y m)" % ad, fontsize=10)
        ax[1].set_title("başlangıçtan uzaklık (m)" + ("; gerçek: 14,4 sn'de 2,00 m, 82,4 sn'de ~0" if ad == "olcum_ham" else "; tur sonunda ~0"), fontsize=9)
        if ad == "olcum_ham":
            ax[1].plot([14.4], [2.0], "k*", ms=12)
            ax[1].plot([82.4], [0.0], "k*", ms=12)
        ax[1].set_xlabel("zaman (sn)")
        ax[1].grid(alpha=0.3)
        ax[1].legend(fontsize=8)
        ax[2].set_title("E yönü (derece)", fontsize=9)
        ax[2].set_xlabel("zaman (sn)")
        ax[2].grid(alpha=0.3)
        fig.tight_layout()
        fig.savefig(os.path.join(km.KALIB, "E_%s.png" % ad), dpi=100)
        plt.close(fig)


if __name__ == "__main__":
    ana(sys.argv[1] if len(sys.argv) > 1 else "kalib")
