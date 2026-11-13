"""Konum + harita (kamera tabanlı SLAM) testleri: poz bileşimi, zemin geometrisi, BEV ötelemesi, tarama-harita eşleştirme.
pytest biçimi; pytest yoksa:  python tools/konum_deney/test_konum.py   (kendi koşucusu var, sayısal sonuçlar sonda).
Veri gerektirmeyen testler sentetik üretimle çalışır; kayıt verisi gerektirenler kayıt yoksa atlanır."""
import os
import sys

import cv2
import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
import konum_ortak as ko  # noqa: E402
import konum_bev as kb  # noqa: E402
import konum_tarama as kt  # noqa: E402
import harita_kur as hk  # noqa: E402
from kamera import isin_esd_piksel, esd_isin_izgarasi  # noqa: E402

SONUCLAR = {}


# ------------------------------------------------------------------ poz sözleşmeleri
def test_poz_sozlesmesi():
    """yaw kuzeyden SAAT YÖNÜNDE: yaw 90 = doğu. ileri/sağ vektörleri ve dünya<->robot gidiş-dönüşü."""
    assert np.allclose(ko.ileri_vek(0), (0, 1)) and np.allclose(ko.ileri_vek(90), (1, 0), atol=1e-12)
    assert np.allclose(ko.sag_vek(0), (1, 0)) and np.allclose(ko.sag_vek(90), (0, -1), atol=1e-12)
    x, y = ko.robot_to_dunya((1.0, 2.0, 90.0), 0.0, 1.0)          # yaw 90 (doğuya bakıyor), 1 m ileri -> doğuya
    assert abs(x - 2.0) < 1e-9 and abs(y - 2.0) < 1e-9
    x, y = ko.robot_to_dunya((1.0, 2.0, 90.0), 1.0, 0.0)          # 1 m sağ -> güneye
    assert abs(x - 1.0) < 1e-9 and abs(y - 1.0) < 1e-9
    rng = np.random.RandomState(1)
    for _ in range(20):
        poz = (rng.randn(), rng.randn(), rng.rand() * 360)
        sag, ileri = rng.randn(5), rng.randn(5)
        X, Y = ko.robot_to_dunya(poz, sag, ileri)
        s2, i2 = ko.dunya_to_robot(poz, X, Y)
        assert np.allclose(sag, s2) and np.allclose(ileri, i2)
    p = ko.poz_ekle((0, 0, 0), 0.0, 1.0, 30.0)                    # 1 m ileri (kuzeye) sonra 30 derece saat yönünde
    assert abs(p[0]) < 1e-9 and abs(p[1] - 1.0) < 1e-9 and abs(p[2] - 30.0) < 1e-9


def test_kamera_dönme_merkezinin_arkasinda():
    """Kamera dönme merkezinin 7 cm ARKASINDA: yerinde 90 derece saat yönünde dönüşte kamera merkez çevresinde daire çizer."""
    poz = (0.0, 0.0, 0.0)                                         # kuzeye bakıyor; merkez = (0, 0.07)
    p = ko.merkez_ilerle(poz, 0.0, 90.0)                          # yaw 90 (doğu): kamera merkezin 7 cm batısında
    assert abs(p[0] - (-0.07)) < 2e-3 and abs(p[1] - 0.07) < 2e-3 and abs(p[2] - 90.0) < 1e-9
    # 360 derece dönüş = başlangıca dönüş, merkez sabit
    p = poz
    for _ in range(90):
        p = ko.merkez_ilerle(p, 0.0, 4.0)
    assert abs(p[0]) < 1e-9 and abs(p[1]) < 1e-9 and abs(ko.sar(p[2])) < 1e-9
    # düz ilerleme: merkez ve kamera aynı miktar gider
    p = ko.merkez_ilerle((1.0, 1.0, 90.0), 0.5, 0.0)
    assert abs(p[0] - 1.5) < 1e-9 and abs(p[1] - 1.0) < 1e-9
    SONUCLAR["donme_merkezi_90_derece_kamera_ofseti_m"] = (round(p[0], 3), round(p[1], 3))


