"""Parametre çalışması grafikleri: python parametre_grafik.py yollar <varyantA> <varyantB> | odak | yan <varyant> [<varyant>...]
Çıktı: kayitlar/gosterim/slam/parametre_*.png. Okuma: kayitlar/slam/parametre/kosu_v2/<varyant>__<tekrar>/ (poz.jsonl.gz, harita.json, olcut.json)."""
import glob
import json
import os
import sys

import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
import parametre_olcut as PO  # noqa: E402
import parametre_ozet as OZ  # noqa: E402

KOK = os.path.abspath(os.path.join(KLASOR, "..", ".."))
KOSU = os.path.join(KOK, "kayitlar", "slam", "parametre", "kosu_v2")
CIKTI = os.path.join(KOK, "kayitlar", "gosterim", "slam")
os.makedirs(CIKTI, exist_ok=True)


def kosular(ad):
    return sorted(glob.glob(os.path.join(KOSU, ad + "__*")))


def olcek_ve_poz(k):
    P, t, izl, c, R = PO.yukle(k)
    o = json.load(open(os.path.join(k, "olcut.json")))
    return t, izl, c, o


def yollar(a, b, ust=None, alt=None):
    """ust/alt: açık koşu klasörü adları listesi (yoksa varyantın ilk 3 koşusu)."""
    fig, ax = plt.subplots(2, 3, figsize=(15, 9))
    for r, ad in enumerate((a, b)):
        liste = (ust if r == 0 else alt) or [os.path.basename(x) for x in kosular(ad)[:3]]
        for j, kn in enumerate(liste[:3]):
            k = os.path.join(KOSU, kn)
            t, izl, c, o = olcek_ve_poz(k)
            s = o.get("olcek_m_birim", 1.0)
            m = izl & (t <= 85)
            x, z = c[m, 0] * s, c[m, 2] * s
            a_ = ax[r, j]
            a_.scatter(x, z, s=3, c=t[m], cmap="viridis")
            ok = np.where(izl)[0]
            i0 = ok[np.argmax(t[ok] >= 3.4)]; ib = ok[np.argmin(abs(t[ok] - 14.4))]; it = ok[np.argmin(abs(t[ok] - 82.4))]
            a_.plot(*(c[i0, [0, 2]] * s), "o", c="red", ms=8, label="bant 1")
            a_.plot(*(c[ib, [0, 2]] * s), "s", c="orange", ms=8, label="bant 2 (2,00 m)")
            a_.plot(*(c[it, [0, 2]] * s), "*", c="magenta", ms=12, label="82,4 sn")
            a_.set_aspect("equal"); a_.grid(alpha=0.3)
            a_.set_title("%s  tekrar %d%s\nyol %.1f m, son-baş %.2f m" % (ad, j, "  PATLADI" if o.get("patladi") else "", o.get("yol_toplam_m", np.nan), o.get("son_baslangic_m", np.nan)), fontsize=9)
            if o.get("patladi"):
                a_.set_xlim(-30, 30); a_.set_ylim(-30, 30)
            if r == 0 and j == 0:
                a_.legend(fontsize=7)
    fig.suptitle("Kuşbakışı kamera yolu, 0-85 sn, bant ölçekli (m). Üst: %s, alt: %s" % (a, b))
    fig.savefig(os.path.join(CIKTI, "parametre_yollar_%s_vs_%s.png" % (a, b)), dpi=100, bbox_inches="tight")


def odak():
    adlar = [("odak_-6", -6), ("odak_-3", -3), ("taban", 0), ("odak_+3", 3), ("odak_+6", 6)]
    fig, ax = plt.subplots(1, 3, figsize=(15, 4.5))
    for k, (anahtar, baslik) in enumerate((("donus_yaricap_m_ort", "yerinde dönüş yarıçapı (m)"), ("yol_orani", "toplam yol / 21 m"), ("son_baslangic_m", "82,4 sn başlangıca uzaklık (m)"))):
        for ad, d in adlar:
            for kk in kosular(ad):
                o = json.load(open(os.path.join(kk, "olcut.json")))
                v = o.get(anahtar)
                if v is None:
                    continue
                o["_olcut2"] = json.load(open(os.path.join(kk, "olcut2.json"))) if os.path.exists(os.path.join(kk, "olcut2.json")) else {}
                bz = OZ.bozuk(o)
                ax[k].plot(d + np.random.uniform(-0.2, 0.2), v, "x" if bz else "o", c="red" if bz else "tab:blue")
            med = []
            for kk in kosular(ad):
                oo = json.load(open(os.path.join(kk, "olcut.json")))
                oo["_olcut2"] = json.load(open(os.path.join(kk, "olcut2.json"))) if os.path.exists(os.path.join(kk, "olcut2.json")) else {}
                if not OZ.bozuk(oo):
                    med.append(oo.get(anahtar))
            med = [m for m in med if m is not None]
            if med:
                ax[k].plot(d, np.median(med), "_", c="k", ms=25, mew=2)
        ax[k].set_xlabel("odak çarpanı (%)"); ax[k].set_title(baslik); ax[k].grid(alpha=0.3)
    fig.suptitle("Odak ölçeği eğrisi (mavi: iyi koşu, kırmızı x: ölçek tutarsız, siyah çizgi: iyilerin ortancası)")
    fig.savefig(os.path.join(CIKTI, "parametre_odak_egrisi.png"), dpi=100, bbox_inches="tight")


def yan(*adlar):
    fig, ax = plt.subplots(1, len(adlar), figsize=(6 * len(adlar), 5), squeeze=False)
    for j, ad in enumerate(adlar):
        for k in kosular(ad)[:1]:
            o = json.load(open(os.path.join(k, "olcut.json")))
            s = o.get("olcek_m_birim", 1.0)
            hm = json.load(open(os.path.join(k, "harita.json")))
            L = np.array(hm["noktalar"]).reshape(-1, 5)[:, :3]
            KF = np.array([np.array(q["poz_wc"]).reshape(4, 4)[:3, 3] for q in hm["anahtar_kareler"]])
            dd = np.linalg.norm(L[:, None, [0, 2]] - KF[None, :, [0, 2]], axis=2)
            en = np.argmin(dd, axis=1)
            yat = dd[np.arange(len(L)), en] * s
            yuk = -(L[:, 1] - KF[en, 1]) * s          # kameranın üstü +
            a_ = ax[0, j]
            a_.scatter(yat, yuk, s=3, alpha=0.5)
            a_.axhline(-0.17, c="r", lw=1, label="zemin (-0,17 m)"); a_.axhline(0, c="k", lw=0.5)
            a_.set_xlim(0, 12); a_.set_ylim(-1, 4)
            a_.set_xlabel("kamera yoluna yatay uzaklık (m)"); a_.set_ylabel("kameraya göre yükseklik (m)")
            alt = (yuk < -0.08).mean()
            a_.set_title("%s (tekrar 0)\n%d nokta, zemin yakını (<-0,08 m): %%%.0f" % (ad, len(L), 100 * alt), fontsize=9)
            a_.legend(fontsize=8)
    fig.savefig(os.path.join(CIKTI, "parametre_yan_gorunum.png"), dpi=100, bbox_inches="tight")


if __name__ == "__main__":
    k = sys.argv[1]
    if k == "yollar":
        yollar(sys.argv[2], sys.argv[3], sys.argv[4].split(",") if len(sys.argv) > 4 else None, sys.argv[5].split(",") if len(sys.argv) > 5 else None)
    elif k == "odak":
        odak()
    else:
        yan(*sys.argv[2:])
