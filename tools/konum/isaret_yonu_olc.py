"""Konum kodunu kayıtta çalıştırır; işaret her görüldüğünde (güncellemeden ÖNCE) beklenen görülme yönü ve uzaklığı ile
ölçüleni karşılaştırır (işaretin yüzey açısına güvenmeyen ölçüt). Gerçek yön kaydı olmayan eski kayıtlar için.
Kullanım: python isaret_yonu_olc.py <konum_kod_klasörü> <robot_kaydı>"""
import json, math, os, sys
import cv2
import numpy as np

KOD, K = sys.argv[1:3]
sys.path.insert(0, KOD)
import konum

R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
EV = json.load(open(os.path.join(K, "ev.json")))["isaretler"]
E = {int(m[0]): m for m in EV}
kb = konum.KonumBulucu(); kb.ev_ayarla(EV)
hy, hu = [], []
for f in R:
    o = kb.ozet()
    gri = cv2.imread(os.path.join(K, f["jpg"]), cv2.IMREAD_GRAYSCALE)
    if gri is None:
        continue
    once = (o["var"], o["x"], o["z"], math.radians(o["yaw"]))
    gor = kb.isle(gri, f["t"], (f["sol"], f["sag"]))
    if not once[0]:
        continue
    for g in gor:
        if g["id"] not in E or g["mesafe"] > 2.5:
            continue
        dx, dz = E[g["id"]][1] - once[1], E[g["id"]][2] - once[2]
        c, s = math.cos(-once[3]), math.sin(-once[3])
        lx = dx * c + dz * s; lz = -dx * s + dz * c
        hy.append(abs(((math.degrees(math.atan2(lx, lz) - math.atan2(g["x"], g["z"]))) + 180) % 360 - 180))
        hu.append(abs(math.hypot(lx, lz) - g["mesafe"]))
hy, hu = np.array(hy), np.array(hu)
print("olcum %d | gorulme yonu hatasi ortanca %.1f %%90 %.1f en cok %.0f der | uzaklik ortanca %.2f %%90 %.2f m" % (
    len(hy), np.median(hy), np.percentile(hy, 90), hy.max(), np.median(hu), np.percentile(hu, 90)))
