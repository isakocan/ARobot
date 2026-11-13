"""M2D yön yöntemini kayıtlar üstünde bir kez çalıştırıp kare başına İÇ DEĞERLERİNİ diske yazar (yöntem dosyası DEĞİŞTİRİLMEDEN).

Saklananlar (kare başına): yon (M2D ham yaw, başlangıç 0), phi (ilerleme yönü azimutu, derece, 0..180; işaretsiz), icerik (iç eşleşme sayısı),
eslesme, kalite, durum_kodu, anahtar_degisim (sayaç), ms (kare süresi). Anahtar kare yaw'ı: anahtar_yon.
Kullanım:  python konum_yon_onbellek.py [--isci N]    (9 theta komutlu + komutsuz, sim ev_turu/salon_turu komutlu; önbellekte olanı atlar)
Çıktı: kayitlar/sonuclar/konum/onbellek/yon_<komutlu|komutsuz>_<girdi>_<kayit>.npz"""
import argparse
import os
import sys
import time
from concurrent.futures import ProcessPoolExecutor

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import konum_ortak as ko  # noqa: E402

DURUMLAR = ["baslangic", "ok", "tut", "anahtar_mesafe", "anahtar_goreli", "anahtar_kalite", "yedek"]


def yol(komutlu, tur, ad):
    return os.path.join(ko.ONBELLEK, "yon_%s_%s_%s.npz" % ("komutlu" if komutlu else "komutsuz", tur, ad))


def yukle(komutlu, tur, ad):
    """Önbellekteki M2D iç değerleri (dict); yoksa hesaplar (yavaş: ~75 ms/kare)."""
    p = yol(komutlu, tur, ad)
    if not os.path.exists(p):
        hesapla((komutlu, tur, ad))
    z = np.load(p)
    return {k: z[k] for k in z.files}


def hesapla(arg):
    komutlu, tur, ad = arg
    p = yol(komutlu, tur, ad)
    if os.path.exists(p):
        return p
    import cv2
    cv2.setNumThreads(1)
    import yontem_pusula_nokta as ypn
    from kayit import Kayit
    from kamera import Kamera
    os.makedirs(ko.ONBELLEK, exist_ok=True)
    kay = Kayit(os.path.join(ko.KAYITLAR, tur, ad))
    kam = Kamera.bilgiden(kay.bilgi)
    bilgi = dict(kay.bilgi)
    bilgi["_kayit_klasoru"] = kay.klasor

    class Izli(ypn.Yontem):
        def _duzlem(self, a, b, merkez_der, pencere_der, N):
            r = super()._duzlem(a, b, merkez_der, pencere_der, N)
            self.son_phi = r[3]["phi"]
            self.son_ic = r[3]["icerik"]
            return r

    y = Izli(bilgi, kam, kip="duzlem", komut_oncel=bool(komutlu))
    n = len(kay)
    yon = np.zeros(n)
    phi = np.full(n, np.nan)
    ic = np.zeros(n, np.int32)
    esl = np.zeros(n, np.int32)
    kal = np.zeros(n)
    dk = np.zeros(n, np.int8)
    deg = np.zeros(n, np.int32)
    ayon = np.zeros(n)
    ms = np.zeros(n)
    t0 = time.time()
    for i in range(n):
        t = int(kay.t_ms[i])
        y.son_phi = np.nan
        y.son_ic = 0
        a = time.perf_counter()
        r = y.guncelle(kay.resim(i), t, kay.komut(t))
        ms[i] = (time.perf_counter() - a) * 1000.0
        yon[i] = r[2]
        phi[i] = y.son_phi
        ic[i] = y.son_ic
        esl[i] = y.son_bilgi.get("eslesme", 0) if y.durum != "tut" else 0
        kal[i] = y.kalite
        dk[i] = DURUMLAR.index(y.durum)
        deg[i] = y.anahtar_degisim
        ayon[i] = y.anahtar[1]
        if i % 300 == 0:
            print("  yon %s %s %s %d/%d (%.0f sn)" % ("komutlu" if komutlu else "komutsuz", tur, ad, i, n, time.time() - t0), flush=True)
    np.savez_compressed(p, kare=kay.kare_numaralari, t_ms=kay.t_ms, yon=yon, phi=phi, icerik=ic, eslesme=esl, kalite=kal,
                        durum=dk, anahtar_degisim=deg, anahtar_yon=ayon, ms=ms)
    print("bitti", p, "%.0f sn" % (time.time() - t0), flush=True)
    return p


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--isci", type=int, default=6)
    a = ap.parse_args()
    isler = []
    for ad in ["ev_turu", "salon_turu", "duz_git"] + [k for k in ko.KAYIT_ADLARI if k.startswith("yerinde")]:
        isler.append((True, "theta", ad))
        isler.append((False, "theta", ad))
    isler += [(True, "sim", "ev_turu"), (True, "sim", "salon_turu")]
    with ProcessPoolExecutor(a.isci) as ex:
        for p in ex.map(hesapla, isler):
            pass
    print("hepsi tamam")
