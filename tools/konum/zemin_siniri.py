"""Teşhis: robot 360 karesinin kutupsal kuşbakışı üstüne telefon haritasından beklenen zemin sınırı (robot/jetson/konum/sinir.py).

Kırmızı: haritada engelde biten yön, sarı: taranmamış yerde biten yön. Yakın altta, uzak (3 m) üstte.
Kullanım: python zemin_siniri.py <kök> <robot_kayıt> <zemin.json> <çıktı.jpg> [kare ...]
"""
import json, math, os, sys
import cv2
import numpy as np

KOK, K, ZJ, OUT = sys.argv[1:5]
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
import teta, sinir

S = sinir.Sinir(teta)
H = sinir.Harita(ZJ)
R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
sec = [int(a) for a in sys.argv[5:]] or [i for i in range(0, len(R), 40) if R[i]["gorulen"]][:6]
ims = []
for i in sec:
    f = R[i]
    p = S.kutup(cv2.imread(os.path.join(K, f["jpg"])))
    kr, eng = H.profil(f["x"], f["z"])
    s = int(round(f["yaw"])) % 360
    for b in range(sinir.NB):
        k = kr[(b + s) % 360]
        if k < sinir.NR:
            p[k, b] = (0, 0, 255) if eng[(b + s) % 360] else (0, 220, 255)
    p = cv2.flip(cv2.resize(p, (sinir.NB * 2, sinir.NR * 2), interpolation=cv2.INTER_NEAREST), 0)
    cv2.putText(p, "kare %d  (%.2f, %.2f) yon %.0f" % (i, f["x"], f["z"], f["yaw"] % 360), (8, 22), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)
    ims.append(p)
cv2.imwrite(OUT, np.vstack(ims))
print("kareler", sec)
