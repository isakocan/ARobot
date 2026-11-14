"""Gercek kayitlarda DUZ GIDIS kare ciftlerinde ham balikgozu karede zemin noktasi eslesmeleri (kalibrasyon/teshis girdisi).
  python gercek_kalib_veri.py [kayit ...]     (varsayilan: 2a 2b 4 5 6c olcum_ham)
Cikti: kayitlar/sonuclar/gercek/kalib/onbellek/eslesme_<kayit>.npz
  cift (C,) : i (ilk kare), j (ikinci kare)           eslesme (N,) : cift indeksi, lens (0|1), u1 v1 u2 v2 (piksel, ham kare)
Yontem: CLAHE'li gri uzerinde ORB (1500 nokta) + Lowe orani 0,8 + yalniz ayni lens icinde eslesme; gercek govde maskesi (kamera_maske_gercek.png, 9 px genisletilmis)
ve NOMINAL geometri (kamera.json, h=0,17) ile 3-45 derece yer cukuru (zemin adayi) bolgesi disindaki noktalar atilir.
Cift secimi: (i, i+LAG), komut (3 kare gecikmeli) pencerede kararli, sol~sag, |PWM|>80. Kalibrasyon KAYIT ZAMANI/yer gercegi KULLANMAZ."""
import os
import sys

import cv2
import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_ortak as go  # noqa: E402
from kamera import Kamera  # noqa: E402
from kayit import Kayit  # noqa: E402

KALIB = os.path.join(go.SONUC, "kalib")
ONB = os.path.join(KALIB, "onbellek")
LAG = 3
KOMUT_GECIKME = 3
H = 0.17
GEVSEK = {"4_parke_vitessiz": 100}      # joystikle dur-kalk kayit: kararlilik penceresi gevsek (hiz serbest, yalniz duz gidis)
VARSAYILAN = ["2a_vitessiz_duz", "2b_otomatik_duz", "4_parke_vitessiz", "5_saksi_turu", "6c_karanlik_surus", "olcum_ham"]


def duz_ciftler(kay, adim=1, aralik=30):
    t = kay.t_ms / 1000.0
    n = len(t)
    kom = np.array([kay.komut(int(x)) for x in kay.t_ms])
    kg = np.vstack([np.zeros((KOMUT_GECIKME, 2)), kom[:-KOMUT_GECIKME]])
    ciftler = []
    for i in range(3, n - LAG, adim):
        w = kg[i - 2:i + LAG + 1]
        if (w.max(0) - w.min(0)).max() < aralik and (np.abs(w[:, 0] - w[:, 1]) < 25).all() and (np.abs(w[:, 0]) > 80).all() and np.sign(w[:, 0]).std() < 0.01:
            ciftler.append((i, i + LAG))
    return ciftler, kg


def zemin_aday_maskesi(kam, govde):
    """Ham 1280x720 karede piksel -> nominal yer cukuru [3,45] derece ve govde disi: lens basina bool maskesi."""
    V, U = np.mgrid[0:720, 0:1280].astype(np.float64)
    maske = np.zeros((720, 1280), bool)
    for k in (0, 1):
        L = kam.lens[k]
        d, ok = kam.piksel_to_isin(U, V, k)
        cuk = np.degrees(np.arcsin(np.clip(-d[..., 1], -1, 1)))
        m = ok & (cuk > 3.0) & (cuk < 45.0)
        sol = (U < 640) if k == 0 else (U >= 640)
        # lens atamasi: nominal geometride ham dairesine gore (sol yari = lens1)
        maske |= m & sol
    gv = cv2.dilate(govde, np.ones((9, 9), np.uint8)) > 0
    return maske & ~gv


def isle(ad):
    kay = Kayit(os.path.join(go.GERCEK, ad))
    cift, kg = duz_ciftler(kay, 1, GEVSEK.get(ad, 30))
    kam = Kamera()
    govde = cv2.imread(os.path.join(go.KLASOR, "kamera_maske_gercek.png"), cv2.IMREAD_GRAYSCALE)
    adaymask = zemin_aday_maskesi(kam, govde)
    amask = adaymask.astype(np.uint8) * 255
    orb = cv2.ORB_create(nfeatures=1500, scaleFactor=1.2, nlevels=4, edgeThreshold=15, fastThreshold=8, patchSize=25)
    bf = cv2.BFMatcher(cv2.NORM_HAMMING)
    clahe = cv2.createCLAHE(clipLimit=3.0, tileGridSize=(8, 8))
    onbellek = {}

    def ozellik(i):
        if i not in onbellek:
            g = clahe.apply(cv2.cvtColor(kay.resim(i), cv2.COLOR_BGR2GRAY))
            g = cv2.GaussianBlur(g, (0, 0), 0.8)
            kp, des = orb.detectAndCompute(g, amask)
            onbellek[i] = (np.array([k.pt for k in kp], np.float32).reshape(-1, 2), des)
            if len(onbellek) > 12:
                onbellek.pop(min(onbellek))
        return onbellek[i]

    cl, cs, L, U1, V1, U2, V2 = [], [], [], [], [], [], []
    for ci, (i, j) in enumerate(cift):
        p1, d1 = ozellik(i)
        p2, d2 = ozellik(j)
        if d1 is None or d2 is None or len(p1) < 10 or len(p2) < 10:
            continue
        for m in bf.knnMatch(d1, d2, k=2):
            if len(m) == 2 and m[0].distance < 0.8 * m[1].distance:
                a, b = p1[m[0].queryIdx], p2[m[0].trainIdx]
                l1 = 0 if a[0] < 640 else 1
                l2 = 0 if b[0] < 640 else 1
                if l1 != l2 or np.hypot(*(a - b)) > 150:
                    continue
                cl.append(ci)
                L.append(l1)
                U1.append(a[0]); V1.append(a[1]); U2.append(b[0]); V2.append(b[1])
        if ci % 100 == 0:
            print("  %s %d/%d eslesme %d" % (ad, ci, len(cift), len(cl)), flush=True)
    os.makedirs(ONB, exist_ok=True)
    np.savez_compressed(os.path.join(ONB, "eslesme_%s.npz" % ad), cift=np.array(cift, np.int64).reshape(-1, 2), c=np.array(cl, np.int32),
                        lens=np.array(L, np.int8), u1=np.array(U1, np.float32), v1=np.array(V1, np.float32), u2=np.array(U2, np.float32), v2=np.array(V2, np.float32),
                        t=(kay.t_ms / 1000.0)[np.array(cift, np.int64)[:, 0]] if cift else np.zeros(0))
    print("tamam", ad, "cift", len(cift), "eslesme", len(cl), flush=True)
    return ad


if __name__ == "__main__":
    from concurrent.futures import ProcessPoolExecutor
    cv2.setNumThreads(1)
    adlar = sys.argv[1:] or VARSAYILAN
    with ProcessPoolExecutor(min(6, len(adlar))) as ex:
        list(ex.map(isle, adlar))
