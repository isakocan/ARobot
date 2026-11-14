"""Gerçek kayıtlarda M2D yön analizi (yer gerçeği YOK; yalnız tutarlılık denetimleri) + yön-zaman grafikleri.
  python gercek_yon_analiz.py [maske=eski]
Ölçülenler: (1) hareketsizde sürüklenme, (2) komutla birlikte işaret/oran (görsel dönüş / komutun nominal dönüşü; nominal = bilgi.json kayma_modeli'ndeki
SİM VARSAYIMI 197 derece/sn), (3) düz gidişte yön kayması (komut sol=sag iken), (4) iç nokta sayısı, kare başına süre, durum dağılımı."""
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
from kayit import Kayit  # noqa: E402

plt.rcParams["font.family"] = "DejaVu Sans"
W_TAM = 197.0      # derece/sn @ (255,-255): SİM VARSAYIMI


def yukle(maske, ad, tur):
    ob = go.onbellek(maske)
    p = os.path.join(ob, {"ham_komutsuz": "yon_komutsuz_gercek_%s.npz", "ham_komutlu": "yon_komutlu_gercek_%s.npz",
                          "lcn_komutlu": "yonL_komutlu_%s.npz", "lcn_komutsuz": "yonL_komutsuz_%s.npz"}[tur] % ad)
    if not os.path.exists(p):
        return None
    z = np.load(p)
    return {k: z[k] for k in z.files}


def komut_yaw(k, t_ms):
    """Komutun nominal dönüşünün integrali (derece, saat yönü +): w = W_TAM*(sol-sag)/510. Hold, yamuk kuralı."""
    t = t_ms / 1000.0
    sol = np.array([k.komut(int(x))[0] for x in t_ms])
    sag = np.array([k.komut(int(x))[1] for x in t_ms])
    w = W_TAM * (sol - sag) / 510.0
    dt = np.diff(t, prepend=t[0])
    return np.cumsum(0.5 * (w + np.r_[w[0], w[:-1]]) * dt), sol, sag


