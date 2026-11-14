"""Taze held-out kümesi (sinama2) için girdi hazırlığı ve A/C/D çalıştırma. sinama_calistir.py'nin aynısı, yalnız yollar farklı:
  kayıtlar -> kayitlar/sinama2/{sim,theta}/<ad>;  çıktılar -> kayitlar/sonuclar/birlesik/sinama2/...
Hiçbir yöntem parametresi değiştirilmez. Kullanım:
  python birlesik_sinama2.py yon|tarama|bev|abcd [--kayit ad ...] [--isci N]
  (yon: M2D komutlu+komutsuz; tarama: SegFormer GPU her 5. kare; bev: komutsuz BEV (C ve D için); abcd: A, C, D çalıştır + ölçüt)
Girdi hazırlığı (yon/tarama/bev) tahmin sonucu GÖSTERMEZ; abcd aşaması ölçüt tablosunu yazar (sinama2 sonuçlarına ilk bakış)."""
import argparse
import os
import sys
import time

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import konum_ortak as ko  # noqa: E402
import zemin_ortak as zo  # noqa: E402
import birlesik_ortak as bo  # noqa: E402

SINAMA2 = os.path.join(bo.KAYITLAR, "sinama2")
ADLAR = bo.SIN2
B = os.path.join(bo.CIKTI, "sinama2")


def yolla():
    zo.KAYITLAR = SINAMA2
    zo.CIKTI = os.path.join(B, "harita")
    ko.KAYITLAR = SINAMA2
    ko.KONUM_CIKTI = os.path.join(B, "konum")
    ko.ONBELLEK = os.path.join(B, "onbellek")
    ko.TAHMIN = os.path.join(ko.KONUM_CIKTI, "tahmin")
    ko.KAYIT_ADLARI = list(ADLAR)
    try:
        import konum_degerlendir as kd
        kd.CIKTI = ko.KONUM_CIKTI
    except Exception:
        pass


yolla()


def yon_is(a):
    yolla()
    import konum_yon_onbellek as yo
    return yo.hesapla(a)


def bev_is(a):
    yolla()
    import cv2
    cv2.setNumThreads(1)
    import konum_bev as kb
    kb.bev_hesapla("theta", a, False)
    return a


def ana():
    ap = argparse.ArgumentParser()
    ap.add_argument("asama", choices=["yon", "tarama", "bev", "abcd"])
    ap.add_argument("--kayit", nargs="*", default=ADLAR)
    ap.add_argument("--isci", type=int, default=4)
    ap.add_argument("--varyant", nargs="*", default=["A", "C", "D"])
    a = ap.parse_args()
    t0 = time.time()
    if a.asama == "yon":
        from concurrent.futures import ProcessPoolExecutor
        isler = [(k, "theta", ad) for ad in a.kayit for k in (True, False)]
        with ProcessPoolExecutor(a.isci) as ex:
            for _ in ex.map(yon_is, isler):
                pass
    elif a.asama == "tarama":
        import zemin_degerlendir as zd
        for ad in a.kayit:
            zd.tara_model_hepsi("theta", ad, 5)
    elif a.asama == "bev":
        from concurrent.futures import ProcessPoolExecutor
        with ProcessPoolExecutor(a.isci) as ex:
            for _ in ex.map(bev_is, a.kayit):
                pass
    else:
        import numpy as np
        import konum_degerlendir as kd
        satirlar = []
        for ad in a.kayit:
            for v in a.varyant:
                s, P, log = kd.kos(v, "theta", ad)
                np.savez_compressed(os.path.join(ko.KONUM_CIKTI, "log_%s_%s.npz" % (v, ad)), P=P)
                satirlar.append(s)
                print("%s %-20s RMSE %.3f maks %.3f son %.3f yaw %.2f/%.2f IoU %.2f (bilinen %.2f) kabul %s" % (
                    v, ad, s["konum_rmse_m"], s["konum_maks_m"], s["konum_son_m"], s["yaw_ort_mutlak_derece"], s["yaw_maks_derece"],
                    s["harita_bos_iou"], s["referans_bos_iou"], s["esle_kabul"]), flush=True)
                kd.tablo_yaz(satirlar, "")
    print("bitti %s %.0f sn" % (a.asama, time.time() - t0))


if __name__ == "__main__":
    ana()
