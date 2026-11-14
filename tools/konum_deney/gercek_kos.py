"""Birleşik kestirici E'yi (birlesik_kestirici.Kestirici, PARAMETRELER AYNEN) gerçek kayıtlarda çalıştırır: yol + doluluk haritası (yer gerçeği YOK).
  python gercek_kos.py bev  <maske> [kayit ...]      LCN BEV artımlarını hesapla (ham girdi; zemin taraması gerekir; h = bilgi.json kamera_yuksekligi_m = 0,17)
  python gercek_kos.py kos  <maske> [kayit ...]      E'yi koş, yol/harita grafiği + sayılar
Girdiler (hepsi gercek_ortak.kur ile yönlendirilmiş yollardan): M2D-LCN komutlu yaw (gercek_yon_calistir.py lcn komutlu), SegFormer taraması (gercek_tarama_calistir.py),
LCN BEV (bu dosya 'bev'). Başlangıç pozu (0,0, yön 0). Nominal hız/dönüş: bilgi.json kayma_modeli (SİM VARSAYIMI 0,55 m/s, 197 derece/sn @ tam PWM).
Değişen tek şey yükseklik (0,255 -> 0,17: bilgi.json'dan) ve (isteğe bağlı) gövde maskesi; E parametreleri ve BEV/M2D kodu aynıdır."""
import json
import os
import sys
import time

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_ortak as go  # noqa: E402


def bev_is(arg):
    maske, ad = arg
    bo, ko, zo = go.kur(maske)
    import cv2
    cv2.setNumThreads(1)
    import birlesik_bev as bb
    bb.bev_lcn(ad, etiket="LLkomutlu", yaw_kaynak="Lkomutlu")
    return ad


def kos(ad, maske, h_gecersiz=None, nominal=None):
    bo, ko, zo = go.kur(maske)
    import birlesik_bev as bb
    import birlesik_kestirici as bk
    import birlesik_yon as by
    import harita_kur as hk
    from kayit import Kayit
    s = bo.spec(ad)
    kay = Kayit(s["theta"])
    n = len(kay)
    yz = by.yukle(ad, True)
    bz = bb.bev_lcn(ad, etiket="LLkomutlu", yaw_kaynak="Lkomutlu")
    tar = bo.npz(s["tar"])
    v_tam, w_tam = ko.nominal_hizlar(kay.bilgi)
    if nominal is not None:                      # DEĞİŞMİŞ VARYANT: (v_tam m/sn, w_tam derece/sn) elle verilir
        v_tam, w_tam = nominal
    t_ms = kay.t_ms
    komut = np.array([kay.komut(int(t)) for t in t_ms], float)
    est = bk.Kestirici((0.0, 0.0, 0.0), v_tam, w_tam, bk.Param())
    P = np.zeros((n, 3))
    seri = {k: np.zeros(n) for k in ("sigma", "guven", "k", "oran", "guven_b", "bev_kullan", "yaw_komut", "komut_sifir", "kare_red")}
    ms = np.zeros(n)
    iz = hk.Izgara(-20.0, -20.0, 800, 800, 0.05)
    sira = {int(k): i for i, k in enumerate(tar["kare"])}
    menzil = float(tar["max_menzil"])
    # komut yolu (E'siz karşılaştırma): yalnız komut, M2D yaw'ı ile (kendi yaw'ı yok)
    for i in range(n):
        a = time.perf_counter()
        d, ok, ic = bz["d"][i], bz["ok"][i], bz["ic"][i]
        r = est.adim(int(t_ms[i]), float(yz["yon"][i]), int(yz["icerik"][i]), (d[0], d[1], ok, ic), tuple(komut[i]))
        ms[i] = (time.perf_counter() - a) * 1000.0
        P[i] = est.poz
        r["x"], r["y"], r["yaw"] = est.poz
        for k in seri:
            seri[k][i] = r.get(k, np.nan)
        j = sira.get(int(kay.kare_numaralari[i]))
        if j is not None:
            iz.tara_ekle(est.poz[0], est.poz[1], est.poz[2], tar["mesafe"][j], tar["guven"][j], menzil, guven_min=0.2)
    return dict(P=P, seri=seri, ms=ms, iz=iz, t_ms=t_ms, komut=komut, bz=bz, yz=yz, menzil=menzil, v_tam=v_tam, w_tam=w_tam, ad=ad)


def kaydet(maske, ad, r):
    ob = os.path.join(go.SONUC, "kos_" + maske)
    os.makedirs(ob, exist_ok=True)
    np.savez_compressed(os.path.join(ob, ad + ".npz"), P=r["P"], t_ms=r["t_ms"], komut=r["komut"], lo=r["iz"].lo, gorulen=r["iz"].gorulen,
                        **{k: np.asarray(v) for k, v in r["seri"].items()})


def is_(arg):
    maske, ad = arg
    r = kos(ad, maske)
    kaydet(maske, ad, r)
    return ad


if __name__ == "__main__":
    asama, maske = sys.argv[1], sys.argv[2]
    adlar = sys.argv[3:] or go.ADLAR
    from concurrent.futures import ProcessPoolExecutor
    t0 = time.time()
    with ProcessPoolExecutor(min(6, len(adlar))) as ex:
        for ad in ex.map(bev_is if asama == "bev" else is_, [(maske, a) for a in adlar]):
            print("tamam", asama, ad, "%.0f sn" % (time.time() - t0), flush=True)
