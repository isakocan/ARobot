"""Kalibre geometriyle TAM hat (gercek_ortak yontemi): tarama (SegFormer) -> M2D-LCN yaw (komutlu) -> LCN BEV -> birlesik kestirici E.
  python gercek_kalib_tam.py <asama tarama|yon|bev|kos> <kayit> [etiket=kalib] [maske=gercek]
Cikti: onbellek_<maske>_<etiket>/ ve harita_<maske>_<etiket>/ ve kos_<maske>_<etiket>/ (gercek_kos.kaydet; maske adi '<maske>_<etiket>')."""
import os
import sys
import time

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_kalib_ortak as ok  # noqa: E402

if __name__ == "__main__":
    asama, ad = sys.argv[1], sys.argv[2]
    etiket = sys.argv[3] if len(sys.argv) > 3 else "kalib"
    maske = sys.argv[4] if len(sys.argv) > 4 else "gercek"
    bo, ko, zo = ok.kur(maske, "gercek", etiket)
    import cv2
    cv2.setNumThreads(2)
    t0 = time.time()
    if asama == "tarama":
        import zemin_degerlendir as zd
        zd.tara_model_hepsi("theta", ad, 5)
    elif asama == "yon":
        import birlesik_yon as by
        by.hesapla((ad, True))
    elif asama == "bev":
        import birlesik_bev as bb
        bb.bev_lcn(ad, etiket="LLkomutlu", yaw_kaynak="Lkomutlu")
    elif asama == "kos":
        import gercek_kos as gk
        r = gk.kos(ad, maske + "_" + etiket)
        gk.kaydet(maske + "_" + etiket, ad, r)
    print("tamam", asama, ad, "%.0f sn" % (time.time() - t0), flush=True)
