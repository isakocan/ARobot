"""Anahtar karelerin konuma katkısı (PC): işaret gizlenince (ilk kilitten sonra) konum ne kadar şaşıyor?

1. Hafıza: verilen kayıtlar konum.py ile (işaretli) oynatılır, robotun kendi kuralıyla anahtar kareler toplanır.
2. Deneme kaydı işaretsiz oynatılır (ilk kilitten sonra ev boşaltılır): (a) hafızasız, (b) eski silüet hafızası,
   (c) ORB hafızası.
3. Gerçek: aynı kayıt işaretli oynatılır; işaretin yakından (<= 1,3 m) ve yön belirsizliği <4 derece görüldüğü kareler
   (jiroskop gerçeği varsa ondan 4 dereceden fazla sapanlar atılır). Jiroskop varsa yön her karede ondan da ölçülür.
Kullanım: python orb_konum_olc.py <kök> <deneme_kaydı> <hafıza_kaydı> [<hafıza_kaydı> ...]"""
import json, math, os, sys, time
import cv2
import numpy as np

KOK, DENEME, HAF = sys.argv[1], sys.argv[2], sys.argv[3:]
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
import konum, orb_hafiza, hafiza


def da(a): return (a + 180.0) % 360.0 - 180.0


def yukle(K):
    R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
    EV = json.load(open(os.path.join(K, "ev.json")))["isaretler"]
    G = [cv2.imread(os.path.join(K, f["jpg"]), cv2.IMREAD_GRAYSCALE) for f in R]
    gy = os.path.join(K, "gercek_yon.json"); GY = {}
    if os.path.exists(gy):
        g_ = json.load(open(gy)); GY = dict(zip(g_["kare"], g_["yaw"]))
    return R, EV, G, GY


def oynat(K, kb, isaretsiz=False, her_kare=None):
    R, EV, G, GY = yukle(K); kb.ev_ayarla(EV); kuruldu = False
    for i, f in enumerate(R):
        if G[i] is None: continue
        if isaretsiz and kuruldu: kb.ev = {}
        kb.isle(G[i], f["t"], (f["sol"], f["sag"]))
        kuruldu = kuruldu or kb.bilinen
        if her_kare: her_kare(i, kb)


def hafiza_kur(orb):
    konum.HAFIZA_ORB = orb
    h = None
    for K in HAF:
        kb = konum.KonumBulucu()
        if h is not None: kb.hafiza = h
        oynat(K, kb)
        h = kb.hafiza
        if orb: h.govde = orb_hafiza.teta.DinamikMaske(h.govde.fark.shape, None, en_az=12)   # gövde yeni kayıtta yeniden öğrenilir
    return h


# gerçek
R, EV, G, GY = yukle(DENEME)
GER = {}
def topla(i, kb):
    yakin = [q for q in kb.gorulen if q[1] <= 1.3]
    if kb.bilinen and yakin and math.degrees(math.sqrt(kb.P[2, 2])) < 4:
        if GY and (i not in GY or abs(da(math.degrees(kb.yaw) - GY[i])) > 4): return
        GER[i] = (kb.x, kb.z, math.degrees(kb.yaw))
konum.HAFIZA_ORB = True
oynat(DENEME, konum.KonumBulucu(), her_kare=topla)
print("deneme %s: %d kare, isaretten gercek %d kare%s" % (os.path.basename(DENEME), len(R), len(GER), ", jiroskop %d kare" % len(GY) if GY else ""))

for ad, orb, haf in (("hafizasiz", True, False), ("eski silüet hafiza", False, True), ("ORB hafiza", True, True)):
    h = hafiza_kur(orb) if haf else None
    konum.HAFIZA_ORB = orb
    kb = konum.KonumBulucu()
    if h is not None: kb.hafiza = h
    ey, ep, eg, orb_say = [], [], [], [0, 0]; t0 = time.time()
    def olc(i, kb):
        if not kb.bilinen: return
        if i in GER:
            x, z, yw = GER[i]; ep.append(math.hypot(kb.x - x, kb.z - z)); ey.append(abs(da(math.degrees(kb.yaw) - yw)))
        if i in GY: eg.append(abs(da(math.degrees(kb.yaw) - GY[i])))
        if kb.hafiza_son: orb_say[kb.hafiza_son[4]] += 1
    oynat(DENEME, kb, isaretsiz=True, her_kare=olc)
    ep, ey, eg = np.array(ep), np.array(ey), np.array(eg)
    s = "%-20s hafiza %3d kare | isaret yerinde: yer ortanca %.2f %%90 %.2f en cok %.2f m, yon ortanca %4.1f %%90 %5.1f der" % (
        ad, len(h) if h is not None else 0, np.median(ep), np.percentile(ep, 90), ep.max(), np.median(ey), np.percentile(ey, 90))
    if len(eg): s += " | jiroskop: yon ortanca %4.1f %%90 %5.1f en cok %5.1f der" % (np.median(eg), np.percentile(eg, 90), eg.max())
    if ad.startswith("ORB"): s += " | ORB olcum kabul %d / ret %d" % (orb_say[1], orb_say[0])
    print(s + " (%.0f sn)" % (time.time() - t0))

if os.environ.get("SERI"):
    # ORB'li işaretsiz oynatmada zamanla yön hatası ve ORB ölçümlerinin kendi yön hatası (jiroskoba göre)
    h = hafiza_kur(True); konum.HAFIZA_ORB = True; kb = konum.KonumBulucu(); kb.hafiza = h
    def yaz(i, kb):
        o = kb.hafiza_son
        if i in GY and (i % 25 == 0 or o):
            e = da(math.degrees(kb.yaw) - GY[i]) if kb.bilinen else float("nan")
            s = "  %3d t %5.1f yon hatasi %+6.1f sy %4.1f" % (i, R[i]["t"] - R[0]["t"], e, math.degrees(math.sqrt(kb.P[2, 2])))
            if o: s += "  ORB %3d nokta, kabul %d" % (o[0], o[4])
            print(s)
    oynat(DENEME, kb, isaretsiz=True, her_kare=yaz)
