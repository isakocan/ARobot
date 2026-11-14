"""M2D yönünü gerçek kayıtlarda çalıştırır (yöntem dosyaları aynen; yalnız yol/maske yönlendirmesi: gercek_ortak.kur).
  python gercek_yon_calistir.py <maske eski|gercek> <ham|lcn> <komutlu|komutsuz> [kayit ...] [--isci N]
ham = yontem_pusula_nokta (konum_yon_onbellek.hesapla); lcn = birlesik_yon (E'nin yaw kaynağı). Çıktı: onbellek_<maske>/yon[L]_<komutlu|komutsuz>_[theta_]<ad>.npz"""
import os
import sys
import time
from concurrent.futures import ProcessPoolExecutor

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_ortak as go  # noqa: E402


def is_(arg):
    maske, tur, komutlu, ad = arg
    bo, ko, zo = go.kur(maske)
    import cv2
    cv2.setNumThreads(1)
    if tur == "ham":
        import konum_yon_onbellek as yo
        return yo.hesapla((komutlu, "gercek", ad))
    import birlesik_yon as by
    return by.hesapla((ad, komutlu))


if __name__ == "__main__":
    a = [x for x in sys.argv[1:] if not x.startswith("--")]
    isci = int(sys.argv[sys.argv.index("--isci") + 1]) if "--isci" in sys.argv else 6
    if "--isci" in sys.argv:
        a = [x for x in a if x != str(isci)]
    maske, tur, kom = a[:3]
    adlar = a[3:] or go.ADLAR
    t0 = time.time()
    # uzun kayıtlar önce
    adlar = sorted(adlar, key=lambda s: -1 if s.startswith("5_") else 0)
    with ProcessPoolExecutor(isci) as ex:
        for r in ex.map(is_, [(maske, tur, kom == "komutlu", ad) for ad in adlar]):
            print("tamam", r, flush=True)
    print("bitti %.0f sn" % (time.time() - t0))
