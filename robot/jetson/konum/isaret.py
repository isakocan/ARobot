"""ArUco işaretlerini Theta S karesinde bulur ve robota göre konum + yön verir (Python 3.6, numpy + OpenCV aruco).

Balık gözü karesi doğrudan aranmaz (kenarlarda kare çok bükülür): robotun etrafına bakan 6 sanal düz kamera
(60 derece arayla, 80 derece görüş, 400x300) üretilir, her birinde DICT_4X4_50 aranır. Köşelerden poz homografiyle
çıkar (telefondaki IsaretPoz.cs ile aynı yöntem). İşaret siyah kare kenarı 18 cm (tools/aruco/yazdir.py).
"""
import math

import cv2
import numpy as np

import teta

KENAR = 0.18
# 28 Kas: yukarı eğim 5 -> 15 derece (pencere -17..+47 derece). Yüksek asılı işaret (merkez 0,47 m) 0,5 m'den pencerenin
# üstünden taşıyıp bulunamıyordu; kayıtlarda eski alçak işaretler de aynı ya da daha çok bulunuyor (114 -> 120 / 283 kare).
YUZLER = [teta.SanalKamera(yaw, 80.0, 400, 300, 15.0) for yaw in range(0, 360, 60)]


def _sozluk_ve_bulucu():
    ar = cv2.aruco
    sozluk = ar.getPredefinedDictionary(ar.DICT_4X4_50)
    if hasattr(ar, "ArucoDetector"):           # OpenCV 4.7+
        p = ar.DetectorParameters()
        p.cornerRefinementMethod = ar.CORNER_REFINE_SUBPIX
        bul = ar.ArucoDetector(sozluk, p)
        return lambda g: bul.detectMarkers(g)
    p = ar.DetectorParameters_create()         # OpenCV 4.1-4.6 (Jetson)
    p.cornerRefinementMethod = ar.CORNER_REFINE_SUBPIX
    return lambda g: ar.detectMarkers(g, sozluk, parameters=p)


try:
    _BUL = _sozluk_ve_bulucu()
    ARUCO_VAR = True
except AttributeError:
    _BUL = None
    ARUCO_VAR = False


def homografi_poz(a, b, s=KENAR):
    """Köşeler TL, TR, BR, BL (normalleştirilmiş a, b) -> (t, r1) kamera koordinatında ya da None."""
    h = s / 2.0
    X = (-h, h, h, -h)
    Y = (h, h, -h, -h)
    M = np.zeros((8, 8)); y = np.zeros(8)
    for i in range(4):
        M[2 * i] = [X[i], Y[i], 1, 0, 0, 0, -a[i] * X[i], -a[i] * Y[i]]; y[2 * i] = a[i]
        M[2 * i + 1] = [0, 0, 0, X[i], Y[i], 1, -b[i] * X[i], -b[i] * Y[i]]; y[2 * i + 1] = b[i]
    try:
        H = np.append(np.linalg.solve(M, y), 1.0).reshape(3, 3)
    except np.linalg.LinAlgError:
        return None
    h1, h2, h3 = H[:, 0], H[:, 1], H[:, 2]
    l = 2.0 / (np.linalg.norm(h1) + np.linalg.norm(h2))
    if h3[2] * l < 0:
        l = -l
    r1 = h1 * l; t = h3 * l
    r1 = r1 / np.linalg.norm(r1)
    if t[2] < 0.05:
        return None
    return t, r1


_H = KENAR / 2.0
_NESNE = np.array([[-_H, _H, 0], [_H, _H, 0], [_H, -_H, 0], [-_H, -_H, 0]], np.float64)
IPPE_VAR = hasattr(cv2, "solvePnPGeneric") and hasattr(cv2, "SOLVEPNP_IPPE_SQUARE")


def _puan(o):
    """Düşük iyi: yeniden yansıtma hatası (px) + eğim cezası. Köşe iyileştirmesinden sonra yanlış çözümün hatası
    bazen daha düşük çıkıyor (yapay test, 1,2 m); ayırt eden işaretin dikliği: doğru çözüm 0-4, yanlış 5-11 derece eğik."""
    return o[2] + 0.1 * o[3]


