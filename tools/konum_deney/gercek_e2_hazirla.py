"""E2 girdi hazirligi, gercek kayitlar: KALIBRE geometri (kamera_gercek.json) + SABIT NOKTA MASKESI (kamera_maske_gercek_sabit.png), etiket 'kalibS'.
  python gercek_e2_hazirla.py tarama|yon|bev <kayit ...> [--isci N]
Onbellek: kayitlar/sonuclar/gercek/onbellek_gercek_kalibS, harita_gercek_kalibS/tarama. Yontem dosyalarina dokunulmaz (gercek_kalib_ortak.kur).
yon: M2D-LCN hem komutlu (E'nin girdisi) hem komutsuz (E2'nin girdisi: saf goruntu, sim sabiti yok). bev: LCN BEV, yaw = ayni kaynak (Lkomutlu / Lkomutsuz)."""
import os
import sys
import time
from concurrent.futures import ProcessPoolExecutor

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_kalib_ortak as ok  # noqa: E402

SABIT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "kamera_maske_gercek_sabit.png")
ETIKET = "kalibS"
ADLAR = ["olcum_ham", "5_saksi_turu", "2a_vitessiz_duz", "2b_otomatik_duz", "3_vitessiz_donus", "4_parke_vitessiz", "6c_karanlik_surus"]


def kur():
    return ok.kur("gercek", "gercek", ETIKET, ozel_maske=SABIT)


def is_(arg):
    asama, ad, komutlu = arg
    kur()
    import cv2
    cv2.setNumThreads(1)
    if asama == "yon":
        import birlesik_yon as by
        by.hesapla((ad, komutlu))
    else:
        import birlesik_bev as bb
        if komutlu:
            bb.bev_lcn(ad, etiket="LLkomutlu", yaw_kaynak="Lkomutlu", ilerleme=False)
        else:
            bb.bev_lcn(ad, etiket="LLkomutsuz", yaw_kaynak="Lkomutsuz", ilerleme=False)
    return asama, ad, komutlu


if __name__ == "__main__":
    a = [x for x in sys.argv[1:] if not x.startswith("--")]
    isci = int(sys.argv[sys.argv.index("--isci") + 1]) if "--isci" in sys.argv else 6
    if "--isci" in sys.argv:
        a = [x for x in a if x != str(isci)]
    asama, adlar = a[0], (a[1:] or ADLAR)
    t0 = time.time()
    if asama == "tarama":
        kur()
        import zemin_degerlendir as zd
        for ad in adlar:
            zd.tara_model_hepsi("theta", ad, 5)
            print("tarama tamam", ad, "%.0f sn" % (time.time() - t0), flush=True)
    else:
        adlar = sorted(adlar, key=lambda s: 0 if s.startswith("olcum") else (1 if s.startswith("5_") else 2))
        isler = [(asama, ad, k) for ad in adlar for k in (True, False)]
        with ProcessPoolExecutor(isci) as ex:
            for r in ex.map(is_, isler):
                print("tamam", r, "%.0f sn" % (time.time() - t0), flush=True)
    print("bitti", asama, "%.0f sn" % (time.time() - t0))
