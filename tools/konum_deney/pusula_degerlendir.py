"""Görsel pusula değerlendirmesi: {M1 profil, M2 nokta} x {sim, theta} x 9 kayıt x {komutlu, komutsuz}.

Çıktı (varsayılan kayitlar/sonuclar/pusula/):
  tablo.csv, tablo.md       her kosu için: son hata, en çok |hata|, ort |hata|, 360 derece başına sürüklenme,
                            ilk tam tur sonundaki hata, anahtar kare değişimi, ms/kare (dönüşüm ayrı, kestirim ayrı, p95)
  grafik/<girdi>_<kayit>_<yontem>.png    yaw hatası - zaman (anahtar kare değişimleri ve yedek kareler işaretli)
  tahmin/<yontem>/<girdi>_<kayit>.csv    kare,t_ms,yon,kalite,durum
Kullanım:
  python tools/konum_deney/pusula_degerlendir.py                      # ana tablo
  python tools/konum_deney/pusula_degerlendir.py --tarama bant        # duyarlılık taraması (bant | genislik | mesafe | tumu)
  python tools/konum_deney/pusula_degerlendir.py --kayit yerinde_donus_salon --girdi sim --yontem M1,M2
Süre: dönüşüm = Theta çift balık gözü -> eşdikdörtgen (sim'de yeniden boyutlandırma); kestirim = geri kalan (özellik
çıkarma + eşleştirme + çözüm). Her işçi tek iş parçacığıyla (cv2.setNumThreads(1)) çalışır; JPEG çözme süreye dahil değil."""
import argparse
import csv
import importlib
import json
import os
import sys
import time

import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
DEPO = os.path.abspath(os.path.join(KLASOR, "..", ".."))
KAYITLAR = ("yerinde_donus_salon", "yerinde_donus_koridor", "yerinde_donus_yatak_odasi", "yerinde_donus_banyo",
            "yerinde_donus_mutfak", "yerinde_donus_calisma_odasi", "duz_git", "salon_turu", "ev_turu")


# ------------------------------------------------------------------ varyantlar
def ana_varyantlar():
    """M1 profil; M2 = nokta + yalnız-yaw RANSAC; M2_uc = nokta + 3 serbestlik (Kabsch); M2D = nokta + düzlemsel hareket modeli."""
    return {
        "M1": ("yontem_pusula_profil", {}),
        "M1_komutsuz": ("yontem_pusula_profil", {"komut_oncel": False}),
        "M2": ("yontem_pusula_nokta", {"kip": "yaw"}),
        "M2_komutsuz": ("yontem_pusula_nokta", {"kip": "yaw", "komut_oncel": False}),
        "M2_uc": ("yontem_pusula_nokta", {"kip": "uc"}),
        "M2D": ("yontem_pusula_nokta", {"kip": "duzlem"}),
        "M2D_komutsuz": ("yontem_pusula_nokta", {"kip": "duzlem", "komut_oncel": False}),
    }


BANTLAR = {"alt_-30_-5": (-30, -5), "ufuk_-5_15": (-5, 15), "orta_15_35": (15, 35), "yuksek_35_55": (35, 55),
           "tepe_55_80": (55, 80), "ust_5_65": (5, 65), "tum_-30_80": (-30, 80), "ust_20_65": (20, 65)}


def tarama_varyantlari(tur):
    v = {}
    if tur in ("bant", "tumu"):
        for ad, b in BANTLAR.items():
            v["M1|bant=" + ad] = ("yontem_pusula_profil", {"bant": b})
            v["M2D|bant=" + ad] = ("yontem_pusula_nokta", {"bant": b})
    if tur in ("genislik", "tumu"):
        for g in (128, 256, 512, 1024):
            v["M1|G=%d" % g] = ("yontem_pusula_profil", {"genislik": g})
        for g in (512, 768, 1024, 1536):
            v["M2D|G=%d" % g] = ("yontem_pusula_nokta", {"genislik": g})
    if tur in ("mesafe", "tumu"):
        for d in (0.15, 0.3, 0.6, 1.2, 1e9):
            v["M1|d=%g" % d] = ("yontem_pusula_profil", {"anahtar_mesafe_m": d})
            v["M2D|d=%g" % d] = ("yontem_pusula_nokta", {"anahtar_mesafe_m": d})
    return v


