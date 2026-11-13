"""Ölçüt: tahmin.csv ile gercek_konum.csv karşılaştırır (yaw ve konum hatası), özet + sonuc.json + grafik üretir.

Hizalama: yöntem kendi başlangıcını bilemez; tahminin İLK GEÇERLİ karesi gerçek başlangıç konumuna oturtulur:
  yaw'a sabit fark eklenir (dyaw = gercek0 - tahmin0), konum tahmin0 etrafında dyaw kadar saat yönünde döndürülüp
  gercek0'a kaydırılır. Yalnızca yaw tahmin eden yöntemlerde (x,y = NaN) konum ölçütleri hesaplanmaz.
Açılar: derece, yön kuzeyden saat yönünde; fark her zaman (-180, 180] aralığına sarılır (0/360 sorunu yok).

Kullanım:  python tools/konum_deney/olc.py <kayit_klasoru> <tahmin.csv> [cikti_klasoru]"""
import csv
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))


def sar(a):
    """Açı farkını (-180, 180] aralığına sarar."""
    a = (np.asarray(a, float) + 180.0) % 360.0 - 180.0
    return np.where(a == -180.0, 180.0, a)


def tahmin_oku(yol):
    with open(yol, newline="", encoding="utf-8") as f:
        satirlar = list(csv.DictReader(f))

    def sayi(r, k):
        v = r.get(k, "")
        try:
            return float(v) if v not in ("", None) else np.nan
        except ValueError:
            return np.nan
    return {"kare": np.array([int(float(r["kare"])) for r in satirlar]),
            "t_ms": np.array([int(float(r["t_ms"])) for r in satirlar]),
            "x_m": np.array([sayi(r, "x_m") for r in satirlar]),
            "y_m": np.array([sayi(r, "y_m") for r in satirlar]),
            "yon_derece": np.array([sayi(r, "yon_derece") for r in satirlar])}


def hizala(tahmin, gercek):
    """Tahmin ve gerçeği kare numarasına göre eşler, ilk geçerli kareyi gerçek başlangıca oturtur.
    Dönüş: dict (kare, t_ms, gx, gy, gyon, tx, ty, tyon) hizalanmış dizilerle."""
    ortak, ti, gi = np.intersect1d(tahmin["kare"], gercek["kare"], return_indices=True)
    g = {k: gercek[k][gi] for k in ("t_ms", "x_m", "y_m", "yon_derece")}
    t = {k: tahmin[k][ti] for k in ("x_m", "y_m", "yon_derece")}
    tyon = t["yon_derece"].copy()
    tx, ty = t["x_m"].copy(), t["y_m"].copy()
    yon_var = np.isfinite(tyon)
    konum_var = np.isfinite(tx) & np.isfinite(ty)
    d = 0.0
    if yon_var.any():
        i0 = int(np.argmax(yon_var))
        d = float(sar(g["yon_derece"][i0] - tyon[i0]))
        tyon = (tyon + d) % 360.0
    if konum_var.any():
        j0 = int(np.argmax(konum_var))
        c, s = np.cos(np.radians(d)), np.sin(np.radians(d))
        dx, dy = tx - tx[j0], ty - ty[j0]
        tx = g["x_m"][j0] + dx * c + dy * s
        ty = g["y_m"][j0] - dx * s + dy * c
    return {"kare": ortak, "t_ms": g["t_ms"], "gx": g["x_m"], "gy": g["y_m"], "gyon": g["yon_derece"],
            "tx": tx, "ty": ty, "tyon": tyon, "baslangic_yaw_farki": d}


