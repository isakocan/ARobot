"""Zemin siniri mesafeleri ('sahte lidar') kalibre geometriyle degisiyor mu? olcum_ham (genis salon), SegFormer taramasi onceki (harita_gercek) ve sonraki (harita_gercek_kalib).
  python gercek_kalib_zemin.py
Olcut: sabit karelerde (komut = 0; t < 3,3 sn ve 11,5-20 sn) yan duvarlarin zemin sinirinin DOGRULUGU:
  (1) her yan sektorde (sol 235-305, sag 55-125 derece) RANSAC dogru uyumu (tolerans 3 cm): inlier orani ve RMS;
  (2) sol ve sag duvarin yonleri (salon duvarlari paralel): |aci_sol - aci_sag| (derece), medyan;
  (3) sol/sag duvarin robota dik uzakliklarinin toplami (salon genisligi) kare-kare tutarliligi (std).
Cikti: kalib/zemin_siniri_once_sonra.png (kusbakisi, 3 ornek kare), kalib/zemin_siniri.json"""
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
from kayit import Kayit  # noqa: E402

plt.rcParams["font.family"] = "DejaVu Sans"
ONCE = os.path.join(go.SONUC, "harita_gercek", "tarama", "model_gorunum", "theta", "olcum_ham.npz")
SONRA = os.path.join(go.SONUC, "harita_gercek_kalib", "tarama", "model_gorunum", "theta", "olcum_ham.npz")
SEKTOR = {"sol": (235, 305), "sag": (55, 125), "on": (320, 40), "arka": (140, 220)}


def noktalar(M, G, menzil, a0, a1):
    az = np.arange(360)
    m = (az >= a0) & (az <= a1) if a0 < a1 else ((az >= a0) | (az <= a1))
    ok = m & np.isfinite(M) & (M < menzil * 0.97) & (G >= 0.2)
    r = np.radians(az[ok] + 0.5)
    return np.stack([M[ok] * np.sin(r), M[ok] * np.cos(r)], 1)


def ransac_dogru(P, tol=0.03, it=200, rng=np.random.default_rng(0)):
    if len(P) < 12:
        return None
    best = None
    for _ in range(it):
        i, j = rng.choice(len(P), 2, replace=False)
        d = P[j] - P[i]
        n = np.hypot(*d)
        if n < 0.05:
            continue
        nrm = np.array([-d[1], d[0]]) / n
        uz = np.abs((P - P[i]) @ nrm)
        ic = uz < tol
        if best is None or ic.sum() > best[0].sum():
            best = (ic, nrm)
    ic = best[0]
    Q = P[ic]
    c = Q.mean(0)
    u, s, vt = np.linalg.svd(Q - c)
    yon = vt[0]
    nrm = np.array([-yon[1], yon[0]])
    rms = float(np.sqrt(np.mean(((Q - c) @ nrm) ** 2)))
    aci = float(np.degrees(np.arctan2(yon[0], yon[1])) % 180)          # dogru yonu (ileri ekseninden saat yonu), 0-180
    dik = float(abs(c @ nrm))                                           # robota dik uzaklik
    return dict(ic_oran=float(ic.mean()), n=int(ic.sum()), rms=rms, aci=aci, dik=dik)


def olc(z, kareler):
    out = {k: [] for k in ("sol", "sag", "on", "arka")}
    par, gen = [], []
    for j in kareler:
        d = {}
        for ad, (a0, a1) in SEKTOR.items():
            P = noktalar(z["mesafe"][j], z["guven"][j], float(z["max_menzil"]), a0, a1)
            r = ransac_dogru(P)
            d[ad] = r
            if r:
                out[ad].append(r)
        if d["sol"] and d["sag"]:
            da = abs(((d["sol"]["aci"] - d["sag"]["aci"]) + 90) % 180 - 90)
            par.append(da)
            gen.append(d["sol"]["dik"] + d["sag"]["dik"])
    ozet = {}
    for ad, L in out.items():
        if L:
            ozet[ad] = dict(kare=len(L), ic_oran_medyan=float(np.median([r["ic_oran"] for r in L])), rms_mm_medyan=float(1000 * np.median([r["rms"] for r in L])),
                            uzaklik_medyan=float(np.median([r["dik"] for r in L])))
    ozet["sol_sag_paralellik_der_medyan"] = float(np.median(par)) if par else None
    ozet["sol_sag_paralel_kare"] = len(par)
    ozet["salon_genisligi_medyan"] = float(np.median(gen)) if gen else None
    ozet["salon_genisligi_std"] = float(np.std(gen)) if gen else None
    return ozet