# ------------------------------------------------------------------ zemin geometrisi
def test_zemin_geometrisi_gidis_donus():
    """piksel -> zemin noktası (r = h/tan a) -> piksel gidiş-dönüşü; BEV tablosu ile aynı sözleşme."""
    G, Y, h = 1536, 768, ko.KAM_H
    rng = np.random.RandomState(0)
    u = rng.uniform(0, G - 1, 400)
    v = rng.uniform(Y / 2 + 8, Y - 1, 400)                        # ufkun altı
    az = np.radians((u + 0.5) / G * 360 - 180)
    ald = np.radians((v + 0.5) / Y * 180 - 90)                    # ufkun altındaki açı
    r = h / np.tan(ald)
    sag, ileri = r * np.sin(az), r * np.cos(az)                   # zemin noktası (kamera çerçevesi)
    # geri: nokta -> ışın -> piksel
    r2 = np.hypot(sag, ileri)
    a2 = np.arctan2(h, r2)
    d = np.stack([np.cos(a2) * sag / r2, -np.sin(a2), np.cos(a2) * ileri / r2], -1)
    u2, v2 = isin_esd_piksel(d, G, Y)
    assert np.abs(u2 - u).max() < 1e-3 or np.abs(((u2 - u + G / 2) % G) - G / 2).max() < 1e-3
    assert np.abs(v2 - v).max() < 1e-3
    # BEV tablosu: bir BEV pikselinin başvurduğu eşdikdörtgen pikseli aynı noktayı göstermeli
    tab = kb.BevTablo("sim", None, h=h, res=0.02)
    i, j = 40, 60                                                  # (sütun, satır)
    ps, pi = tab.x[j, i], tab.y[j, i]
    az = np.arctan2(ps, pi)
    ald = np.arctan2(h, np.hypot(ps, pi))
    u_b = (np.degrees(az) + 180) / 360 * 1536 - 0.5
    v_b = (90 + np.degrees(ald)) / 180 * 768 - 0.5
    assert abs(tab.u[j, i] - u_b) < 1e-2 and abs(tab.v[j, i] - v_b) < 1e-2
    SONUCLAR["zemin_3m_icin_ufuk_alti_derece"] = round(float(np.degrees(np.arctan2(h, 3.0))), 2)


def _zemin_pano(poz, doku, G=1536, Y=768, h=ko.KAM_H, doku_m=0.01):
    """Düz zemin üstü doku olan sahneyi, kamera pozundan (x,y,yaw) eşdikdörtgen kare olarak çizer (ufkun üstü gri)."""
    d = esd_isin_izgarasi(G, Y)
    ald = -d[..., 1]                                               # aşağı bakan ışın bileşeni
    asagi = ald > 0.02
    t = np.where(asagi, h / np.maximum(ald, 1e-6), 0.0)            # ışın parametresi (zemine ulaşana kadar)
    sag, ileri = t * d[..., 0], t * d[..., 2]                      # kamera çerçevesinde zemin noktası
    X, Yd = ko.robot_to_dunya(poz, sag, ileri)
    mx = (X / doku_m + doku.shape[1] / 2).astype(np.float32)
    my = (doku.shape[0] / 2 - Yd / doku_m).astype(np.float32)
    im = cv2.remap(doku, mx, my, cv2.INTER_LINEAR, borderMode=cv2.BORDER_REFLECT)
    im = np.where(asagi, im, 128).astype(np.uint8)
    return cv2.cvtColor(im, cv2.COLOR_GRAY2BGR)


def _doku(n=1400, tohum=3):
    rng = np.random.RandomState(tohum)
    a = rng.rand(n, n).astype(np.float32)
    a = cv2.GaussianBlur(a, (0, 0), 3.0) * 3 + cv2.GaussianBlur(rng.rand(n, n).astype(np.float32), (0, 0), 1.2)
    a = (a - a.min()) / (a.max() - a.min()) * 255
    return a.astype(np.uint8)


