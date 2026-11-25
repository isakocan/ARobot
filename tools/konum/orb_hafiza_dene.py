"""Nokta eşleşmeli anahtar kare (ORB) ile yer tanıma denemesi (PC). hafiza_olc.py ile aynı düzen: işarete yakın, yeri
bilinen karelerden bir kayıtta hafıza, başka kayıtta soru; şimdiki ufuk şeridi yöntemiyle karşılaştırma.

Görüntü: ham çift balık gözünden eş dikdörtgen panorama (boylam -180..180, enlem ALT..UST, derece başına PX piksel).
Robotun kendi gövdesi (kayıtta robot hareket ederken değişmeyen pikseller) maskelenir. Eşleşme: ORB tanımlayıcıları,
Lowe oran testi, sonra geometrik doğrulama: düz zeminde robot yalnız döner / kayar, bu yüzden doğru eşleşmelerin hepsi
aşağı yukarı aynı boylam kaymasını (= yön farkı) ve küçük enlem farkını verir. O kaymaya uyan eşleşme sayısı = puan.
Kullanım: python orb_hafiza_dene.py <kök> <kayıt_A> <kayıt_B> ..."""
import json, math, os, sys
import cv2
import numpy as np

KOK = sys.argv[1]; KAYITLAR = sys.argv[2:]
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
import konum, teta, hafiza

YAKIN = 1.3
ARALIK = float(os.environ.get("ARALIK", "0.10"))
PX, ALT, UST = 4, -25.0, 65.0
GEN, YUK = 360 * PX, int((UST - ALT) * PX)
MX, MY = teta.es_dikdortgen_haritasi(GEN, YUK, ALT, UST)
orb = cv2.ORB_create(nfeatures=int(os.environ.get("NOKTA", "1500")), scaleFactor=1.2, nlevels=6, edgeThreshold=15, patchSize=21)
TOL = float(os.environ.get("TOL", "6"))     # derece: boylam kayması uyum toleransı (yakın nesnelerde paralaks)


def panorama(g):
    return cv2.remap(g, MX, MY, cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0)


def oku(K):
    R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
    EV = json.load(open(os.path.join(K, "ev.json")))["isaretler"]
    kb = konum.KonumBulucu(); kb.ev_ayarla(EV); bil, hareket = [], []
    gy = os.path.join(K, "gercek_yon.json"); GY = {}
    if os.path.exists(gy):                    # jiroskop gercegi varsa: yonu ondan 4 dereceden fazla sasan kareler "bilinen" sayilmaz
        g_ = json.load(open(gy)); GY = dict(zip(g_["kare"], g_["yaw"]))
    onceki = None
    for i, f in enumerate(R):
        g = cv2.imread(os.path.join(K, f["jpg"]), cv2.IMREAD_GRAYSCALE)
        if g is None: continue
        kb.isle(g, f["t"], (f["sol"], f["sag"]))
        p = panorama(g)
        if GY and (i not in GY or abs(da(math.degrees(kb.yaw) - GY[i])) > 4): continue
        if f["sol"] or f["sag"]:
            if onceki is not None and len(hareket) < 150: hareket.append(cv2.absdiff(p, onceki))
        onceki = p
        yakin = [q for q in kb.gorulen if q[1] <= YAKIN]
        if kb.bilinen and yakin and math.degrees(math.sqrt(kb.P[2, 2])) < 4:
            bil.append((f["t"], kb.x, kb.z, kb.yaw, p, hafiza.Hafiza.ozellik(kb.pusula.onceki)))
    # gövde maskesi: robot hareket ederken neredeyse hiç değişmeyen pikseller
    m = np.ones((YUK, GEN), np.uint8) * 255
    if hareket:
        fark = cv2.GaussianBlur(np.median(np.stack(hareket), 0).astype(np.float32), (0, 0), 3)
        m[fark < 2.0] = 0
        m = cv2.erode(m, np.ones((7, 7), np.uint8))
    m[panorama(np.full((720, 1280), 255, np.uint8)) == 0] = 0
    out = []
    for t, x, z, yaw, p, F in bil:
        kp, d = orb.detectAndCompute(p, m)
        lon = np.array([((k.pt[0] / GEN) - 0.5) * 360.0 for k in kp]); lat = np.array([UST - k.pt[1] / PX for k in kp])
        out.append(dict(t=t, x=x, z=z, yaw=yaw, d=d, lon=lon, lat=lat, F=F))
    return out, m


