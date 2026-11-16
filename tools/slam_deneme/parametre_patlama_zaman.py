"""Kötü ve iyi koşuların zaman çizelgesi: harita nokta sayısı, 5 sn pencerelerde SLAM yolu / komuttan beklenen yol (ölçek göstergesi), log olayları (başlatma, döngü kapama, BEKÇİ).
python parametre_patlama_zaman.py <kotu_klasor_adi> <iyi_klasor_adi> ..."""
import os, re, sys, json
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import parametre_olcut as po

KOK = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
KOSU = os.path.join(KOK, "kayitlar", "slam", "parametre", "kosu_v2")


def olaylar(k):
    """log.txt zaman damgaları duvar saatidir. Gerçek zamanda koşulduğu için veri zamanı ~ duvar - başlangıç (yaklaşık, 1-2 sn belirsiz)."""
    p = os.path.join(KOSU, k, "log.txt")
    if not os.path.exists(p):
        return []
    L = open(p, encoding="utf-8", errors="replace").read().split("\n")
    def sn(l):
        m = re.match(r"\[(\d+):(\d+):(\d+\.\d+)\]", l)
        return int(m[1]) * 3600 + int(m[2]) * 60 + float(m[3]) if m else None
    t0 = next((sn(l) for l in L if "config file loaded" in l), None)
    out = []
    for l in L:
        for anahtar, ad in (("initialization succeeded", "başlatma"), ("detect loop", "döngü kapama"), ("BEKCI", "bekçi sıfırla")):
            if anahtar in l and sn(l) is not None and t0 is not None:
                out.append((sn(l) - t0 - 1.0, ad))     # ~1 sn: sözlük yükleme
    return out


def ciz(adlar, yol):
    fig, ax = plt.subplots(len(adlar), 2, figsize=(13, 3.2 * len(adlar)), squeeze=False)
    tt, kom = po.komutlar()
    for j, k in enumerate(adlar):
        P, t, izl, c, R = po.yukle(os.path.join(KOSU, k))
        o = json.load(open(os.path.join(KOSU, k, "olcut.json")))
        n = np.array([p["harita_nokta_sayisi"] for p in P])
        a = ax[j, 0]
        a.plot(t, n, lw=1); a.set_xlim(0, 90); a.set_ylabel("harita noktası")
        for te, ad in olaylar(k):
            if te < 90:
                a.axvline(te, c={"başlatma": "g", "döngü kapama": "r", "bekçi sıfırla": "m"}[ad], ls="--", lw=1)
        a.set_title("%s  %s  ölçek %.1f m/birim" % (k, "BOZUK" if (o.get("patladi") or o.get("yol_orani", 1) < 0.5) else "iyi", o.get("olcek_m_birim", np.nan)), fontsize=9)
        pw = np.array([abs(kom[np.argmin(abs(tt - x)), :].sum() / 2) / 255 for x in t])
        b = ax[j, 1]
        xs, ys = [], []
        for w in range(4, 85, 5):
            m = np.where(izl & (t >= w) & (t < w + 5))[0]
            if len(m) < 8:
                continue
            bek = pw[m[:-1]].sum() / 15.0
            if bek < 0.4:
                continue
            xs.append(w + 2.5); ys.append(np.linalg.norm(np.diff(c[m], axis=0), axis=1).sum() / bek)
        b.plot(xs, ys, "o-"); b.set_ylabel("SLAM yolu / beklenen (birim)"); b.set_xlim(0, 90); b.grid(alpha=0.3)
    ax[-1, 0].set_xlabel("veri zamanı (sn)"); ax[-1, 1].set_xlabel("veri zamanı (sn)")
    fig.suptitle("yeşil: başlatma, kırmızı: döngü kapama, mor: bekçi sıfırlama (log zamanı yaklaşık)")
    fig.tight_layout(); fig.savefig(yol, dpi=100)


if __name__ == "__main__":
    ciz(sys.argv[2:], sys.argv[1])
