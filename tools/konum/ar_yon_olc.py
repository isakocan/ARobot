"""Sürüş uygulamasının AR yönünü (RobotAR.cs) gerçek yöne (gercek_yon.json, telefon jiroskobu) karşı ölçer (PC).

Uygulama AR nesnelerini çizerken robotun gönderdiği yönü değil, kendi görüntü pusulasını (VideoCompass: sabit maskeli,
sıfır tepesi kuralı yok; robottaki eski teta.Pusula'nın kopyası) kullanır; robotun yönü yalnız aradaki farkı, robot
dönmüyorken her konumda %12 düzeltir, fark 45 dereceyi aşarsa bir anda eşitler. Burada o mantık kayıttaki karelerle
(5 Hz) taklit edilir; uygulamadaki pusula 15 kare/sn gördüğü için sayılar yaklaşık. Karşılaştırma: robotun kendi yönü
(konum.py yeniden oynatılır) doğrudan kullanılsaydı.
Kullanım: python ar_yon_olc.py <kök> <robot_kaydı>"""
import json, math, os, sys
import cv2
import numpy as np

KOK, K = sys.argv[1:3]
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
import konum, teta

R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
EV = json.load(open(os.path.join(K, "ev.json")))["isaretler"]
g = json.load(open(os.path.join(K, "gercek_yon.json"))); GY = dict(zip(g["kare"], g["yaw"]))
G = [cv2.imread(os.path.join(K, f["jpg"]), cv2.IMREAD_GRAYSCALE) for f in R]
def da(a): return (a + 180.0) % 360.0 - 180.0

# robotun kendi yönü (yeni konum kodu)
kb = konum.KonumBulucu(); kb.ev_ayarla(EV); KY = []
for i, f in enumerate(R):
    kb.isle(G[i], f["t"], (f["sol"], f["sag"])); o = kb.ozet()
    KY.append(o["yaw"] if o["var"] else None)

# uygulamanın görüntü pusulası: eski pusula (sabit maske, dinamik yok)
teta.Pusula.DINAMIK = False
pus = teta.Pusula(); C = [0.0]; GUV = [0.0]
for i in range(1, len(R)):
    d, guv = pus.guncelle(G[i]); C.append(C[-1] + d); GUV.append(guv)
C = np.array(C); GUV = np.array(GUV); T = np.array([f["t"] for f in R])

GEC = 2                       # robotun konumu uygulamaya ~0,3-0,4 sn geç: 2 kare (5 Hz)
off = None; son_iyi = -10.0; hiz = 0.0; e_app, e_rob, e_pus, e_yeni, e_gec = [], [], [], [], []; sicrama = 0
for i in range(len(R)):
    dt = T[i] - T[i - 1] if i else 0.2
    if GUV[i] >= 0.3: son_iyi = T[i]
    if i: hiz = 0.7 * hiz + 0.3 * abs(C[i] - C[i - 1]) / max(dt, 1e-3)
    saglam = T[i] - son_iyi < 0.5
    k = KY[i - GEC] if i >= GEC else None
    if k is None: continue
    if saglam:
        hedef = da(k - C[i])
        if off is None or abs(da(hedef - off)) > 45: off = hedef; sicrama += 1
        elif hiz < 12: off += da(hedef - off) * 0.12
    if off is None or i not in GY or i < int(os.environ.get("BAS", "0")): continue
    app = C[i] + off if saglam else k
    e_app.append(da(app - GY[i])); e_rob.append(da(KY[i] - GY[i]) if KY[i] is not None else np.nan); e_pus.append(saglam)
    # yeni kural (RobotAR, 25 Kas): robotun (gec gelen) yonu + o andan beri ekrandaki pusulanin donusu; pusula sifira
    # yapisip komut donuyor diyorsa komut
    dol = 0.0
    for j in range(i - GEC + 1, i + 1):
        p_ = C[j] - C[j - 1]; dtj = T[j] - T[j - 1]
        km = 127.0 * (R[j - 1]["sol"] - R[j - 1]["sag"]) / 510.0 * dtj
        dol += km if (abs(p_) < 1 and abs(km) > 4) or GUV[j] < 0.3 else p_
    e_yeni.append(da(k + dol - GY[i])); e_gec.append(da(k - GY[i]))

ea, er = np.abs(np.array(e_app)), np.abs(np.array(e_rob)); er = er[~np.isnan(er)]
print("kare %d (uygulama pusulasi saglam %%%d), 45 derece esitleme %d kez" % (len(ea), 100 * np.mean(e_pus), sicrama))
print("AR yonu hatasi (uygulama, gorunen pusula + yavas duzeltme): ortanca %5.1f  %%90 %5.1f  en cok %5.1f der" % (np.median(ea), np.percentile(ea, 90), ea.max()))
print("robotun kendi yonu (konum.py, ~0,3 sn gec dahil degil)   : ortanca %5.1f  %%90 %5.1f  en cok %5.1f der" % (np.median(er), np.percentile(er, 90), er.max()))
ey, eg = np.abs(np.array(e_yeni)), np.abs(np.array(e_gec))
print("yeni kural (robot yonu + son 0,4 sn pusula/komut)         : ortanca %5.1f  %%90 %5.1f  en cok %5.1f der" % (np.median(ey), np.percentile(ey, 90), ey.max()))
print("yalniz robotun gec gelen yonu (dolgu yok)                 : ortanca %5.1f  %%90 %5.1f  en cok %5.1f der" % (np.median(eg), np.percentile(eg, 90), eg.max()))
for lim in (5, 10, 20, 30):
    print("  hata > %2d der: uygulama %%%d, yeni kural %%%d, robot %%%d" % (lim, 100 * np.mean(ea > lim), 100 * np.mean(ey > lim), 100 * np.mean(er > lim)))

if os.environ.get("SERI"):
    j = 0
    for i in range(len(R)):
        if i % 15 == 0 and i in GY and KY[i] is not None:
            gm = [q for q in R[i]["gorulen"]]
            print("  %3d t %5.0f gercek %7.1f robot hata %+6.1f  isaret %s" % (i, T[i] - T[0], GY[i], da(KY[i] - GY[i]),
                  " ".join("%.1fm" % math.hypot(q[1], q[2]) for q in gm)))
