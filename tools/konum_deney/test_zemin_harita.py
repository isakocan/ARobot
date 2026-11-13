"""Zemin sınırı ve haritalama testleri.  Çalıştırma: python -m pytest tools/konum_deney/test_zemin_harita.py -q
(pytest yoksa: python tools/konum_deney/test_zemin_harita.py)"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zemin_ortak as zo  # noqa: E402
import harita_kur as hk  # noqa: E402
from kamera import Kamera  # noqa: E402

H_KAM = 0.255


def test_geometri_sim_satir_mesafe_gidis_donus():
    for Y in (768, 640):
        v = np.linspace(Y / 2 + 3, Y - 40, 50)                      # ufuk altı satır kenarları
        d = zo.satir_mesafe(v, Y, H_KAM)
        v2 = zo.mesafe_satir(d, Y, H_KAM)
        assert np.allclose(v, v2, atol=1e-6)
        # bilinen değer: 45 derece altı -> d = h
        assert abs(float(zo.aci_mesafe(45.0, H_KAM)) - H_KAM) < 1e-9
        # bir piksel hatası formülü sayısal türevle uyuşur
        vv = Y / 2 + 100.0
        dd = float(zo.satir_mesafe(vv, Y, H_KAM) - zo.satir_mesafe(vv + 1, Y, H_KAM))
        assert abs(dd - float(zo.piksel_hatasi(zo.satir_mesafe(vv + 0.5, Y, H_KAM), Y, H_KAM))) < 0.02 * dd


def test_geometri_theta_balikgozu_gidis_donus():
    """Zemin noktası (azimut, d) -> robot ışını -> balık gözü piksel -> ışın -> ufuk altı açı -> d."""
    kam = Kamera()
    for az in (0, 40, 90, 135, 180, 250, 300):
        for d in (0.6, 1.0, 2.0, 3.5):
            a = np.arctan2(H_KAM, d)
            isin = np.array([np.cos(a) * np.sin(np.radians(az)), -np.sin(a), np.cos(a) * np.cos(np.radians(az))])
            # iki lensten görünen ağırlığı baskın olanı al
            w1, w2 = kam.agirliklar(isin[None])
            lens = 0 if w1[0] >= w2[0] else 1
            u, v, _ = kam.robot_to_lens(isin, lens)
            geri, ok = kam.piksel_to_isin(u, v, lens)
            assert ok
            el = np.degrees(np.arcsin(geri[1]))
            az2 = np.degrees(np.arctan2(geri[0], geri[2])) % 360
            assert abs(float(zo.aci_mesafe(-el, H_KAM)) - d) < 1e-6 * max(d, 1)
            assert abs((az2 - az + 180) % 360 - 180) < 1e-6


def test_azimut_sutunu_sozlesmesi():
    """Orta sütun = ön (azimut 0), sütunlar saat yönünde: panoramada 3/4 genişlikteki sütun azimut +90."""
    G, Y = 1536, 768
    pan = np.zeros((Y, G), np.float32)
    pan[:, G // 2 - 2:G // 2 + 2] = 1.0                              # ön
    pan[:, 3 * G // 4 - 2:3 * G // 4 + 2] = 2.0                      # sağ (saat yönü +90)
    P = zo.azimut_sutunlari(pan)
    assert P.shape == (Y, 360)
    assert np.argmax(P[10]) in (0, 359, 1) or P[10, 0] > 0.5
    assert P[10, 0] > 0.5 and P[10, 90] > 1.0
    assert P[10, 270] < 0.1 and P[10, 180] < 0.1


def _oda_taramasi(x, y, yon, xa, xb, ya, yb):
    """Dikdörtgen oda (x in [xa,xb], y in [ya,yb]) içinde mükemmel 360 mesafe taraması (azimut k: yön + k, saat yönü)."""
    mes = np.zeros(360)
    for k in range(360):
        t = np.radians(yon + k)
        dx, dy = np.sin(t), np.cos(t)
        ds = []
        if dx > 1e-12:
            ds.append((xb - x) / dx)
        if dx < -1e-12:
            ds.append((xa - x) / dx)
        if dy > 1e-12:
            ds.append((yb - y) / dy)
        if dy < -1e-12:
            ds.append((ya - y) / dy)
        mes[k] = min(ds)
    return mes


def _izgara(xa=-4.0, ya=-4.0, W=160, Hh=160):
    return hk.Izgara(xa, ya, W, Hh, 0.05)


def test_harita_mukemmel_taramadan_oda():
    xa, xb, ya, yb = -2.0, 3.0, -1.5, 2.5
    iz = _izgara()
    for (x, y, yon) in [(0.0, 0.0, 0.0), (1.0, 0.5, 40.0), (-1.0, -0.5, 200.0), (2.0, 1.5, 310.0), (0.5, -1.0, 90.0)]:
        iz.tara_ekle(x, y, yon, _oda_taramasi(x, y, yon, xa, xb, ya, yb), np.ones(360), max_menzil=20.0)
    E = iz.durum()
    # gerçek: iç boş, duvar çizgisi dolu
    gx, gy = np.meshgrid(np.arange(iz.W), np.arange(iz.Hh))
    r, c = gy, gx
    X, Y = iz.hucre_merkezi(r, c)
    ic = (X > xa + 0.15) & (X < xb - 0.15) & (Y > ya + 0.15) & (Y < yb - 0.15)
    assert (E[ic] == 255).mean() > 0.97, (E[ic] == 255).mean()
    dolu = E == 0
    assert dolu.sum() > 100
    # dolu hücrelerin hepsi duvara ~1 hücre içinde
    dist = np.minimum.reduce([np.abs(X - xa), np.abs(X - xb), np.abs(Y - ya), np.abs(Y - yb)])
    assert (dist[dolu] < 0.11).mean() > 0.98
    # odanın dışı bilinmiyor kalır
    dis = (X < xa - 0.3) | (X > xb + 0.3) | (Y < ya - 0.3) | (Y > yb + 0.3)
    assert (E[dis] == 128).mean() > 0.99


def test_harita_menzil_siniri_dolu_koymaz():
    """Duvar güvenilir menzilden uzaksa yalnızca menzile kadar boş kanıt; dolu kanıt yok."""
    iz = _izgara()
    m = np.full(360, 5.0)
    iz.tara_ekle(0.0, 0.0, 0.0, m, np.ones(360), max_menzil=3.0)
    E = iz.durum()
    assert (E == 0).sum() == 0
    assert (E == 255).sum() > 100
    X, Y = iz.hucre_merkezi(*np.meshgrid(np.arange(iz.Hh), np.arange(iz.W), indexing="ij"))
    assert (np.hypot(X, Y)[E == 255] <= 3.0 + 0.1).all()


def test_yon_sozlesmesi_kuzeydeki_duvar_haritada_kuzeyde():
    """Robot kuzeye bakarken ön azimut 0'da 2 m'de duvar -> duvar robotun kuzeyinde (y büyük). Doğuya bakarken duvar azimut 270'te."""
    for yon, k_duvar in ((0.0, 0), (90.0, 270), (180.0, 180), (270.0, 90)):
        iz = _izgara()
        m = np.full(360, np.nan)
        m[k_duvar - 2:k_duvar + 3] = 2.0
        if k_duvar < 2:
            m[:3] = 2.0
            m[-2:] = 2.0
        iz.tara_ekle(0.0, 0.0, yon, m, np.ones(360), max_menzil=5.0)
        E = iz.durum()
        r, c = np.where(E == 0)
        assert len(r) > 0
        X, Y = iz.hucre_merkezi(r, c)
        assert abs(np.mean(X)) < 0.2, (yon, np.mean(X))
        assert abs(np.mean(Y) - 2.0) < 0.15, (yon, np.mean(Y))
        assert np.mean(Y) > 1.5       # kuzey (y büyük)
    # satır 0 = kuzey sözleşmesi: dolu hücrenin satırı, robotun satırından küçük
    iz = _izgara()
    m = np.full(360, np.nan)
    m[0:3] = 2.0
    m[-2:] = 2.0
    iz.tara_ekle(0.0, 0.0, 0.0, m, np.ones(360), max_menzil=5.0)
    r, c = np.where(iz.durum() == 0)
    r0, _ = iz.hucre_idx(np.array([0.0]), np.array([0.0]))
    assert r.mean() < r0[0]


def test_gozlenebilir_bolge_duvar_arkasini_icermez():
    import harita_olc as ho
    T = np.full((100, 100), 255, np.uint8)
    T[:, 60:63] = 0                                                  # dikey duvar
    iz = hk.Izgara(0, 0, 100, 100, 0.05)
    R = ho.gozlenebilir_bolge(T, iz, np.array([[1.0, 2.5, 0.0]]), 4.0)
    assert R[50, 30] and not R[50, 80]


if __name__ == "__main__":
    import traceback
    hata = 0
    for ad, f in sorted(globals().items()):
        if ad.startswith("test_") and callable(f):
            try:
                f()
                print("TAMAM", ad)
            except Exception:
                hata += 1
                print("HATA ", ad)
                traceback.print_exc()
    sys.exit(1 if hata else 0)
