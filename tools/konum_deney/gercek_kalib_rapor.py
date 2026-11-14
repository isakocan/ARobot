"""Kalibrasyon raporu: sektor hiz cubuklari (once/sonra), olcum_ham ilk duz gidis egrisi, tum kayitlarda B hizi (once/sonra), B faz duyarliligi. Girdi: eslesme_*.npz, bev_*.npz (gercek_kalib_bev.py kos).
  python gercek_kalib_rapor.py   -> kalib/sektor_hiz_once_sonra.png, kalib/olcum_ilk_gidis_once_sonra.png, kalib/bev_ozet.json (+ ekran tablolari)"""
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
import gercek_kalib_coz as kc  # noqa: E402
import gercek_kalib_bev as kb  # noqa: E402
from kayit import Kayit  # noqa: E402

plt.rcParams["font.family"] = "DejaVu Sans"
KAYITLAR = ["2a_vitessiz_duz", "2b_otomatik_duz", "4_parke_vitessiz", "5_saksi_turu", "6c_karanlik_surus", "olcum_ham"]
ETIKET = {"2a_vitessiz_duz": "2a halı", "2b_otomatik_duz": "2b halı", "4_parke_vitessiz": "4 parke", "5_saksi_turu": "5 saksı parke", "6c_karanlik_surus": "6c karanlık", "olcum_ham": "olcum_ham parlak parke"}


def yol_egri(ad, konf):
    z = np.load(kb.yol(ad, konf))
    di = np.where(z["ok"] & np.isfinite(z["d"][:, 1]), z["d"][:, 1], 0.0)
    return z["t"], np.cumsum(di), z


def duz_hiz(ad, konf):
    """gercek_ilerleme ile ayni olcut: kararli duz gidis karelerinde (komut 3 kare gecikmeli) hareket yonunde ileri hiz (B gecerli kareler), m/sn; ve B gecerli orani."""
    z = np.load(kb.yol(ad, konf))
    kay = Kayit(os.path.join(go.GERCEK, ad))
    t = kay.t_ms / 1000.0
    kom = np.array([kay.komut(int(x)) for x in kay.t_ms])
    kg = np.vstack([np.zeros((3, 2)), kom[:-3]])[z["idx"]]
    n = len(z["idx"])
    kar = np.zeros(n, bool)
    for i in range(12, n):
        w = kg[i - 12:i + 1]
        kar[i] = (w.max(0) - w.min(0)).max() < 20
    duz = (np.abs(kg[:, 0] - kg[:, 1]) < 25) & (np.abs(kg[:, 0]) > 100) & kar
    tt = z["t"]
    dt = np.clip(np.diff(tt, prepend=tt[0] - 1 / 15.0), 0.02, 0.3)
    isaret = np.sign(kg[:, 0])
    out = {}
    for ad_y, m0 in (("ileri", isaret > 0), ("geri", isaret < 0)):
        m = duz & m0
        mo = m & z["ok"] & np.isfinite(z["d"][:, 1])
        if m.sum() < 15 or mo.sum() < 8:
            out[ad_y] = None
            continue
        out[ad_y] = dict(hiz=float(np.sum(z["d"][mo, 1] * isaret[mo]) / np.sum(dt[mo])), gecerli=float(mo.sum() / m.sum()), kare=int(m.sum()))
    return out


