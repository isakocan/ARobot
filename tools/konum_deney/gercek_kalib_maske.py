"""Robota sabit (H3) noktalar icin ek gövde maskesi: ham karede pikseli HIC KAYMAYAN (< 1 px) eslesmelerin ortak bolgeleri (anten ucu, kablo, mercek kenari).
  python gercek_kalib_maske.py     ->  kamera_maske_gercek_sabit.png (kamera_maske_gercek.png + sabit noktalar, 255 = gövde)  ve kalib/maske_sabit.jpg
Kural (yalniz FIZIKSEL: kamerayla birlikte hareket eden cisim): 12 px kutuda >= 25 sabit eslesme (tum duz gidis cifleri, 6 kayit) olan kutularin 21 px genisletilmisi."""
import os
import sys

import cv2
import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_kalib_model as km  # noqa: E402

KAYITLAR = ["2a_vitessiz_duz", "2b_otomatik_duz", "4_parke_vitessiz", "5_saksi_turu", "6c_karanlik_surus", "olcum_ham"]
KUTU, ESIK, GEN = 12, 25, 21

if __name__ == "__main__":
    H = np.zeros((720 // KUTU + 1, 1280 // KUTU + 1), int)
    for ad in KAYITLAR:
        E = km.eslesme_yukle(ad)
        pix = np.hypot(E["u2"] - E["u1"], E["v2"] - E["v1"])
        m = pix < 1.0
        np.add.at(H, (E["v1"][m].astype(int) // KUTU, E["u1"][m].astype(int) // KUTU), 1)
    govde = cv2.imread(os.path.join(km.KLASOR, "kamera_maske_gercek.png"), cv2.IMREAD_GRAYSCALE)
    yeni = np.zeros_like(govde)
    for j, i in zip(*np.where(H >= ESIK)):
        yeni[j * KUTU:(j + 1) * KUTU, i * KUTU:(i + 1) * KUTU] = 255
    yeni = cv2.dilate(yeni, np.ones((GEN, GEN), np.uint8))
    son = np.maximum(govde, yeni)
    cv2.imwrite(os.path.join(km.KLASOR, "kamera_maske_gercek_sabit.png"), son)
    print("sabit kutu", int((H >= ESIK).sum()), "ek piksel", int(((son > 0) & (govde == 0)).sum()), "govde", int((govde > 0).sum()))
    for j, i in zip(*np.where(H >= ESIK)):
        print("  kutu (x %d, y %d) sayi %d" % (i * KUTU, j * KUTU, H[j, i]))
    # TANI amacli ikinci maske: ek olarak mercek kenar halkasi (yaricap > 262 px, theta > ~83 derece: yan azimutlarda zemin bu halkada, kenar karartmasi/parlama)
    V, U = np.mgrid[0:720, 0:1280]
    halka = ((np.hypot(U - 322.42, V - 319.04) > 262) & (U < 640)) | ((np.hypot(U - 956.67, V - 320.15) > 262) & (U >= 640))
    kenar = np.maximum(son, (halka * 255).astype(np.uint8))
    cv2.imwrite(os.path.join(km.KLASOR, "kamera_maske_gercek_sabit_kenar.png"), kenar)
    ornek = cv2.imread(os.path.join(km.KALIB, "_ornek.jpg"))
    if ornek is not None:
        ornek[(son > 0) & (govde == 0)] = (0, 0, 255)
        cv2.imwrite(os.path.join(km.KALIB, "maske_sabit.jpg"), ornek)
