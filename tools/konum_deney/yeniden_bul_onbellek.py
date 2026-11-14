"""Panorama özellik önbelleğini kayıtlar için paralel hesaplar. Kullanım: python yeniden_bul_onbellek.py [kayit ...]
İlerleme: <onbellek>/ilerleme.log"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import yeniden_bul_ortak as yo  # noqa: E402


def is_(ad):
    import cv2
    cv2.setNumThreads(1)
    t = time.time()
    yo.oz_hesapla(ad)
    return ad, time.time() - t


def ana():
    adlar = sys.argv[1:] or list(yo.KAYITLAR_TUM)
    from concurrent.futures import ProcessPoolExecutor
    os.makedirs(yo.ONBELLEK, exist_ok=True)
    log = open(os.path.join(yo.ONBELLEK, "ilerleme.log"), "a", encoding="utf-8")
    t0 = time.time()
    # en uzun kayıtlar önce
    adlar.sort(key=lambda a: -len(yo.tarama_oku(a)["kare"]))
    with ProcessPoolExecutor(4) as ex:
        for ad, s in ex.map(is_, adlar):
            m = "%s tamam %.0f sn (toplam %.0f)" % (ad, s, time.time() - t0)
            print(m, flush=True)
            log.write(m + "\n")
            log.flush()


if __name__ == "__main__":
    ana()
