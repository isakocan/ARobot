"""Kusbakisi (BEV) zemin hizinin AZIMUT SEKTORUNE gore tutarliligi: kamera geometrisi (mercek merkezi / egim) icin bagimsiz denetim.
Duz giden robotta zemin tum yonlerde ayni hizla kayar. Ufuk/merkez hatasi (egim) varsa on ve arka sektor ters yonde yanilir, yan sektorler az;
yukseklik h hatasi ise hepsini ayni oranda olcekler. Her sektor icin (on, arka, sag, sol) ayri BEV oteleyicisi calistirilir (yalnizca o sektorun
zemin noktalariyla), ileri hiz = sum(ileri)/sum(dt) ve toplam |adim| ayri raporlanir.
  python gercek_sektor.py <maske> <kayit> [kayit ...]
Yalnizca teshis; E'nin parametrelerine dokunmaz. BEV/LCN sinifi birlesik_bev.LcnTablo, oteleyici konum_bev.BevOtelemeci (aynen)."""
import json
import os
import sys

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_ortak as go  # noqa: E402


def sektor_hizlari(maske, ad):
    bo, ko, zo = go.kur(maske)
    import cv2
    cv2.setNumThreads(2)
    import birlesik_bev as bb
    import birlesik_yon as by
    import konum_bev as kb
    from kayit import Kayit
    from kamera import Kamera
    s = bo.spec(ad)
    kay = Kayit(s["theta"])
    kam = Kamera.bilgiden(kay.bilgi)
    yz = by.yukle(ad, True)
    tab = bb.LcnTablo("theta", kam, lcn_par=bb.LcnParam, h=float(kay.bilgi["kamera_yuksekligi_m"]))
    n = len(kay)
    t = kay.t_ms / 1000.0
    kom = np.array([kay.komut(int(x)) for x in kay.t_ms])
    # hareketli aralik: ilk ve son hareketli kareden 30 kare pay
    hareket = np.where((np.abs(kom[:, 0]) + np.abs(kom[:, 1])) > 60)[0]
    i0, i1 = max(hareket[0] - 10, 0), min(hareket[-1] + 40, n)
    az = tab.az_der
    sektorler = {"on": (np.abs(((az + 180) % 360) - 180) < 50), "arka": (np.abs(az - 180) < 50), "sag": (np.abs(az - 90) < 50), "sol": (np.abs(az - 270) < 50)}
    sonuc = {}
    for ad_s, m in sektorler.items():
        oz = kb.BevOtelemeci(tab)
        oz.orb = cv2.ORB_create(nfeatures=500, scaleFactor=1.2, nlevels=3, edgeThreshold=13, fastThreshold=bb.LcnParam.fast_esik, patchSize=21)
        em = (m.astype(np.uint8) * 255)
        D = np.full((n, 2), np.nan)
        OK = np.zeros(n, bool)
        for i in range(i0, i1):
            ds, di, ok, ic = oz.guncelle(kay.resim(i), float(yz["yon"][i]), em)
            D[i] = (ds, di)
            OK[i] = ok
        sonuc[ad_s] = (D, OK)
    # ileri/geri duz gidis kareleri (komut 3 kare gecikmeli, kararli)
    kg = np.vstack([np.zeros((3, 2)), kom[:-3]])
    kar = np.zeros(n, bool)
    for i in range(12, n):
        w = kg[i - 12:i + 1]
        kar[i] = (w.max(0) - w.min(0)).max() < 20
    duz = (np.abs(kg[:, 0] - kg[:, 1]) < 25) & (np.abs(kg[:, 0]) > 100) & kar
    dt = np.clip(np.diff(t, prepend=t[0] - 1 / 15.0), 0.02, 0.3)
    cikti = {}
    print("== %s (maske %s, h=%.3f): duz gidis kareleri ileri %d geri %d" % (ad, maske, float(kay.bilgi["kamera_yuksekligi_m"]), int((duz & (kg[:, 0] > 0)).sum()), int((duz & (kg[:, 0] < 0)).sum())))
    for yon, isaret in (("ileri", 1), ("geri", -1)):
        m = duz & (np.sign(kg[:, 0]) == isaret)
        satir = {}
        for ad_s, (D, OK) in sonuc.items():
            mo = m & OK & np.isfinite(D[:, 1])
            if mo.sum() < 8:
                satir[ad_s] = None
                continue
            v = float(np.sum(D[mo, 1] * isaret) / np.sum(dt[mo]))          # komut yonunde ileri hiz (m/sn)
            vy = float(np.sum(D[mo, 0]) / np.sum(dt[mo]))                  # yanal (sag) hiz, 0 beklenir
            satir[ad_s] = dict(n=int(mo.sum()), ok_orani=float(mo.sum() / m.sum()), hiz=v, yanal=vy)
        cikti[yon] = satir
        print("  %-5s: " % yon + "  ".join("%s %s" % (k, "yok" if v is None else "%.3f m/sn (n=%d, ok %.2f, yanal %+.3f)" % (v["hiz"], v["n"], v["ok_orani"], v["yanal"])) for k, v in satir.items()))
    return cikti


if __name__ == "__main__":
    maske = sys.argv[1]
    tum = {}
    for ad in sys.argv[2:]:
        tum[ad] = sektor_hizlari(maske, ad)
    json.dump(tum, open(os.path.join(go.SONUC, "sektor_hiz_%s.json" % maske), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