# ------------------------------------------------------------------ çalışan (işçi)
def _isci_baslat():
    import cv2
    cv2.setNumThreads(1)
    for k in ("OMP_NUM_THREADS", "MKL_NUM_THREADS", "OPENBLAS_NUM_THREADS"):
        os.environ[k] = "1"


def _ilk_tam_tur_hatasi(e, gyon):
    """İlk tam tur (gerçek toplam |dönüş| 360 derece olduğu kare) sonundaki yaw hatası; yoksa None."""
    from olc import sar
    ac = np.r_[0.0, np.cumsum(np.abs(sar(np.diff(gyon))))]
    i = np.where(ac >= 360.0)[0]
    return float(e[i[0]]) if len(i) else None


def kos(arg):
    """Bir kaydı (girdi türü + ad) tüm varyantlarla kosar. arg: (tur, ad, varyantlar, max_kare)."""
    tur, ad, varyantlar, max_kare = arg
    _isci_baslat()
    import olc
    from kamera import Kamera
    from kayit import Kayit
    kay = Kayit(os.path.join(DEPO, "kayitlar", tur, ad))
    kam = Kamera.bilgiden(kay.bilgi)
    bilgi = dict(kay.bilgi)
    bilgi["_kayit_klasoru"] = kay.klasor
    nesneler = {}
    for vad, (modul, kw) in varyantlar.items():
        nesneler[vad] = importlib.import_module(modul).Yontem(bilgi, kam, **kw)
    kayit = {v: {"yon": [], "kalite": [], "durum": [], "ms_don": [], "ms_kes": [], "degisim": []} for v in nesneler}
    kn = kay.kare_numaralari
    n = len(kay) if max_kare is None else min(max_kare, len(kay))
    for i in range(n):
        t = int(kay.t_ms[i])
        im = kay.resim(i)
        komut = kay.komut(t)
        for v, y in nesneler.items():
            r = y.guncelle(im, t, komut)
            d = kayit[v]
            d["yon"].append(r[2])
            d["kalite"].append(y.kalite)
            d["durum"].append(y.durum)
            d["ms_don"].append(y.son_donusum_ms)
            d["ms_kes"].append(y.son_kestirim_ms)
            d["degisim"].append(y.anahtar_degisim)
    gercek = kay.gercek_konum()
    cikti = []
    for v, d in kayit.items():
        tah = {"kare": kn[:n], "t_ms": kay.t_ms[:n], "x_m": np.full(n, np.nan), "y_m": np.full(n, np.nan),
               "yon_derece": np.array(d["yon"], float)}
        h = olc.hizala(tah, gercek)
        s = olc.olc_hesapla(h)
        e = s["_yaw_hata"]
        ms_don = np.array(d["ms_don"])
        ms_kes = np.array(d["ms_kes"])
        ms_top = ms_don + ms_kes
        calisan = np.array([x not in ("tut",) for x in d["durum"]])
        satir = {
            "yontem": v, "girdi": tur, "kayit": ad, "kare": n,
            "son_hata": float(e[-1]), "maks_hata": s["yaw_maks_derece"], "ort_hata": s["yaw_ort_mutlak_derece"],
            "surukleme_360": s["yaw_surukleme_derece_360_basina"],
            "tam_tur_hata": _ilk_tam_tur_hatasi(e, h["gyon"]),
            "anahtar_degisim": int(d["degisim"][-1]),
            "ms_donusum_ort": float(ms_don.mean()), "ms_kestirim_ort": float(ms_kes.mean()),
            "ms_kestirim_ort_calisan": float(ms_kes[calisan].mean()) if calisan.any() else 0.0,
            "ms_toplam_ort": float(ms_top.mean()), "ms_toplam_p95": float(np.percentile(ms_top, 95)),
            "tut": int((np.array(d["durum"]) == "tut").sum()), "yedek": int((np.array(d["durum"]) == "yedek").sum()),
            "kalite_ort": float(np.mean(d["kalite"])),
        }
        cikti.append((satir, {"kare": kn[:n], "t_ms": h["t_ms"], "yon": h["tyon"], "hata": e, "kalite": d["kalite"],
                              "durum": d["durum"], "degisim": d["degisim"], "gyon": h["gyon"]}))
    return cikti


