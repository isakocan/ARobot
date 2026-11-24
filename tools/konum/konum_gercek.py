"""Robotun konum kodunu (robot/jetson/konum/konum.py) kayıtta çalıştırır, yönünü gerçek yöne (gercek_yon.json, telefon
jiroskobu) karşı ölçer. İşaretli (tam) ve işaretsiz (yalnız başta kurulum) iki çeşit.
Kullanım: python konum_gercek.py <kök> <robot_kaydı>"""
import json, math, os, sys
import cv2
import numpy as np

KOK, K = sys.argv[1:3]
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
import konum

R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
EV = json.load(open(os.path.join(K, "ev.json")))["isaretler"]
g = json.load(open(os.path.join(K, "gercek_yon.json"))); GY = dict(zip(g["kare"], g["yaw"]))
G = [cv2.imread(os.path.join(K, f["jpg"]), cv2.IMREAD_GRAYSCALE) for f in R]


def calistir(isaretsiz):
    kb = konum.KonumBulucu(); kb.ev_ayarla(EV); e = []; kuruldu = None
    for i, f in enumerate(R):
        if isaretsiz and kuruldu is not None:
            kb.ev = {}                      # kurulduktan sonra işaret yok
        kb.isle(G[i], f["t"], (f["sol"], f["sag"]))
        o = kb.ozet()
        if o["var"] and kuruldu is None:
            kuruldu = i
        if o["var"] and i in GY:
            e.append(((o["yaw"] - GY[i] + 180) % 360) - 180)
    e = np.array(e)
    return e


for ad, isz in (("isaretli", False), ("isaretsiz", True)):
    e = calistir(isz); a = np.abs(e - np.median(e)) if ad == "isaretsiz" else np.abs(e)
    print("%-10s kare %3d | yon hatasi ortanca %5.1f  %%90 %5.1f  en cok %5.1f der%s" % (
        ad, len(e), np.median(a), np.percentile(a, 90), a.max(), "  (baslangic farki %.1f cikarildi)" % np.median(e) if isz else ""))
