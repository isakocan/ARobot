"""olcum_ham: birlesik kestirici E (gercek_kos.py kos gercek olcum_ham ciktisi) yolu, bant araligi olcegi, kapanis ve baglanti kopmasi davranisi.
  python gercek_olcum_kos.py [etiket=gercek]
Yer gercegi (kullanici): ilk duz gidis 3,4-14,4 sn sonunda on kenar 2,00 m ilerideki bantta; tur 22,8-82,4 sn sonunda baslangic bolgesine donus
(goruntu: t=0,5 ile 82,5/100/133 ayni yer ve yon). Baglanti kopmasi anlari: komutlar_ham_kopru.csv 'ayrildi' satirlari."""
import csv
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
HAM = r"C:\Users\PC\Desktop\ARobot\kayitlar\2026-11-14_20-12-25_olcum_ham"


def poz_at(t, P, ts):
    i = int(np.argmin(abs(t - ts)))
    return P[i], i


def ana(etiket="gercek"):
    z = np.load(os.path.join(go.SONUC, "kos_" + etiket, "olcum_ham.npz"))
    P, t, kom = z["P"], z["t_ms"] / 1000.0, z["komut"]
    out = {}
    adim = np.hypot(np.diff(P[:, 0]), np.diff(P[:, 1]))
    out["toplam_yol_m"] = float(adim.sum())
    # nominal yol (komutun entegrali) ile ilk duz gidis
    for ts in (3.4, 8.8, 14.4, 22.8, 50.0, 82.4, 100.0, 133.0, 149.0, 165.0, 182.0, 299.0):
        p, i = poz_at(t, P, ts)
        out["t%.1f" % ts] = [float(p[0]), float(p[1]), float(p[2] % 360), float(np.hypot(p[0], p[1]))]
    p14, _ = poz_at(t, P, 14.4)
    mx = float(np.hypot(P[t <= 14.4, 0], P[t <= 14.4, 1]).max())
    print("E: t=14,4 sn konumu (%.2f, %.2f) m, başlangıçtan uzaklık %.2f m (gerçek 2,00 m; oran %.2f); 0-14,4 sn en uzak nokta %.2f m" % (p14[0], p14[1], np.hypot(p14[0], p14[1]), np.hypot(p14[0], p14[1]) / 2.0, mx))
    for ts in (22.8, 82.4, 100.0, 133.0, 149.0, 165.0, 182.0, 299.0):
        p, _ = poz_at(t, P, ts)
        print("   t=%6.1f konum (%6.2f, %6.2f) m  başlangıçtan %.2f m  yön %.0f°" % (ts, p[0], p[1], np.hypot(p[0], p[1]), p[2] % 360))
    hareket = (np.abs(kom[:, 0]) + np.abs(kom[:, 1])) > 60
    print("B kullanımı (E, tüm kare) %.2f, hareketliyken %.2f; yaw komuta düştü (M2D kapısı) %.2f; kare reddi %.2f; k=%.2f" % (
        z["bev_kullan"].mean(), z["bev_kullan"][hareket].mean(), z["yaw_komut"].mean(), z["kare_red"].mean(), z["k"][-1]))
    out["bev_kullan"] = float(z["bev_kullan"][hareket].mean())
    out["yaw_komut"] = float(z["yaw_komut"].mean())
    # baglanti kopmasi
    kop = []
    for r in csv.DictReader(open(os.path.join(go.GERCEK, "olcum_ham", "komutlar_ham_kopru.csv"))):
        if r["kaynak"] == "ayrildi":
            kop.append(float(r["t_ms"]) / 1000.0)
    print("bağlantı kopmaları (ayrildi):", kop)
    for ts in kop:
        m = (t >= ts - 1.0) & (t <= ts + 2.0)
        i0 = int(np.argmin(abs(t - (ts - 1.0))))
        i1 = int(np.argmin(abs(t - (ts + 2.0))))
        dt = np.diff(t[m])
        yol = float(np.hypot(*(P[i1, :2] - P[i0, :2])))
        yaw = float(((P[i1, 2] - P[i0, 2] + 180) % 360) - 180)
        print("   kopma t=%.1f: motor(durum) %s -> %s ; E yol %.2f m, yaw değişimi %.1f°, B kullanımı %.2f, yaw-komut %.2f, kare aralığı maks %.0f ms; güven %.2f->%.2f" % (
            ts, tuple(kom[i0]), tuple(kom[i1]), yol, yaw, z["bev_kullan"][m].mean(), z["yaw_komut"][m].mean(), dt.max() * 1000, z["guven"][i0], z["guven"][i1]))
        out["kopma_%.1f" % ts] = dict(motor_once=kom[i0].tolist(), motor_sonra=kom[i1].tolist(), yol=yol, yaw=yaw, bev=float(z["bev_kullan"][m].mean()))
    fig, ax = plt.subplots(1, 2, figsize=(13, 6))
    sc = ax[0].scatter(P[:, 0], P[:, 1], c=t, s=2, cmap="viridis")
    for ts, ad in ((14.4, "14,4 (2 m bant)"), (22.8, "22,8"), (82.4, "82,4 (tur sonu)"), (149, "149"), (182, "182")):
        p, _ = poz_at(t, P, ts)
        ax[0].plot(p[0], p[1], "o", ms=7)
        ax[0].annotate(ad, (p[0], p[1]), fontsize=8)
    ax[0].plot(0, 0, "go", ms=9)
    ax[0].plot([0, 0], [0, 2], "r:", label="gerçek: başlangıç bandı -> 2 m (yön 0 varsayımı)")
    ax[0].set_aspect("equal")
    ax[0].grid(alpha=0.3)
    ax[0].legend(fontsize=8)
    ax[0].set_title("olcum_ham: E yolu (renk = zaman, sn)", fontsize=10)
    plt.colorbar(sc, ax=ax[0], fraction=0.04)
    d = np.hypot(P[:, 0], P[:, 1])
    ax[1].plot(t, d, label="E: başlangıçtan uzaklık (m)")
    for ts in kop:
        ax[1].axvline(ts, color="r", lw=0.6)
    ax[1].axhline(2.0, color="k", ls=":")
    ax[1].set_xlabel("zaman (sn); kırmızı çizgi: bağlantı kopması")
    ax[1].grid(alpha=0.3)
    ax[1].legend(fontsize=8)
    fig.tight_layout()
    fig.savefig(os.path.join(go.SONUC, "olcum", "E_yol.png"), dpi=105)
    json.dump(out, open(os.path.join(go.SONUC, "olcum", "E.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)


if __name__ == "__main__":
    ana(sys.argv[1] if len(sys.argv) > 1 else "gercek")
