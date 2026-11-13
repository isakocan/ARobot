"""Testler (pytest biçimi; pytest yoksa testleri_calistir.py ile çalışır).  Sayısal sonuçlar SONUCLAR sözlüğünde toplanır."""
import os
import shutil
import sys
import tempfile

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import calistir  # noqa: E402
import olc  # noqa: E402
import ornek_kayit_uret as ok  # noqa: E402
import theta_benzet  # noqa: E402
from kamera import Kamera, esd_isin_izgarasi, isin_esd_piksel  # noqa: E402
from kayit import Kayit, KayitYazici  # noqa: E402

SONUCLAR = {}
_GECICI = {}


def _klasor():
    if "k" not in _GECICI:
        _GECICI["k"] = tempfile.mkdtemp(prefix="konum_deney_")
    return _GECICI["k"]


def ornek(varyant="A"):
    """Küçük sentetik kayıt (önbellekli)."""
    ad = "ornek_" + varyant
    yol = os.path.join(_klasor(), ad)
    if not os.path.exists(yol):
        ok.uret(yol, 75, varyant, 400, 200)
    return yol


def psnr(a, b, maske=None):
    a = a.astype(np.float64)
    b = b.astype(np.float64)
    d = (a - b) ** 2
    if d.ndim == 3:
        d = d.mean(axis=2)
    if maske is not None:
        d = d[maske]
    mse = d.mean()
    return 99.0 if mse < 1e-9 else float(10 * np.log10(255.0 ** 2 / mse))


# ----------------------------------------------------------------------------- okuyucu
def test_okuyucu_gidis_donus():
    for varyant in ("A", "B"):
        yol = os.path.join(_klasor(), "rt_" + varyant)
        rng = np.random.RandomState(1)
        kareler = [cv2.GaussianBlur(rng.randint(0, 255, (60, 80, 3)).astype(np.uint8), (0, 0), 2) for _ in range(6)]
        t = [0, 66, 133, 200, 266, 333]
        with KayitYazici(yol, {"kaynak": "sim", "ad": "rt", "kamera": {"tur": "esdikdortgen", "genislik": 80, "yukseklik": 60,
                                                                       "fps": 15}}, varyant) as yz:
            for ti, k in zip(t, kareler):
                yz.kare_ekle(ti, k, 95)
            yz.komutlar_yaz([0, 100, 250], [255, 0, -255], [255, 0, 255])
            yz.gercek_konum_yaz(range(6), t, np.arange(6) * 0.1, np.zeros(6), np.array([0, 10, 20, 30, 40, 370.0]))
        k = Kayit(yol)
        assert len(k) == 6 and list(k.t_ms) == t, varyant
        for i, (n, ti, im) in enumerate(k.kareler()):
            assert n == i and ti == t[i]
            assert psnr(im, kareler[i]) > 30, "JPEG gidiş-dönüş çok bozuk"
        assert k.komut(-5) == (0.0, 0.0) and k.komut(0) == (255.0, 255.0)
        assert k.komut(99) == (255.0, 255.0) and k.komut(100) == (0.0, 0.0) and k.komut(10 ** 6) == (-255.0, 255.0)
        g = k.gercek_konum()
        assert abs(g["yon_derece"][5] - 10.0) < 1e-3 and abs(g["x_m"][3] - 0.3) < 1e-4
        assert k.bilgi["bicim"] == 1
    SONUCLAR["okuyucu"] = "A ve B gidiş-dönüş tamam"