def ippe_poz(k, yuz):
    """Köşeler (piksel, TL TR BR BL) -> [(t, n, hata_px, egim_derece)] iyiden kötüye; t, n sanal kamera koordinatında.

    Kare işaretin uzakta iki olası duruşu vardır (biri yanlış, ayna gibi). 21 Kasım kaydında doğru çözümün
    yeniden yansıtma hatası 0,1-0,5 px, yanlışınki 1,0-1,5 px idi; eski homografi yöntemi 2,2 m'de 50 derece
    yanılıyordu. İşaret duvarda dik durduğu için normali yatay olmalı: eğik çözüm cezalandırılır (_puan)."""
    Kc = np.array([[yuz.f, 0, yuz.cx], [0, yuz.f, yuz.cy], [0, 0, 1.0]])
    try:
        n, rv, tv, err = cv2.solvePnPGeneric(_NESNE, k.astype(np.float64), Kc, None, flags=cv2.SOLVEPNP_IPPE_SQUARE)
    except cv2.error:
        return []
    out = []
    for j in range(n):
        Rm, _ = cv2.Rodrigues(rv[j])
        nr = yuz.R @ Rm[:, 2]
        egim = math.degrees(math.asin(min(1.0, abs(nr[1]))))
        out.append((tv[j].ravel(), Rm[:, 2], float(np.ravel(err[j])[0]), egim))
    out.sort(key=_puan)
    return out


def bul(ham_gri):
    """Kare (1280x720 gri) -> [{"id", "x", "z", "y", "aci", "mesafe", "yon"}] robot koordinatında.

    x, z: işaret merkezi (metre, x sağ, z ileri); aci: işaretin baktığı yön atan2(nx, nz) (radyan);
    belirsiz: işaretin yönü güvenilmez (iki duruş çözümü birbirine yakın), konumu yine kullanılabilir;
    yon: robottan işarete yatay açı (derece, sağa +). Aynı işaret iki yüzde görünürse ortaya en yakın olan alınır."""
    if not ARUCO_VAR:
        return []
    sonuc = {}
    for yuz in YUZLER:
        g = yuz.goruntu(ham_gri)
        koseler, idler, _ = _BUL(g)
        if idler is None:
            continue
        for k, i in zip(koseler, idler.ravel()):
            k = k.reshape(4, 2)
            a = (k[:, 0] - yuz.cx) / yuz.f
            b = (k[:, 1] - yuz.cy) / yuz.f
            cozum = ippe_poz(k, yuz) if IPPE_VAR else []
            belirsiz = False
            if cozum:
                t, nk, hata, _ = cozum[0]
                tr = yuz.R @ t; n3 = yuz.R @ nk
                n = np.array([n3[0], 0.0, n3[2]])
                # ikinci çözüm neredeyse aynı iyilikteyse yön güvenilmez (konum yine iyi)
                belirsiz = len(cozum) > 1 and _puan(cozum[1]) < 1.3 * _puan(cozum[0]) + 0.1
            else:
                p = homografi_poz(a, b)
                if p is None:
                    continue
                t, r1 = p
                tr = yuz.R @ t; sag = yuz.R @ r1
                n = np.array([-sag[2], 0.0, sag[0]])
                belirsiz = True
            nn = np.linalg.norm(n)
            if nn < 1e-6:
                continue
            n /= nn
            if n[0] * -tr[0] + n[2] * -tr[2] < 0:
                n = -n                                      # işaretten robota bakar
            merkez_uzak = abs(k[:, 0].mean() - yuz.cx) + abs(k[:, 1].mean() - yuz.cy)
            kenar_px = float(np.mean([np.linalg.norm(k[j] - k[(j + 1) % 4]) for j in range(4)]))
            kayit = {"id": int(i), "x": float(tr[0]), "y": float(tr[1]), "z": float(tr[2]), "aci": float(math.atan2(n[0], n[2])),
                     "mesafe": float(math.hypot(tr[0], tr[2])), "yon": float(math.degrees(math.atan2(tr[0], tr[2]))),
                     "kenar_px": kenar_px, "belirsiz": bool(belirsiz), "_m": merkez_uzak}
            if int(i) not in sonuc or merkez_uzak < sonuc[int(i)]["_m"]:
                sonuc[int(i)] = kayit
    for v in sonuc.values():
        del v["_m"]
    return list(sonuc.values())
