"""Gorev 1: kusbakisi olcek tutarsizligi teshisi (H1 egim/merkez, H2 tavan yansimasi, H3 robota sabit noktalar). Girdi: eslesme_<kayit>.npz (gercek_kalib_veri.py).
  python gercek_kalib_teshis.py [etiket=once] [kamera_json]      etiket 'once' (nominal kamera.json) ya da 'sonra' (kamera_gercek.json)
Cikti (kalib/): teshis_<etiket>_kutup_hiz.png, teshis_<etiket>_kutup_dusuk.png, teshis_<etiket>_dagilim.png, teshis_<etiket>_goruntu.png, teshis_<etiket>.json
Tanimlar: hiz s = hareket yonunde zemin kaymasi / dt (m/sn); q = s / vref (vref: ayni cift icin ON sektor +-45 derece medyani); DUSUK = q < 0,35;
SABIT = ham pikselde yer degistirme < 1 px (H3 adayi); YANSIMA-benzeri = DUSUK ve >= 1 px (H2 adayi, ayirt edilemeyenlerle birlikte)."""
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
import gercek_kalib_model as km  # noqa: E402
import gercek_kalib_analiz as ka  # noqa: E402

plt.rcParams["font.family"] = "DejaVu Sans"
KAYITLAR = ["2a_vitessiz_duz", "2b_otomatik_duz", "4_parke_vitessiz", "5_saksi_turu", "6c_karanlik_surus", "olcum_ham"]
ETIKET = {"2a_vitessiz_duz": "2a halı", "2b_otomatik_duz": "2b halı", "4_parke_vitessiz": "4 parke", "5_saksi_turu": "5 saksı parke", "6c_karanlik_surus": "6c karanlık", "olcum_ham": "olcum_ham parlak parke"}
DUSUK = 0.35
AZ_BIN = np.arange(0, 361, 20)
R_BIN = np.array([0.32, 0.5, 0.7, 0.9, 1.1, 1.3, 1.45])


def secim_g(T):
    return T["gec"] & np.isfinite(T["q"]) & (np.abs(T["vref"][T["c"]]) > 0.3)


def hucre(T, deger, secim, istatistik):
    out = np.full((len(R_BIN) - 1, len(AZ_BIN) - 1), np.nan)
    for i in range(len(R_BIN) - 1):
        for j in range(len(AZ_BIN) - 1):
            m = secim & (T["r"] >= R_BIN[i]) & (T["r"] < R_BIN[i + 1]) & (T["az"] >= AZ_BIN[j]) & (T["az"] < AZ_BIN[j + 1])
            if m.sum() >= 12:
                out[i, j] = istatistik(deger[m])
    return out


def kutup_figur(Ts, deger_fn, istatistik, baslik, dosya, vmin, vmax, cmap, etiket):
    fig, axs = plt.subplots(2, 3, figsize=(13, 8.6), subplot_kw={"projection": "polar"})
    for ax, (ad, T) in zip(axs.ravel(), Ts.items()):
        Z = hucre(T, deger_fn(T), secim_g(T), istatistik)
        pc = ax.pcolormesh(np.radians(AZ_BIN), R_BIN, Z, cmap=cmap, vmin=vmin, vmax=vmax, shading="flat")
        ax.set_theta_zero_location("N")
        ax.set_theta_direction(-1)
        ax.set_title(ETIKET[ad], fontsize=10)
        ax.set_yticks([0.5, 1.0, 1.4])
        ax.set_yticklabels(["0,5", "1,0", "1,4 m"], fontsize=7)
        ax.tick_params(labelsize=7)
        plt.colorbar(pc, ax=ax, shrink=0.65, pad=0.08)
    fig.suptitle(baslik + " [" + etiket + "]\n(0 derece = robotun önü, saat yönü; sağ=90, arka=180, sol=270; yarıçap = zeminde kameradan uzaklık)", fontsize=11)
    fig.tight_layout()
    fig.savefig(os.path.join(km.KALIB, dosya), dpi=95)
    plt.close(fig)


def ozet(ad, T):
    g = secim_g(T)
    q, pix = T["q"], T["pix"]
    dus = g & (q < DUSUK)
    sab = g & (pix < 1.0)
    out = dict(n=int(g.sum()), dusuk=float(dus.sum() / g.sum()), sabit_pix_lt1=float(sab.sum() / g.sum()), yansima_benzeri=float((dus & ~sab).sum() / g.sum()),
               q_yuksek=float((g & (q > 1.5)).sum() / g.sum()), ana_tepe=float((g & (q > 0.7) & (q < 1.3)).sum() / g.sum()))
    sh = ka.sektor_hiz(T, g)
    out["sektor"] = {k: dict(zip(("hiz", "yanal", "cift"), v)) for k, v in sh.items()}
    out["yayilim"] = ka.yayilim(sh)
    sh2 = ka.sektor_hiz(T, g & ~dus)
    out["sektor_ana_mod"] = {k: dict(zip(("hiz", "yanal", "cift"), v)) for k, v in sh2.items()}
    out["yayilim_ana_mod"] = ka.yayilim(sh2)
    # oylama benzetimi (B kurali): cift basina en yogun kume hizi / vref
    sonuc = {k: [] for k in ("tum", "H3_cikar", "H2_cikar", "ikisi_cikar", "on_sektor")}
    nc = 0
    for i in np.unique(T["c"][g]):
        mi = g & (T["c"] == i)
        if mi.sum() < 60:
            continue
        vref, sg, dt = T["vref"][i], T["sg"][mi][0], T["dt"][mi][0]
        varyant = {"tum": mi, "H3_cikar": mi & ~sab, "H2_cikar": mi & ~(dus & ~sab), "ikisi_cikar": mi & ~dus & ~sab, "on_sektor": mi & (np.abs(ka.aci_fark(T["az"], 0.0)) < 50)}
        for k, m in varyant.items():
            c, ic = ka.oylama(T["D"][m])
            sonuc[k].append(-c[1] * sg / dt / vref if c is not None else 0.0)
        nc += 1
    out["oylama_hiz_orani_vref"] = {k: float(np.mean(v)) if v else None for k, v in sonuc.items()}
    out["oylama_cift"] = nc
    out["oylama_dagilim_p10_50_90"] = {k: [float(np.percentile(v, 10)), float(np.percentile(v, 50)), float(np.percentile(v, 90))] for k, v in sonuc.items() if v}
    return out


