"""Birkaç koşunun ana sayılarını yan yana yazar (bant ölçeği; yer düzlemi ölçeği güvenilmez olduğu için ayrı)."""
import os, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from slam_degerlendir import *

KOK = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
for ad in sys.argv[1:]:
    D = yukle(os.path.join(KOK, "kayitlar", "slam", "sonuc", ad))
    s, d = olcek_bant(D)
    o = ozet(D, s)
    ok = D["izl"]
    print("%-18s izlenen %.3f kf %3d nokta %4d | bant ölçek %.2f | bant %.2f m | tur sonu %.2f m | yön kap %.1f | tur yol %.1f m | toplam yol %.1f m" % (
        ad, ok.mean(), len(D["kf"]), len(D["noktalar"]), s, o["bant_uzaklik_m"], o["tur_bas_son_uzaklik_m"], o["yon_kapanis_derece"], o["tur_yol_m"], o["toplam_yol_m"]))