def ana(maske="eski"):
    satirlar = []
    for ad in go.ADLAR:
        k = Kayit(os.path.join(go.GERCEK, ad))
        t_ms = k.t_ms
        t = t_ms / 1000.0
        yc, sol, sag = komut_yaw(k, t_ms)
        v = {tur: yukle(maske, ad, tur) for tur in ("ham_komutsuz", "lcn_komutlu")}
        if v["ham_komutsuz"] is None:
            continue
        yh = np.degrees(np.unwrap(np.radians(v["ham_komutsuz"]["yon"])))
        # komut dönüşü sıfır olmayan karelerde ölçek oranı (görsel / komut), pencere: |komut dönüşü|>0,3 derece/kare
        dyh = np.diff(yh, prepend=yh[0])
        dyc = np.diff(yc, prepend=yc[0])
        m = np.abs(dyc) > 0.3
        oran = np.sum(dyh[m] * np.sign(dyc[m])) / max(np.sum(np.abs(dyc[m])), 1e-9) if m.any() else np.nan
        isaret = np.mean(np.sign(dyh[m]) == np.sign(dyc[m])) if m.any() else np.nan
        # düz: sol ~ sag ve |sol|>100
        duz = (np.abs(sol - sag) < 20) & (np.abs(sol) > 100)
        duz_kayma = np.sum(dyh[duz])
        ic = v["ham_komutsuz"]["icerik"]
        durum = np.bincount(v["ham_komutsuz"]["durum"], minlength=7)
        satir = dict(ad=ad, kare=len(t), sure=float(t[-1] - t[0]), yaw_son_gorsel=float(yh[-1]), yaw_son_komut=float(yc[-1]),
                     yaw_min=float(yh.min()), yaw_maks=float(yh.max()), komut_min=float(yc.min()), komut_maks=float(yc.max()),
                     oran=float(oran), isaret_uyumu=float(isaret), duz_kare=int(duz.sum()), duz_kayma_derece=float(duz_kayma),
                     ic_ort=float(ic[ic > 0].mean()) if (ic > 0).any() else 0.0, ic_p10=float(np.percentile(ic[ic > 0], 10)) if (ic > 0).any() else 0.0,
                     tut_orani=float(durum[2] / len(t)), yedek_orani=float((durum[6] + durum[5]) / len(t)), ms=float(v["ham_komutsuz"]["ms"].mean()),
                     ms_p95=float(np.percentile(v["ham_komutsuz"]["ms"], 95)))
        if v["lcn_komutlu"] is not None:
            yl = np.degrees(np.unwrap(np.radians(v["lcn_komutlu"]["yon"])))
            satir["lcn_yaw_son"] = float(yl[-1])
            satir["lcn_ic_ort"] = float(v["lcn_komutlu"]["icerik"][v["lcn_komutlu"]["icerik"] > 0].mean()) if (v["lcn_komutlu"]["icerik"] > 0).any() else 0.0
            dl = np.bincount(v["lcn_komutlu"]["durum"], minlength=7)
            satir["lcn_yedek_orani"] = float((dl[6] + dl[5]) / len(t))
        satirlar.append(satir)
        fig, ax = plt.subplots(3, 1, figsize=(11, 8), sharex=True, gridspec_kw=dict(height_ratios=[3, 1.2, 1.2]))
        ax[0].plot(t, yh, label="görsel pusula M2D (komutsuz, yalnız görüntü)", lw=1.4)
        if v["lcn_komutlu"] is not None:
            ax[0].plot(t, yl, label="M2D-LCN + komut önceli (birleşik kestiricinin girdisi)", lw=1, alpha=0.8)
        ax[0].plot(t, yc, "k--", label="komutun nominal dönüşü (197 derece/sn @ tam fark; SİM varsayımı)", lw=1)
        ax[0].set_ylabel("birikimli yön (derece, saat yönü +)")
        ax[0].legend(fontsize=8, loc="best")
        ax[0].grid(alpha=0.3)
        ax[0].set_title("%s: yön-zaman (yer gerçeği yok; çizgi aynı başlangıç 0'dan)" % ad, fontsize=10)
        ax[1].plot(t, sol, label="sol motor", lw=0.8)
        ax[1].plot(t, sag, label="sağ motor", lw=0.8)
        ax[1].set_ylabel("PWM")
        ax[1].legend(fontsize=7, loc="upper right")
        ax[2].plot(t, ic, "g", lw=0.7)
        ax[2].set_ylabel("M2D iç nokta")
        ax[2].set_xlabel("zaman (sn)")
        fig.tight_layout()
        fig.savefig(os.path.join(go.SONUC, "yon_%s.png" % ad), dpi=105)
        plt.close(fig)
    json.dump(satirlar, open(os.path.join(go.SONUC, "yon_ozet_%s.json" % maske), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    for s in satirlar:
        print("%-20s süre %5.1f yaw_son(görsel) %8.1f  komut %8.1f  | min/maks görsel %7.1f/%7.1f komut %7.1f/%7.1f | oran %.2f işaret %.2f | düz kare %d kayma %.1f° | iç ort %.0f p10 %.0f | tut %.2f yedek %.2f | %.0f ms (p95 %.0f)" % (
            s["ad"], s["sure"], s["yaw_son_gorsel"], s["yaw_son_komut"], s["yaw_min"], s["yaw_maks"], s["komut_min"], s["komut_maks"], s["oran"], s["isaret_uyumu"],
            s["duz_kare"], s["duz_kayma_derece"], s["ic_ort"], s["ic_p10"], s["tut_orani"], s["yedek_orani"], s["ms"], s["ms_p95"]))
        if "lcn_yaw_son" in s:
            print("     LCN+komut: yaw_son %.1f iç ort %.0f yedek %.2f" % (s["lcn_yaw_son"], s["lcn_ic_ort"], s["lcn_yedek_orani"]))


if __name__ == "__main__":
    ana(sys.argv[1] if len(sys.argv) > 1 else "eski")
