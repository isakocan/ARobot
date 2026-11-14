"""Ek deneyler: (1) tek süreçte kesin süre ölçümü, (2) anahtar kare yoğunluğu, (3) loş ışık için kontrast normalleştirmeli (CLAHE) özellik.
Kullanım: python yeniden_bul_ek_deney.py sure|yogunluk|isik     Çıktı: kayitlar/sonuclar/yeniden_bul/ek_<ad>.json + .md"""
import json
import os
import pickle
import sys
import time

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import konum_ortak as ko  # noqa: E402
import yeniden_bul_ortak as yo  # noqa: E402
import yeniden_bul_cekirdek as yc  # noqa: E402
import yeniden_bul_olc as ol  # noqa: E402
from kayit import Kayit  # noqa: E402


def sure():
    H = yc.hafiza_kur()
    ad = "salon_turu"
    kay = Kayit(yo.KAYITLAR_TUM[ad]["klasor"])
    tar = yo.tarama_oku(ad)
    dep = yo.OzDeposu(ad)
    sec = list(range(0, len(tar["kare"]), 4))[:30]
    r_oku, r_oz, r_son, r_son_g = [], [], [], []
    y = yo.pusula_yontemi()
    for j in sec:
        k = int(tar["kare"][j])
        a = time.perf_counter(); im = kay.resim(k); b = time.perf_counter()
        yo.oz_cikar(im); c = time.perf_counter()
        r_oku.append((b - a) * 1000); r_oz.append((c - b) * 1000)
        oz = dep.al(k)
        ts = (tar["mesafe"][j], tar["guven"][j], tar["menzil"])
        a = time.perf_counter(); r = yc.sorgula(H, oz, ts, genel=False); r_son.append((time.perf_counter() - a) * 1000)
        r_son_g.append(r["ms"])
    # yalnız görüntü (tarama yok)
    r_t = []
    for j in sec[:15]:
        k = int(tar["kare"][j])
        a = time.perf_counter(); yc.sorgula(H, dep.al(k), None, genel=False); r_t.append((time.perf_counter() - a) * 1000)
    r_g = []
    for j in sec[:6]:
        k = int(tar["kare"][j])
        a = time.perf_counter(); yc.sorgula(H, dep.al(k), (tar["mesafe"][j], tar["guven"][j], tar["menzil"]), genel=True); r_g.append((time.perf_counter() - a) * 1000)
    o = dict(jpeg_oku_ms=float(np.median(r_oku)), balikgozu_esdikdortgen_ve_orb_ms=float(np.median(r_oz)),
             sorgu_tarama_ile_genelsiz_ms=float(np.median(r_son)), sorgu_yalniz_goruntu_ms=float(np.median(r_t)), sorgu_genel_dahil_ms=float(np.median(r_g)),
             parca_ms={k: float(np.median([p[k] for p in r_son_g])) for k in r_son_g[0]}, anahtar_kare=len(H.kf_R), n=len(sec),
             cpu_cekirdek=os.cpu_count())
    json.dump(o, open(os.path.join(yo.CIKTI, "ek_sure.json"), "w"), indent=1)
    print(json.dumps(o, indent=1))


# ------------------------------------------------------------------ yoğunluk
def _yog_is(args):
    import cv2 as _c
    _c.setNumThreads(1)
    aralik, yar, ad, js = args
    H = yc.hafiza_kur(aralik, yar)
    dep = yo.OzDeposu(ad)
    tar = yo.tarama_oku(ad)
    g = yo.gercek_oku(ad)
    gs = {int(k): i for i, k in enumerate(g["kare"])}
    out = []
    for j in js:
        k = int(tar["kare"][j])
        i = gs[k]
        haric = (np.abs(H.kf_t - g["t_ms"][i]) <= 3000.0) if ad == yo.HARITA_KAYDI else None
        r = yc.sorgula(H, dep.al(k), (tar["mesafe"][j], tar["guven"][j], tar["menzil"]), haric=haric, genel=False)
        r["kare"] = k
        r["gercek"] = (float(g["x"][i]), float(g["y"][i]), float(g["yaw"][i]))
        d_kf = np.hypot(H.kf_poz[:, 0] - g["x"][i], H.kf_poz[:, 1] - g["y"][i])
        uygun = ~haric if haric is not None else np.ones(len(d_kf), bool)
        out.append((ad, r, float(d_kf[uygun].min())))
    return out


