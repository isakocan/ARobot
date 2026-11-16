"""Özet tablo: her yapılandırma için tekrar sayısı, bozuk sayısı (Wilson %95 aralığı), taban ile Fisher kesin sınama p değeri, bozuk olmayan koşularda ortanca (min..maks).
Bozuk: bant ölçekli yol > 63 m ya da 82,4 sn başlangıca uzaklık > 3 m ya da başlama > 20 sn ya da yol < 10,5 m (PO.olc 'patladi' + yol_orani<0.5).
Aynı (varyant, tekrar) için en son satır alınır. Çıktı: kayitlar/slam/sonuc/parametre_ozet.md ve parametre_ozet.json."""
import json, os, sys
import numpy as np
from scipy.stats import fisher_exact
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import parametre_liste as PL

KOK = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SONUC = os.path.join(KOK, "kayitlar", "slam", "sonuc")


def wilson(k, n, z=1.96):
    if n == 0:
        return (np.nan, np.nan)
    p = k / n
    d = 1 + z * z / n
    m = (p + z * z / (2 * n)) / d
    h = z * np.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / d
    return (max(0, m - h), min(1, m + h))


def bozuk_bant(r):
    """Eski ölçüt: bant ölçekli yol/uzaklık (bant ölçeği başlatma 3,4 sn'den geç olursa yanlı)."""
    return bool(r.get("patladi")) or (r.get("yol_orani") is not None and r["yol_orani"] < 0.5)


def bozuk(r):
    """ANA ÖLÇÜT: bant mesafesinden bağımsız ölçek tutarlılığı (PO.tutarlilik): 0-88 sn'de son başlatmadan sonraki ilk gidiş / tur oranı 0,75-1,33 dışında ya da hesaplanamadı."""
    c = r.get("_olcut2") or {}
    t = c.get("tut")
    return t is None or not (0.75 <= t <= 1.33)


def yukle():
    d = {}
    for s in open(os.path.join(SONUC, "parametre_sonuclar_v2.jsonl"), encoding="utf-8"):
        o = json.loads(s)
        if "hata" in o:
            continue
        p = os.path.join(KOK, "kayitlar", "slam", "parametre", "kosu_v2", "%s__%d" % (o["varyant"], o["tekrar"]), "olcut2.json")
        if not os.path.exists(p):
            import parametre_olcut as PO
            try:
                json.dump(PO.tutarlilik(os.path.dirname(p)), open(p, "w"))
            except Exception:
                pass
        if os.path.exists(p):
            o["_olcut2"] = json.load(open(p))
        d[(o["varyant"], o["tekrar"])] = o
    S = {}
    for (v, t), o in d.items():
        S.setdefault(v, []).append(o)
    return S


KOL = [("sifirlama_85", ".0f"), ("baslatma_sn", ".1f"), ("eslesen_medyan", ".0f"), ("kf_85", ".0f"), ("nokta_85", ".0f"), ("nokta_1_5m_orani", ".2f"), ("nokta_zemin_orani", ".2f"),
       ("yol_toplam_m", ".1f"), ("son_baslangic_m", ".2f"), ("yon_kapanis_derece", ".1f"), ("donus_yaricap_m_ort", ".3f"), ("takip_ms_medyan", ".0f")]


def tablo():
    S = yukle()
    # taban ile harita_residual_0.4 AYNI ayardır (taban yaml'ında residual_deg_thr zaten 0,4): havuzlanır, gürültü tabanı bu
    taban = S.get("taban", []) + S.get("harita_residual_0.4", [])
    S["TABAN_HAVUZU (taban + harita_residual_0.4, ayni ayar)"] = taban
    tb = sum(bozuk(r) for r in taban)
    out, L = {}, []
    L.append("| varyant | n | bozuk | bozuk oranı (%95 Wilson) | bant ölçütüyle bozuk | p (Fisher, taban havuzuna karşı) | " + " | ".join(k for k, _ in KOL) + " |")
    L.append("|" + "---|" * (6 + len(KOL)))
    for ad in ["TABAN_HAVUZU (taban + harita_residual_0.4, ayni ayar)"] + list(PL.V):
        R = S.get(ad)
        if not R:
            continue
        n, k = len(R), sum(bozuk(r) for r in R)
        lo, hi = wilson(k, n)
        p = fisher_exact([[k, n - k], [tb, len(taban) - tb]])[1] if taban and not ad.startswith("TABAN_HAVUZU") and ad not in ("taban", "harita_residual_0.4") else float("nan")
        for r in R:
            r["sifirlama_85"] = (r.get("_olcut2") or {}).get("sifirlama")
        iyi = [r for r in R if not bozuk(r)]
        sat = [ad, str(n), "%d" % k, "%.2f (%.2f-%.2f)" % (k / n, lo, hi), "%d" % sum(bozuk_bant(r) for r in R), "%.3f" % p if p == p else "-"]
        for kk, f in KOL:
            v = np.array([r[kk] for r in iyi if r.get(kk) is not None], float)
            sat.append("%s (%s..%s)" % (format(np.median(v), f), format(v.min(), f), format(v.max(), f)) if len(v) else "-")
        L.append("| " + " | ".join(sat) + " |")
        out[ad] = dict(n=n, bozuk=k, p=None if p != p else p, wilson=[lo, hi])
    yol = os.path.join(SONUC, "parametre_ozet.md")
    open(yol, "w", encoding="utf-8").write("Hücre = bozuk olmayan koşularda ortanca (min..maks). takip_ms_medyan makine yükünün göstergesidir (11 ms boş makine, 60+ ms yük altında).\n\n" + "\n".join(L) + "\n")
    json.dump(out, open(os.path.join(SONUC, "parametre_ozet.json"), "w"), indent=1)
    print("\n".join(L))


if __name__ == "__main__":
    tablo()