def da(a): return (a + 180.0) % 360.0 - 180.0


bf = cv2.BFMatcher(cv2.NORM_HAMMING)


def eslestir(q, k):
    """-> (uyan eşleşme sayısı, yön farkı derece: k'dan q'ya sağa dönüş +)."""
    if q["d"] is None or k["d"] is None or len(q["d"]) < 10 or len(k["d"]) < 10: return 0, 0.0
    mm = bf.knnMatch(q["d"], k["d"], k=2)
    iyi = [a for a in (p for p in mm if len(p) == 2) if a[0].distance < 0.8 * a[1].distance]
    if len(iyi) < 6: return 0, 0.0
    qi = np.array([a[0].queryIdx for a in iyi]); ki = np.array([a[0].trainIdx for a in iyi])
    dl = da(q["lon"][qi] - k["lon"][ki]); db = q["lat"][qi] - k["lat"][ki]
    en, kay = 0, 0.0
    for c in dl:                              # 1B RANSAC (her aday kayma)
        u = (np.abs(da(dl - c)) < TOL) & (np.abs(db) < 4.0)
        if u.sum() > en:
            en = int(u.sum()); kay = float(np.median(dl[u]))
    return en, -kay


def dene(H, Q, ad):
    hk = []
    for k in H:                               # ARALIK m'de bir anahtar kare
        if all(math.hypot(k["x"] - o["x"], k["z"] - o["z"]) >= ARALIK for o in hk): hk.append(k)
    ep, ey, sk_d, sk_y, en_iyi, rast = [], [], [], [], [], []
    for q in Q:
        sonuc = sorted(((eslestir(q, k), k) for k in hk), key=lambda a: -a[0][0])
        (n, kay), k = sonuc[0]
        if n == 0: continue
        e = math.hypot(k["x"] - q["x"], k["z"] - q["z"]); ep.append(e)
        ey.append(abs(da(math.degrees(k["yaw"]) + kay - math.degrees(q["yaw"]))))
        (sk_d if e < 0.4 else sk_y).append(n)
        dd = np.array([math.hypot(o["x"] - q["x"], o["z"] - q["z"]) for o in hk]); en_iyi.append(dd.min()); rast.append(np.median(dd))
    ep, ey = np.array(ep), np.array(ey)
    print("%-34s hafiza %3d, soru %3d | yer ortanca %.2f m %%90 %.2f, <0,4 m %%%3d | yon ortanca %4.1f %%90 %5.1f der, <10 der %%%3d | uyan nokta dogru %d / yanlis %d | sans %.2f m" % (
        ad, len(hk), len(ep), np.median(ep), np.percentile(ep, 90), 100 * np.mean(ep < 0.4), np.median(ey), np.percentile(ey, 90),
        100 * np.mean(ey < 10), np.median(sk_d) if sk_d else -1, np.median(sk_y) if sk_y else -1, np.median(rast)))


V = {}
for K in KAYITLAR:
    v, m = oku(K); V[K] = v
    cv2.imwrite(os.path.join(os.environ.get("CIKTI", "."), "orb_maske_%s.png" % os.path.basename(K)), m)
    print("%s: yeri bilinen kare %d, kare basina nokta ortanca %d" % (os.path.basename(K), len(v), np.median([len(q["lon"]) for q in v])))
ks = list(V)
for a in ks:
    for b in ks:
        if a != b:
            dene(V[a], V[b], "%s -> %s" % (os.path.basename(a)[:15], os.path.basename(b)[:15]))