def yogunluk():
    from concurrent.futures import ProcessPoolExecutor
    ayar = {"seyrek (0,60 m aralık)": (0.60, 0.40), "varsayılan (0,30 m)": (0.30, 0.20), "sık (0,15 m)": (0.15, 0.10)}
    for a in ayar.values():
        yc.hafiza_kur(*a)
    adlar = ["ev_turu", "salon_turu", "duz_git", "sinama_ters_tur", "sinama_hizli", "sinama_yeni_rota"]
    isler_q = []
    for ad in adlar:
        n = len(yo.tarama_oku(ad)["kare"])
        js = list(range(0, n, 3))
        for b in range(0, len(js), 20):
            isler_q.append((ad, js[b:b + 20]))
    sat = []
    sonuc = {}
    e = yc.ESIK
    for isim, (aralik, yar) in ayar.items():
        H = yc.hafiza_kur(aralik, yar)
        with ProcessPoolExecutor(4) as ex:
            res = [x for r in ex.map(_yog_is, [(aralik, yar, ad, js) for ad, js in isler_q]) for x in r]
        n = tp = yan = 0
        for ad, r, dk in res:
            # sınıf 0 yaklaşımı: haritalı alan maskesi bu hafızadan değil varsayılandan; burada tüm sorgular (alan dışı hariç değil) -> yalnız d_kf <= 2 m
            if dk > 2.0:
                continue
            n += 1
            t, p, gv = yc.karar(r, e)
            if t:
                ok = np.hypot(p[0] - r["gercek"][0], p[1] - r["gercek"][1]) <= 0.5 and abs(ko.sar(p[2] - r["gercek"][2])) <= 15
                tp += int(ok)
                yan += int(not ok)
        b = H.bayt()
        sat.append([isim, b["anahtar_kare"], "%.2f MB" % (b["kf_bayt"] / 1e6), n, "%.0f%%" % (100 * tp / n), "%.1f%%" % (100 * yan / n)])
        sonuc[isim] = dict(anahtar_kare=b["anahtar_kare"], bayt=b["kf_bayt"], sorgu=n, basari=tp, yanlis=yan)
    from yeniden_bul_degerlendir import md_tablo
    md = "Anahtar kare yoğunluğu (sorgular: 6 normal ışıklı kayıttan her 3. tarama karesi, haritalı alandaki ve en yakın anahtar kare <= 2 m olanlar; ev_turu ±3 sn dışlı):\n\n"
    md += md_tablo(["anahtar kare kuralı", "anahtar kare", "özellik boyutu", "sorgu", "başarı (0,5 m, 15°)", "yanlış-kesin"], sat)
    open(os.path.join(yo.CIKTI, "ek_yogunluk.md"), "w", encoding="utf-8").write(md + "\n")
    json.dump(sonuc, open(os.path.join(yo.CIKTI, "ek_yogunluk.json"), "w"), indent=1)
    print(md)


# ------------------------------------------------------------------ ışık
def oz_cikar_clahe(kare_bgr, klip=3.0):
    y = yo.pusula_yontemi()
    gri, gec = y.don(kare_bgr)
    g8 = np.clip(gri, 0, 255).astype(np.uint8)
    cl = cv2.createCLAHE(clipLimit=klip, tileGridSize=(8, 4))
    g8 = cl.apply(g8)
    r, d = y._cikar(g8.astype(np.float32), gec)
    return r.astype(np.float32), d