def test_okuyucu_ek_sutun_ve_ofsetsiz_mjpg():
    yol = os.path.join(_klasor(), "ek")
    with KayitYazici(yol, {"kaynak": "robot", "kamera": {"tur": "cift_balikgozu", "fps": 15}}, "B") as yz:
        for i in range(3):
            yz.kare_ekle(i * 66, np.full((20, 20, 3), 40 * i, np.uint8))
    # ofset/bayt sütunlarını sil: okuyucu JPEG imlerinden bölmeli; komutlara fazladan sütun ekle
    with open(os.path.join(yol, "kareler.csv"), "w") as f:
        f.write("kare,t_ms\n0,0\n1,66\n2,133\n")
    with open(os.path.join(yol, "komutlar.csv"), "w") as f:
        f.write("t_ms,sol,sag,akim\n0,10,20,0.3\n")
    k = Kayit(yol)
    assert len(k) == 3 and k.komut(5) == (10.0, 20.0)
    assert abs(float(k.resim(2).mean()) - 80) < 3


# ----------------------------------------------------------------------------- kamera
def test_kamera_geometri():
    kam = Kamera()
    d0 = kam.merkez_isini(0)
    assert np.allclose(d0, [0, 0, 1], atol=1e-9), d0        # lens 1 merkezi = robot ilerisi (yon=0 varsayımı)
    # ileri-aşağı ışın: görüntüde SAĞA düşmeli; sağa ışın: görüntüde YUKARI
    u, v, _ = kam.robot_to_lens(np.array([[0, -1.0, 0]]), 0)
    L = kam.lens[0]
    assert abs(u[0] - (L["merkez_x"] + L["f"] * np.pi / 2)) < 1e-6 and abs(v[0] - L["merkez_y"]) < 1e-6
    u, v, _ = kam.robot_to_lens(np.array([[1.0, 0, 0]]), 0)
    assert abs(u[0] - L["merkez_x"]) < 1e-6 and abs(v[0] - (L["merkez_y"] - L["f"] * np.pi / 2)) < 1e-6
    # eşdikdörtgen: ileri = orta sütun; sağ = 3/4 genişlik; geri = kenar; üst = en üst satır
    G, Y = 1000, 500
    u, v = isin_esd_piksel(np.array([[0, 0, 1.0]]), G, Y)
    assert abs(u[0] - (G / 2 - 0.5)) < 1e-9 and abs(v[0] - (Y / 2 - 0.5)) < 1e-9
    u, _ = isin_esd_piksel(np.array([[1.0, 0, 0]]), G, Y)
    assert abs(u[0] - (0.75 * G - 0.5)) < 1e-9
    u, _ = isin_esd_piksel(np.array([[-1.0, 0, 0]]), G, Y)
    assert abs(u[0] - (0.25 * G - 0.5)) < 1e-9
    _, v = isin_esd_piksel(np.array([[0, 1.0, 0]]), G, Y)
    assert v[0] < 0
    # lens 2 merkezi robotun arkasına (+-1 derece) bakar
    d1 = kam.merkez_isini(1)
    assert d1[2] < -0.99
    # piksel -> ışın -> piksel
    for lens in (0, 1):
        Lk = kam.lens[lens]
        uu = np.array([Lk["merkez_x"] + 120.0, Lk["merkez_x"] - 200.0, Lk["merkez_x"] + 10.0])
        vv = np.array([Lk["merkez_y"] - 80.0, Lk["merkez_y"] + 150.0, Lk["merkez_y"] + 5.0])
        d, gecerli = kam.piksel_to_isin(uu, vv, lens)
        assert gecerli.all() and np.allclose(np.linalg.norm(d, axis=-1), 1.0)
        u2, v2, _ = kam.robot_to_lens(d, lens)
        assert np.abs(u2 - uu).max() < 1e-6 and np.abs(v2 - vv).max() < 1e-6
    SONUCLAR["geometri"] = "tamam"


def _duzgun_panorama(G=1024, Y=512):
    """Pürüzsüz ama yönlü bir eşdikdörtgen (kürede tanımlı: yön vektörünün yumuşak fonksiyonu)."""
    d = esd_isin_izgarasi(G, Y)
    r = (128 + 90 * np.sin(3 * d[..., 0] + 2 * d[..., 1]) + 30 * np.cos(5 * d[..., 2] - d[..., 0] * 2)).clip(0, 255)
    g = (128 + 90 * np.sin(4 * d[..., 1] - 2 * d[..., 2])).clip(0, 255)
    b = (128 + 90 * np.cos(3 * d[..., 2] + 3 * d[..., 0] * d[..., 1])).clip(0, 255)
    return np.stack([b, g, r], -1).astype(np.uint8)


