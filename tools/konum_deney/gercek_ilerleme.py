"""Ilerleme olcumu (kusbakisi zemin dokusu, h=0,17 m): olculen hiz ve komut PWM'i ile tutarlilik. Yer gercegi YOK.
  python gercek_ilerleme.py [maske=eski]
Girdi: onbellek_<maske>/bevLLkomutlu_<kayit>.npz (kare, d (n,2) = (sag, ileri) artim, ok, ic, ms), motor durumu (komutlar.csv).
Olculenler: (1) B'nin gecerli oldugu kare orani (ok) ve ic nokta; (2) duz gidiste (sol~sag) olculen ileri hiz (m/sn) PWM seviyesine gore,
hali/parke ayri, ileri/geri ayri; (3) komut-nominal (0,55 m/sn @255, SIM VARSAYIMI) ile oran; (4) hiz-zaman grafigi."""
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
ZEMIN = {"1_bekleme": "hali", "2a_vitessiz_duz": "hali", "2b_otomatik_duz": "hali", "3_vitessiz_donus": "hali", "4_parke_vitessiz": "parke",
         "5_saksi_turu": "parke", "6a_far_isik_acik": "parke", "6b_far_karanlik": "parke", "6c_karanlik_surus": "parke"}
LAG = 3      # kare: motor durumu -> goruntu gecikmesi (gercek_zaman.py: 2-4 kare)
V_TAM = 0.55


def seri(ad, maske):
    p = os.path.join(go.onbellek(maske), "bevLLkomutlu_%s.npz" % ad)
    if not os.path.exists(p):
        return None
    z = np.load(p)
    k = Kayit(os.path.join(go.GERCEK, ad))
    t = k.t_ms / 1000.0
    kom = np.array([k.komut(int(x)) for x in k.t_ms])
    kom_g = np.vstack([np.zeros((LAG, 2)), kom[:-LAG]])           # goruntu karesi i, motor i-LAG'i yansitir
    return dict(t=t, d=z["d"], ok=z["ok"], ic=z["ic"], kom=kom, kom_g=kom_g)


