"""İşaretsiz harita konumu rastgele seçime (RANSAC tohumu) ne kadar bağlı? Aynı harita, aynı test, farklı tohumlar.
Kullanım: python tohum_dene.py <kök> <harita_kaydı> <test_kaydı>   (TOHUM=0,1,2,3,4  ADIM=2)"""
import math, os, sys
import numpy as np
_kod = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "harita_genislet.py"), encoding="utf-8").read()
exec(_kod.split("# ------------------------------------------------------------------ 2. test")[0])
harita.duzelt(yazdir=False)
R, EV, GY = yukle(T)
G = {i: cv2.imread(os.path.join(T, f["jpg"]), cv2.IMREAD_GRAYSCALE) for i, f in enumerate(R)}
ref = konum.KonumBulucu(); ref.ev_ayarla(EV); YAK = set()
for i, f in enumerate(R):
    if G[i] is None: continue
    ref.isle(G[i], f["t"], (f["sol"], f["sag"]))
    if any(q[1] <= 1.5 for q in ref.gorulen): YAK.add(i)
ADIM = int(os.environ.get("ADIM", "2")); PAN = {}
for t_ in os.environ.get("TOHUM", "0,1,2,3,4").split(","):
    harita.rng = np.random.default_rng(int(t_))
    kb = konum.KonumBulucu(); kb.ev_ayarla(EV); kb.ev = {}; oh = orb_hafiza.OrbHafiza(); e = {}; ilk = None; kab = red = 0
    for i, f in enumerate(R):
        if G[i] is None: continue
        kb.isle(G[i], f["t"], (f["sol"], f["sag"])); oh.gozle(G[i], f["sol"] != 0 or f["sag"] != 0)
        if i % ADIM == 0:
            oz = Y.cikar(nh.panorama(G[i]), kb.gorulen, oh.govde.kullan())
            r = harita.yerles(oz, *((kb.x, kb.z, kb.yaw) if kb.bilinen else (None, None, None)), yaricap=0.8)
            if r is not None and r[3] >= 25:
                k_ = max(1.0, math.sqrt(40.0 / r[3]))
                o = kb.harita_olcumu(r[0], r[1], r[2], 0.05 * k_, math.radians(2.0) * k_, r[3], f["t"])
                kab += o[4]; red += 1 - o[4]
                if ilk is None: ilk = (i, round(math.degrees(r[2]) - GY.get(i, float("nan")), 1))
        if kb.bilinen and i in GY: e[i] = abs(da(math.degrees(kb.yaw) - GY[i]))
    y = np.array([v for i, v in e.items() if i in YAK]); u = np.array([v for i, v in e.items() if i not in YAK])
    bolum = [np.median([v for i, v in e.items() if a <= i < a + 160] or [np.nan]) for a in range(0, len(R), 160)]
    print("tohum %s: kabul %d red %d, ilk olcum kare %s | yakin ortanca %.1f en cok %.1f | uzak ortanca %.1f %%90 %.1f en cok %.1f | 32 sn'lik bolumlerin ortancasi %s" % (
        t_, kab, red, ilk, np.median(y), y.max(), np.median(u), np.percentile(u, 90), u.max(), " ".join("%.1f" % b for b in bolum)), flush=True)