def isik():
    """Normal ışıkta kurulmuş hafıza; sorgu: los / far / yeni_rota (referans) için (a) olduğu gibi (b) CLAHE'li sorgu özellikleri (anahtar kareler normal)
    (c) CLAHE'li anahtar kareler + CLAHE'li sorgu. Yalnız haritalı alan sınıf 0 ve anahtar kare <= 1,5 m olan sorgular."""
    import yeniden_bul_tablo as yt
    H = yc.hafiza_kur()
    # CLAHE'li hafıza
    Hc = yc.hafiza_kur()
    import copy
    Hc = copy.copy(H)
    kay = Kayit(yo.KAYITLAR_TUM[yo.HARITA_KAYDI]["klasor"])
    Hc.kf_R, Hc.kf_D = [], []
    for k in H.kf_kare:
        r, d = oz_cikar_clahe(kay.resim(int(k)))
        Hc.kf_R.append(r)
        Hc.kf_D.append(d)
    sat = []
    sonuc = {}
    T = {s["kare"]: s for s in []}
    for ad in ["sinama_yeni_rota", "sinama_los", "sinama_far", "sinama_duz_zemin"]:
        kr = Kayit(yo.KAYITLAR_TUM[ad]["klasor"])
        tar = yo.tarama_oku(ad)
        g = yo.gercek_oku(ad)
        gs = {int(k): i for i, k in enumerate(g["kare"])}
        dep = yo.OzDeposu(ad)
        sonuclar = {"normal": [], "clahe_sorgu": [], "clahe_hepsi": []}
        for j in range(0, len(tar["kare"])):
            k = int(tar["kare"][j])
            i = gs[k]
            t = (float(g["x"][i]), float(g["y"][i]), float(g["yaw"][i]))
            dk = float(np.hypot(H.kf_poz[:, 0] - t[0], H.kf_poz[:, 1] - t[1]).min())
            if dk > 1.5:
                continue
            ts = (tar["mesafe"][j], tar["guven"][j], tar["menzil"])
            zc = oz_cikar_clahe(kr.resim(k))
            for ad2, HH, oz in (("normal", H, dep.al(k)), ("clahe_sorgu", H, zc), ("clahe_hepsi", Hc, zc)):
                r = yc.sorgula(HH, oz, ts, genel=False)
                tn, p, gv = yc.karar(r)
                ok = tn and np.hypot(p[0] - t[0], p[1] - t[1]) <= 0.5 and abs(ko.sar(p[2] - t[2])) <= 15
                sonuclar[ad2].append((tn, ok))
        n = len(sonuclar["normal"])
        sat.append([ad, n] + ["%d (%.0f%%) / yanlış %d" % (sum(1 for x in sonuclar[a] if x[1]), 100 * sum(1 for x in sonuclar[a] if x[1]) / max(n, 1),
                                                           sum(1 for x in sonuclar[a] if x[0] and not x[1])) for a in ("normal", "clahe_sorgu", "clahe_hepsi")])
        sonuc[ad] = dict(n=n, **{a: dict(basari=sum(1 for x in sonuclar[a] if x[1]), yanlis=sum(1 for x in sonuclar[a] if x[0] and not x[1])) for a in sonuclar})
    from yeniden_bul_degerlendir import md_tablo
    md = "Loş ışık için kontrast normalleştirme (CLAHE, 8x4 karo, klip 3): sorgular = en yakın anahtar kare <= 1,5 m olan tüm kareler.\n\n"
    md += md_tablo(["kayıt", "sorgu", "olduğu gibi (başarı)", "yalnız sorgu CLAHE", "anahtar kareler ve sorgu CLAHE"], sat)
    open(os.path.join(yo.CIKTI, "ek_isik.md"), "w", encoding="utf-8").write(md + "\n")
    json.dump(sonuc, open(os.path.join(yo.CIKTI, "ek_isik.json"), "w"), indent=1)
    print(md)


if __name__ == "__main__":
    {"sure": sure, "yogunluk": yogunluk, "isik": isik}[sys.argv[1]]()
