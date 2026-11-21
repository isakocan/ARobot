"""PC testi: gerçek bir Theta S karesine (çift balık gözü) bilinen konumda 18 cm ArUco işareti ışın izlemeyle çizilir,
isaret.bul() ile aranır; mesafeye göre bulma oranı ve konum/yön hatası yazılır.

Kullanım: python robot/jetson/konum/dene_isaret.py <ham_kare.jpg>
"""
import math
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import isaret  # noqa: E402
import konum   # noqa: E402
import teta    # noqa: E402


def piksel_isinlari():
    v, u = np.mgrid[0:teta.YUK, 0:teta.GEN].astype(np.float64)
    isin = np.full((teta.YUK, teta.GEN, 3), np.nan)
    for on, cx in ((True, teta.ON_X), (False, teta.ARKA_X)):
        du, dv = u - cx, v - teta.CEMBER_Y
        r = np.hypot(du, dv)
        m = (r <= teta.YARICAP) & ((u < 640) if on else (u >= 640))
        th = r / teta.YARICAP * teta.FOV / 2
        with np.errstate(invalid="ignore", divide="ignore"):
            cph = np.where(r > 0, (-du if on else du) / r, 1.0); sph = np.where(r > 0, dv / r, 0.0)
        kx, ky, kz = np.sin(th) * cph, np.sin(th) * sph, np.cos(th) * (1 if on else -1)
        isin[m] = np.stack([-ky, kx, kz], -1)[m]       # kamera -> robot (teta.yon_pikseller'in tersi)
    return isin


def ciz(ham, isin, desen, mx, my, mz, aci, kenar=0.18, beyaz=0.03):
    """İşaret düzlemi: merkez (mx,my,mz), normal (sin aci, 0, cos aci) robota doğru; desen kenar + beyaz pay."""
    n = np.array([math.sin(aci), 0, math.cos(aci)]); sag = np.array([n[2], 0, -n[0]]) * -1   # robottan bakınca sağ
    yuk = np.array([0, 1.0, 0]); c = np.array([mx, my, mz])
    d = isin.reshape(-1, 3)
    den = d @ n
    with np.errstate(invalid="ignore", divide="ignore"):
        t = (c @ n) / den
    p = d * t[:, None] - c
    tam = kenar + 2 * beyaz
    a = (p @ sag) / tam + 0.5; b = 0.5 - (p @ yuk) / tam
    m = (t > 0) & (a >= 0) & (a < 1) & (b >= 0) & (b < 1) & np.isfinite(t)
    out = ham.reshape(-1).copy().astype(np.float32)
    D = desen.shape[0]
    out[m] = desen[(b[m] * D).astype(int), (a[m] * D).astype(int)] * 0.85 + 20   # kağıt tam beyaz değil
    return out.reshape(ham.shape).clip(0, 255).astype(np.uint8)


def main():
    ham = cv2.imread(sys.argv[1], cv2.IMREAD_GRAYSCALE)
    isin = piksel_isinlari()
    ar = cv2.aruco; s = ar.getPredefinedDictionary(ar.DICT_4X4_50)
    ic = ar.generateImageMarker(s, 1, 180) if hasattr(ar, "generateImageMarker") else ar.drawMarker(s, 1, 180)
    desen = cv2.copyMakeBorder(ic, 30, 30, 30, 30, cv2.BORDER_CONSTANT, value=255).astype(np.float32)
    rnd = np.random.RandomState(4)
    print("mesafe  bulunan  konum_hatasi_cm(medyan)  yon_hatasi_derece(medyan)  kenar_px")
    for mes in (0.6, 1.0, 1.5, 2.0, 2.5, 3.0):
        bul, ek, ey, kp = 0, [], [], []
        N = 12
        for k in range(N):
            yon = rnd.uniform(-180, 180); egik = math.radians(rnd.uniform(-40, 40))
            mx, mz = mes * math.sin(math.radians(yon)), mes * math.cos(math.radians(yon))
            aci = math.atan2(-mx, -mz) + egik                 # robota bakar, biraz eğik
            g = ciz(ham, isin, desen, mx, 0.25 - 0.17, mz, aci)
            g = cv2.imdecode(cv2.imencode(".jpg", g, [cv2.IMWRITE_JPEG_QUALITY, 85])[1], cv2.IMREAD_GRAYSCALE)
            r = [x for x in isaret.bul(g) if x["id"] == 1]
            if not r:
                continue
            r = r[0]; bul += 1
            ek.append(math.hypot(r["x"] - mx, r["z"] - mz) * 100)
            ey.append(abs(math.degrees(konum._aci_fark(r["aci"], aci)))); kp.append(r["kenar_px"])
        med = lambda l: float(np.median(l)) if l else float("nan")
        print("%4.1f m   %2d/%d    %6.1f                  %6.1f                     %5.1f" % (mes, bul, N, med(ek), med(ey), med(kp)))


if __name__ == "__main__" and len(sys.argv) == 2:
    main()


def konum_testi(yol):
    """Durağan robot: ev işareti 0 = (2, 3), baktığı yön 200 derece. Robot gerçek konumu ve yönü bilinir;
    işaret robota göre çizilir, KonumBulucu 10 kare işler, sonuç gerçekle karşılaştırılır."""
    ham = cv2.imread(yol, cv2.IMREAD_GRAYSCALE)
    isin = piksel_isinlari()
    ar = cv2.aruco; s = ar.getPredefinedDictionary(ar.DICT_4X4_50)
    ic = ar.generateImageMarker(s, 0, 180) if hasattr(ar, "generateImageMarker") else ar.drawMarker(s, 0, 180)
    desen = cv2.copyMakeBorder(ic, 30, 30, 30, 30, cv2.BORDER_CONSTANT, value=255).astype(np.float32)
    E = konum.Poz2(2.0, 3.0, math.radians(200))
    print("gercek robot (x, z, yon)        ->  bulunan                      hata")
    for rx, rz, ryaw in ((2.3, 2.2, 10.0), (1.4, 2.0, 60.0), (2.4, 2.3, -30.0), (1.9, 2.1, 150.0), (1.6, 1.8, -120.0), (2.0, 2.3, 90.0)):
        R = konum.Poz2(rx, rz, math.radians(ryaw))                   # robot -> ev
        S = R.ters().sonra(E)                                          # isaret yerel -> robot
        g = ciz(ham, isin, desen, S.x, 0.08, S.z, S.aci)
        g = cv2.imdecode(cv2.imencode(".jpg", g, [cv2.IMWRITE_JPEG_QUALITY, 85])[1], cv2.IMREAD_GRAYSCALE)
        kb = konum.KonumBulucu()
        kb.ev_ayarla([[0, E.x, E.z, E.aci]])
        for k in range(10):
            kb.isle(g, simdi=k * 0.2, sol_sag=(0, 0))
        o = kb.ozet()
        print("(%4.2f, %4.2f, %6.1f)  %.2f m  ->  (%5.2f, %5.2f, %6.1f) var=%s   %4.1f cm, %4.1f derece" % (
            rx, rz, ryaw, math.hypot(S.x, S.z), o["x"], o["z"], o["yaw"], o["var"],
            math.hypot(o["x"] - rx, o["z"] - rz) * 100, abs((o["yaw"] - ryaw + 180) % 360 - 180)))


if __name__ == "__main__" and len(sys.argv) > 2 and sys.argv[2] == "konum":
    konum_testi(sys.argv[1])
