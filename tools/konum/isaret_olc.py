"""İşaret yüzey yönü ölçümünü gerçek yöne (gercek_yon.json) karşı ölçer (PC). (Dik kısıtlı çözüm de denendi, 25 Kasım:
uzak / eğikte kazanç yok, yakında +2,7 derece kayma; bırakıldı.)
Kullanım: python isaret_olc.py <kök> <robot_kaydı>"""
import json, math, os, sys
import cv2
import numpy as np

KOK, K = sys.argv[1:3]
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
import isaret

R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
EV = {int(m[0]): m for m in json.load(open(os.path.join(K, "ev.json")))["isaretler"]}
g = json.load(open(os.path.join(K, "gercek_yon.json"))); GY = dict(zip(g["kare"], g["yaw"]))
sonuc = {}
for kisit in (False,):
    rows = []
    for i in GY:
        if not R[i]["gorulen"]:
            continue
        gri = cv2.imread(os.path.join(K, R[i]["jpg"]), cv2.IMREAD_GRAYSCALE)
        for q in isaret.bul(gri):
            if q["id"] not in EV:
                continue
            yaw = math.degrees(EV[q["id"]][3] - q["aci"])
            e = ((yaw - GY[i] + 180) % 360) - 180
            egik = math.degrees(((q["aci"] - (math.atan2(q["x"], q["z"]) + math.pi)) + math.pi) % (2 * math.pi) - math.pi)
            rows.append((i, q["mesafe"], egik, e, q["belirsiz"]))
    sonuc[kisit] = np.array(rows, float)
for kisit, a in sonuc.items():
    print("%s: %d gozlem" % ("DIK KISITLI" if kisit else "SERBEST    ", len(a)))
    for ad, m in (("hepsi", np.ones(len(a), bool)), ("<0,8 m", a[:, 1] < 0.8), ("0,8-1,6 m", (a[:, 1] >= 0.8) & (a[:, 1] < 1.6)), (">1,6 m", a[:, 1] >= 1.6),
                  ("egik<20", np.abs(a[:, 2]) < 20), ("egik 20-60", (np.abs(a[:, 2]) >= 20) & (np.abs(a[:, 2]) < 60))):
        if m.sum():
            e = a[m, 3]
            print("   %-10s n=%3d  ortanca %+5.1f  mutlak ortanca %4.1f  %%90 %5.1f der  (belirsiz %d)" % (ad, m.sum(), np.median(e), np.median(np.abs(e)), np.percentile(np.abs(e), 90), int(a[m, 4].sum())))