def ana(etiket="once", kamera_json=None):
    if kamera_json:
        kam = km.KameraGercek(json.load(open(kamera_json, encoding="utf-8")))
    else:
        kam = km.kamera()
    Ts = {ad: ka.tablo(km.eslesme_yukle(ad), kam) for ad in KAYITLAR}
    kutup_figur(Ts, lambda T: T["s"], np.median, "Zemin yer değiştirme hızı (m/sn), hücre medyanı", "teshis_%s_kutup_hiz.png" % etiket, 0.0, 0.55, "viridis", etiket)
    kutup_figur(Ts, lambda T: (T["q"] < DUSUK).astype(float), np.mean, "Düşük hızlı (q<0,35) nokta oranı", "teshis_%s_kutup_dusuk.png" % etiket, 0.0, 1.0, "magma_r", etiket)
    sonuc = {}
    for ad, T in Ts.items():
        sonuc[ad] = o = ozet(ad, T)
        print("%-18s n %6d dusuk %.3f sabit(<1px) %.3f yansima-benzeri %.3f ana tepe %.3f | yayilim tum %.2f ana-mod %.2f | oylama/vref %s" % (
            ad, o["n"], o["dusuk"], o["sabit_pix_lt1"], o["yansima_benzeri"], o["ana_tepe"], o["yayilim"], o["yayilim_ana_mod"],
            {k: round(v, 2) for k, v in o["oylama_hiz_orani_vref"].items()}), flush=True)
    fig, axs = plt.subplots(2, 3, figsize=(13, 7))
    for ax, (ad, T) in zip(axs.ravel(), Ts.items()):
        g = secim_g(T)
        b = np.linspace(-0.5, 2.5, 61)
        sab = g & (T["pix"] < 1.0)
        ax.hist([T["q"][g & ~sab], T["q"][sab]], bins=b, stacked=True, color=["tab:blue", "tab:red"], label=["pikseli >= 1 px kayan", "pikseli sabit (< 1 px)"])
        ax.set_yscale("log")
        ax.set_title(ETIKET[ad], fontsize=10)
        ax.set_xlabel("q = hız / ön-sektör hızı")
        ax.legend(fontsize=7)
    fig.suptitle("Normalize zemin hızı dağılımı [%s]: ana tepe q~1, ikinci tepe q~0 (H2/H3)" % etiket, fontsize=11)
    fig.tight_layout()
    fig.savefig(os.path.join(km.KALIB, "teshis_%s_dagilim.png" % etiket), dpi=95)
    plt.close(fig)
    fig, axs = plt.subplots(1, 3, figsize=(15, 4.6))
    for ax, ad in zip(axs, ["2a_vitessiz_duz", "olcum_ham", "5_saksi_turu"]):
        T = Ts[ad]
        g = secim_g(T)
        ub, vb = np.arange(0, 1281, 16), np.arange(0, 641, 16)
        Ha, _, _ = np.histogram2d(T["v1"][g], T["u1"][g], bins=[vb, ub])
        Hd, _, _ = np.histogram2d(T["v1"][g & (T["q"] < DUSUK)], T["u1"][g & (T["q"] < DUSUK)], bins=[vb, ub])
        im = ax.imshow(np.where(Ha >= 10, Hd / np.maximum(Ha, 1), np.nan), extent=[0, 1280, 640, 0], cmap="magma_r", vmin=0, vmax=1)
        ax.set_title(ETIKET[ad] + ": ham karede düşük-hız oranı", fontsize=9)
        plt.colorbar(im, ax=ax, shrink=0.8)
    fig.suptitle("Düşük hızlı (q<0,35) eşleşme oranı, ham balık gözü piksel kutusu 16 px [%s]" % etiket, fontsize=11)
    fig.tight_layout()
    fig.savefig(os.path.join(km.KALIB, "teshis_%s_goruntu.png" % etiket), dpi=95)
    plt.close(fig)
    json.dump(sonuc, open(os.path.join(km.KALIB, "teshis_%s.json" % etiket), "w", encoding="utf-8"), indent=1, ensure_ascii=False)


if __name__ == "__main__":
    ana(sys.argv[1] if len(sys.argv) > 1 else "once", sys.argv[2] if len(sys.argv) > 2 else None)