def test_kamera_gidis_donus_psnr():
    kam = Kamera()
    G, Y = 1024, 512
    pano = _duzgun_panorama(G, Y)
    balik, daire = kam.esdikdortgen_to_balikgozu(pano)
    assert balik.shape == (720, 1280, 3) and daire.sum() > 300000
    assert balik[0, 0].sum() == 0 and balik[719, 1279].sum() == 0
    geri, kapsam = kam.balikgozu_to_esdikdortgen(balik, G, Y)
    d = esd_isin_izgarasi(G, Y).reshape(-1, 3)
    w1, w2 = kam.agirliklar(d)
    w1 = w1.reshape(Y, G)
    w2 = w2.reshape(Y, G)
    iki = (w1 > 0) & (w2 > 0)                       # iki lensin de gördüğü karışım bandı
    tum = kapsam
    SONUCLAR["psnr_pürüzsüz_tum_kure_db"] = round(psnr(geri, pano, tum), 2)
    SONUCLAR["psnr_pürüzsüz_ortak_bant_db"] = round(psnr(geri, pano, iki), 2)
    SONUCLAR["kapsam_kure_yuzde"] = round(100 * float(tum.mean()), 2)
    SONUCLAR["ortak_bant_kure_yuzde"] = round(100 * float(iki.mean()), 2)
    assert tum.mean() > 0.99
    assert psnr(geri, pano, tum) > 35, SONUCLAR
    assert psnr(geri, pano, iki) > 30, SONUCLAR
    # gövde maskesi dışı: maskeyi eşdikdörtgene çevirip dışarıda bırak
    m = theta_benzet.govde_maskesi()
    me, _ = kam.balikgozu_to_esdikdortgen(m, G, Y)
    dis = tum & (cv2.dilate((me > 10).astype(np.uint8), np.ones((9, 9), np.uint8)) == 0)
    balik_m = theta_benzet.govde_uygula(balik, m, [28, 26, 26], np.random.default_rng(0))
    geri_m, _ = kam.balikgozu_to_esdikdortgen(balik_m, G, Y)
    SONUCLAR["psnr_pürüzsüz_govde_disi_db"] = round(psnr(geri_m, pano, dis), 2)
    SONUCLAR["govde_disi_kure_yuzde"] = round(100 * float(dis.mean()), 2)
    SONUCLAR["govde_maskeli_kure_yuzde"] = round(100 * float((~dis & tum).mean()), 2)
    assert psnr(geri_m, pano, dis) > 33
    # dokulu oda karesi (keskin kenarlar): daha düşük PSNR beklenir (kaynak çözünürlüğü sınırı)
    kare = ok.OdaCizici(1024, 512).ciz(1.0, 1.5, 40)
    b2, _ = kam.esdikdortgen_to_balikgozu(kare, cv2.INTER_CUBIC)
    g2, _ = kam.balikgozu_to_esdikdortgen(b2, 1024, 512, cv2.INTER_CUBIC)
    SONUCLAR["psnr_dokulu_oda_tum_db"] = round(psnr(g2, kare, tum), 2)
    SONUCLAR["psnr_dokulu_oda_govde_disi_db"] = round(psnr(g2, kare, dis), 2)
    assert psnr(g2, kare, tum) > 22


