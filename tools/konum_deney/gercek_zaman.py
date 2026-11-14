"""Zaman eşlemesi kanıtı: motor komutu (durum.jsonl) ile görüntüdeki hareketin (ardışık kare farkı) zamanını karşılaştırır.
Çıktı: kayitlar/sonuclar/gercek/zaman_<kayit>.png + ekrana sayılar (kare aralığı istatistiği, hareket başlama gecikmesi, çapraz ilinti gecikmesi).
Kullanım: python gercek_zaman.py [kayit ...]"""
import os
import sys

import cv2
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
from kayit import Kayit  # noqa: E402

KOK = os.path.abspath(os.path.join(KLASOR, "..", ".."))
CIKTI = os.path.join(KOK, "kayitlar", "sonuclar", "gercek")
plt.rcParams["font.family"] = "DejaVu Sans"


def hareket_enerjisi(k):
    n = len(k)
    E = np.zeros(n)
    prev = None
    for i in range(n):
        g = cv2.cvtColor(k.resim(i), cv2.COLOR_BGR2GRAY)
        g = cv2.resize(g, (320, 180), interpolation=cv2.INTER_AREA).astype(np.float32)
        if prev is not None:
            E[i] = np.abs(g - prev).mean()
        prev = g
    return E


def analiz(ad):
    k = Kayit(os.path.join(KOK, "kayitlar", "gercek", ad))
    t = k.t_ms / 1000.0
    dt = np.diff(t)
    E = hareket_enerjisi(k)
    ct, sol, sag = k.komut_dizisi
    ct = ct / 1000.0
    hiz = np.interp(t, ct, np.abs(sol) + np.abs(sag))
    print("== %s: %d kare, süre %.1f sn, kare aralığı ort %.1f ms (std %.1f, p5 %.0f, p95 %.0f, maks %.0f ms), ort fps %.2f" % (
        ad, len(t), t[-1] - t[0], dt.mean() * 1000, dt.std() * 1000, np.percentile(dt, 5) * 1000, np.percentile(dt, 95) * 1000, dt.max() * 1000, 1 / dt.mean()))
    # başlangıç gecikmesi: motor ilk kez |sol|+|sag|>60 olduğu an -> E taban + 4 sigma geçtiği ilk kare
    taban = E[2:25].mean() if (hiz[2:25] < 5).all() else np.median(E[E > 0])
    sig = E[2:25].std() if (hiz[2:25] < 5).all() else E.std()
    esik = taban + max(4 * sig, 0.3)
    bas = np.where(hiz > 60)[0]
    gec = None
    if len(bas):
        i0 = bas[0]
        j = np.where(E[i0 - 3:] > esik)[0]
        if len(j):
            gec = t[i0 - 3 + j[0]] - t[i0]
        print("   ilk motor>60 anı t=%.2f sn (kare %d); hareket eşiği %.2f aşıldı: t=%.2f sn  -> gecikme %.0f ms" % (
            t[i0], i0, esik, t[i0] + (gec or np.nan), (gec or np.nan) * 1000))
    # çapraz ilinti (hiz ile E): en iyi kayma
    h = hiz - hiz.mean()
    e = E - E.mean()
    kay = np.arange(-30, 31)
    r = []
    for s in kay:
        if s >= 0:
            a, b = h[:len(h) - s], e[s:]
        else:
            a, b = h[-s:], e[:len(e) + s]
        r.append(np.corrcoef(a, b)[0, 1])
    r = np.array(r)
    kb = kay[np.argmax(r)]
    print("   çapraz ilinti: en iyi kayma %+d kare (%.0f ms, motor -> görüntü), r=%.2f; kayma 0'da r=%.2f" % (kb, kb * 1000 / 15.0, r.max(), r[30]))
    fig, ax = plt.subplots(2, 1, figsize=(11, 5.5), sharex=True)
    ax[0].plot(ct, sol, label="sol motor", lw=1)
    ax[0].plot(ct, sag, label="sağ motor", lw=1)
    ax[0].set_ylabel("motor PWM (−255..255)")
    ax[0].legend(loc="upper right")
    ax[1].plot(t, E, lw=0.8, color="k")
    ax[1].set_ylabel("görüntü hareketi\n(ardışık kare farkı, gri seviye)")
    ax[1].set_xlabel("zaman (sn, kayıt başından)")
    if gec is not None:
        for a in ax:
            a.axvline(t[bas[0]], color="r", ls="--", lw=0.8)
        ax[1].axvline(t[bas[0]] + gec, color="g", ls="--", lw=0.8)
    ax[0].set_title("%s: motor komutu ve görüntüdeki hareket (kırmızı: motor başlangıcı, yeşil: görüntüde hareket başlangıcı)" % ad, fontsize=10)
    fig.tight_layout()
    fig.savefig(os.path.join(CIKTI, "zaman_%s.png" % ad), dpi=110)
    plt.close(fig)
    return dict(ad=ad, aralik_ort_ms=dt.mean() * 1000, aralik_std_ms=dt.std() * 1000, aralik_maks_ms=dt.max() * 1000, gecikme_ms=None if gec is None else gec * 1000,
                capraz_kare=int(kb), capraz_r=float(r.max()))


if __name__ == "__main__":
    adlar = sys.argv[1:] or sorted(os.listdir(os.path.join(KOK, "kayitlar", "gercek")))
    adlar = [a for a in adlar if not a.startswith("_")]
    for a in adlar:
        analiz(a)
