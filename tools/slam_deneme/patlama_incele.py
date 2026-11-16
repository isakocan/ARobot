"""Patlayan ve patlamayan koşuları zaman içinde karşılaştırır: ölçek kayması göstergesi (birim/komut-hızı, pencere pencere), ilk harita, ani sıçramalar.
Ölçek kayması: pencerede SLAM yolu (birim) / komuttan beklenen yol (|ileri PWM|/255 * dt, birimsiz). Sabit ölçekte oran sabittir. Pencere oranının ilk gidişe göre değişimi = kayma."""
import glob, json, os, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import parametre_olcut as po

KOSU = os.path.join(po.KLASOR, "..", "..", "kayitlar", "slam", "parametre", "kosu_v2")

def pencere_oranlari(klasor, aralik=((4, 14), (14, 22), (22, 40), (40, 60), (60, 85))):
    P, t, izl, c, R = po.yukle(klasor)
    tt, kom = po.komutlar()
    pw = np.array([abs(kom[np.argmin(abs(tt - x)), :].sum() / 2) / 255 for x in t])
    out = []
    for a, b in aralik:
        m = np.where(izl & (t >= a) & (t < b))[0]
        if len(m) < 5:
            out.append(np.nan); continue
        yol = np.linalg.norm(np.diff(c[m], axis=0), axis=1).sum()
        bek = pw[m[:-1]].sum() / 15.0
        out.append(yol / bek if bek > 0.2 else np.nan)
    return np.array(out), (P, t, izl, c)

if __name__ == "__main__":
    ad = sys.argv[1] if len(sys.argv) > 1 else "taban"
    for k in sorted(glob.glob(os.path.join(KOSU, ad + "__*"))):
        if not os.path.exists(os.path.join(k, "olcut.json")):
            continue
        o = json.load(open(os.path.join(k, "olcut.json")))
        r, (P, t, izl, c) = pencere_oranlari(k)
        # ilk harita: başlatma sonrası ilk 3 sn nokta sayısı
        ilk = np.where(izl)[0][0]
        nm = P[min(ilk + 45, len(P) - 1)]["harita_nokta_sayisi"]
        print("%-12s %s ölçek %7.2f m/birim  oranlar(birim/beklenen) %s  ilk3sn nokta %d" % (os.path.basename(k), "BOZUK" if (o.get("patladi") or o.get("yol_orani", 1) < 0.5) else "iyi  ", o.get("olcek_m_birim", np.nan), np.round(r, 3), nm))
