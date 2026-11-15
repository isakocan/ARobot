"""Gerçek kayıtlarda E ve E2 karşılaştırma tablosu + görseller (kayitlar/sonuclar/gercek/e2/).
  python gercek_e2_rapor.py   (önce gercek_e2_hazirla.py, gercek_e2_kos.py, gercek_e2_kapanis.py)
Çıktı: gercek_tablo.json, yol_olcum_ham.png, yol_5_saksi.png, profil_zaman.png, yon_zaman_<kayit>.png"""
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
E2D = os.path.join(go.SONUC, "e2")
ADLAR = ["olcum_ham", "5_saksi_turu", "2a_vitessiz_duz", "2b_otomatik_duz", "3_vitessiz_donus", "4_parke_vitessiz", "6c_karanlik_surus"]


def sar(x):
    return (np.asarray(x) + 180.0) % 360.0 - 180.0


def oku(ad):
    p = os.path.join(E2D, "kos", ad + ".npz")
    return np.load(p) if os.path.exists(p) else None


def dogrudan():
    p = os.path.join(E2D, "kapanis_dogrudan.json")
    return json.load(open(p)) if os.path.exists(p) else {}


def uzak(P, t, ts):
    i = int(np.argmin(abs(t - ts)))
    return float(np.hypot(P[i, 0] - P[0, 0], P[i, 1] - P[0, 1]))


def ozet(z, ad, onek):
    P, t, kom = z[onek + "_P"], z["t_ms"] / 1000.0, z["komut"]
    hareket = (np.abs(kom[:, 0]) + np.abs(kom[:, 1])) > 60
    adim = np.hypot(np.diff(P[:, 0]), np.diff(P[:, 1]))
    o = dict(yol=float(adim.sum()), bas_bit=float(np.hypot(*(P[-1, :2] - P[0, :2]))), yon_son=float(P[-1, 2] % 360),
             yon_fark_baslangic=float(sar(P[-1, 2] - P[0, 2])), bev=float(z[onek + "_bev_kullan"][hareket].mean()) if hareket.any() else None,
             yaw_komut=float(z[onek + "_yaw_komut"].mean()), kare_red=float(z[onek + "_kare_red"].mean()), k_son=float(z[onek + "_k"][-1]))
    pm = (kom[:-1, 0] + kom[:-1, 1]) / 2.0
    o["yol_ileri"] = float(adim[pm > 20].sum())
    o["yol_geri"] = float(adim[pm < -20].sum())
    if ad == "olcum_ham":
        o["d14_4"] = uzak(P, t, 14.4)
        o["maks_0_14_4"] = float(np.hypot(P[t <= 14.4, 0] - P[0, 0], P[t <= 14.4, 1] - P[0, 1]).max())
        i = int(np.argmin(abs(t - 82.4)))
        o["d82_4"] = uzak(P, t, 82.4)
        o["yon_fark_82_4"] = float(sar(P[i, 2] - P[0, 2]))
        for ts in (100.0, 133.0):
            o["d%.0f" % ts] = uzak(P, t, ts)
        o["yol_22_8_82_4"] = float(np.hypot(np.diff(P[(t >= 22.8) & (t <= 82.4), 0]), np.diff(P[(t >= 22.8) & (t <= 82.4), 1])).sum())
    return o


def duz_hiz(z, onek, ad):
    """Düz, sabit komut pencerelerinde (|sol-sag| < 20, |ort| > 100) pozdan ölçülen hız (m/sn), 255'e ölçekli, ileri/geri ayrı."""
    P, t, kom = z[onek + "_P"], z["t_ms"] / 1000.0, z["komut"]
    sonuc = {}
    W = 15
    for ad_, isaret in (("ileri", 1), ("geri", -1)):
        v = []
        for i in range(0, len(t) - W, W):
            k = kom[i:i + W]
            if np.all(np.abs(k[:, 0] - k[:, 1]) < 20) and np.all(isaret * (k[:, 0] + k[:, 1]) / 2 > 100) and np.ptp((k[:, 0] + k[:, 1]) / 2) < 25:
                yol = float(np.hypot(np.diff(P[i:i + W + 1, 0]), np.diff(P[i:i + W + 1, 1])).sum())
                dt = t[i + W] - t[i]
                if 0.5 < dt < 2.0:
                    v.append((yol / dt, abs(k[:, 0].mean() + k[:, 1].mean()) / 2))
        if v:
            v = np.array(v)
            sonuc[ad_] = dict(n=len(v), v_medyan=float(np.median(v[:, 0])), v255=float(np.median(v[:, 0] * 255.0 / v[:, 1])))
    return sonuc


