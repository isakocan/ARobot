"""ORB anahtar kare eşleşmesini gösteren görseller (PC). Bir günün anahtar karesi ile başka günün karesi:
1) panorama + ORB noktaları (sarı), robotun gövdesi (gri) ayıklanmış;
2) üstte anahtar kare, altta şimdiki kare; yeşil = aynı kaymaya uyan (doğru) eşleşme, kırmızı = reddedilen.
Kullanım: python orb_gorsel.py <kök> <hafıza_kaydı> <soru_kaydı> <çıktı_klasörü>"""
import json, math, os, sys
import cv2
import numpy as np

KOK, HK, SK, OUT = sys.argv[1:5]
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
import konum, orb_hafiza, isaret


def bilinenler(K):
    R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
    kb = konum.KonumBulucu(); kb.ev_ayarla(json.load(open(os.path.join(K, "ev.json")))["isaretler"]); out = []
    for f in R:
        g = cv2.imread(os.path.join(K, f["jpg"]), cv2.IMREAD_GRAYSCALE)
        if g is None: continue
        kb.isle(g, f["t"], (f["sol"], f["sag"]))
        if kb.bilinen and [q for q in kb.gorulen if q[1] <= 1.3] and math.degrees(math.sqrt(kb.P[2, 2])) < 4:
            out.append((kb.x, kb.z, math.degrees(kb.yaw), os.path.join(K, f["jpg"])))
    return out, kb.hafiza


H, h1 = bilinenler(HK)
S, h2 = bilinenler(SK)
oh = h2                                   # gövde maskesi soru kaydında öğrenildi
# yakın yerde ama yönü farklı bir çift: aday çiftlerden en çok uyan noktalısı (kayma görünsün)
en = None; oz = {}
def O(p):
    if p not in oz:
        g_ = cv2.imread(p, 0); oz[p] = oh.ozellik(g_, isaret.bul(g_))
    return oz[p]
for i in range(0, len(S), 5):
    x, z, y, p = S[i]
    for hx, hz, hy, hp in H[::2]:
        d = math.hypot(x - hx, z - hz); dy = abs((y - hy + 180) % 360 - 180)
        if d < 0.2 and 25 < dy < 150:
            n_, _ = oh._bir(O(p), O(hp))
            if en is None or n_ > en[0]: en = (n_, (hx, hz, hy, hp), (x, z, y, p))
_, A, B = en
ga, gb = cv2.imread(A[3], 0), cv2.imread(B[3], 0)
qa, qb = O(A[3]), O(B[3])
pa = cv2.remap(ga, oh.mx, oh.my, cv2.INTER_LINEAR); pb = cv2.remap(gb, oh.mx, oh.my, cv2.INTER_LINEAR)
G, Y, PX, UST = orb_hafiza.GEN, orb_hafiza.YUK, orb_hafiza.PX, orb_hafiza.UST


def w(a): return (a + 180) % 360 - 180


def xy(lon, lat): return int((lon / 360.0 + 0.5) * G), int((UST - lat) * PX)


# 1) noktalar
m = cv2.resize(oh.govde.kullan().astype(np.uint8), (G, Y), interpolation=cv2.INTER_NEAREST)
c = cv2.cvtColor(pb, cv2.COLOR_GRAY2BGR)
c[m == 0] = (c[m == 0] * 0.35 + np.array([90, 90, 90]) * 0.65).astype(np.uint8)
for lo, la in zip(qb[1], qb[2]): cv2.circle(c, xy(lo, la), 3, (0, 215, 255), 1, cv2.LINE_AA)
cv2.putText(c, "%d ORB noktasi (sari). Gri: robotun kendi govdesi, kullanilmiyor" % len(qb[1]), (10, Y - 12), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2, cv2.LINE_AA)
cv2.imwrite(os.path.join(OUT, "orb_1_noktalar.jpg"), c)

# 2) eşleşmeler
mm = oh.bf.knnMatch(qb[0], qa[0], k=2)
iyi = [p[0] for p in mm if len(p) == 2 and p[0].distance < orb_hafiza.ORAN * p[1].distance]
dl = np.array([((qb[1][a.queryIdx] - qa[1][a.trainIdx] + 180) % 360) - 180 for a in iyi])
db = np.array([qb[2][a.queryIdx] - qa[2][a.trainIdx] for a in iyi])
n, kay = oh._bir(qb, qa)
uy = (np.abs(((dl + kay + 180) % 360) - 180) < orb_hafiza.TOL) & (np.abs(db) < orb_hafiza.ENLEM_TOL)
ust = cv2.cvtColor(pa, cv2.COLOR_GRAY2BGR); alt = cv2.cvtColor(pb, cv2.COLOR_GRAY2BGR)
bos = np.zeros((40, G, 3), np.uint8)
im = np.vstack([ust, bos, alt])
for a, u in zip(iyi, uy):
    p1 = xy(qa[1][a.trainIdx], qa[2][a.trainIdx]); p2 = xy(qb[1][a.queryIdx], qb[2][a.queryIdx]); p2 = (p2[0], p2[1] + Y + 40)
    if abs(p1[0] - p2[0]) > G / 2: continue       # panoramanın kenarından dolananı çizme
    cv2.line(im, p1, p2, (60, 220, 60) if u else (60, 60, 230), 1, cv2.LINE_AA)
gun = lambda K: os.path.basename(K)[:8]
cv2.putText(im, "Anahtar kare (%s): yer (%.2f, %.2f) yon %.0f" % (gun(HK), A[0], A[1], w(A[2])), (10, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2, cv2.LINE_AA)
cv2.putText(im, "Simdiki kare (%s, baska gun): gercek yon %.0f" % (gun(SK), w(B[2])), (10, Y + 40 + 28), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2, cv2.LINE_AA)
tah = A[2] + kay; e = ((tah - B[2] + 180) % 360) - 180
cv2.putText(im, "Esles: %d, ayni kaymaya uyan (yesil) %d -> robot %.0f derece donmus, tahmini yon %.0f (hata %.1f)" % (len(iyi), n, kay, w(tah), e),
            (10, Y + 28), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 215, 255), 2, cv2.LINE_AA)
cv2.imwrite(os.path.join(OUT, "orb_2_eslesme.jpg"), im)
print("cift: yer farki %.2f m, anahtar kare yon %.0f, simdiki %.0f; eslesme %d, uyan %d, kayma %.1f, hata %.1f" % (math.hypot(A[0]-B[0], A[1]-B[1]), w(A[2]), w(B[2]), len(iyi), n, kay, e))
