"""Ham sorgu sonuçlarından (sorgu_<ad>.pkl) düz tablo kurar: gerçek poz, haritalı alan sınıfı, anahtar kareye uzaklık, her konum
varyantının hatası, karar özellikleri. Tablo = list[dict]."""
import os
import pickle
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import yeniden_bul_ortak as yo  # noqa: E402
import yeniden_bul_cekirdek as yc  # noqa: E402

KONUM_VARYANTLARI = ["kf", "kume", "tri", "tarama", "son"]


def hata(p, t):
    return float(np.hypot(p[0] - t[0], p[1] - t[1])), float(abs(yo.aci_fark(p[2], t[2])))


def ozellikler(r):
    """Karar için düz özellikler (yoksa 0 / nan)."""
    f = dict(n_uyumlu=r.get("n_uyumlu", 0), toplam_icerik=r.get("toplam_icerik", 0), en_icerik=r["en"]["icerik"] if "en" in r else 0,
             cizgi=r.get("cizgi_tri", 0.0), tri_skor=r.get("tri_skor", np.nan), sayi_max=r["sayi_max"],
             n_dogrulu=r.get("n_dogrulu", 0), genel_skor=r["genel"][0][1] if r.get("genel") else np.nan,
             genel_ayirt=r.get("genel_ayirt", np.nan), n_nokta=r["n_nokta"])
    return f


def tablo_kur(hafiza=None):
    H = hafiza or yc.hafiza_kur()
    iz = H.iz
    sat = []
    for ad in yo.KAYITLAR_TUM:
        yol = os.path.join(yo.ONBELLEK, "sorgu_%s.pkl" % ad)
        if not os.path.exists(yol):
            continue
        with open(yol, "rb") as f:
            rs = pickle.load(f)
        yol_g = os.path.join(yo.ONBELLEK, "sorgu_g_%s.pkl" % ad)          # G (genel tarama araması) ilk koşudan; konum çekirdeğinden bağımsız
        if os.path.exists(yol_g):
            with open(yol_g, "rb") as f:
                gs = {r["kare"]: r for r in pickle.load(f)}
            for r in rs:
                g = gs.get(r["kare"])
                if g is not None and "genel" in g:
                    r["genel"] = g["genel"]
                    r["genel_ayirt"] = g.get("genel_ayirt", np.nan)
                    r["ms"]["genel"] = g["ms"]["genel"]
        gx = np.array([r["gercek"][0] for r in rs])
        gy = np.array([r["gercek"][1] for r in rs])
        sinif, d_alan = yo.alan_sinifi(iz, H.alan, gx, gy)
        for i, r in enumerate(rs):
            t = r["gercek"]
            d_kf = np.hypot(H.kf_poz[:, 0] - t[0], H.kf_poz[:, 1] - t[1])
            if ad == yo.HARITA_KAYDI:
                uygun = np.abs(H.kf_t - r["t_ms"]) > 3000.0
            else:
                uygun = np.ones(len(d_kf), bool)
            satir = dict(kayit=ad, kare=r["kare"], t_ms=r["t_ms"], gx=t[0], gy=t[1], gyaw=t[2], sinif=int(sinif[i]),
                         d_alan=float(d_alan[i]), d_kf=float(d_kf[uygun].min()), d_kf_tum=float(d_kf.min()), ms=r["toplam_ms"],
                         ms_parca=r["ms"])
            satir.update(ozellikler(r))
            for v in KONUM_VARYANTLARI:
                p = r.get({"kf": "en", "kume": "kume_poz", "tri": "tri", "tarama": "son_tarama", "son": "son"}[v])
                if v == "kf" and p is not None:
                    p = p["poz"]
                if p is None:
                    satir["e_" + v] = np.nan
                    satir["y_" + v] = np.nan
                    satir["p_" + v] = None
                else:
                    e, y = hata(p, t)
                    satir["e_" + v] = e
                    satir["y_" + v] = y
                    satir["p_" + v] = tuple(float(q) for q in p)
            g = r.get("genel")
            if g:
                e, y = hata(g[0][0], t)
                satir["e_genel"], satir["y_genel"], satir["p_genel"] = e, y, tuple(g[0][0])
            else:
                satir["e_genel"] = satir["y_genel"] = np.nan
                satir["p_genel"] = None
            satir["ham"] = r
            sat.append(satir)
    return sat
