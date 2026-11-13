"""Görsel pusula testleri (M1 profil, M2 nokta). pytest biçimi; pytest yoksa:  python tools/konum_deney/test_pusula.py
(testleri_calistir.py yalnızca test_konum_deney'i bulur ve ortak dosya olduğu için değiştirilmedi; bu dosyanın kendi koşucusu var.)

Veri: kayitlar/sim içinden gerçek bir panorama (yoksa testler atlanır). Dönme = sütun kaydırma: robot saat yönünde theta
dönerse içerik theta*G/360 sütun SOLA kayar. Dizi, robotun 4 derece/kare (15 fps'de 60 derece/sn) dönmesi gibi verilir; komut
buna uygun (sol=255, sag=-255 nominal 60 derece/sn varsayımıyla). Sayısal sonuçlar SONUCLAR sözlüğünde."""
import os
import sys

import cv2
import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
from kamera import Kamera  # noqa: E402
from kayit import Kayit  # noqa: E402
from olc import sar  # noqa: E402
import yontem_pusula_nokta  # noqa: E402
import yontem_pusula_profil  # noqa: E402

SONUCLAR = {}
DEPO = os.path.abspath(os.path.join(KLASOR, "..", ".."))
ADIM = 4.0                      # derece/kare
DT_MS = 1000.0 / 15.0
KOMUT_DON = (255.0, -255.0)     # bilgi {} iken nominal: 60 derece/sn saat yönü
PANORAMALAR = (("yerinde_donus_salon", 100), ("yerinde_donus_yatak_odasi", 60))
import functools  # noqa: E402
YONTEMLER = (("M1", yontem_pusula_profil.Yontem),
             ("M2_yaw", functools.partial(yontem_pusula_nokta.Yontem, kip="yaw")),
             ("M2D", yontem_pusula_nokta.Yontem))


def panorama(ad, kare):
    yol = os.path.join(DEPO, "kayitlar", "sim", ad)
    if not os.path.isdir(yol):
        return None
    return Kayit(yol).resim(kare)


def don(pano, derece):
    """Saat yönünde `derece` dönen robotun gördüğü panorama (alt sütun kayması, sarmalı)."""
    H, G = pano.shape[:2]
    s = derece / 360.0 * G
    mx = ((np.arange(G, dtype=np.float32) + np.float32(s)) % G)[None, :].repeat(H, 0)
    my = np.arange(H, dtype=np.float32)[:, None].repeat(G, 1)
    gen = cv2.copyMakeBorder(pano, 0, 0, 0, 2, cv2.BORDER_WRAP)
    return cv2.remap(gen, mx, my, cv2.INTER_LINEAR, borderMode=cv2.BORDER_WRAP)


def bozucu(kare, rng, sigma=2.0, gurultu=3.0, jpeg=60):
    k = cv2.GaussianBlur(kare, (0, 0), sigma)
    k = np.clip(k.astype(np.float32) + rng.normal(0, gurultu, k.shape), 0, 255).astype(np.uint8)
    ok, b = cv2.imencode(".jpg", k, [cv2.IMWRITE_JPEG_QUALITY, jpeg])
    return cv2.imdecode(b, cv2.IMREAD_COLOR)


def kos(yontem, pano, hedefler, komutlu=True, donustur=None):
    """Hedef açı dizisini (derece, saat yönü, TOPLAM) 4 derece/kare adımlarıyla verir; her hedefte ulaşılan açı kaydedilir.
    Dönüş: [(hedef, tahmin)] ve kestirici."""
    y = yontem
    t = 0.0
    yon = 0.0
    cikti = []
    y.guncelle(donustur(pano) if donustur else pano, 0, (0.0, 0.0))
    for h in hedefler:
        while abs(h - yon) > 1e-9:
            adim = float(np.clip(h - yon, -ADIM, ADIM))
            yon += adim
            t += DT_MS
            k = (KOMUT_DON[0] * np.sign(adim), KOMUT_DON[1] * np.sign(adim))
            if abs(adim) < ADIM:                 # yarım adım: komut orantılı
                k = (k[0] * abs(adim) / ADIM, k[1] * abs(adim) / ADIM)
            im = don(pano, yon)
            r = y.guncelle(donustur(im) if donustur else im, int(round(t)), k if komutlu else (0.0, 0.0))
        cikti.append((h, r[2]))
    return cikti, y


def _yeni(sinif, **kw):
    return sinif({"kamera": {"tur": "esdikdortgen"}}, None, **kw)


def _hata(h, tahmin):
    return float(sar(tahmin - h))


def test_sinirlar_ve_sarma():
    """0,5 derece altı, 10, 45, 90, 179, 181, 270, 359,5 (>180 ve sarma). Komutlu ve komutsuz."""
    hedefler = [0.5, 10.0, 45.0, 90.0, 179.0, 181.0, 270.0, 359.5, 400.0, -30.0]
    for ad, sinif in YONTEMLER:
        for komutlu in (True, False):
            for pad, kare in PANORAMALAR:
                pano = panorama(pad, kare)
                if pano is None:
                    return
                y = _yeni(sinif, komut_oncel=komutlu)
                # 0,5 derecelik ilk adım komutla 0,5/4 oranında verilir
                sonuc, _ = kos(y, pano, hedefler, komutlu)
                hata = [abs(_hata(h, t)) for h, t in sonuc]
                SONUCLAR["%s_%s_%s_en_cok_hata" % (ad, "komutlu" if komutlu else "komutsuz", pad[-8:])] = round(max(hata), 3)
                assert max(hata) < 1.0, (ad, komutlu, pad, list(zip(hedefler, hata)))


