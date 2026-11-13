"""Bir konum yöntemini kayıt üzerinde çalıştırır: tahmin.csv + süre + ölçüt (olc.py).

Yöntem arayüzü: bir Python modülü, içinde __init__(self, bilgi, kamera) ve
    guncelle(self, kare_bgr, t_ms, komut) -> (x_m, y_m, yon_derece)     (hangisi bilinmiyorsa None/NaN)
olan bir sınıf. Sınıf adı `Yontem` olabilir; yoksa modülde tanımlı ilk sınıf alınır. komut = (sol, sag) PWM.
Kare, kayıttaki gibi verilir (çift balık gözü ya da eşdikdörtgen); dönüşüm için kamera (kamera.py) kullanılabilir.
bilgi: bilgi.json içeriği (+ yalnızca deney düzeneği için "_kayit_klasoru").

Kullanım:
    python tools/konum_deney/calistir.py --kayit <klasör> --yontem yontem_komut [--cikti <klasör>] [--max_kare N]
Çıktı klasörü varsayılanı: <kayit>/sonuc_<yontem>/"""
import argparse
import csv
import importlib
import inspect
import os
import sys
import time

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
from kamera import Kamera  # noqa: E402
from kayit import Kayit  # noqa: E402
import olc  # noqa: E402


def yontem_sinifi(modul_adi):
    m = importlib.import_module(modul_adi)
    if hasattr(m, "Yontem"):
        return m.Yontem
    for _, c in inspect.getmembers(m, inspect.isclass):
        if c.__module__ == m.__name__ and hasattr(c, "guncelle"):
            return c
    raise SystemExit("%s modülünde guncelle() olan sınıf yok" % modul_adi)


def _f(v):
    return "" if v is None or (isinstance(v, float) and not np.isfinite(v)) else "%.6f" % v


def calistir(kayit_klasoru, yontem, cikti=None, max_kare=None, sessiz=False):
    kay = Kayit(kayit_klasoru)
    cikti = cikti or os.path.join(kayit_klasoru, "sonuc_" + yontem.split(".")[-1])
    os.makedirs(cikti, exist_ok=True)
    bilgi = dict(kay.bilgi)
    bilgi["_kayit_klasoru"] = os.path.abspath(kayit_klasoru)
    kam = Kamera.bilgiden(kay.bilgi)
    y = yontem_sinifi(yontem)(bilgi, kam)
    satirlar, sureler = [], []
    for n, t, im in kay.kareler(0, max_kare):
        k = kay.komut(t)
        a = time.perf_counter()
        r = y.guncelle(im, t, k)
        sureler.append((time.perf_counter() - a) * 1000.0)
        x, yy, yaw = (list(r) + [None, None, None])[:3]
        satirlar.append((n, t, x, yy, yaw))
    yol = os.path.join(cikti, "tahmin.csv")
    with open(yol, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["kare", "t_ms", "x_m", "y_m", "yon_derece"])
        for n, t, x, yy, yaw in satirlar:
            w.writerow([n, t, _f(x), _f(yy), _f(yaw)])
    zaman = {"ort_ms": float(np.mean(sureler)), "p95_ms": float(np.percentile(sureler, 95)), "kare": len(sureler)}
    sonuc = None
    if kay.gercek_konum() is not None:
        sonuc = olc.degerlendir(kay, yol, cikti, zaman, ad=yontem)
        if not sessiz:
            print(sonuc["ozet"])
    elif not sessiz:
        print("gercek_konum.csv yok; yalnızca tahmin.csv yazıldı. Süre: ort %.2f ms, p95 %.2f ms" % (zaman["ort_ms"], zaman["p95_ms"]))
    return sonuc, zaman, cikti


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--kayit", required=True)
    p.add_argument("--yontem", required=True, help="modül adı, ör. yontem_komut")
    p.add_argument("--cikti")
    p.add_argument("--max_kare", type=int)
    a = p.parse_args()
    calistir(a.kayit, a.yontem, a.cikti, a.max_kare)


if __name__ == "__main__":
    main()