if __name__ == "__main__":
    a = np.load(ONCE)
    b = np.load(SONRA)
    kay = Kayit(os.path.join(go.GERCEK, "olcum_ham"))
    t = kay.t_ms / 1000.0
    kom = np.array([kay.komut(int(x)) for x in kay.t_ms])
    kg = np.vstack([np.zeros((6, 2)), kom[:-6]])
    dur = (np.abs(kg).sum(1) == 0)
    kareler = []
    for j, k in enumerate(a["kare"]):
        tt = t[int(k)]
        if dur[int(k)] and (tt < 3.3 or 11.5 <= tt <= 20.0):
            kareler.append(j)
    assert (a["kare"] == b["kare"]).all()
    print("sabit kare", len(kareler))
    oz = dict(once=olc(a, kareler), sonra=olc(b, kareler), kare=len(kareler))
    for k in ("once", "sonra"):
        print(k, json.dumps(oz[k], indent=None, ensure_ascii=False))
    # sektor ozeti (menzil siniri 2,63 m; 6 m ustu atilir): menzil ici orani, medyan uzaklik (guven >= 0,2)
    for k, z in (("once", a), ("sonra", b)):
        sk = {}
        for ad, (a0, a1) in SEKTOR.items():
            az = np.arange(360)
            m = (az >= a0) & (az <= a1) if a0 < a1 else ((az >= a0) | (az <= a1))
            M = z["mesafe"][kareler][:, m]
            G = z["guven"][kareler][:, m]
            ok = np.isfinite(M) & (G >= 0.2) & (M < 6.0)
            sk[ad] = dict(menzil_ici=float((ok & (M < float(z["max_menzil"]) * 0.97)).mean()), medyan_uzaklik=float(np.median(M[ok])) if ok.any() else None, gecerli=float(ok.mean()))
        oz[k]["sektor_ozet"] = sk
        print(k, {a_: (round(v["menzil_ici"], 2), None if v["medyan_uzaklik"] is None else round(v["medyan_uzaklik"], 2), round(v["gecerli"], 2)) for a_, v in sk.items()})
    json.dump(oz, open(os.path.join(km.KALIB, "zemin_siniri.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    ornek = [kareler[2], kareler[len(kareler) // 2], kareler[-3]]
    fig, axs = plt.subplots(2, 3, figsize=(14, 9), sharex=True, sharey=True)
    for c, j in enumerate(ornek):
        for r, (z, et) in enumerate(((a, "önce (kamera.json)"), (b, "sonra (kamera_gercek.json)"))):
            ax = axs[r, c]
            az = np.radians(np.arange(360) + 0.5)
            M = z["mesafe"][j]
            ok = np.isfinite(M) & (M < float(z["max_menzil"]) * 0.97) & (z["guven"][j] >= 0.2)
            ax.plot((M * np.sin(az))[ok], (M * np.cos(az))[ok], ".", ms=3.5, color="tab:red" if r == 0 else "tab:green")
            ax.plot(0, 0, "k^", ms=9)
            ax.set_aspect("equal")
            ax.set_xlim(-2.8, 2.8)
            ax.set_ylim(-2.8, 2.8)
            ax.grid(alpha=0.3)
            ax.set_title("%s, kare %d (t=%.1f sn)" % (et, a["kare"][j], t[int(a["kare"][j])]), fontsize=9)
    fig.suptitle("olcum_ham geniş salon: zemin sınırı (kuşbakışı, robot önü yukarı; boş yer = menzil dışı/güvensiz)", fontsize=11)
    fig.tight_layout()
    fig.savefig(os.path.join(km.KALIB, "zemin_siniri_once_sonra.png"), dpi=95)
