"""Birleşik (kaynak kaynaştırmalı) konum kestirimi için ortak parçalar: kayıt kümeleri, yollar, girdi önbelleği.

Üç küme (hepsi theta = gerçek Theta S gibi benzetilmiş kayıt):
  gel   kayitlar/theta/*            9 geliştirme kaydı
  sin   kayitlar/sinama/theta/*     6 ilk held-out kayıt (BU ÇALIŞMADA ARTIK GELİŞTİRME: parametreler 15 kayıtla seçildi)
  sin2  kayitlar/sinama2/theta/*    5 TAZE kayıt (yalnız donmuş kestiriciyle bir kez)
Girdiler önceki aşamalardan OKUNUR (yazılmaz): M2D yaw önbelleği (komutlu), zemin sınırı taramaları (SegFormer-B0).
Bu dosyada hesaplanan: M2D'nin KOMUTLU yaw'ıyla HAM BEV artımı (karşılaştırma); asıl kestirici girdileri birlesik_bev.py (LCN BEV) ve
birlesik_yon.py (LCN M2D). Çıktı: kayitlar/sonuclar/birlesik/onbellek/."""
import json
import os
import sys
import time

import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
KOK = os.path.abspath(os.path.join(KLASOR, "..", ".."))
KAYITLAR = os.path.join(KOK, "kayitlar")
SONUC = os.path.join(KAYITLAR, "sonuclar")
CIKTI = os.path.join(SONUC, "birlesik")
ONB = os.path.join(CIKTI, "onbellek")

GEL = ["yerinde_donus_salon", "yerinde_donus_koridor", "yerinde_donus_yatak_odasi", "yerinde_donus_banyo",
       "yerinde_donus_mutfak", "yerinde_donus_calisma_odasi", "duz_git", "salon_turu", "ev_turu"]
SIN = ["sinama_yeni_rota", "sinama_ters_tur", "sinama_los", "sinama_far", "sinama_duz_zemin", "sinama_hizli"]
SIN2 = ["sinama2_t1_normal", "sinama2_t2_los", "sinama2_t3_far", "sinama2_t4_duz", "sinama2_t5_karisik"]


def spec(ad):
    """Kayıt adından yol sözlüğü."""
    if ad in GEL:
        return dict(ad=ad, kume="gel", theta=os.path.join(KAYITLAR, "theta", ad), sim=os.path.join(KAYITLAR, "sim", ad),
                    onb=os.path.join(SONUC, "konum", "onbellek"),
                    tar=os.path.join(SONUC, "harita", "tarama", "model_gorunum", "theta", ad + ".npz"),
                    tahmin=os.path.join(SONUC, "konum", "tahmin"))
    if ad in SIN:
        return dict(ad=ad, kume="sin", theta=os.path.join(KAYITLAR, "sinama", "theta", ad), sim=os.path.join(KAYITLAR, "sinama", "sim", ad),
                    onb=os.path.join(SONUC, "sinama", "konum", "onbellek"),
                    tar=os.path.join(SONUC, "sinama", "harita", "tarama", "model_gorunum", "theta", ad + ".npz"),
                    tahmin=os.path.join(SONUC, "sinama", "konum", "tahmin"))
    if ad in SIN2:
        b = os.path.join(CIKTI, "sinama2")
        return dict(ad=ad, kume="sin2", theta=os.path.join(KAYITLAR, "sinama2", "theta", ad), sim=os.path.join(KAYITLAR, "sinama2", "sim", ad),
                    onb=os.path.join(b, "onbellek"),
                    tar=os.path.join(b, "harita", "tarama", "model_gorunum", "theta", ad + ".npz"),
                    tahmin=os.path.join(b, "konum", "tahmin"))
    raise KeyError(ad)


def npz(yol):
    z = np.load(yol)
    return {k: z[k] for k in z.files}


def yon_yolu(s, komutlu=True):
    return os.path.join(s["onb"], "yon_%s_theta_%s.npz" % ("komutlu" if komutlu else "komutsuz", s["ad"]))


def bev_yolu(s):
    return os.path.join(ONB, "bevK_%s.npz" % s["ad"])


def bev_komutlu(ad, yeniden=False, ilerleme=True):
    """BEV artımı, yaw = M2D KOMUTLU, özellikler zemin taramasının (nedensel, son tarama) sınırı içinde. konum_bev.bev_hesapla ile
    AYNI sınıflar/parametreler; yalnız yaw kaynağı ve çıktı yolu farklı.(HAM BEV, LCN'siz; yalnız karşılaştırma için). Çıktı: kare, d (n,2), ok, ic, ms."""
    s = spec(ad)
    p = bev_yolu(s)
    if os.path.exists(p) and not yeniden:
        return npz(p)
    import cv2
    cv2.setNumThreads(1)
    import konum_bev as kb
    from kayit import Kayit
    from kamera import Kamera
    yz = npz(yon_yolu(s, True))
    kay = Kayit(s["theta"])
    kam = Kamera.bilgiden(kay.bilgi)
    tab = kb.BevTablo("theta", kam)
    oz = kb.BevOtelemeci(tab)
    tz = npz(s["tar"])
    tk = tz["kare"]
    n = len(kay)
    D = np.full((n, 2), np.nan)
    OK = np.zeros(n, bool)
    IC = np.zeros(n, np.int32)
    MS = np.zeros(n)
    t0 = time.time()
    for i in range(n):
        j = max(int(np.searchsorted(tk, i, side="right")) - 1, 0)
        em = kb.tarama_maskesi(tab, tz["mesafe"][j], tz["guven"][j], 0.25)
        ds, di, ok, ic = oz.guncelle(kay.resim(i), float(yz["yon"][i]), em)
        D[i] = (ds, di)
        OK[i] = ok
        IC[i] = ic
        MS[i] = oz.son_ms
        if ilerleme and i % 300 == 0:
            print("  bevK %s %d/%d (%.0f sn)" % (ad, i, n, time.time() - t0), flush=True)
    os.makedirs(ONB, exist_ok=True)
    z = {"kare": kay.kare_numaralari, "d": D, "ok": OK, "ic": IC, "ms": MS}
    np.savez_compressed(p, **z)
    return z


def _is(ad):
    bev_komutlu(ad)
    return ad


if __name__ == "__main__":
    import argparse
    from concurrent.futures import ProcessPoolExecutor
    ap = argparse.ArgumentParser()
    ap.add_argument("kume", choices=["gel", "sin", "sin2", "hepsi15"])
    ap.add_argument("--isci", type=int, default=6)
    a = ap.parse_args()
    adlar = {"gel": GEL, "sin": SIN, "sin2": SIN2, "hepsi15": GEL + SIN}[a.kume]
    with ProcessPoolExecutor(a.isci) as ex:
        for r in ex.map(_is, adlar):
            print("tamam", r, flush=True)