def kopmalar():
    out = []
    p = os.path.join(go.GERCEK, "olcum_ham", "komutlar_ham_kopru.csv")
    for r in csv.DictReader(open(p)):
        if r["kaynak"] == "ayrildi":
            out.append(float(r["t_ms"]) / 1000.0)
    return out


def ana():
    tablo = {}
    dg = dogrudan()
    for ad in ADLAR:
        z = oku(ad)
        if z is None:
            continue
        r = dict(E=ozet(z, ad, "E"), E2=ozet(z, ad, "E2"), duz_E=duz_hiz(z, "E", ad), duz_E2=duz_hiz(z, "E2", ad),
                 profil_son=[float(x) for x in z["E2_profil"][-1]], v_noktalar=z["v_noktalar"].tolist(), w_noktalar=z["w_noktalar"].tolist())
        t = z["t_ms"] / 1000.0
        # yön kapanışı doğrudan eşleşmeye göre
        d = dg.get(ad)
        if d:
            hedef = d[0]["dogrudan_yaw"]
            for onek in ("E", "E2"):
                P = z[onek + "_P"]
                if ad == "olcum_ham":
                    i = int(np.argmin(abs(t - 82.5)))
                    r[onek]["kapanis_hata_82_5"] = float(sar(sar(P[i, 2] - P[0, 2]) - d[0]["dogrudan_yaw"]))
                else:
                    r[onek]["kapanis_hata"] = float(sar(sar(P[-1, 2] - P[0, 2]) - hedef))
            for src in ("komutsuz", "komutlu"):
                y = z["yon_m2d_" + src]
                i = int(np.argmin(abs(t - 82.5))) if ad == "olcum_ham" else len(y) - 1
                r["m2d_%s_kapanis_hata" % src] = float(sar(sar(y[i] - y[0]) - hedef))
            r["dogrudan"] = d[0]
        tablo[ad] = r
        print("== %s" % ad)
        for onek in ("E", "E2"):
            print("  %-3s %s" % (onek, {k: (round(v, 3) if isinstance(v, float) else v) for k, v in r[onek].items()}))
        print("  düz hız E ", r["duz_E"], "\n  düz hız E2", r["duz_E2"])
        print("  profil_son v255 %.3f v150 %.3f w255(yerinde) %.0f w110(yerinde) %.0f v150(yay q=100) %.3f w110(hızlı yay p=200) %.0f" % tuple(r["profil_son"]))
        print("  v kutuları (rejim,x,v)", [(int(a), round(b), round(c, 3)) for a, b, c in r["v_noktalar"]])
        print("  w kutuları (rejim,x,w)", [(int(a), round(b), round(c)) for a, b, c in r["w_noktalar"]])
        for src in ("komutsuz", "komutlu"):
            if "m2d_%s_kapanis_hata" % src in r:
                print("  M2D %s birikimli kapanış hatası %.1f°" % (src, r["m2d_%s_kapanis_hata" % src]))
    # bağlantı kopmaları
    kop = kopmalar()
    z = oku("olcum_ham")
    if z is not None:
        t = z["t_ms"] / 1000.0
        k_out = []
        for ts in kop:
            i0, i1 = int(np.argmin(abs(t - (ts - 1.0)))), int(np.argmin(abs(t - (ts + 2.0))))
            m = (t >= ts - 1.0) & (t <= ts + 2.0)
            row = dict(t=ts, kom_once=z["komut"][i0].tolist(), kom_sonra=z["komut"][i1].tolist(), dt_maks=float(np.diff(t[m]).max()) if m.sum() > 1 else None)
            for onek in ("E", "E2"):
                P = z[onek + "_P"]
                row[onek] = dict(yol=float(np.hypot(*(P[i1, :2] - P[i0, :2]))), yaw=float(sar(P[i1, 2] - P[i0, 2])), bev=float(z[onek + "_bev_kullan"][m].mean()), yawk=float(z[onek + "_yaw_komut"][m].mean()))
            k_out.append(row)
            print("kopma t=%.1f %s" % (ts, row))
        tablo["_kopmalar"] = k_out
    json.dump(tablo, open(os.path.join(E2D, "gercek_tablo.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    # ---------- görseller
    z = oku("olcum_ham")
    if z is not None:
        t = z["t_ms"] / 1000.0
        fig, ax = plt.subplots(1, 3, figsize=(18, 6))
        for onek, c, ad in (("E", "tab:red", "E (sim sabitleri)"), ("E2", "tab:green", "E2 (öğrenilen profil)")):
            P = z[onek + "_P"]
            ax[0].plot(P[:, 0], P[:, 1], color=c, lw=0.9, label=ad)
            ax[1].plot(t, np.hypot(P[:, 0] - P[0, 0], P[:, 1] - P[0, 1]), color=c, label=ad)
            ax[2].plot(t, np.cumsum(np.r_[0, np.hypot(np.diff(P[:, 0]), np.diff(P[:, 1]))]), color=c, label=ad)
            for ts, nm in ((14.4, "14,4 sn"), (82.4, "82,4 sn")):
                i = int(np.argmin(abs(t - ts)))
                ax[0].plot(P[i, 0], P[i, 1], "o", color=c, ms=6)
                ax[0].annotate(nm, (P[i, 0], P[i, 1]), fontsize=8, color=c)
        ax[0].plot(0, 0, "ko", ms=8, label="başlangıç")
        ax[0].plot([0, 0], [0, 2.0], "k-", lw=2, alpha=0.5, label="gerçek: 2,00 m bant")
        ax[0].plot(0, 2.0, "k*", ms=14)
        ax[0].set_aspect("equal")
        ax[0].grid(alpha=0.3)
        ax[0].legend(fontsize=8)
        ax[0].set_xlabel("x (m)")
        ax[0].set_ylabel("y (m)")
        ax[0].set_title("olcum_ham: konum yolu (başlangıç yönü +y; ilk düz gidiş 2,00 m)", fontsize=10)
        ax[1].plot([14.4], [2.0], "k*", ms=14, label="gerçek t=14,4 sn: 2,00 m")
        ax[1].plot([82.4], [0.0], "ks", ms=9, label="gerçek t=82,4 sn: başlangıç bölgesi (~0)")
        for ts in kopmalar():
            ax[1].axvline(ts, color="gray", lw=0.4)
        ax[1].set_xlabel("zaman (sn); gri çizgi: bağlantı kopması")
        ax[1].set_ylabel("başlangıçtan uzaklık (m)")
        ax[1].grid(alpha=0.3)
        ax[1].legend(fontsize=8)
        ax[1].set_title("başlangıçtan uzaklık", fontsize=10)
        ax[2].set_xlabel("zaman (sn)")
        ax[2].set_ylabel("birikimli yol (m)")
        ax[2].grid(alpha=0.3)
        ax[2].legend(fontsize=8)
        ax[2].set_title("birikimli yol uzunluğu", fontsize=10)
        fig.tight_layout()
        fig.savefig(os.path.join(E2D, "yol_olcum_ham.png"), dpi=100)
        plt.close(fig)
    z = oku("5_saksi_turu")
    if z is not None:
        t = z["t_ms"] / 1000.0
        fig, ax = plt.subplots(1, 2, figsize=(14, 6.5))
        for onek, c, ad in (("E", "tab:red", "E (sim sabitleri)"), ("E2", "tab:green", "E2 (öğrenilen profil)")):
            P = z[onek + "_P"]
            ax[0].plot(P[:, 0], P[:, 1], color=c, lw=0.9, label=ad)
            ax[0].plot(P[-1, 0], P[-1, 1], "s", color=c, ms=7)
            ax[1].plot(t, np.hypot(P[:, 0] - P[0, 0], P[:, 1] - P[0, 1]), color=c, label=ad)
        ax[0].plot(0, 0, "ko", ms=9, label="başlangıç (gerçek: bitiş aynı yer)")
        ax[0].set_aspect("equal")
        ax[0].grid(alpha=0.3)
        ax[0].legend(fontsize=8)
        ax[0].set_title("5_saksi_turu: konum yolu (kare = bitiş)", fontsize=10)
        ax[0].set_xlabel("x (m)")
        ax[0].set_ylabel("y (m)")
        ax[1].axhline(0, color="k", lw=0.5)
        ax[1].set_xlabel("zaman (sn)")
        ax[1].set_ylabel("başlangıçtan uzaklık (m)")
        ax[1].grid(alpha=0.3)
        ax[1].legend(fontsize=8)
        ax[1].set_title("başlangıçtan uzaklık (gerçek: son kare başlangıçla aynı yer)", fontsize=10)
        fig.tight_layout()
        fig.savefig(os.path.join(E2D, "yol_5_saksi.png"), dpi=100)
        plt.close(fig)
    # profil zaman
    fig, ax = plt.subplots(2, 2, figsize=(14, 8))
    for ad in ADLAR:
        z = oku(ad)
        if z is None:
            continue
        t = z["t_ms"] / 1000.0
        pr = z["E2_profil"]
        for k, (a, nm) in enumerate(((ax[0, 0], "ileri hız, tam gaza (255) ölçekli (m/sn)"), (ax[0, 1], "ileri hız, PWM 150'de (m/sn)"), (ax[1, 0], "dönüş hızı, q=255'e ölçekli (derece/sn)"), (ax[1, 1], "dönüş hızı, q=110'da (derece/sn)"))):
            a.plot(t, pr[:, k], lw=1.0, label=ad)
            a.set_title(nm, fontsize=9)
            a.grid(alpha=0.3)
            a.set_xlabel("zaman (sn)")
    ax[0, 0].legend(fontsize=7)
    fig.suptitle("E2'nin öğrendiği robot profili zamanla (başlangıç öncülü: 0,40 m/sn ve 100 derece/sn)", fontsize=11)
    fig.tight_layout()
    fig.savefig(os.path.join(E2D, "profil_zaman.png"), dpi=100)
    plt.close(fig)
    # yön-zaman
    for ad in ("olcum_ham", "5_saksi_turu", "3_vitessiz_donus"):
        z = oku(ad)
        if z is None:
            continue
        t = z["t_ms"] / 1000.0
        fig, ax = plt.subplots(2, 1, figsize=(15, 8), sharex=True)
        for y, c, nm in ((np.degrees(np.unwrap(np.radians(z["yon_m2d_komutsuz"]))), "tab:blue", "M2D birikimli (komutsuz, girdi)"), (np.degrees(np.unwrap(np.radians(z["yon_m2d_komutlu"]))), "tab:cyan", "M2D birikimli (komutlu)"), (np.unwrap(np.radians(z["E_P"][:, 2])) * 180 / np.pi, "tab:red", "E yönü"),
                         (np.unwrap(np.radians(z["E2_P"][:, 2])) * 180 / np.pi, "tab:green", "E2 yönü")):
            ax[0].plot(t, y - y[0], color=c, lw=0.9, label=nm)
        ax[0].set_ylabel("birikimli yön (derece)")
        ax[0].grid(alpha=0.3)
        ax[0].legend(fontsize=8)
        ax[0].set_title("%s: yön-zaman (kapanışta doğru değer: doğrudan eşleşme)" % ad, fontsize=10)
        d = dg.get(ad)
        if d:
            ts = d[0]["t_b"]
            ax[0].plot([ts], [d[0]["dogrudan_yaw"] + 360.0 * np.round((np.degrees(np.unwrap(np.radians(z["yon_m2d_komutsuz"])))[int(np.argmin(abs(t - ts)))] - z["yon_m2d_komutsuz"][0] - d[0]["dogrudan_yaw"]) / 360.0)], "k*", ms=12, label="doğrudan eşleşme (en yakın 360 katı)")
            ax[0].legend(fontsize=8)
        ax[1].plot(t, z["E2_yaw_komut"], color="tab:green", lw=0.4, label="E2: M2D reddedildi (komuta düşüldü)")
        ax[1].plot(t, 1.2 + z["E_yaw_komut"], color="tab:red", lw=0.4, label="E: M2D reddedildi")
        ax[1].plot(t, 2.4 + (z["ic_m2d_komutsuz"] < 10), color="tab:blue", lw=0.4, label="M2D iç eşleşme < 10")
        ax[1].set_yticks([])
        ax[1].set_xlabel("zaman (sn)")
        ax[1].legend(fontsize=8, loc="upper right")
        fig.tight_layout()
        fig.savefig(os.path.join(E2D, "yon_zaman_%s.png" % ad), dpi=100)
        plt.close(fig)


if __name__ == "__main__":
    ana()