def olc_hesapla(h):
    sonuc = {"kare_sayisi": int(len(h["kare"]))}
    # --- yaw
    gecerli = np.isfinite(h["tyon"])
    if gecerli.sum() >= 2:
        e = sar(h["tyon"] - h["gyon"])
        ev = e[gecerli]
        sonuc["yaw_ort_mutlak_derece"] = float(np.mean(np.abs(ev)))
        sonuc["yaw_maks_derece"] = float(np.max(np.abs(ev)))
        son = np.where(gecerli)[0][-1]
        sonuc["yaw_son_derece"] = float(e[son])
        gy = np.asarray(h["gyon"], float)
        toplam_don = float(np.sum(np.abs(sar(np.diff(gy)))))
        sonuc["gercek_toplam_donus_derece"] = toplam_don
        if toplam_don >= 30.0:
            # sürüklenme: hata serisinin açılmış (unwrap) toplam değişimi, 360 derece gerçek dönüş başına
            eu = np.degrees(np.unwrap(np.radians(ev)))
            sonuc["yaw_surukleme_derece_360_basina"] = float(abs(eu[-1] - eu[0]) / (toplam_don / 360.0))
        else:
            sonuc["yaw_surukleme_derece_360_basina"] = None
        sonuc["_yaw_hata"] = e
    # --- konum
    kv = np.isfinite(h["tx"]) & np.isfinite(h["ty"])
    if kv.sum() >= 2:
        hx, hy = h["tx"] - h["gx"], h["ty"] - h["gy"]
        hata = np.hypot(hx, hy)
        hv = hata[kv]
        mesafe = float(np.sum(np.hypot(np.diff(h["gx"]), np.diff(h["gy"]))))
        son = np.where(kv)[0][-1]
        sonuc["konum_rmse_m"] = float(np.sqrt(np.mean(hv ** 2)))
        sonuc["konum_maks_m"] = float(np.max(hv))
        sonuc["konum_son_m"] = float(hata[son])
        sonuc["gidilen_mesafe_m"] = mesafe
        sonuc["konum_son_yuzde_mesafe"] = float(100.0 * hata[son] / mesafe) if mesafe > 0.05 else None
        sonuc["konum_rmse_yuzde_mesafe"] = float(100.0 * sonuc["konum_rmse_m"] / mesafe) if mesafe > 0.05 else None
        sonuc["_konum_hata"] = hata
    return sonuc


def ozet_metni(s, ad="", zaman=None):
    satir = ["Ölçüt özeti" + (" (%s)" % ad if ad else "") + ": %d kare" % s["kare_sayisi"]]
    if "yaw_ort_mutlak_derece" in s:
        satir.append("Yaw: ort |hata| %.2f°, en çok %.2f°, son %+.2f°" % (s["yaw_ort_mutlak_derece"], s["yaw_maks_derece"],
                                                                         s["yaw_son_derece"]))
        d = s["yaw_surukleme_derece_360_basina"]
        satir.append("Yaw sürüklenmesi: " + ("%.2f° / 360° gerçek dönüş (toplam dönüş %.0f°)" % (d, s["gercek_toplam_donus_derece"])
                                              if d is not None else "hesaplanmadı (toplam dönüş 30°'den az)"))
    else:
        satir.append("Yaw: yöntem yaw vermedi")
    if "konum_rmse_m" in s:
        y1 = s["konum_son_yuzde_mesafe"]
        y2 = s["konum_rmse_yuzde_mesafe"]
        satir.append("Konum: RMSE %.3f m, en çok %.3f m, son (uç nokta) %.3f m" % (s["konum_rmse_m"], s["konum_maks_m"], s["konum_son_m"]))
        satir.append("Gidilen yol %.2f m; uç nokta hatası %s, RMSE %s" % (
            s["gidilen_mesafe_m"], "%%%.1f" % y1 if y1 is not None else "—", "%%%.1f" % y2 if y2 is not None else "—"))
    else:
        satir.append("Konum: yöntem konum vermedi")
    if zaman:
        satir.append("Süre: kare başına ort %.2f ms, p95 %.2f ms" % (zaman["ort_ms"], zaman["p95_ms"]))
    return "\n".join(satir)