# ------------------------------------------------------------------ çıktılar
ALANLAR = ["yontem", "girdi", "kayit", "kare", "son_hata", "maks_hata", "ort_hata", "surukleme_360", "tam_tur_hata",
           "anahtar_degisim", "ms_donusum_ort", "ms_kestirim_ort", "ms_kestirim_ort_calisan", "ms_toplam_ort",
           "ms_toplam_p95", "tut", "yedek", "kalite_ort"]


def _f(v, nd=2):
    return "-" if v is None else ("%.*f" % (nd, v))


def tablo_yaz(satirlar, klasor):
    os.makedirs(klasor, exist_ok=True)
    with open(os.path.join(klasor, "tablo.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, ALANLAR)
        w.writeheader()
        for s in satirlar:
            w.writerow({k: s.get(k) for k in ALANLAR})
    md = ["| yöntem | girdi | kayıt | kare | son hata ° | en çok ° | ort ° | sürükl. °/360° | 1. tam tur ° | anahtar değ. | "
          "dönüşüm ms | kestirim ms | toplam ms (p95) | tut | yedek |", "|" + "---|" * 15]
    for s in satirlar:
        md.append("| %s | %s | %s | %d | %+.2f | %.2f | %.2f | %s | %s | %d | %.2f | %.2f | %.2f (%.2f) | %d | %d |" % (
            s["yontem"], s["girdi"], s["kayit"], s["kare"], s["son_hata"], s["maks_hata"], s["ort_hata"],
            _f(s["surukleme_360"]), _f(s["tam_tur_hata"]), s["anahtar_degisim"], s["ms_donusum_ort"], s["ms_kestirim_ort"],
            s["ms_toplam_ort"], s["ms_toplam_p95"], s["tut"], s["yedek"]))
    with open(os.path.join(klasor, "tablo.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(md) + "\n")


def grafik_yaz(satir, d, klasor):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    os.makedirs(klasor, exist_ok=True)
    t = (d["t_ms"] - d["t_ms"][0]) / 1000.0
    fig, ax = plt.subplots(2, 1, figsize=(10, 5.5), sharex=True, gridspec_kw={"height_ratios": [3, 1.3]})
    ax[0].plot(t, d["hata"], color="#d1342f", lw=1.3, label="yaw hatası")
    dg = np.diff(np.r_[0, d["degisim"]]) > 0
    for ti in t[dg]:
        ax[0].axvline(ti, color="#1f5fa8", alpha=0.25, lw=0.8)
    yedek = np.array([x == "yedek" for x in d["durum"]])
    if yedek.any():
        ax[0].plot(t[yedek], d["hata"][yedek], "k.", ms=4, label="yedek (eşleşme yok)")
    ax[0].axhline(0, color="gray", lw=0.5)
    ax[0].set_ylabel("hata (derece)")
    ax[0].grid(alpha=0.3)
    ax[0].legend(loc="best", fontsize=8)
    ax[0].set_title("%s | %s | %s | anahtar değişimi %d | son %+.2f° en çok %.2f°" % (
        satir["yontem"], satir["girdi"], satir["kayit"], satir["anahtar_degisim"], satir["son_hata"], satir["maks_hata"]))
    ax[1].plot(t, d["kalite"], color="#1b7f3b", lw=1)
    ax[1].set_ylabel("kalite")
    ax[1].set_xlabel("zaman (s)")
    ax[1].set_ylim(-0.05, 1.05)
    ax[1].grid(alpha=0.3)
    fig.tight_layout()
    fig.savefig(os.path.join(klasor, "%s_%s_%s.png" % (satir["girdi"], satir["kayit"], satir["yontem"].replace("|", "_"))), dpi=90)
    plt.close(fig)


def tahmin_yaz(satir, d, klasor):
    k = os.path.join(klasor, satir["yontem"].replace("|", "_"))
    os.makedirs(k, exist_ok=True)
    with open(os.path.join(k, "%s_%s.csv" % (satir["girdi"], satir["kayit"])), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["kare", "t_ms", "yon_derece", "hata_derece", "kalite", "durum", "anahtar_degisim"])
        for i in range(len(d["kare"])):
            w.writerow([int(d["kare"][i]), int(d["t_ms"][i]), "%.4f" % d["yon"][i], "%.4f" % d["hata"][i],
                        "%.4f" % d["kalite"][i], d["durum"][i], d["degisim"][i]])


def ozet_tarama(satirlar):
    """Tarama özeti: yontem (varyant) başına girdi türüne göre ortanca ve en kötü sayılar (yerinde dönüşlerde ve hepsinde)."""
    gruplar = {}
    for s in satirlar:
        gruplar.setdefault((s["yontem"], s["girdi"]), []).append(s)
    md = ["| varyant | girdi | kosu | ort° (medyan) | ort° (en kötü) | en çok° (en kötü) | yerinde 1. tam tur ° (en kötü) | "
          "anahtar değ. (toplam) | kestirim ms (ort) |", "|" + "---|" * 9]
    for (v, g), ss in sorted(gruplar.items()):
        yer = [abs(x["tam_tur_hata"]) for x in ss if x["kayit"].startswith("yerinde") and x["tam_tur_hata"] is not None]
        md.append("| %s | %s | %d | %.2f | %.2f | %.2f | %s | %d | %.2f |" % (
            v, g, len(ss), np.median([x["ort_hata"] for x in ss]), max(x["ort_hata"] for x in ss),
            max(x["maks_hata"] for x in ss), "%.2f" % max(yer) if yer else "-", sum(x["anahtar_degisim"] for x in ss),
            np.mean([x["ms_kestirim_ort"] for x in ss])))
    return "\n".join(md) + "\n"


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--cikti", default=os.path.join(DEPO, "kayitlar", "sonuclar", "pusula"))
    p.add_argument("--tarama", choices=["bant", "genislik", "mesafe", "tumu"])
    p.add_argument("--girdi", default="sim,theta")
    p.add_argument("--kayit", default=",".join(KAYITLAR))
    p.add_argument("--yontem", help="virgüllü varyant adları (süzgeç)")
    p.add_argument("--isci", type=int, default=8)
    p.add_argument("--max_kare", type=int)
    p.add_argument("--grafiksiz", action="store_true")
    a = p.parse_args()
    varyantlar = tarama_varyantlari(a.tarama) if a.tarama else ana_varyantlar()
    if a.yontem:
        sec = a.yontem.split(",")
        varyantlar = {k: v for k, v in varyantlar.items() if k in sec or k.split("|")[0] in sec}
    cikti = a.cikti if not a.tarama else os.path.join(a.cikti, "tarama_" + a.tarama)
    isler = [(g, k, varyantlar, a.max_kare) for g in a.girdi.split(",") for k in a.kayit.split(",")]
    isler.sort(key=lambda x: -(1618 if x[1] == "ev_turu" else 600 if x[1] in ("salon_turu", "duz_git") else 130))
    t0 = time.time()
    satirlar = []
    if a.isci > 1:
        import multiprocessing as mp
        with mp.Pool(a.isci) as h:
            sonuclar = h.map(kos, isler, chunksize=1)
    else:
        sonuclar = [kos(i) for i in isler]
    for res in sonuclar:
        for satir, d in res:
            satirlar.append(satir)
            if not a.tarama and not a.grafiksiz:
                grafik_yaz(satir, d, os.path.join(cikti, "grafik"))
            tahmin_yaz(satir, d, os.path.join(cikti, "tahmin"))
    satirlar.sort(key=lambda s: (s["yontem"], s["girdi"], KAYITLAR.index(s["kayit"])))
    tablo_yaz(satirlar, cikti)
    if a.tarama:
        with open(os.path.join(cikti, "ozet.md"), "w", encoding="utf-8") as f:
            f.write(ozet_tarama(satirlar))
        print(ozet_tarama(satirlar))
    print("Bitti: %d kosu, %.0f sn; çıktı: %s" % (len(satirlar), time.time() - t0, cikti))


if __name__ == "__main__":
    main()
