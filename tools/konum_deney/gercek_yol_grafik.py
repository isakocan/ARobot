"""Birlesik kestirici E'nin gercek kayitlardaki yolu ve doluluk haritasi grafikleri (yer gercegi YOK). gercek_kos.py kos ciktisini okur.
  python gercek_yol_grafik.py <maske> [kayit ...]
Cikti: kayitlar/sonuclar/gercek/yol_harita_<maske>_<kayit>.png ve ekrana sayilar (yol uzunlugu, baslangic-bitis mesafesi, bitis yonu, B kullanim orani, k, guven)."""
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

plt.rcParams["font.family"] = "DejaVu Sans"
X0, Y0, W, H, HUC = -20.0, -20.0, 800, 800, 0.05


def durum(lo):
    d = np.full(lo.shape, 128, np.uint8)
    d[lo <= -0.4] = 255
    d[lo >= 0.4] = 0
    return d


def ciz(maske, ad):
    p = os.path.join(go.SONUC, "kos_" + maske, ad + ".npz")
    if not os.path.exists(p):
        return None
    z = np.load(p)
    P, t, lo, kom = z["P"], z["t_ms"] / 1000.0, z["lo"], z["komut"]
    adim = np.hypot(np.diff(P[:, 0]), np.diff(P[:, 1]))
    yol = float(adim.sum())
    uc = float(np.hypot(P[-1, 0] - P[0, 0], P[-1, 1] - P[0, 1]))
    D = durum(lo)
    # harita kirpma
    gor = np.argwhere(D != 128)
    fig, ax = plt.subplots(1, 2, figsize=(13, 6.2))
    sc = ax[0].scatter(P[:, 0], P[:, 1], c=t, s=3, cmap="viridis")
    ax[0].plot(P[0, 0], P[0, 1], "go", ms=9, label="başlangıç")
    ax[0].plot(P[-1, 0], P[-1, 1], "rs", ms=9, label="bitiş")
    ax[0].set_aspect("equal")
    ax[0].grid(alpha=0.3)
    ax[0].legend(fontsize=8)
    ax[0].set_xlabel("x (m, doğu)")
    ax[0].set_ylabel("y (m, kuzey)")
    ax[0].set_title("%s: E yolu (renk = zaman, sn)\nyol %.1f m, başlangıç-bitiş %.2f m, bitiş yönü %.0f°" % (ad, yol, uc, P[-1, 2] % 360), fontsize=10)
    plt.colorbar(sc, ax=ax[0], fraction=0.04)
    if len(gor):
        r0, c0 = gor.min(0)
        r1, c1 = gor.max(0)
        m = 12
        r0, c0, r1, c1 = max(r0 - m, 0), max(c0 - m, 0), min(r1 + m, H - 1), min(c1 + m, W - 1)
        crop = D[r0:r1 + 1, c0:c1 + 1]
        ext = (X0 + c0 * HUC, X0 + (c1 + 1) * HUC, Y0 + (H - 1 - r1) * HUC, Y0 + (H - r0) * HUC)
        ax[1].imshow(crop, cmap="gray", vmin=0, vmax=255, extent=ext, origin="upper", interpolation="nearest")
        ax[1].plot(P[:, 0], P[:, 1], "r-", lw=0.8)
        ax[1].plot(P[0, 0], P[0, 1], "go", ms=7)
    ax[1].set_aspect("equal")
    ax[1].set_title("doluluk haritası (beyaz: boş zemin, siyah: dolu, gri: bilinmiyor; SegFormer taraması + E konumu)", fontsize=9)
    ax[1].set_xlabel("x (m)")
    fig.tight_layout()
    fig.savefig(os.path.join(go.SONUC, "yol_harita_%s_%s.png" % (maske, ad)), dpi=105)
    plt.close(fig)
    bev = float(z["bev_kullan"].mean())
    hareket = (np.abs(kom[:, 0]) + np.abs(kom[:, 1])) > 60
    print("%-18s yol %6.2f m  baş-bit %5.2f m (%4.1f%% yol)  bitiş yönü %6.1f°  B kullanımı %.2f (hareketliyken %.2f)  k=%.2f  yaw_komut kare %.2f  güven_son %.2f  boş hücre %d dolu hücre %d" % (
        ad, yol, uc, 100 * uc / max(yol, 1e-9), P[-1, 2] % 360, bev, float(z["bev_kullan"][hareket].mean()) if hareket.any() else 0, float(z["k"][-1]),
        float(z["yaw_komut"].mean()), float(z["guven"][-1]), int((D == 255).sum()), int((D == 0).sum())))
    return dict(ad=ad, yol=yol, uc=uc, bitis_yon=float(P[-1, 2] % 360), bev=bev, k=float(z["k"][-1]))


if __name__ == "__main__":
    maske = sys.argv[1]
    adlar = sys.argv[2:] or go.ADLAR
    out = [ciz(maske, a) for a in adlar]
    json.dump([o for o in out if o], open(os.path.join(go.SONUC, "yol_ozet_%s.json" % maske), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