def test_bev_otelemesi_sentetik_zemin():
    """Bilinen kamera hareketi (öteleme + 8 derece yaw) -> BEV öteleme artımı doğru metre ve doğru eksen işaretiyle bulunmalı."""
    doku = _doku()
    tab = kb.BevTablo("sim", None, h=ko.KAM_H, res=0.02)
    oz = kb.BevOtelemeci(tab, kapi_m=0.30)
    poz0 = (0.0, 0.0, 20.0)
    oz.guncelle(_zemin_pano(poz0, doku), poz0[2])
    # üç adım: her adım önceki çerçevede (sag, ileri) öteleme + yaw
    adimlar = [(0.00, 0.04, 2.0), (0.01, 0.05, 3.0), (-0.02, 0.06, 3.0), (0.00, 0.03, 0.0)]
    poz = poz0
    hatalar = []
    for ds, di, dy in adimlar:
        poz = ko.poz_ekle(poz, ds, di, dy)
        okuma = oz.guncelle(_zemin_pano(poz, doku), poz[2])
        assert okuma[2], "öteleme bulunamadı"
        hatalar.append(np.hypot(okuma[0] - ds, okuma[1] - di))
    SONUCLAR["bev_sentetik_adim_hatasi_cm"] = [round(100 * e, 2) for e in hatalar]
    assert max(hatalar) < 0.012, hatalar                            # 1,2 cm (2 cm/piksel BEV, eşleşme ortalaması)


# ------------------------------------------------------------------ tarama -> harita
def _oda_isin(poz, kutu=(-3.0, 3.0, -2.0, 2.0), sutun=(0.8, 0.4, 0.6, 0.6), n=360):
    """Dikdörtgen oda + kare sütun için 360 mesafe (azimut k = robot önünden saat yönünde k derece)."""
    x0, x1, y0, y1 = kutu
    cx, cy, w, hh = sutun[0], sutun[1], sutun[2] / 2, sutun[3] / 2
    mes = np.full(n, np.inf)
    for k in range(n):
        a = np.radians(poz[2] + k)
        dx, dy = np.sin(a), np.cos(a)
        en = np.inf
        for t_ in ((x1 - poz[0]) / dx if dx > 1e-9 else None, (x0 - poz[0]) / dx if dx < -1e-9 else None,
                   (y1 - poz[1]) / dy if dy > 1e-9 else None, (y0 - poz[1]) / dy if dy < -1e-9 else None):
            if t_ is not None and t_ > 0:
                en = min(en, t_)
        # sütun (eksen hizalı kutu) ışın-kutu kesişimi
        tmin, tmax = 0.0, en
        for o, d_, lo, hi in ((poz[0], dx, cx - w, cx + w), (poz[1], dy, cy - hh, cy + hh)):
            if abs(d_) < 1e-12:
                if o < lo or o > hi:
                    tmin, tmax = 1.0, 0.0
            else:
                ta, tb = (lo - o) / d_, (hi - o) / d_
                tmin, tmax = max(tmin, min(ta, tb)), min(tmax, max(ta, tb))
        if tmin <= tmax and tmin > 0:
            en = min(en, tmin)
        mes[k] = en
    return mes


def test_tarama_harita_ofset_geri_kazanimi():
    """Bilinen pozlardan kurulan sentetik oda haritasına, 0,2 m ve 3 derece yanlış verilen bir poz eşlenmeli: ofset geri kazanılır."""
    iz = hk.Izgara(-5.0, -4.0, 200, 160, 0.05)
    gp = [(-1.0, -0.5, 10.0), (0.5, 0.8, 100.0), (-1.5, 1.0, 200.0), (1.5, -1.0, 300.0), (0.0, 0.0, 45.0), (-2.0, -1.0, 0.0)]
    for p in gp:
        iz.tara_ekle(p[0], p[1], p[2], _oda_isin(p), np.ones(360), 4.0)
    gercek = (0.3, 0.2, 20.0)
    mes = _oda_isin(gercek)
    par = kt.EsleParam(gecikme_tarama=0, min_dolu_hucre=10, ay_xy=0.30, ay_yaw=5.0, oncul_agirlik=0.0, menzil=4.0)
    sonuclar = []
    for ofs in ((0.20, -0.15, 3.0), (-0.12, 0.2, -2.0), (0.0, 0.25, 0.0)):
        tahmin = (gercek[0] + ofs[0], gercek[1] + ofs[1], gercek[2] + ofs[2])
        s = kt.eslestir(iz, tahmin, mes, np.ones(360), 4.0, par)
        assert s["durum"] == "ok", s
        px, py, pyaw = s["poz"]
        e = (np.hypot(px - gercek[0], py - gercek[1]), abs(float(ko.sar(pyaw - gercek[2]))))
        sonuclar.append((round(100 * e[0], 2), round(e[1], 2)))
        assert e[0] < 0.04 and e[1] < 1.0, (ofs, e)
    SONUCLAR["tarama_eslestirme_hata_(cm,derece)"] = sonuclar