def test_yaw_sozlesmesi():
    """Robot saat yönünde 90 derece dönünce görüntü W/4 SOLA kayar (yön 90'daki kare = yön 0'daki karenin sola kaymışı)."""
    c = ok.OdaCizici(480, 240)
    a = c.ciz(2.0, 1.5, 0.0)
    b = c.ciz(2.0, 1.5, 90.0)
    W = 480
    e_sol = np.abs(b.astype(int) - np.roll(a, -W // 4, axis=1).astype(int)).mean()
    e_sag = np.abs(b.astype(int) - np.roll(a, W // 4, axis=1).astype(int)).mean()
    SONUCLAR["yaw90_sola_kayma_ort_hata"] = round(float(e_sol), 3)
    SONUCLAR["yaw90_saga_kayma_ort_hata"] = round(float(e_sag), 3)
    assert e_sol < 1.5 and e_sag > 10 * max(e_sol, 0.1)


# ----------------------------------------------------------------------------- ölçüt
def _olc(gercek, tahmin_x, tahmin_y, tahmin_yon):
    n = len(gercek["yon_derece"])
    g = dict(gercek)
    g["kare"] = np.arange(n)
    t = {"kare": np.arange(n), "t_ms": g["t_ms"], "x_m": tahmin_x, "y_m": tahmin_y, "yon_derece": tahmin_yon}
    h = olc.hizala(t, g)
    return olc.olc_hesapla(h)


def _gercek_yol(n=101):
    t = np.arange(n) * 100
    yon = np.linspace(0, 360, n)                   # tam bir tur
    x = np.linspace(0, 5, n)
    return {"t_ms": t, "x_m": x, "y_m": np.zeros(n), "yon_derece": yon % 360.0}


def test_olc_bilinen_degerler():
    g = _gercek_yol()
    # tam doğru
    s = _olc(g, g["x_m"], g["y_m"], g["yon_derece"])
    assert s["yaw_maks_derece"] < 1e-9 and s["konum_maks_m"] < 1e-9
    # doğrusal sürüklenme: tur sonunda +4 derece -> 4 derece / 360
    est = (g["yon_derece"] + np.linspace(0, 4, 101)) % 360.0
    s = _olc(g, g["x_m"], g["y_m"], est)
    assert abs(s["yaw_son_derece"] - 4.0) < 1e-6 and abs(s["yaw_maks_derece"] - 4.0) < 1e-6
    assert abs(s["yaw_ort_mutlak_derece"] - 2.0) < 1e-6
    assert abs(s["yaw_surukleme_derece_360_basina"] - 4.0) < 1e-6, s
    # sabit ofset hizalamayla silinir (başlangıç gerçek konuma oturur)
    s = _olc(g, g["x_m"], g["y_m"], (g["yon_derece"] + 37.0) % 360.0)
    assert s["yaw_maks_derece"] < 1e-9
    # 0/360 sarması: gerçek 358 -> 2, tahmin 359 -> 3 (fark 1 derece, 358 değil)
    gg = {"t_ms": np.arange(5) * 100, "x_m": np.zeros(5), "y_m": np.zeros(5), "yon_derece": np.array([356, 358, 0, 2, 4.0])}
    s = _olc(gg, np.zeros(5), np.zeros(5), np.array([356, 359, 1.0, 3.0, 5.0]))
    assert abs(s["yaw_maks_derece"] - 1.0) < 1e-6, s["yaw_maks_derece"]
    # uç nokta hatası: yol 5 m düz, tahmin sonda 0,5 m geride -> 0,5 m ve %10
    gl = {"t_ms": np.arange(11) * 100, "x_m": np.linspace(0, 5, 11), "y_m": np.zeros(11), "yon_derece": np.full(11, 90.0)}
    tx = gl["x_m"].copy()
    tx[-1] -= 0.5
    s = _olc(gl, tx, np.zeros(11), np.full(11, 90.0))
    assert abs(s["konum_son_m"] - 0.5) < 1e-9 and abs(s["konum_son_yuzde_mesafe"] - 10.0) < 1e-6
    assert abs(s["konum_maks_m"] - 0.5) < 1e-9 and abs(s["gidilen_mesafe_m"] - 5.0) < 1e-9
    # yalnız yaw veren yöntem
    s = _olc(gl, np.full(11, np.nan), np.full(11, np.nan), np.full(11, 90.0))
    assert "konum_rmse_m" not in s and "yaw_ort_mutlak_derece" in s
    # konum hizalaması yaw farkı kadar döndürür: tahmin çerçevesi 90 derece dönükse yol yine örtüşmeli
    est_x = np.zeros(11)
    est_y = np.linspace(0, 5, 11)                   # yöntem "kuzey" sanıyor, gerçek doğuya gidiyor
    s = _olc(gl, est_x, est_y, np.full(11, 0.0))    # yöntem yaw 0 diyor, gerçek 90 -> hizalama 90 derece döndürür
    assert s["konum_maks_m"] < 1e-9 and s["yaw_maks_derece"] < 1e-9
    SONUCLAR["olc"] = "bilinen değerler doğru"


# ----------------------------------------------------------------------------- yöntemler
def test_yontem_gercek_sifir_hata():
    sonuc, zaman, _ = calistir.calistir(ornek("A"), "yontem_gercek", os.path.join(_klasor(), "c_gercek"), sessiz=True)
    SONUCLAR["yontem_gercek_yaw_maks"] = sonuc["yaw_maks_derece"]
    SONUCLAR["yontem_gercek_konum_maks"] = sonuc["konum_maks_m"]
    assert sonuc["yaw_maks_derece"] < 1e-3 and sonuc["konum_maks_m"] < 1e-4
    assert os.path.exists(os.path.join(_klasor(), "c_gercek", "grafik.png"))
    assert os.path.exists(os.path.join(_klasor(), "c_gercek", "sonuc.json"))


def test_yontem_komut_sifirdan_buyuk_hata():
    sonuc, zaman, _ = calistir.calistir(ornek("B"), "yontem_komut", os.path.join(_klasor(), "c_komut"), sessiz=True)
    SONUCLAR["yontem_komut_yaw_son"] = round(sonuc["yaw_son_derece"], 2)
    SONUCLAR["yontem_komut_konum_son_m"] = round(sonuc["konum_son_m"], 3)
    SONUCLAR["yontem_komut_konum_rmse_m"] = round(sonuc["konum_rmse_m"], 3)
    SONUCLAR["yontem_komut_sure_ms_ort"] = round(zaman["ort_ms"], 3)
    assert sonuc["konum_son_m"] > 0.02 and sonuc["yaw_maks_derece"] > 0.5
    assert sonuc["konum_son_m"] < 1.0                # ama makul: nominal değerler gerçeğe yakın


# ----------------------------------------------------------------------------- benzetim
def test_theta_benzet_duman():
    cikti = os.path.join(_klasor(), "theta")
    boyutlar = theta_benzet.benzet(ornek("A"), cikti, "B", max_kare=6, hareket=True, sessiz=True)
    k = Kayit(cikti)
    assert k.bilgi["kamera"]["tur"] == "cift_balikgozu" and k.bilgi["kamera"]["genislik"] == 1280
    assert "sigma_px" in k.bilgi["theta_benzetim"] and k.bilgi["theta_benzetim"]["jpeg_kalite"] >= 50
    im = k.resim(3)
    assert im.shape == (720, 1280, 3)
    assert im[5, 5].max() < 12                                  # daire dışı karanlık
    L = Kamera().lens[0]
    assert float(im[300:340, 540:600].mean()) < 45               # lens 1'de gövde (maske) koyu
    assert k.gercek_konum() is not None and k.komut(2000) != (0.0, 0.0)
    SONUCLAR["benzet_jpeg_kb"] = round(float(np.mean(boyutlar)) / 1024, 0)
    assert 100 < np.mean(boyutlar) / 1024 < 450
    # benzetilmiş kayıt üstünde yöntem çalışır (kamera.py'den dönüşüm yok, yalnızca komut)
    sonuc, _, _ = calistir.calistir(cikti, "yontem_gercek", os.path.join(cikti, "s"), sessiz=True)
    assert sonuc["konum_maks_m"] < 1e-4


def test_temizlik():
    if "k" in _GECICI:
        shutil.rmtree(_GECICI.pop("k"), ignore_errors=True)
