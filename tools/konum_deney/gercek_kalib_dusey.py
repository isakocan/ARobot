"""Bagimsiz egim denetimi: panoramadan (sanal iğne deligi gorunumleri) cikarilan DUSEY kenarlarin (kapi pervazi, duvar kosesi, raf) duseyden sapmasi.
  python gercek_kalib_dusey.py [kamera_gercek.json]
Yontem: robot cercevesinde (zemine gore) 12 azimutta (30 derece aralik) ufuk dogrultulu, 70 derece genis iğne deligi gorunumu uretilir (cift balik gozu + karisim, geometri = kamera.json YA DA kamera_gercek.json).
Duz egimsiz kamerada gercek duseyler gorunumde dusey olur; egim varsa kenarlar, azimuta gore sinuzoidal yatar: sapma(az) = A cos(az - az0). LSD ile >= 50 px, duseyden < 20 derece sapan,
govde maskesine degmeyen cizgilerin uzunluk agirlikli medyan sapmasi. Cok sayida sabit (komut=0) kareden (olcum_ham, 1_bekleme, 2a, 6a, 5) toplanir.
Cikti: kalib/dusey_kenar.png (once/sonra), kalib/dusey.json."""
import json
import os
import sys

import cv2
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_kalib_model as km  # noqa: E402
import gercek_ortak as go  # noqa: E402
from kayit import Kayit  # noqa: E402

plt.rcParams["font.family"] = "DejaVu Sans"
W, Hh, FOV = 640, 480, 70.0
AZ = np.arange(0, 360, 30)
KAYITLAR = {"olcum_ham": 14, "1_bekleme": 5, "2a_vitessiz_duz": 8, "6a_far_isik_acik": 5, "5_saksi_turu": 10, "2b_otomatik_duz": 6}


def gorunum_haritasi(kam, az_der, el_der=0.0):
    f = (W / 2) / np.tan(np.radians(FOV / 2))
    x, y = np.meshgrid(np.arange(W) - (W - 1) / 2, np.arange(Hh) - (Hh - 1) / 2)
    d = np.stack([x / f, -y / f, np.ones_like(x)], -1)
    d = d / np.linalg.norm(d, axis=-1, keepdims=True)
    a = np.radians(az_der)
    e = np.radians(el_der)
    Rx = np.array([[1, 0, 0], [0, np.cos(e), -np.sin(e)], [0, np.sin(e), np.cos(e)]])      # bakisi yukari kaldir
    Ry = np.array([[np.cos(a), 0, np.sin(a)], [0, 1, 0], [-np.sin(a), 0, np.cos(a)]])
    d = (d.reshape(-1, 3) @ Rx.T) @ Ry.T
    w1, w2 = kam.agirliklar(d)
    tab = []
    for k in (0, 1):
        u, v, _ = kam.robot_to_lens(d, k)
        tab.append((u.reshape(Hh, W).astype(np.float32), v.reshape(Hh, W).astype(np.float32)))
    top = np.maximum(w1 + w2, 1e-4)
    return tab, (w1 / top).reshape(Hh, W).astype(np.float32), (w2 / top).reshape(Hh, W).astype(np.float32), ((w1 + w2) > 0).reshape(Hh, W)


def gorunum(resim_gri, govde, hrt):
    tab, k1, k2, kap = hrt
    a = cv2.remap(resim_gri, *tab[0], cv2.INTER_LINEAR)
    b = cv2.remap(resim_gri, *tab[1], cv2.INTER_LINEAR)
    g = np.clip(a * k1 + b * k2 + 0.5, 0, 255).astype(np.uint8)
    m1 = cv2.remap(govde, *tab[0], cv2.INTER_NEAREST).astype(np.float32) / 255.0
    m2 = cv2.remap(govde, *tab[1], cv2.INTER_NEAREST).astype(np.float32) / 255.0
    gm = ((k1 * m1 + k2 * m2) > 0.1) | ~kap
    gm = cv2.dilate(gm.astype(np.uint8), np.ones((15, 15), np.uint8)) > 0
    return g, gm


def sapma_olc(g, gm, lsd):
    seg = lsd.detect(g)[0]
    if seg is None:
        return []
    out = []
    for x1, y1, x2, y2 in seg[:, 0, :]:
        L = np.hypot(x2 - x1, y2 - y1)
        if L < 50:
            continue
        # dusey: dy buyuk; sapma = atan(dx/dy) (ust uc saga yatik = +)
        if y1 < y2:
            xu, yu, xa, ya = x1, y1, x2, y2
        else:
            xu, yu, xa, ya = x2, y2, x1, y1
        sap = np.degrees(np.arctan2(xu - xa, ya - yu))
        if abs(sap) > 20:
            continue
        orn = np.linspace(0, 1, 8)
        px = np.clip((xa + (xu - xa) * orn).astype(int), 0, W - 1)
        py = np.clip((ya + (yu - ya) * orn).astype(int), 0, Hh - 1)
        if gm[py, px].any():
            continue
        out.append((sap, L))
    return out