def ana():
    z = json.load(open(os.path.join(km.ONB, "coz_2a+4.json")))
    PA = z["A"]["parametre"]
    P0 = km.nominal()
    # 1. sektor hizlari once/sonra
    once = kc.sektor_olc(P0, KAYITLAR)
    sonra = kc.sektor_olc(PA, KAYITLAR)
    fig, axs = plt.subplots(2, 3, figsize=(13, 7), sharey=True)
    for ax, ad in zip(axs.ravel(), KAYITLAR):
        a, b = once[ad], sonra[ad]
        x = np.arange(4)
        ax.bar(x - 0.2, [a["sektor"][k] for k in ("on", "sag", "arka", "sol")], 0.38, color="tab:red", label="önce: yayılım %s" % ("%.0f%%" % (100 * a["yayilim"]) if np.isfinite(a["yayilim"]) else "-"))
        ax.bar(x + 0.2, [b["sektor"][k] for k in ("on", "sag", "arka", "sol")], 0.38, color="tab:green", label="sonra: yayılım %s" % ("%.0f%%" % (100 * b["yayilim"]) if np.isfinite(b["yayilim"]) else "-"))
        ax.set_xticks(x)
        ax.set_xticklabels(["ön", "sağ", "arka", "sol"])
        ax.set_title(ETIKET[ad], fontsize=10)
        ax.legend(fontsize=8, loc="lower right")
        ax.grid(alpha=0.3, axis="y")
    axs[0, 0].set_ylabel("zemin hızı, sektör medyanı (m/sn)")
    axs[1, 0].set_ylabel("zemin hızı, sektör medyanı (m/sn)")
    fig.suptitle("Düz gidişte sektör (±50°) zemin hızları: kamera.json (önce) ve kamera_gercek.json (sonra). Yayılım = (en büyük - en küçük) / ortalama", fontsize=10)
    fig.tight_layout()
    fig.savefig(os.path.join(km.KALIB, "sektor_hiz_once_sonra.png"), dpi=100)
    plt.close(fig)
    ozet = dict(sektor_once=once, sektor_sonra=sonra)
    # 2. olcum_ham ilk duz gidis
    fig, ax = plt.subplots(figsize=(10.5, 5.2))
    for konf, ad, renk, stil in (("nom_g_tum", "önce: tüm azimut B (kamera.json)", "tab:red", "-"), ("A_g_tum", "sonra: tüm azimut B (kamera_gercek.json)", "tab:green", "-"),
                                 ("A_s_tum", "sonra + sabit noktalar maskeli (tüm azimut)", "tab:blue", "-"), ("nom_g_on", "önce: yalnız ön ±50° (kamera.json)", "tab:red", "--"),
                                 ("A_g_on", "sonra: yalnız ön ±50°", "tab:green", "--")):
        t, y, _ = yol_egri("olcum_ham", konf)
        ax.plot(t, y, stil, color=renk, label="%s: 14,4 sn'de %.2f m" % (ad, y[np.searchsorted(t, 14.4) - 1]))
    ax.axhline(2.0, color="k", ls=":", lw=1.5, label="gerçek: bantlar arası 2,00 m")
    ax.axvspan(3.4, 8.8, color="0.9", zorder=0)
    ax.text(3.5, 2.12, "tam gaz 3,4-8,8 sn", fontsize=8)
    ax.set_xlim(2.5, 20)
    ax.set_ylim(-0.1, 2.4)
    ax.set_xlabel("zaman (sn)")
    ax.set_ylabel("birikimli ileri yol, kuşbakışı zemin ölçümü B (m)")
    ax.set_title("olcum_ham, ilk düz gidiş: B ölçümünün 2,00 m'ye karşı yolu (h = 0,17 m sabit)", fontsize=10)
    ax.legend(fontsize=8, loc="lower right")
    ax.grid(alpha=0.3)
    fig.tight_layout()
    fig.savefig(os.path.join(km.KALIB, "olcum_ilk_gidis_once_sonra.png"), dpi=105)
    plt.close(fig)
    # tablo: olcum konfigurasyonlari (faz ortalamasi)
    tablo = {}
    for (k, m, sk) in (("nom", "g", "tum"), ("A", "g", "tum"), ("B", "g", "tum"), ("nom", "s", "tum"), ("A", "s", "tum"), ("nom", "r", "tum"), ("A", "r", "tum"),
                       ("nom", "g", "on"), ("A", "g", "on"), ("nom", "g", "sag"), ("A", "g", "sag"), ("nom", "g", "arka"), ("A", "g", "arka"), ("nom", "g", "sol"), ("A", "g", "sol"),
                       ("nom", "s", "on"), ("A", "s", "on"), ("nom", "s", "sag"), ("A", "s", "sag"), ("nom", "s", "sol"), ("A", "s", "sol")):
        adlar = ["%s_%s_%s" % (k, m, sk)] + ["%s_%s_%s_o%d" % (k, m, sk, n) for n in range(1, 9)]
        v, mx, v9 = [], [], []
        for nm in adlar:
            if not os.path.exists(kb.yol("olcum_ham", nm)):
                continue
            t, y, _ = yol_egri("olcum_ham", nm)
            v.append(y[np.searchsorted(t, 14.4) - 1]); mx.append(y.max()); v9.append(y[np.searchsorted(t, 9.0) - 1])
        if v:
            tablo["%s_%s_%s" % (k, m, sk)] = dict(yol_14_4=float(np.mean(v)), std=float(np.std(v)), n=len(v), maks=float(np.mean(mx)), yol_9_0=float(np.mean(v9)))
    ozet["olcum_ham_B_yol"] = tablo
    print("olcum_ham ilk duz gidis, B yolu @14,4 sn (faz ortalamasi +- std, gercek 2,00):")
    for k, v in tablo.items():
        print("  %-12s %.2f +- %.2f (n=%d)  maks %.2f  @9,0 %.2f  oran %.2f  h(2,00 verecek) %.3f" % (k, v["yol_14_4"], v["std"], v["n"], v["maks"], v["yol_9_0"], v["yol_14_4"] / 2.0, 0.17 * 2.0 / v["yol_14_4"]))
    # 3. tum kayitlarda B hizi
    hiz = {}
    print("\nB hizi (tum azimut / on +-50), kararli duz gidis, m/sn; B gecerli orani:")
    for ad in KAYITLAR:
        hiz[ad] = {}
        for konf in ("nom_g_tum", "A_g_tum", "nom_g_on", "A_g_on", "nom_s_tum", "A_s_tum"):
            if os.path.exists(kb.yol(ad, konf)):
                hiz[ad][konf] = duz_hiz(ad, konf)
        s = ad
        for yon in ("ileri", "geri"):
            sat = []
            for konf in ("nom_g_tum", "A_g_tum", "nom_g_on", "A_g_on", "nom_s_tum", "A_s_tum"):
                r = hiz[ad].get(konf, {}).get(yon)
                sat.append("%s %s" % (konf, "-" if r is None else "%.3f(%.2f)" % (r["hiz"], r["gecerli"])))
            print("  %-18s %-5s %s" % (s, yon, "  ".join(sat)))
    ozet["B_hiz_duz"] = hiz
    json.dump(ozet, open(os.path.join(km.KALIB, "bev_ozet.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False, default=float)


if __name__ == "__main__":
    ana()
