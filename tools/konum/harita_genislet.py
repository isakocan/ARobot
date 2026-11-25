"""Nokta haritasını işaretten uzağa büyütme ve işaretsiz konum denemesi (PC).

1. Harita kaydı (M): konum.py işaretli oynatılır. İşaret yakınken (<= 1,3 m, yön belirsizliği < 4 derece) robotun yeri
   kesin: anahtar kare o yerle eklenir. İşaret uzakken yer, önceki karenin yeri + konum.py'nin hareketi (zemin izi,
   pusula) ile tahmin edilir ve haritayla düzeltilir; haritayla bulunan yerde yeni anahtar kare eklenir (harita büyür).
2. Test kaydı (T, jiroskoplu): işaret HİÇ kullanılmaz. konum.py yalnız harita ölçümleriyle (harita_olcumu) çalışır;
   başta yer de haritadan bulunur. Yön her karede jiroskopla karşılaştırılır: işaretin yakını / uzağı ayrı.
   Karşılaştırma: bugünkü sistem (işaret görünce düzeltir, uzakta yalnız tahmin).
Kullanım: python harita_genislet.py <kök> <harita_kaydı> <test_kaydı> [çıktı.png]"""
import json, math, os, sys, time
import cv2
import numpy as np

KOK, M, T = sys.argv[1:4]
CIKTI = sys.argv[4] if len(sys.argv) > 4 else None
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import konum, orb_hafiza, isaret
import nokta_harita as nh

konum.HAFIZA_ORB = False                 # eski anahtar kare hafızası bu denemede kapalı


def da(a): return (a + 180.0) % 360.0 - 180.0


def yukle(K):
    R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
    EV = json.load(open(os.path.join(K, "ev.json")))["isaretler"]
    gy = os.path.join(K, "gercek_yon.json"); GY = {}
    if os.path.exists(gy):
        g_ = json.load(open(gy)); GY = dict(zip(g_["kare"], g_["yaw"]))
    return R, EV, GY


def goreli(a, b):
    """a'dan b'ye robot çerçevesinde hareket: (ileri, sağ, dönüş)"""
    dx, dz = b[0] - a[0], b[1] - a[1]; c, s = math.cos(a[2]), math.sin(a[2])
    return dx * s + dz * c, dx * c - dz * s, b[2] - a[2]


def uygula(p, d):
    ileri, sag, dy = d; c, s = math.cos(p[2]), math.sin(p[2])
    return (p[0] + ileri * s + sag * c, p[1] + ileri * c - sag * s, p[2] + dy)


Y = nh.XFeat()

# ------------------------------------------------------------------ 1. harita
R, EV, _ = yukle(M)
kb = konum.KonumBulucu(); kb.ev_ayarla(EV)                  # işaretli: yakında kesin yer
od = konum.KonumBulucu(); od.ev_ayarla(EV); od_kur = False   # hareket: ilk kilitten sonra işaretsiz
oh = orb_hafiza.OrbHafiza()                                  # gövde maskesi
harita = nh.Harita(Y)
hp = None; onceki_od = None; yol = []; t0 = time.time(); say = {"isaret": 0, "harita": 0, "tahmin": 0}
for i, f in enumerate(R):
    g = cv2.imread(os.path.join(M, f["jpg"]), cv2.IMREAD_GRAYSCALE)
    if g is None: continue
    kb.isle(g, f["t"], (f["sol"], f["sag"]))
    if od_kur: od.ev = {}
    od.isle(g, f["t"], (f["sol"], f["sag"])); od_kur = od_kur or od.bilinen
    oh.gozle(g, f["sol"] != 0 or f["sag"] != 0)
    odp = (od.x, od.z, od.yaw) if od.bilinen else None
    yakin = kb.bilinen and [q for q in kb.gorulen if q[1] <= 1.3] and math.degrees(math.sqrt(kb.P[2, 2])) < 4
    oz = None
    if yakin:
        hp = (kb.x, kb.z, kb.yaw); kaynak = "isaret"
    elif hp is not None and odp is not None and onceki_od is not None:
        hp = uygula(hp, goreli(onceki_od, odp)); kaynak = "tahmin"
        if i % 2 == 0:
            oz = Y.cikar(nh.panorama(g), kb.gorulen, oh.govde.kullan())
            r = harita.yerles(oz, hp[0], hp[1], hp[2], yaricap=0.8)
            if r is not None and r[3] >= 25:
                hp = (r[0], r[1], r[2]); kaynak = "harita"
            else:
                kaynak = "tahmin"
    else:
        kaynak = None
    onceki_od = odp
    if kaynak:
        say[kaynak] += 1; yol.append((hp[0], hp[1], kaynak))
        if kaynak in ("isaret", "harita") and harita.bos_yer(hp[0], hp[1]):
            if oz is None: oz = Y.cikar(nh.panorama(g), kb.gorulen, oh.govde.kullan())
            harita.ekle(oz, hp[0], hp[1], hp[2], kaynak)
kk = harita.kareler
uz = [math.hypot(k["x"], k["z"]) for k in kk]
print("harita (%s): %d anahtar kare (isaretten %d, haritadan %d), %d 3B nokta; isarete uzaklik en cok %.1f m; kare kaynagi isaret %d / harita %d / tahmin %d (%.0f sn)" % (
    os.path.basename(M), len(kk), sum(k["kaynak"] == "isaret" for k in kk), sum(k["kaynak"] == "harita" for k in kk),
    harita.nokta_sayisi(), max(uz) if uz else 0, say["isaret"], say["harita"], say["tahmin"], time.time() - t0), flush=True)