def olc(kam, kareler, etiket):
    govde = cv2.imread(os.path.join(go.KLASOR, "kamera_maske_gercek.png"), cv2.IMREAD_GRAYSCALE)
    lsd = cv2.createLineSegmentDetector(cv2.LSD_REFINE_STD)
    haritalar = {int(a): gorunum_haritasi(kam, a) for a in AZ}
    sonuc = {int(a): [] for a in AZ}
    for gri in kareler:
        gf = gri.astype(np.float32)
        for a in AZ:
            g, gm = gorunum(gf, govde, haritalar[int(a)])
            sonuc[int(a)] += sapma_olc(cv2.GaussianBlur(g, (0, 0), 0.8), gm, lsd)
    ozet = {}
    for a, lst in sonuc.items():
        if len(lst) < 8:
            ozet[a] = (np.nan, len(lst))
            continue
        s = np.array([x[0] for x in lst]); w = np.array([x[1] for x in lst])
        o = np.argsort(s)
        c = np.cumsum(w[o]) / w.sum()
        ozet[a] = (float(s[o][np.searchsorted(c, 0.5)]), len(lst))
    return ozet


def sinuzoid(ozet):
    a = np.array([k for k, v in ozet.items() if np.isfinite(v[0])], float)
    y = np.array([ozet[int(k)][0] for k in a])
    n = np.array([ozet[int(k)][1] for k in a])
    A = np.stack([np.cos(np.radians(a)), np.sin(np.radians(a)), np.ones_like(a)], 1)
    c, *_ = np.linalg.lstsq(A * np.sqrt(n)[:, None], y * np.sqrt(n), rcond=None)
    gen = float(np.hypot(c[0], c[1]))
    faz = float(np.degrees(np.arctan2(c[1], c[0])))
    kal = y - A @ c
    return dict(genlik_der=gen, faz_der=faz, sabit_der=float(c[2]), kalinti_rms=float(np.sqrt(np.average(kal ** 2, weights=n))), c=c.tolist())


def kare_sec():
    kareler = []
    for ad, n in KAYITLAR.items():
        kay = Kayit(os.path.join(go.GERCEK, ad))
        t = kay.t_ms / 1000.0
        kom = np.array([kay.komut(int(x)) for x in kay.t_ms])
        kg = np.vstack([np.zeros((6, 2)), kom[:-6]])
        dur = np.where((np.abs(kg).sum(1) == 0))[0]
        # son 6 karede de durgun
        ok = [i for i in dur if i >= 12 and (np.abs(kg[i - 12:i + 1]).sum() == 0)]
        if not ok:
            continue
        sec = np.linspace(0, len(ok) - 1, n).astype(int)
        for j in sec:
            kareler.append((ad, ok[j]))
    return kareler


if __name__ == "__main__":
    kal_json = sys.argv[1] if len(sys.argv) > 1 else os.path.join(go.KLASOR, "kamera_gercek.json")
    secim = kare_sec()
    kareler = []
    kayc = {}
    for ad, i in secim:
        if ad not in kayc:
            kayc[ad] = Kayit(os.path.join(go.GERCEK, ad))
        kareler.append(cv2.cvtColor(kayc[ad].resim(i), cv2.COLOR_BGR2GRAY))
    print("kare", len(kareler), flush=True)
    kam_o = km.kamera()
    kam_s = km.KameraGercek(json.load(open(kal_json, encoding="utf-8")))
    oz_o = olc(kam_o, kareler, "once")
    oz_s = olc(kam_s, kareler, "sonra")
    sin_o, sin_s = sinuzoid(oz_o), sinuzoid(oz_s)
    print("once : sapma(az) genlik %.2f derece, faz %.0f, sabit %.2f, kalinti rms %.2f" % (sin_o["genlik_der"], sin_o["faz_der"], sin_o["sabit_der"], sin_o["kalinti_rms"]))
    print("sonra: sapma(az) genlik %.2f derece, faz %.0f, sabit %.2f, kalinti rms %.2f" % (sin_s["genlik_der"], sin_s["faz_der"], sin_s["sabit_der"], sin_s["kalinti_rms"]))
    fig, ax = plt.subplots(figsize=(9, 4.5))
    az = np.arange(0, 361, 5)
    for oz, sn, renk, ad in ((oz_o, sin_o, "tab:red", "önce (kamera.json)"), (oz_s, sin_s, "tab:green", "sonra (kamera_gercek.json)")):
        a = np.array([k for k, v in oz.items() if np.isfinite(v[0])], float)
        ax.plot(a, [oz[int(k)][0] for k in a], "o", color=renk, label="%s: ölçüm" % ad)
        c = sn["c"]
        ax.plot(az, c[0] * np.cos(np.radians(az)) + c[1] * np.sin(np.radians(az)) + c[2], "-", color=renk, alpha=0.6, label="sinüs uyumu, genlik %.2f°" % sn["genlik_der"])
    ax.axhline(0, color="k", lw=0.6)
    ax.set_xlabel("görünüm azimutu (robot önü = 0, saat yönü)")
    ax.set_ylabel("düşey kenarın düşeyden sapması (derece, üst uç sağa +)")
    ax.set_title("Bağımsız eğim denetimi: panoramadaki düşey kenarlar (%d sabit kare)" % len(kareler), fontsize=10)
    ax.legend(fontsize=8)
    ax.grid(alpha=0.3)
    fig.tight_layout()
    fig.savefig(os.path.join(km.KALIB, "dusey_kenar.png"), dpi=110)
    json.dump(dict(once=dict(sinuzoid=sin_o, olcum={str(k): v for k, v in oz_o.items()}), sonra=dict(sinuzoid=sin_s, olcum={str(k): v for k, v in oz_s.items()}), kare=len(kareler)),
              open(os.path.join(km.KALIB, "dusey.json"), "w", encoding="utf-8"), indent=1)
