"""BEV artımlarını tüm kayıtlar için hesaplayıp önbelleğe yazar (konum_bev.bev_hesapla). Kullanım: python konum_bev_onbellek.py [--isci N] [--yeniden]"""
import argparse
import os
import sys
from concurrent.futures import ProcessPoolExecutor

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import konum_ortak as ko  # noqa: E402


def is_(a):
    import cv2
    cv2.setNumThreads(1)
    import konum_bev as kb
    tur, ad, km, yeniden = a
    kb.bev_hesapla(tur, ad, km, yeniden)
    return a


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--isci", type=int, default=6)
    ap.add_argument("--yeniden", action="store_true")
    a = ap.parse_args()
    isler = [("theta", ad, False, a.yeniden) for ad in ko.KAYIT_ADLARI]
    isler += [("sim", "ev_turu", False, a.yeniden), ("sim", "salon_turu", False, a.yeniden)]
    with ProcessPoolExecutor(a.isci) as ex:
        for r in ex.map(is_, isler):
            print("tamam", r[:3], flush=True)