def test_alt_derece():
    """Tek 0,3 ve 0,7 derecelik küçük dönüşler (komut dönüyor derken): hata < 0,15 derece."""
    for ad, sinif in YONTEMLER:
        pano = panorama(*PANORAMALAR[0])
        if pano is None:
            return
        for kucuk in (0.3, 0.7, -0.45):
            y = _yeni(sinif, komut_oncel=True)
            y.guncelle(pano, 0, (0.0, 0.0))
            r = y.guncelle(don(pano, kucuk), 67, (30.0, -30.0))
            e = abs(_hata(kucuk, r[2]))
            SONUCLAR["%s_alt_derece_%+.2f_hata" % (ad, kucuk)] = round(e, 3)
            assert e < 0.15, (ad, kucuk, e)


def test_durgun_tutar():
    """Komut sıfır, görüntü yalnızca gürültü: yaw hiç oynamamalı (tam sabit)."""
    rng = np.random.RandomState(3)
    for ad, sinif in YONTEMLER:
        pano = panorama(*PANORAMALAR[0])
        if pano is None:
            return
        y = _yeni(sinif)
        y.guncelle(bozucu(pano, rng), 0, (0.0, 0.0))
        son = None
        for i in range(1, 40):
            son = y.guncelle(bozucu(pano, rng), int(i * DT_MS), (0.0, 0.0))[2]
            assert son == 0.0, (ad, i, son)
        assert y.anahtar_degisim == 0


def test_cok_tur_hata_buyumez():
    """1, 3 ve 6 tam tur saat yönünde dönüp başlangıca (0) gelinir: hata tur sayısıyla artmamalı."""
    for ad, sinif in YONTEMLER:
        for komutlu in (True, False):
            pano = panorama(*PANORAMALAR[0])
            if pano is None:
                return
            hatalar = {}
            for tur in (1, 3, 6):
                y = _yeni(sinif, komut_oncel=komutlu)
                hedefler = [360.0 * tur]
                sonuc, y = kos(y, pano, hedefler, komutlu)
                hatalar[tur] = abs(_hata(0.0, sonuc[-1][1]))
                if tur == 6:
                    SONUCLAR["%s_%s_anahtar_degisim_6tur" % (ad, "komutlu" if komutlu else "komutsuz")] = y.anahtar_degisim
            SONUCLAR["%s_%s_tur_hatasi_1_3_6" % (ad, "komutlu" if komutlu else "komutsuz")] = {k: round(v, 3) for k, v in hatalar.items()}
            assert hatalar[6] < 1.0, (ad, komutlu, hatalar)
            assert hatalar[6] <= hatalar[1] + 0.5, (ad, komutlu, hatalar)


def test_bulanik_gurultulu():
    """Gauss bulanıklığı (sigma 2 piksel), gürültü (sigma 3) ve JPEG 60 eklenmiş dönüş dizisi, 270 ve -135 derece."""
    rng = np.random.RandomState(5)
    for ad, sinif in YONTEMLER:
        pano = panorama(*PANORAMALAR[1])
        if pano is None:
            return
        y = _yeni(sinif)
        sonuc, _ = kos(y, pano, [270.0, -135.0], True, donustur=lambda im: bozucu(im, rng))
        hata = [abs(_hata(h, t)) for h, t in sonuc]
        SONUCLAR["%s_bulanik_gurultulu_hata" % ad] = [round(x, 3) for x in hata]
        assert max(hata) < 1.5, (ad, hata)


def test_theta_girdisi():
    """Panorama çift balık gözüne çevrilip (Theta biçimi) verilir: dönüşüm yolu dahil, 90 ve 200 derece."""
    kam = Kamera()
    bilgi = {"kamera": {"tur": "cift_balikgozu"}}
    for ad, sinif in YONTEMLER:
        pano = panorama(*PANORAMALAR[0])
        if pano is None:
            return
        y = sinif(bilgi, kam)
        sonuc, _ = kos(y, pano, [90.0, 200.0], True, donustur=lambda im: kam.esdikdortgen_to_balikgozu(im)[0])
        hata = [abs(_hata(h, t)) for h, t in sonuc]
        SONUCLAR["%s_theta_girdisi_hata" % ad] = [round(x, 3) for x in hata]
        assert max(hata) < 1.5, (ad, hata)


def test_temizlik():
    pass


if __name__ == "__main__":
    import time
    import traceback
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    adlar = [a for a in sorted(globals()) if a.startswith("test_") and a != "test_temizlik"]
    basarisiz = 0
    for ad in adlar:
        a = time.time()
        try:
            globals()[ad]()
            print("GEÇTİ  %-35s %.1f sn" % (ad, time.time() - a))
        except Exception:
            basarisiz += 1
            print("KALDI  %s" % ad)
            traceback.print_exc()
    print("\nSayısal sonuçlar:")
    for k, v in SONUCLAR.items():
        print("  %s = %s" % (k, v))
    print("\n%d test, %d başarısız" % (len(adlar), basarisiz))
    sys.exit(1 if basarisiz else 0)