def test_tarama_esleme_koridorda_oncul_kazanir():
    """Eksen boyunca belirsiz (düz koridor) taramada öncül devreye girer: ofset, belirsiz eksende tahminin etrafında kalır (uçmaz)."""
    iz = hk.Izgara(-5.0, -3.0, 200, 120, 0.05)
    kutu = (-4.0, 4.0, -0.6, 0.6)
    for p in [(-2.0, 0.0, 90.0), (-1.0, 0.0, 90.0), (0.0, 0.0, 90.0), (1.0, 0.0, 90.0), (2.0, 0.0, 90.0)]:
        iz.tara_ekle(p[0], p[1], p[2], _oda_isin(p, kutu, sutun=(100, 100, 0.1, 0.1)), np.ones(360), 4.0)
    mes = _oda_isin((0.5, 0.0, 90.0), kutu, sutun=(100, 100, 0.1, 0.1))
    par = kt.EsleParam(min_dolu_hucre=10, menzil=3.2)
    s = kt.eslestir(iz, (0.5, 0.0, 90.0), mes, np.ones(360), 4.0, par)
    assert s["durum"] == "ok"
    assert abs(s["d"][0]) < 0.06 and abs(s["d"][1]) < 0.06, s["d"]       # kuvvetli öncül: tahmin etrafında


def test_gerceke_yakin_kayitta_bev():
    """(Veri varsa) theta duz_git ilk 120 karede BEV ileri toplamı gerçeğin %8'i içinde (hızlı bakış)."""
    from kayit import Kayit
    from kamera import Kamera
    yol = os.path.join(ko.KAYITLAR, "theta", "duz_git")
    if not os.path.isdir(yol):
        return
    kay = Kayit(yol)
    gk = kay.gercek_konum()
    import konum_yon_onbellek as yo
    yz = yo.yukle(False, "theta", "duz_git")
    kam = Kamera.bilgiden(kay.bilgi)
    oz = kb.BevOtelemeci(kb.BevTablo("theta", kam))
    s_b = s_g = 0.0
    for i in range(0, 220):
        ds, di, ok, ic = oz.guncelle(kay.resim(i), float(yz["yon"][i]))
        if i > 0:
            a = (gk["x_m"][i - 1], gk["y_m"][i - 1], gk["yon_derece"][i - 1])
            s_g += ko.dunya_to_robot(a, gk["x_m"][i], gk["y_m"][i])[1]
            s_b += di if np.isfinite(di) else 0.0
    SONUCLAR["bev_duz_git_ilk_220_kare_ileri_m_(bev,gercek)"] = (round(s_b, 2), round(float(s_g), 2))
    assert s_g > 1.0 and abs(s_b - s_g) < 0.08 * s_g, (s_b, s_g)


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
            print("GEÇTİ  %-45s %.1f sn" % (ad, time.time() - a))
        except Exception:
            basarisiz += 1
            print("KALDI  %s" % ad)
            traceback.print_exc()
    print("\nSayısal sonuçlar:")
    for k, v in SONUCLAR.items():
        print("  %s = %s" % (k, v))
    print("\n%d test, %d başarısız" % (len(adlar), basarisiz))
    sys.exit(1 if basarisiz else 0)