def grafik_ciz(h, s, harita, yol, ad=""):
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        return _grafik_cv2(h, s, yol)
    fig, ax = plt.subplots(1, 2, figsize=(13, 5.5), gridspec_kw={"width_ratios": [1.15, 1]})
    a = ax[0]
    if harita is not None:
        xl, xr, yb, yt = harita.kapsam()
        a.imshow(harita.resim, cmap="gray", extent=(xl, xr, yb, yt), vmin=0, vmax=255, alpha=0.6, origin="upper")
        a.set_xlim(xl, xr)
        a.set_ylim(yb, yt)
    a.plot(h["gx"], h["gy"], "-", color="#1b7f3b", lw=2, label="gerçek")
    kv = np.isfinite(h["tx"]) & np.isfinite(h["ty"])
    if kv.any():
        a.plot(h["tx"][kv], h["ty"][kv], "-", color="#d1342f", lw=1.6, label="tahmin")
    a.plot(h["gx"][0], h["gy"][0], "o", color="#1b7f3b")
    a.set_aspect("equal")
    a.set_xlabel("x (m, doğu)")
    a.set_ylabel("y (m, kuzey)")
    a.set_title("Yol" + (" - " + ad if ad else ""))
    a.legend(loc="best")
    a.grid(alpha=0.3)
    b = ax[1]
    t = (h["t_ms"] - h["t_ms"][0]) / 1000.0
    if "_yaw_hata" in s:
        b.plot(t, s["_yaw_hata"], color="#d1342f", label="yaw hatası (°)")
        b.yaxis.label.set_color("#d1342f")
    if "_konum_hata" in s:
        b2 = b.twinx()
        b2.plot(t, s["_konum_hata"], color="#1f5fa8", label="konum hatası (m)")
        b2.set_ylabel("konum hatası (m)", color="#1f5fa8")
    b.set_xlabel("zaman (s)")
    b.set_ylabel("yaw hatası (derece)", color="#d1342f")
    b.set_title("Hata - zaman")
    b.grid(alpha=0.3)
    fig.tight_layout()
    fig.savefig(yol, dpi=110)
    plt.close(fig)


def _grafik_cv2(h, s, yol):
    import cv2
    tuval = np.full((520, 1000, 3), 255, np.uint8)
    xs = np.concatenate([h["gx"], h["tx"][np.isfinite(h["tx"])]])
    ys = np.concatenate([h["gy"], h["ty"][np.isfinite(h["ty"])]])
    x0, x1, y0, y1 = xs.min() - 0.2, xs.max() + 0.2, ys.min() - 0.2, ys.max() + 0.2
    olcek = min(460 / (x1 - x0), 460 / (y1 - y0))

    def p(x, y):
        return int(20 + (x - x0) * olcek), int(500 - (y - y0) * olcek)
    for i in range(len(h["gx"]) - 1):
        cv2.line(tuval, p(h["gx"][i], h["gy"][i]), p(h["gx"][i + 1], h["gy"][i + 1]), (59, 127, 27), 2)
    for i in range(len(h["tx"]) - 1):
        if np.isfinite(h["tx"][i]) and np.isfinite(h["tx"][i + 1]):
            cv2.line(tuval, p(h["tx"][i], h["ty"][i]), p(h["tx"][i + 1], h["ty"][i + 1]), (47, 52, 209), 2)
    if "_yaw_hata" in s:
        e = np.nan_to_num(s["_yaw_hata"])
        m = max(5.0, np.abs(e).max())
        for i in range(len(e) - 1):
            a = (520 + int(i / len(e) * 450), 260 - int(e[i] / m * 230))
            b = (520 + int((i + 1) / len(e) * 450), 260 - int(e[i + 1] / m * 230))
            cv2.line(tuval, a, b, (47, 52, 209), 2)
    cv2.imwrite(yol, tuval)


def degerlendir(kayit, tahmin_csv, cikti=None, zaman=None, ad=""):
    """kayit: Kayit nesnesi. Dönüş: sonuç sözlüğü (özet metni 'ozet' anahtarında)."""
    gercek = kayit.gercek_konum()
    if gercek is None:
        raise SystemExit("gercek_konum.csv yok: ölçüt hesaplanamaz")
    h = hizala(tahmin_oku(tahmin_csv), gercek)
    s = olc_hesapla(h)
    metin = ozet_metni(s, ad, zaman)
    ozel = {k: v for k, v in s.items() if k.startswith("_")}
    kayit_json = {k: v for k, v in s.items() if not k.startswith("_")}
    kayit_json["baslangic_yaw_hizalama_derece"] = h["baslangic_yaw_farki"]
    if zaman:
        kayit_json["sure"] = zaman
    kayit_json["ozet"] = metin
    if cikti:
        os.makedirs(cikti, exist_ok=True)
        with open(os.path.join(cikti, "sonuc.json"), "w", encoding="utf-8") as f:
            json.dump(kayit_json, f, indent=1, ensure_ascii=False)
        grafik_ciz(h, ozel, kayit.harita(), os.path.join(cikti, "grafik.png"), ad)
    kayit_json["_hizali"] = h
    return kayit_json


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    from kayit import Kayit
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    r = degerlendir(Kayit(sys.argv[1]), sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else None)
    print(r["ozet"])