# ------------------------------------------------------------------ 2. test
R, EV, GY = yukle(T)
# gerçek yer (işaretin yakından ve jiroskopla tutarlı görüldüğü kareler) ve "yakın" bilgisi: işaretli normal oynatma
ref = konum.KonumBulucu(); ref.ev_ayarla(EV); GER = {}; YAKIN = set(); NORMAL = {}
for i, f in enumerate(R):
    g = cv2.imread(os.path.join(T, f["jpg"]), cv2.IMREAD_GRAYSCALE)
    if g is None: continue
    ref.isle(g, f["t"], (f["sol"], f["sag"]))
    if any(q[1] <= 1.5 for q in ref.gorulen): YAKIN.add(i)
    if ref.bilinen: NORMAL[i] = math.degrees(ref.yaw)
    if ref.bilinen and [q for q in ref.gorulen if q[1] <= 1.3] and math.degrees(math.sqrt(ref.P[2, 2])) < 4 and i in GY and abs(da(math.degrees(ref.yaw) - GY[i])) < 4:
        GER[i] = (ref.x, ref.z)

kb = konum.KonumBulucu(); kb.ev_ayarla(EV); kb.ev = {}       # işaret hiç yok
oh = orb_hafiza.OrbHafiza(); ey, ep, kabul, olc = {}, [], 0, 0; yerT = []
for i, f in enumerate(R):
    g = cv2.imread(os.path.join(T, f["jpg"]), cv2.IMREAD_GRAYSCALE)
    if g is None: continue
    kb.isle(g, f["t"], (f["sol"], f["sag"]))
    oh.gozle(g, f["sol"] != 0 or f["sag"] != 0)
    if i % 2 == 0:
        oz = Y.cikar(nh.panorama(g), kb.gorulen, oh.govde.kullan())
        r = harita.yerles(oz, *((kb.x, kb.z, kb.yaw) if kb.bilinen else (None, None, None)), yaricap=0.8)
        if r is not None and r[3] >= 25:
            k_ = max(1.0, math.sqrt(40.0 / r[3]))
            o = kb.harita_olcumu(r[0], r[1], r[2], 0.05 * k_, math.radians(2.0) * k_, r[3], f["t"]); olc += 1; kabul += o[4]
    if kb.bilinen:
        yerT.append((kb.x, kb.z))
        if i in GY: ey[i] = abs(da(math.degrees(kb.yaw) - GY[i]))
        if i in GER: ep.append(math.hypot(kb.x - GER[i][0], kb.z - GER[i][1]))


def ozet(d, ad):
    a = np.array(list(d.values()))
    if not len(a): return "%s: -" % ad
    return "%s %3d kare: ortanca %4.1f, %%90 %5.1f, en cok %5.1f der" % (ad, len(a), np.median(a), np.percentile(a, 90), a.max())


en = {i: abs(da(NORMAL[i] - GY[i])) for i in NORMAL if i in GY}
print("test (%s), isaret HIC kullanilmadan, yalniz harita: %d olcum, %d kabul; ilk konum karesi %s" % (os.path.basename(T), olc, kabul, min(ey) if ey else "-"))
for ad, d in (("harita", ey), ("bugunku sistem (isaretli)", en)):
    print("  %-26s yon | %s | %s" % (ad, ozet({i: v for i, v in d.items() if i in YAKIN}, "isaret yakinda"), ozet({i: v for i, v in d.items() if i not in YAKIN}, "isaret uzakta/yok")))
if ep: print("  harita: yer hatasi (isaretin yakindan gorundugu karelerde, isaret kullanilmadan) ortanca %.2f %%90 %.2f m" % (np.median(ep), np.percentile(ep, 90)))

if CIKTI:
    S = 120; ox, oz0 = 3.2, 1.2; W, Hh = int(6.4 * S), int(4.2 * S)
    im = np.full((Hh, W, 3), 255, np.uint8)
    def px(x, z): return int((x + ox) * S), int(Hh - (z + oz0) * S)
    for x in range(-3, 4):
        cv2.line(im, px(x, -1.2), px(x, 3.0), (235, 235, 235), 1)
    for z in range(-1, 4):
        cv2.line(im, px(-3.2, z), px(3.2, z), (235, 235, 235), 1)
    for k in kk:
        for P in k["P"].values():
            cv2.circle(im, px(P[0], P[2]), 1, (170, 170, 170), -1)
    for x, z, s in yol:
        cv2.circle(im, px(x, z), 2, {"isaret": (60, 160, 60), "harita": (200, 120, 30), "tahmin": (60, 60, 220)}[s], -1)
    for k in kk:
        cv2.circle(im, px(k["x"], k["z"]), 6, (60, 160, 60) if k["kaynak"] == "isaret" else (200, 120, 30), 2)
    cv2.rectangle(im, px(-0.1, 0.03), px(0.1, -0.03), (0, 0, 0), -1)
    cv2.putText(im, "ArUco", (px(0.12, 0)[0], px(0, 0)[1] + 5), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 0, 0), 1, cv2.LINE_AA)
    for j, (t_, c_) in enumerate((("yesil: isaretten bilinen yer", (60, 160, 60)), ("mavi: haritadan bulunan yer", (200, 120, 30)),
                                   ("kirmizi: yalniz tahmin", (60, 60, 220)), ("halka: anahtar kare, gri: 3B noktalar (ustten)", (120, 120, 120)))):
        cv2.putText(im, t_, (10, 22 + 20 * j), cv2.FONT_HERSHEY_SIMPLEX, 0.5, c_, 1, cv2.LINE_AA)
    cv2.putText(im, "1 m izgara", (W - 100, Hh - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (120, 120, 120), 1, cv2.LINE_AA)
    cv2.imwrite(CIKTI, im)
