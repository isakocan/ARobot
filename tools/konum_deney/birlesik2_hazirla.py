"""E2 icin SIM girdi hazirligi: sinama2 kayitlarinda eksik olan LCN M2D komutsuz yaw ve LCN BEV (yaw = komutsuz) onbellegi (gel+sin icin zaten var).
  python birlesik2_hazirla.py [--isci N]    (yontem dosyalarina dokunmaz; birlesik_yon.hesapla / birlesik_bev.bev_lcn cagrilir)"""
import os
import sys
from concurrent.futures import ProcessPoolExecutor

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import birlesik_ortak as bo  # noqa: E402


def yon_is(ad):
    import cv2
    cv2.setNumThreads(1)
    import birlesik_yon as by
    by.hesapla((ad, False))
    return ad


def bev_is(ad):
    import cv2
    cv2.setNumThreads(1)
    import birlesik_bev as bb
    bb.bev_lcn(ad, etiket="LLkomutsuz", yaw_kaynak="Lkomutsuz", ilerleme=False)
    return ad


if __name__ == "__main__":
    isci = int(sys.argv[sys.argv.index("--isci") + 1]) if "--isci" in sys.argv else 5
    adlar = bo.GEL + bo.SIN + bo.SIN2
    with ProcessPoolExecutor(isci) as ex:
        for r in ex.map(yon_is, adlar):
            print("yon", r, flush=True)
        for r in ex.map(bev_is, adlar):
            print("bev", r, flush=True)
