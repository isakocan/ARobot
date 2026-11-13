"""Görsel pusula ek analizleri (rapor için).
  ablasyon : Theta benzetiminin etkilerini tek tek kapatıp/şiddetlendirip pusula doğruluğuna bakar (geçici klasöre yazar).
  oteleme  : duz_git'te düz gidiş bölümlerinde yaw hatasının mesafeyle eğimi (derece/m); tahmin/ klasöründeki CSV'lerden.
  bolge    : bir koşuda hatanın büyüdüğü kare aralıklarını ve o anki gerçek konumu/komutu/kaliteyi listeler.
Kullanım:
  python tools/konum_deney/pusula_analiz.py ablasyon <gecici_klasor> [kayit] [max_kare]
  python tools/konum_deney/pusula_analiz.py oteleme [tahmin_klasoru]
  python tools/konum_deney/pusula_analiz.py bolge <yontem> <girdi> <kayit> [esik_derece]"""
import csv
import os
import sys

import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
import pusula_degerlendir as pd  # noqa: E402
from kayit import Kayit  # noqa: E402
from olc import sar  # noqa: E402
import theta_benzet  # noqa: E402

ABLASYON = {
    "varsayilan": {},
    "yalniz_izdusum": {"govde": False, "sigma": 0.0, "hareket": False, "gurultu": False, "jpeg_kalite": 100},
    "govde_yok": {"govde": False},
    "sigma_0": {"sigma": 0.0},
    "hareket_bulanikligi_yok": {"hareket": False},
    "gurultu_yok": {"gurultu": False},
    "jpeg_100": {"jpeg_kalite": 100},
    "sert": {"sigma": 2.0, "pozlama_ms": 50.0, "jpeg_kalite": 40},
    "sert_yalniz_sigma2": {"sigma": 2.0},
    "sert_yalniz_pozlama50": {"pozlama_ms": 50.0},
    "sert_yalniz_jpeg40": {"jpeg_kalite": 40},
}


def ablasyon(gecici, kayit="yerinde_donus_mutfak", max_kare=None):
    kaynak = os.path.join(pd.DEPO, "kayitlar", "sim", kayit)
    os.makedirs(gecici, exist_ok=True)
    for ad, kw in ABLASYON.items():
        hedef = os.path.join(gecici, ad)
        if not os.path.exists(os.path.join(hedef, "bilgi.json")):
            theta_benzet.benzet(kaynak, hedef, max_kare=max_kare, sessiz=True, **kw)
    # sim referansı: kendi klasöründen
    varyantlar = {"M1": pd.ana_varyantlar()["M1"], "M2D": pd.ana_varyantlar()["M2D"]}
    print("| koşul | yöntem | son ° | en çok ° | ort ° |\n|---|---|---|---|---|")
    for tur, ad in [("sim", kayit)] + [(gecici, a) for a in ABLASYON]:
        for satir, _ in pd.kos((tur, ad, varyantlar, max_kare)):
            print("| %s | %s | %+.2f | %.2f | %.2f |" % ("sim (temiz)" if tur == "sim" else ad, satir["yontem"], satir["son_hata"],
                                                          satir["maks_hata"], satir["ort_hata"]))


def oteleme(klasor=None):
    """duz_git: düz gidiş bölümleri (komut sol=sag, gerçek dönüş yok) için hata - mesafe eğimi."""
    klasor = klasor or os.path.join(pd.DEPO, "kayitlar", "sonuclar", "pusula", "tahmin")
    kay = Kayit(os.path.join(pd.DEPO, "kayitlar", "sim", "duz_git"))
    g = kay.gercek_konum()
    mesafe = np.r_[0.0, np.cumsum(np.hypot(np.diff(g["x_m"]), np.diff(g["y_m"])))]
    bolumler = {"gidis": (20, 215), "donus_yolu": (340, 520)}
    print("| yöntem | girdi | bölüm | gerçek yaw eğimi °/m | tahmin yaw eğimi °/m | hata eğimi °/m | bölüm sonu hata ° |\n|---|---|---|---|---|---|---|")
    for v in sorted(os.listdir(klasor)):
        for girdi in ("sim", "theta"):
            yol = os.path.join(klasor, v, "%s_duz_git.csv" % girdi)
            if not os.path.exists(yol):
                continue
            r = list(csv.DictReader(open(yol, encoding="utf-8")))
            hata = np.array([float(x["hata_derece"]) for x in r])
            yon = np.array([float(x["yon_derece"]) for x in r])
            for ad, (a, b) in bolumler.items():
                m = mesafe[a:b + 1]
                gy = np.degrees(np.unwrap(np.radians(g["yon_derece"][a:b + 1])))
                ty = np.degrees(np.unwrap(np.radians(yon[a:b + 1])))
                eg = np.degrees(np.unwrap(np.radians(hata[a:b + 1])))
                sl = lambda y: np.polyfit(m, y, 1)[0]      # noqa: E731
                print("| %s | %s | %s | %+.2f | %+.2f | %+.2f | %+.2f |" % (v, girdi, ad, sl(gy), sl(ty), sl(eg), hata[b] - hata[a]))


def bolge(yontem, girdi, kayit, esik=3.0):
    yol = os.path.join(pd.DEPO, "kayitlar", "sonuclar", "pusula", "tahmin", yontem, "%s_%s.csv" % (girdi, kayit))
    r = list(csv.DictReader(open(yol, encoding="utf-8")))
    kay = Kayit(os.path.join(pd.DEPO, "kayitlar", girdi, kayit))
    g = kay.gercek_konum()
    hata = np.array([float(x["hata_derece"]) for x in r])
    print("kare | hata | gerçek yaw | x,y | komut | kalite | durum | anahtar")
    for i in range(0, len(r), max(1, len(r) // 60)):
        t = int(r[i]["t_ms"])
        print("%4d | %+6.1f | %6.1f | %5.2f,%5.2f | %s | %s | %s | %s" % (
            i, hata[i], g["yon_derece"][i], g["x_m"][i], g["y_m"][i], tuple(int(x) for x in kay.komut(t)), r[i]["kalite"][:4],
            r[i]["durum"], r[i]["anahtar_degisim"]))


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    k = sys.argv[1]
    if k == "ablasyon":
        ablasyon(sys.argv[2], *(sys.argv[3:4] or ["yerinde_donus_mutfak"]), *([int(sys.argv[4])] if len(sys.argv) > 4 else []))
    elif k == "oteleme":
        oteleme(*sys.argv[2:3])
    elif k == "bolge":
        bolge(sys.argv[2], sys.argv[3], sys.argv[4], *([float(sys.argv[5])] if len(sys.argv) > 5 else []))
    else:
        raise SystemExit(__doc__)
