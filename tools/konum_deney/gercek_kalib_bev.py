"""Kusbakisi zemin olcumu B (LCN BEV, konum_bev.BevOtelemeci AYNEN) kamera geometrisine / maskeye / sektore gore: onceki ve sonraki olcumler.
  python gercek_kalib_bev.py kos          tum konfigurasyonlari (6 isci) kosar, onbellege yazar: kalib/onbellek/bev_<kayit>_<konf>.npz
  python gercek_kalib_bev.py rapor        onbellekten tablolar/grafikler (kalib/bev_ozet.json, olcum_ilk_gidis.png, sektor_hiz_once_sonra.png)
Konfigurasyon adi: <kamera: nom|A|B>_<maske: g|s>_<sektor: tum|on|sag|arka|sol>.  g = kamera_maske_gercek.png, s = kamera_maske_gercek_sabit.png (sabit noktalar eklenmis).
B'nin girdisi: LCN, yaw = M2D-LCN KOMUTLU (nominal geometriyle onceden hesaplanmis onbellek, onbellek_eski/gercek; duz gidiste yaw sabit, sonuc buna duyarsiz), tarama maskesi YOK
(gercek_olcum.bev_maskesiz ile ayni), h = 0,17 m, ORB/oylama parametreleri aynen."""
import json
import os
import sys
import time

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_ortak as go  # noqa: E402
import gercek_kalib_model as km  # noqa: E402

H = 0.17
KAMJSON = {"nom": None, "A": os.path.join(go.KLASOR, "kamera_gercek.json"), "B": os.path.join(go.KLASOR, "kamera_gercek_B.json")}
MASKEPNG = {"g": None, "s": os.path.join(go.KLASOR, "kamera_maske_gercek_sabit.png"),
           "r": os.path.join(go.KLASOR, "kamera_maske_gercek_sabit_kenar.png")}
SEKTOR = {"tum": None, "on": 0.0, "sag": 90.0, "arka": 180.0, "sol": 270.0}
YAW_ONB = {"olcum_ham": "gercek", "2a_vitessiz_duz": "eski", "2b_otomatik_duz": "eski", "4_parke_vitessiz": "eski", "5_saksi_turu": "eski", "6c_karanlik_surus": "eski"}
PENCERE = {"olcum_ham": (2.5, 20.0)}
ONB = km.ONB


def yol(ad, konf):
    return os.path.join(ONB, "bev_%s_%s.npz" % (ad, konf))


def calis(arg):
    ad, konf = arg
    p = yol(ad, konf)
    if os.path.exists(p):
        return ad, konf, "var"
    parca = konf.split("_")
    kam_k, mask_k, sek_k = parca[:3]
    kayma = int(parca[3][1:]) if len(parca) > 3 else 0          # oN: B baslangic karesini N kare kaydir (anahtar kare fazi degisir: tek kosunun gurultusunu olcmek icin)
    import gercek_kalib_ortak as ok
    if kam_k == "nom":
        ok.kur("gercek", "nominal", ozel_maske=MASKEPNG[mask_k])
    else:
        ok.kur("gercek", "gercek", "bevtest", json_yolu=KAMJSON[kam_k], ozel_maske=MASKEPNG[mask_k])
    import cv2
    cv2.setNumThreads(1)
    import birlesik_bev as bb
    import konum_bev as kb
    from kayit import Kayit
    from kamera import Kamera
    kay = Kayit(os.path.join(go.GERCEK, ad))
    kam = Kamera.bilgiden(kay.bilgi)
    yz = np.load(os.path.join(go.SONUC, "onbellek_" + YAW_ONB[ad], "yonL_komutlu_%s.npz" % ad))
    tab = bb.LcnTablo("theta", kam, lcn_par=bb.LcnParam, h=H)
    oz = kb.BevOtelemeci(tab)
    oz.orb = cv2.ORB_create(nfeatures=500, scaleFactor=1.2, nlevels=3, edgeThreshold=13, fastThreshold=bb.LcnParam.fast_esik, patchSize=21)
    t = kay.t_ms / 1000.0
    t0, t1 = PENCERE.get(ad, (0.0, 1e9))
    t0 += kayma / 15.0
    idx = np.where((t >= t0) & (t <= t1))[0]
    em = None
    if SEKTOR[sek_k] is not None:
        d = np.abs(((tab.az_der - SEKTOR[sek_k] + 180) % 360) - 180)
        em = ((d < 50).astype(np.uint8) * 255)
    D = np.full((len(idx), 2), np.nan); OK = np.zeros(len(idx), bool); IC = np.zeros(len(idx), int)
    for j, i in enumerate(idx):
        ds, di, o, ic = oz.guncelle(kay.resim(i), float(yz["yon"][i]), em)
        D[j] = (ds, di); OK[j] = o; IC[j] = ic
    os.makedirs(ONB, exist_ok=True)
    np.savez_compressed(p, idx=idx, t=t[idx], d=D, ok=OK, ic=IC)
    return ad, konf, "bitti"


KONF_OLCUM = ["nom_g_tum", "nom_g_on", "nom_g_sag", "nom_g_arka", "nom_g_sol", "A_g_tum", "A_g_on", "A_g_sag", "A_g_arka", "A_g_sol", "B_g_tum", "nom_s_tum", "A_s_tum", "nom_s_on", "A_s_on", "nom_s_sag", "nom_s_arka", "nom_s_sol", "A_s_sag", "A_s_arka", "A_s_sol", "B_s_tum", "nom_r_tum", "A_r_tum", "B_r_tum", "nom_r_on", "A_r_on", "nom_r_sag", "A_r_sag", "nom_r_sol", "A_r_sol", "nom_r_arka", "A_r_arka"]
KONF_FAZ = ["%s_%s_%s_o%d" % (k, m, sk, n) for k, m, sk in (("nom", "g", "tum"), ("A", "g", "tum"), ("nom", "s", "tum"), ("A", "s", "tum"), ("nom", "g", "on"), ("A", "g", "on"), ("B", "g", "tum")) for n in range(1, 9)]
KONF_DIGER = ["nom_g_tum", "nom_g_on", "A_g_tum", "A_g_on", "nom_s_tum", "A_s_tum", "nom_s_on", "A_s_on", "nom_r_tum", "A_r_tum"]
DIGER = ["2a_vitessiz_duz", "2b_otomatik_duz", "4_parke_vitessiz", "5_saksi_turu", "6c_karanlik_surus"]


if __name__ == "__main__":
    if sys.argv[1] == "kos":
        import multiprocessing as mp
        isler = [("olcum_ham", k) for k in KONF_OLCUM + KONF_FAZ] + [(a, k) for a in DIGER for k in KONF_DIGER]
        isler.sort(key=lambda x: -(1 if x[0] == "5_saksi_turu" else 0))
        t0 = time.time()
        with mp.get_context("spawn").Pool(12, maxtasksperchild=1) as pool:       # her gorev temiz surec (kamera/maske yonlendirmesi sizmasin)
            for r in pool.imap_unordered(calis, isler):
                print(r, "%.0f sn" % (time.time() - t0), flush=True)
