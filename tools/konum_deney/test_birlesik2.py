"""E2 (birlesik2_kestirici.py) testleri: sentetik profil ogrenme + sim'de uctan uca RMSE siniri + (kayit/onbellek varsa) gercek olcum_ham 2,00 m siniri.
Calistir: python tools/konum_deney/test_birlesik2.py   (ya da pytest)"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import birlesik2_kestirici as b2  # noqa: E402

DT = 1000.0 / 15.0


def gercek_w(q):                       # olu bolgeli, dogrusal olmayan donus (derece/sn) — E2 bunu KODDA bilmiyor
    q = abs(q)
    return 0.0 if q < 40 else 64.0 + (q - 40) * 0.30


def kos(komut_dizisi, v_gercek=lambda p: 0.43 * p / 255.0, bev_iyi=True, m2d_iyi=True):
    est = b2.Kestirici((0.0, 0.0, 0.0))
    yaw_m = 0.0
    out = []
    for i, (sol, sag) in enumerate(komut_dizisi):
        p, q = (sol + sag) / 2.0, (sol - sag) / 2.0
        w = np.sign(q) * gercek_w(q)
        yaw_m += w * DT / 1000.0
        v = v_gercek(abs(p)) * np.sign(p)
        b = (0.0, v * DT / 1000.0, bev_iyi, 30) if abs(q) < 20 else (0.0, v * DT / 1000.0, bev_iyi, 30)
        out.append(est.adim(int(i * DT), yaw_m, 40 if m2d_iyi else 0, b, (sol, sag)))
    return est, out, yaw_m


def test_profil_ogrenir_ve_komuta_dusunce_kullanir():
    seq = [(180, 180)] * 300 + [(100, -100)] * 300 + [(0, 0)] * 15
    est, out, yaw_m = kos(seq)
    v = est.tv.tahmin(180.0)
    assert abs(v - 0.43 * 180 / 255.0) < 0.1 * 0.43 * 180 / 255.0, v        # ileri hiz profili %10 icinde
    w = est.tw.tahmin(100.0)
    assert abs(w - gercek_w(100.0)) < 0.12 * gercek_w(100.0), w               # donus profili %12 icinde (olu bolge dahil olcekli)
    # M2D kaybolunca ogrenilen profil kullanilir (kestirici ayni dizinin devaminda, M2D yok)
    yaw0 = est.poz[2]
    t0 = est.t_onc
    for i in range(1, 46):
        est.adim(t0 + int(i * DT), 0.0, 0, (0.0, 0.0, False, 0), (100, -100))
    donus = (est.poz[2] - yaw0) % 360
    beklenen = (gercek_w(100.0) * 45 * DT / 1000.0) % 360
    assert abs(donus - beklenen) < 0.3 * beklenen, (donus, beklenen)


def test_zemin_degisince_profil_uyum_saglar():
    """Zemin degisimi: hiz %25 duser (hali -> parke gibi); 12 sn (180 kare) sonra profil yeni degere %10 icinde."""
    est = b2.Kestirici((0.0, 0.0, 0.0))
    yaw_m = 0.0
    for i in range(900):
        sol = sag = 200
        k = 0.43 if i < 450 else 0.43 * 0.75
        v = k * 200 / 255.0
        est.adim(int(i * DT), 0.0, 40, (0.0, v * DT / 1000.0, True, 30), (sol, sag))
        if i == 449:
            onceki = est.tv.tahmin(200.0, 0)
        if i == 450 + 180:
            ara = est.tv.tahmin(200.0, 0)
    son = est.tv.tahmin(200.0, 0)
    assert abs(onceki - 0.43 * 200 / 255.0) < 0.1 * onceki, onceki
    assert abs(son - 0.75 * 0.43 * 200 / 255.0) < 0.1 * son, son
    assert abs(ara - son) < 0.15 * son, (ara, son)


def test_sim_uctan_uca():
    try:
        import birlesik_degerlendir as be
        import birlesik2_degerlendir as bd
        g = be.Girdi("duz_git", None, "Lkomutsuz")
        P, s, prof, est = bd.kos_E2(g, b2.Param())
    except Exception as e:           # onbellek yoksa atla
        print("atlandi (sim onbellegi yok):", e)
        return
    r = be.metrik(g, P)
    assert r["konum_rmse_m"] < 0.10, r["konum_rmse_m"]                        # E: 0,092
    assert 0.45 < prof[-1][0] < 0.70, prof[-1]                                # sim gercegi 0,55 m/sn (tam gaz)
    assert 150 < prof[-1][2] < 250, prof[-1]                                  # sim gercegi 197 derece/sn


def test_gercek_olcum_ham_2m():
    yol = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "kayitlar", "sonuclar", "gercek", "e2", "kos", "olcum_ham.npz")
    if not os.path.exists(yol):
        print("atlandi (gercek kos yok)")
        return
    z = np.load(yol)
    t = z["t_ms"] / 1000.0
    P = z["E2_P"]
    i = int(np.argmin(abs(t - 14.4)))
    d = float(np.hypot(P[i, 0] - P[0, 0], P[i, 1] - P[0, 1]))
    assert abs(d - 2.0) < 0.5, d                                              # bantla olculen 2,00 m; +-%25


if __name__ == "__main__":
    for ad, f in list(globals().items()):
        if ad.startswith("test_"):
            f()
            print("tamam", ad)
