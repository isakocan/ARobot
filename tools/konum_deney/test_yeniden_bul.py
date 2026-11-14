"""Yeniden bulucu testleri (kendi küçük koşucusu; pytest varsa o da toplar).
Çalıştırma:  python tools/konum_deney/test_yeniden_bul.py
Gerekli: kayitlar/ altındaki sim kayıtları ve zemin tarama önbelleği (depoda var). Hafıza ilk seferde kurulur (~5 sn) ve önbelleğe yazılır."""
import os
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import yeniden_bul_ortak as yo  # noqa: E402
import yeniden_bul_cekirdek as yc  # noqa: E402

_H = {}


def hafiza():
    if "h" not in _H:
        _H["h"] = yc.hafiza_kur()
    return _H["h"]


def _kf_poz_yaw_hata(p, q):
    return float(np.hypot(p[0] - q[0], p[1] - q[1])), float(abs(yo.aci_fark(p[2], q[2])))


def test_ayni_anahtar_kare():
    """Saklı bir anahtar karenin kendisi sorgu olursa kendi pozunu döndürmeli (tanındı)."""
    H = hafiza()
    for k in (3, len(H.kf_R) // 2, len(H.kf_R) - 2):
        r = yc.sorgula(H, (H.kf_R[k], H.kf_D[k]), None, genel=False)
        assert r["en"]["kf"] == k, "en iyi anahtar kare %s, beklenen %d" % (r["en"]["kf"], k)
        e, y = _kf_poz_yaw_hata(r["tri"], H.kf_poz[k])
        assert e < 0.25 and y < 2.0, "poz hatası %.2f m, %.1f derece" % (e, y)
        tanindi, poz, g = yc.karar(r)
        assert tanindi, "kendi anahtar karesi tanınmadı"


def test_donmus_kopya():
    """Aynı karenin ışınları dikey eksende th kadar döndürülürse (robot yerinde th döndü): aynı konum, yaw + th."""
    H = hafiza()
    k = len(H.kf_R) // 3
    for th in (70.0, -135.0):
        c, s = np.cos(np.radians(th)), np.sin(np.radians(th))
        R = H.kf_R[k].copy()
        x, z = R[:, 0].copy(), R[:, 2].copy()
        R[:, 0] = x * c - z * s          # azimut th kadar AZALIR = robot saat yönünde th döndü
        R[:, 2] = x * s + z * c
        r = yc.sorgula(H, (R, H.kf_D[k]), None, genel=False)
        beklenen = (H.kf_poz[k][0], H.kf_poz[k][1], (H.kf_poz[k][2] + th) % 360.0)
        e, y = _kf_poz_yaw_hata(r["tri"], beklenen)
        assert e < 0.25 and y < 2.0, "th=%g: konum %.2f m, yaw %.1f derece" % (th, e, y)
        assert yc.karar(r)[0]


def _gorulmemis_oda(kayit, kare):
    from kayit import Kayit
    ad = kayit
    kay = Kayit(yo.KAYITLAR_TUM[ad]["klasor"])
    oz = yo.oz_cikar(kay.resim(kare))
    tar = yo.tarama_oku(ad)
    j = int(np.argmin(np.abs(tar["kare"] - kare)))
    return oz, (tar["mesafe"][j], tar["guven"][j], tar["menzil"])


def test_gorulmemis_oda_reddedilir():
    """Haritalama koşusunun hiç girmediği yatak odası ve banyodan kareler 'tanımıyorum' demeli."""
    H = hafiza()
    for ad, kare in (("yerinde_donus_banyo", 0), ("yerinde_donus_banyo", 60), ("yerinde_donus_yatak_odasi", 0),
                     ("yerinde_donus_yatak_odasi", 60)):
        oz, tar = _gorulmemis_oda(ad, kare)
        r = yc.sorgula(H, oz, tar, genel=False)
        tanindi, poz, g = yc.karar(r)
        assert not tanindi, "%s kare %d yanlış tanındı: %s (en_icerik %d)" % (ad, kare, poz, r["en"]["icerik"] if "en" in r else 0)


def ana():
    testler = [(n, f) for n, f in sorted(globals().items()) if n.startswith("test_") and callable(f)]
    kalan = 0
    for n, f in testler:
        t = time.time()
        try:
            f()
            print("GECTI  %-36s %.1f sn" % (n, time.time() - t))
        except AssertionError as e:
            kalan += 1
            print("KALDI  %-36s %s" % (n, e))
        except Exception as e:  # noqa: BLE001
            kalan += 1
            print("HATA   %-36s %r" % (n, e))
    print("%d test, %d basarisiz" % (len(testler), kalan))
    return kalan


if __name__ == "__main__":
    sys.exit(1 if ana() else 0)