def ana(maske="eski"):
    sonuc = {}
    ham = {"hali": {"ileri": [], "geri": []}, "parke": {"ileri": [], "geri": []}}
    for ad in go.ADLAR:
        s = seri(ad, maske)
        if s is None:
            continue
        t, d, ok, ic, kg = s["t"], s["d"], s["ok"], s["ic"], s["kom_g"]
        dt = np.clip(np.diff(t, prepend=t[0] - 1 / 15.0), 0.02, 0.3)
        adim = np.hypot(d[:, 0], d[:, 1])
        ileri = d[:, 1]
        hareketli = (np.abs(kg[:, 0]) + np.abs(kg[:, 1])) > 60
        duz = (np.abs(kg[:, 0] - kg[:, 1]) < 25) & (np.abs(kg[:, 0]) > 80)
        # kararli: komut son 12 karede (0,8 sn) 20 PWM'den fazla degismemis (hizlanma/yavaslama rampasi disarida)
        kar = np.zeros(len(t), bool)
        for i in range(12, len(t)):
            w_ = kg[i - 12:i + 1]
            kar[i] = (w_.max(0) - w_.min(0)).max() < 20
        duz &= kar
        ok_ = ok & np.isfinite(adim)
        r = dict(kare=len(t), hareketli_kare=int(hareketli.sum()), ok_orani_hareketli=float(ok_[hareketli].mean()) if hareketli.any() else None,
                 ic_ort=float(ic[ok_ & hareketli].mean()) if (ok_ & hareketli).any() else None,
                 ok_orani_durgun=float(ok_[~hareketli].mean()) if (~hareketli).any() else None,
                 durgun_adim_ort_mm=float(1000 * np.nanmean(adim[~hareketli & ok_])) if (~hareketli & ok_).any() else None)
        kov = {}
        for yon, isaret in (("ileri", 1), ("geri", -1)):
            for lo, hi in ((80, 140), (140, 200), (200, 240), (240, 256)):
                m = duz & (np.sign(kg[:, 0]) == isaret) & (np.abs(kg[:, 0]) >= lo) & (np.abs(kg[:, 0]) < hi)
                if m.sum() < 15:
                    continue
                mo = m & ok_
                if mo.sum() < 8:
                    kov["%s %d-%d" % (yon, lo, hi)] = dict(n=int(m.sum()), ok_orani=float(mo.sum() / m.sum()), hiz_buyukluk=None)
                    continue
                v = float(np.sum(ileri[mo]) / np.sum(dt[mo]))
                vh = float(np.sum(adim[mo]) / np.sum(dt[mo]))
                kov["%s %d-%d" % (yon, lo, hi)] = dict(n=int(m.sum()), ok_orani=float(mo.sum() / m.sum()), hiz_isaretli=v, hiz_buyukluk=vh,
                                                      pwm_ort=float(np.abs(kg[mo, 0]).mean()), nominal=float(V_TAM * np.abs(kg[mo, 0]).mean() / 255.0),
                                                      yanlis_isaret_orani=float(np.mean(np.sign(ileri[mo]) != isaret)))
                ham[ZEMIN[ad]][yon].append((np.abs(kg[mo, 0]).mean(), vh, int(mo.sum()), ad))
        # toplam oran: B gecerli ve komutun ileri hizi (nominal) > 0,05 m/sn olan karelerde (olculen ileri yol) / (nominal ileri yol); kararli kosul YOK
        vnom = V_TAM * (kg[:, 0] + kg[:, 1]) / 2 / 255.0
        mm = ok_ & (np.abs(vnom) > 0.05)
        if mm.sum() > 10:
            r["toplam_oran"] = float(np.sum(ileri[mm] * np.sign(vnom[mm])) / np.sum(np.abs(vnom[mm]) * dt[mm]))
            r["toplam_oran_kare"] = int(mm.sum())
            r["isaret_uyumu"] = float(np.mean(np.sign(ileri[mm]) == np.sign(vnom[mm])))
            print("   toplam: B-ileri-yol / nominal-yol = %.2f (%d kare), isaret uyumu %.2f" % (r["toplam_oran"], mm.sum(), r["isaret_uyumu"]))
        r["kovalar"] = kov
        sonuc[ad] = r
        print("== %s (%s): hareketli kare %d, B gecerli (ok) %.2f, ic ort %.0f, durgunken ok %.2f adim ort %.1f mm/kare" % (
            ad, ZEMIN[ad], r["hareketli_kare"], r["ok_orani_hareketli"] or np.nan, r["ic_ort"] or np.nan, r["ok_orani_durgun"] or np.nan, r["durgun_adim_ort_mm"] or np.nan))
        for kk, v in kov.items():
            if v.get("hiz_buyukluk") is None:
                print("    %-14s n=%3d ok=%.2f  (yetersiz gecerli kare)" % (kk, v["n"], v["ok_orani"]))
            else:
                print("    %-14s n=%3d ok=%.2f  olculen %.3f m/sn (isaretli %.3f; yanlis isaret %.2f)  PWM ort %.0f -> nominal(0,55@255) %.3f m/sn  oran %.2f" % (
                    kk, v["n"], v["ok_orani"], v["hiz_buyukluk"], v["hiz_isaretli"], v["yanlis_isaret_orani"], v["pwm_ort"], v["nominal"], v["hiz_buyukluk"] / v["nominal"]))
        fig, ax = plt.subplots(2, 1, figsize=(11, 5.5), sharex=True)
        w = 15
        sp = np.where(ok_, ileri / dt, np.nan)
        ker = np.ones(w) / w
        sv = np.isfinite(sp).astype(float)
        yog = np.convolve(sv, ker, "same")
        sm = np.convolve(np.nan_to_num(sp), ker, "same") / np.maximum(yog, 1e-3)
        ax[0].plot(t, sp, ".", ms=1.5, color="0.6", label="kare basina olcum (gecerli)")
        ax[0].plot(t, np.where(yog > 0.15, sm, np.nan), "b", lw=1.2, label="1 sn ortalama")
        ax[0].plot(t, V_TAM * (kg[:, 0] + kg[:, 1]) / 2 / 255.0, "k--", lw=1, label="komut nominal (0,55 m/sn @255, SIM varsayimi)")
        ax[0].set_ylim(-1.2, 1.2)
        ax[0].set_ylabel("ileri hiz (m/sn)")
        ax[0].legend(fontsize=8, loc="upper right")
        ax[0].set_title("%s (%s): zemin dokusundan olculen ileri hiz (h=0,17 m) ve komut" % (ad, ZEMIN[ad]), fontsize=10)
        ax[0].grid(alpha=0.3)
        ax[1].plot(t, ok_.astype(float) * 0.9 + 0.05, lw=0.6, color="g", label="yesil: B gecerli")
        ax[1].plot(t, kg[:, 0] / 255.0 * 0.4 + 0.5, lw=0.8, label="sol motor", alpha=0.7)
        ax[1].plot(t, kg[:, 1] / 255.0 * 0.4 + 0.5, lw=0.8, label="sag motor", alpha=0.7)
        ax[1].set_ylabel("B gecerli; motor (olcekli)")
        ax[1].set_xlabel("zaman (sn)")
        ax[1].legend(fontsize=7, loc="upper right")
        fig.tight_layout()
        fig.savefig(os.path.join(go.SONUC, "ilerleme_%s_%s.png" % (maske, ad)), dpi=105)
        plt.close(fig)
    json.dump(sonuc, open(os.path.join(go.SONUC, "ilerleme_%s.json" % maske), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    fig, ax = plt.subplots(figsize=(7, 5))
    for zem, renk in (("hali", "tab:blue"), ("parke", "tab:orange")):
        for yon, mark in (("ileri", "o"), ("geri", "s")):
            for pwm, v, n, ad in ham[zem][yon]:
                ax.scatter(pwm, v, c=renk, marker=mark, s=20 + n / 3, alpha=0.7)
    ax.plot([0, 255], [0, V_TAM], "k--", label="nominal (0,55 m/sn @255)")
    ax.scatter([], [], c="tab:blue", label="hali")
    ax.scatter([], [], c="tab:orange", label="parke")
    ax.scatter([], [], c="k", marker="o", label="ileri")
    ax.scatter([], [], c="k", marker="s", label="geri")
    ax.set_xlabel("PWM (|sol|, duz gidis)")
    ax.set_ylabel("olculen hiz (m/sn)")
    ax.legend(fontsize=8)
    ax.grid(alpha=0.3)
    ax.set_title("Olculen hiz ve PWM (zemin dokusu, h=0,17 m)", fontsize=10)
    fig.tight_layout()
    fig.savefig(os.path.join(go.SONUC, "ilerleme_pwm_hiz_%s.png" % maske), dpi=110)


if __name__ == "__main__":
    ana(sys.argv[1] if len(sys.argv) > 1 else "eski")
