"""Yeniden bulma ölçüt yardımcıları: kümeler (E1-E3), başarı/yanlış-kesin oranları, uzaklık dilimleri, markdown tablo."""
import numpy as np

import yeniden_bul_cekirdek as yc

ESIK1 = (0.3, 10.0)
ESIK2 = (0.5, 15.0)
D_DILIM = [(0.0, 0.5), (0.5, 1.0), (1.0, 1.5), (1.5, 2.0), (2.0, 99.0)]


def karar_uygula(satirlar, e=None, konum="son"):
    """Her satıra tek kare kararı: tanindi, poz, hata (m, derece)."""
    e = e or yc.ESIK
    for s in satirlar:
        t, p, g = yc.karar(s["ham"], e)
        s["tanindi"], s["guven"] = t, g
        if t:
            pz = s["p_" + konum]
            s["poz"] = pz
            s["hata_m"], s["hata_y"] = s["e_" + konum], s["y_" + konum]
        else:
            s["poz"] = None
            s["hata_m"] = s["hata_y"] = np.nan
    return satirlar


def dogru(s, esik):
    return s["tanindi"] and s["hata_m"] <= esik[0] and s["hata_y"] <= esik[1]


def yanlis(s, esik=ESIK2):
    return s["tanindi"] and not (s["hata_m"] <= esik[0] and s["hata_y"] <= esik[1])


def ozet(satirlar):
    """Tanınma kümesi (konumu bilinen yerler) için ölçütler."""
    n = len(satirlar)
    if n == 0:
        return dict(n=0)
    tan = [s for s in satirlar if s["tanindi"]]
    d1 = [s for s in satirlar if dogru(s, ESIK1)]
    d2 = [s for s in satirlar if dogru(s, ESIK2)]
    yan = [s for s in satirlar if yanlis(s)]
    h = np.array([s["hata_m"] for s in d2]) if d2 else np.array([])
    y = np.array([s["hata_y"] for s in d2]) if d2 else np.array([])
    return dict(n=n, tanindi=len(tan), basari1=len(d1), basari2=len(d2), yanlis=len(yan),
                med_m=float(np.median(h)) if len(h) else np.nan, med_y=float(np.median(y)) if len(y) else np.nan,
                p90_m=float(np.percentile(h, 90)) if len(h) else np.nan)


def pct(a, b):
    return "%.0f%%" % (100.0 * a / b) if b else "-"


def ozet_satir(ad, o):
    if o["n"] == 0:
        return "| %s | 0 | - | - | - | - | - | - |" % ad
    return "| %s | %d | %s (%d) | %s (%d) | %s (%d) | %s (%d) | %s | %s |" % (
        ad, o["n"], pct(o["tanindi"], o["n"]), o["tanindi"], pct(o["basari1"], o["n"]), o["basari1"], pct(o["basari2"], o["n"]),
        o["basari2"], pct(o["yanlis"], o["n"]), o["yanlis"],
        "%.2f" % o["med_m"] if np.isfinite(o["med_m"]) else "-", "%.1f" % o["med_y"] if np.isfinite(o["med_y"]) else "-")


BASLIK = ("| küme | sorgu | tanıdım | başarı (0,3 m, 10°) | başarı (0,5 m, 15°) | yanlış-kesin | medyan hata m | medyan yaw ° |\n"
          "|---|---|---|---|---|---|---|---|")


def dilim_tablosu(satirlar, anahtar="d_kf"):
    sat = ["| %s | sorgu | tanıdım | başarı (0,5 m, 15°) | yanlış-kesin | medyan hata m |" % ("en yakın anahtar kare uzaklığı (m)"), "|---|---|---|---|---|---|"]
    for a, b in D_DILIM:
        k = [s for s in satirlar if a <= s[anahtar] < b]
        o = ozet(k)
        if o["n"] == 0:
            continue
        sat.append("| %.1f-%s | %d | %s | %s | %s | %s |" % (a, ("%.1f" % b) if b < 90 else "+", o["n"], pct(o["tanindi"], o["n"]),
                                                              pct(o["basari2"], o["n"]), pct(o["yanlis"], o["n"]),
                                                              "%.2f" % o["med_m"] if np.isfinite(o["med_m"]) else "-"))
    return "\n".join(sat)
