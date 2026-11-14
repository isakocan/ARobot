"""Birleşik kestirici (birlesik_kestirici.py) testleri: sentetik senaryolar (kayıt gerektirmez) + varsa bir geliştirme kaydında uçtan uca.
Çalıştır: python tools/konum_deney/test_birlesik.py   (ya da pytest)"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import birlesik_kestirici as bk  # noqa: E402

DT = 1000.0 / 15.0
V_TAM, W_TAM = 0.40, 60.0
PWM = 128                    # (128+128)/2/255*0.4 = 0.2008 m/s nominal


def kos(n, kayma=1.0, bev=None, yaw=None, komut=None, par=None):
    """Düz giden robot. bev(i, gercek_adim) -> (ds, di, ok, ic); yaw(i) -> M2D yaw (derece). Dönüş: (kestirici, serim listesi, gerçek yol m)."""
    est = bk.Kestirici((0.0, 0.0, 0.0), V_TAM, W_TAM, par)
    v_nom = V_TAM * PWM / 255.0
    adim = kayma * v_nom * DT / 1000.0
    out = []
    for i in range(n):
        k = komut(i) if komut else (PWM, PWM)
        b = bev(i, adim) if bev else (0.0, adim, True, 30)
        y = yaw(i) if yaw else 0.0
        r = est.adim(int(i * DT), y, 40, b, k)
        out.append(r)
    return est, out, n * adim


def test_saglikli_bev_kullanilir_ve_k_izlenir():
    est, out, yol = kos(300, kayma=0.9)
    kul = np.mean([o["bev_kullan"] for o in out[30:]])
    assert kul > 0.9, kul
    assert 0.85 <= est.k <= 0.95, est.k                                     # gerçek kayma 0,9 (bev_olcek payıyla)
    assert abs(out[-1]["y"] - yol) < 0.1 * yol, (out[-1]["y"], yol)


def test_far_hatasi_yakalanir_bev_0_3_kat():
    """Bevin 'yalnız %30 hareket' ölçmesi (far): B güvensiz sayılır, konum komuttan gider (yaklaşık doğru)."""
    est, out, yol = kos(300, kayma=1.0, bev=lambda i, a: (0.0, 0.3 * a, True, 80))
    kul = np.mean([o["bev_kullan"] for o in out[40:]])
    assert kul < 0.1, kul
    assert abs(out[-1]["y"] - yol) < 0.1 * yol, (out[-1]["y"], yol)          # C gibi 0,3 katına çökmez


def test_bev_3_kat_abartisi_yakalanir():
    est, out, yol = kos(300, bev=lambda i, a: (0.0, 3.0 * a, True, 80))
    assert abs(out[-1]["y"] - yol) < 0.1 * yol, (out[-1]["y"], yol)


def test_bev_kopar_komuta_doner_ve_geri_gelir():
    def bev(i, a):
        return (0.0, a, False, 0) if 100 <= i < 200 else (0.0, a, True, 40)
    est, out, yol = kos(300, kayma=0.95, bev=bev)
    assert out[150]["bev_kullan"] == 0
    assert np.mean([o["bev_kullan"] for o in out[230:]]) > 0.9
    assert abs(out[-1]["y"] - yol) < 0.06 * yol, (out[-1]["y"], yol)         # kopukluk boyunca k son sağlıklı değerde (kayma ~0,95) kalır


def test_yaw_sicramasi_reddedilir():
    """M2D bir karede 65 derece sıçrayıp orada kalırsa (far), komut düz diyor: sıçrama reddedilir, yaw ~0 kalır."""
    est, out, yol = kos(200, yaw=lambda i: 0.0 if i < 100 else 65.0)
    assert abs(((out[-1]["yaw"] + 180) % 360) - 180) < 5.0, out[-1]["yaw"]
    assert out[100]["yaw_komut"] == 1


def test_durgun_robot_bevin_sahte_hareketini_yok_sayar():
    est, out, yol = kos(120, komut=lambda i: (0, 0), bev=lambda i, a: (0.03, 0.05, True, 50))
    assert abs(out[-1]["x"]) < 1e-6 and abs(out[-1]["y"]) < 1e-6


def test_guven_tekduze_azalir_ve_sinirli():
    est, out, yol = kos(300, bev=lambda i, a: (0.0, 0.3 * a, True, 80))
    g = np.array([o["guven"] for o in out])
    s = np.array([o["sigma"] for o in out])
    assert np.all(np.diff(s) >= -1e-12) and np.all((g > 0) & (g <= 1))
    est2, out2, _ = kos(300)
    assert out2[-1]["sigma"] < out[-1]["sigma"]                               # sağlıklı kamerada belirsizlik daha az birikir


def test_yerinde_donus_komut_geometrisi():
    """Yerinde dönüşte kamera dönme merkezi çevresinde döner (0,07 m): komut geometrisi, BEV gürültüsü işlemez."""
    est = bk.Kestirici((0.0, 0.0, 0.0), V_TAM, W_TAM)
    yaw = 0.0
    for i in range(1, 91):
        yaw += 2.0
        r = est.adim(int(i * DT), yaw, 50, (0.004, -0.003, True, 12), (100, -100))
    assert abs(r["yaw"] - 180.0) < 15.0                                       # motor gecikmesi + kw payı
    assert np.hypot(r["x"], r["y"]) < 0.2


def test_uctan_uca_kayit_varsa():
    import birlesik_ortak as bo
    import birlesik_degerlendir as bd
    ad = "duz_git"
    s = bo.spec(ad)
    import birlesik_bev as bb
    if not (os.path.exists(bb.bev_yolu(ad, "LLkomutlu")) and os.path.exists(os.path.join(bo.ONB, "yonL_komutlu_%s.npz" % ad))):
        print("  (uctan uca atlandı: önbellek yok)")
        return
    g = bd.Girdi(ad)
    P, seri, ms = bd.kos_E(g)
    r = bd.metrik(g, P)
    assert r["konum_rmse_m"] < 0.2, r["konum_rmse_m"]
    assert ms.mean() < 5.0


if __name__ == "__main__":
    sayi = 0
    for ad, f in list(globals().items()):
        if ad.startswith("test_") and callable(f):
            f()
            sayi += 1
            print("tamam", ad)
    print("%d test geçti" % sayi)
