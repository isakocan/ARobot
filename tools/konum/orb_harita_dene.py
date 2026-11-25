"""Seyrek 3B nokta haritasıyla yer bulma denemesi (PC).

Anahtar kare yöntemi yeri "en benzer anahtar karenin yeri" sayıyor (doğruluk kare aralığı kadar, yanlış kare seçilirse
daha kötü). Burada: aynı kayıttaki, yeri bilinen iki anahtar karede ortak görülen ORB noktaları üçgenlenir (iki ışının
en yakın noktası) -> noktanın odadaki 3B yeri. Soruda şimdiki karenin noktaları harita noktalarıyla eşleştirilir ve
robotun (x, z, yön) değeri, noktaların görülme yönlerini (boylam, enlem) en iyi açıklayacak şekilde çözülür
(RANSAC + sağlam Gauss-Newton). İşaretin yaptığının aynısı, ama yüzlerce noktayla ve işaretsiz.

Karşılaştırma (yeri bilinen soru karelerinde, süzgeç yok, tek kare): (a) anahtar kare yöntemi, (b) 3B harita.
Kullanım: python orb_harita_dene.py <kök> <soru_kaydı> <hafıza_kaydı> [...]"""
import json, math, os, sys, time
import cv2
import numpy as np

KOK, SORU, HAF = sys.argv[1], sys.argv[2], sys.argv[3:]
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
import konum, orb_hafiza, isaret

H = 0.17
if os.environ.get("ORB_UST"):                     # deney: panoramanın üst sınırı (tavan)
    orb_hafiza.UST = float(os.environ["ORB_UST"]); orb_hafiza.YUK = int((orb_hafiza.UST - orb_hafiza.ALT) * orb_hafiza.PX)
ARALIK = 0.10


def da(a): return (a + 180.0) % 360.0 - 180.0


def bilinen(K):
    R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
    EV = json.load(open(os.path.join(K, "ev.json")))["isaretler"]
    gy = os.path.join(K, "gercek_yon.json"); GY = {}
    if os.path.exists(gy):
        g_ = json.load(open(gy)); GY = dict(zip(g_["kare"], g_["yaw"]))
    kb = konum.KonumBulucu(); kb.ev_ayarla(EV); out = []
    for i, f in enumerate(R):
        g = cv2.imread(os.path.join(K, f["jpg"]), cv2.IMREAD_GRAYSCALE)
        if g is None: continue
        gor = isaret.bul(g)
        kb.isle(g, f["t"], (f["sol"], f["sag"]))
        if not (kb.bilinen and [q for q in kb.gorulen if q[1] <= 1.3] and math.degrees(math.sqrt(kb.P[2, 2])) < 4): continue
        if GY and (i not in GY or abs(da(math.degrees(kb.yaw) - GY[i])) > 4): continue
        out.append(dict(t=f["t"], x=kb.x, z=kb.z, yaw=kb.yaw, g=g, gor=gor))
    oh = kb.hafiza
    for o in out:
        o["oz"] = oh.ozellik(o["g"], o["gor"]); del o["g"]
    return [o for o in out if o["oz"] is not None], oh


def isin(lon, lat, yaw):
    """robot çerçevesindeki görülme yönleri (derece) -> dünya birim vektörleri (x, y yukarı, z)."""
    lo, la = np.radians(lon), np.radians(lat)
    r, u, f = np.cos(la) * np.sin(lo), np.sin(la), np.cos(la) * np.cos(lo)
    c, s = math.cos(yaw), math.sin(yaw)
    return np.stack([r * c + f * s, u, -r * s + f * c], 1)


def ucgenle(a, b, oh):
    """İki anahtar kare -> [(3B nokta, tanımlayıcı)]"""
    qa, qb = a["oz"], b["oz"]
    mm = oh.bf.knnMatch(qb[0], qa[0], k=2)
    iyi = [p[0] for p in mm if len(p) == 2 and p[0].distance < 0.8 * p[1].distance]
    if len(iyi) < 10: return []
    ia = np.array([m.trainIdx for m in iyi]); ib = np.array([m.queryIdx for m in iyi])
    da_ = isin(qa[1][ia], qa[2][ia], a["yaw"]); db_ = isin(qb[1][ib], qb[2][ib], b["yaw"])
    pa = np.array([a["x"], H, a["z"]]); pb = np.array([b["x"], H, b["z"]])
    w = pb - pa
    # iki ışının en yakın noktaları: pa + s*da, pb + t*db
    A_ = (da_ * da_).sum(1); B_ = (da_ * db_).sum(1); C_ = (db_ * db_).sum(1); D_ = (da_ * w).sum(1); E_ = (db_ * w).sum(1)
    den = A_ * C_ - B_ * B_
    ok = den > 1e-6
    s = np.where(ok, (D_ * C_ - B_ * E_) / np.where(ok, den, 1), 0); t = np.where(ok, (D_ * B_ - A_ * E_) / np.where(ok, den, 1), 0)
    P1 = pa + da_ * s[:, None]; P2 = pb + db_ * t[:, None]; P = (P1 + P2) / 2
    ayrik = np.linalg.norm(P1 - P2, axis=1)
    paralaks = np.degrees(np.arccos(np.clip(B_, -1, 1)))
    ok &= (s > 0.3) & (t > 0.3) & (s < 8) & (t < 8) & (paralaks > 2.0) & (ayrik < 0.02 + 0.02 * s) & (P[:, 1] > -0.05) & (P[:, 1] < 2.6)
    return [(P[k], qb[0][ib[k]]) for k in np.where(ok)[0]]


def harita_kur(K, kareler, oh):
    hk = []
    for k in kareler:
        if all(math.hypot(k["x"] - o["x"], k["z"] - o["z"]) >= ARALIK for o in hk): hk.append(k)
    noktalar = []
    for i in range(len(hk)):
        for j in range(i + 1, len(hk)):
            d = math.hypot(hk[i]["x"] - hk[j]["x"], hk[i]["z"] - hk[j]["z"])
            if 0.12 <= d <= 0.9: noktalar += ucgenle(hk[i], hk[j], oh)
    print("  %s: %d anahtar kare, %d 3B nokta" % (os.path.basename(K), len(hk), len(noktalar)))
    return hk, noktalar


def tahmin(P, x, z, yaw):
    v = P - np.array([x, H, z])
    c, s = math.cos(yaw), math.sin(yaw)
    r = v[:, 0] * c - v[:, 2] * s; f = v[:, 0] * s + v[:, 2] * c
    return np.degrees(np.arctan2(r, f)), np.degrees(np.arctan2(v[:, 1], np.hypot(r, f)))


def coz(P, lon, lat, x0, z0, y0, it=12):
    p = np.array([x0, z0, y0], float)
    for _ in range(it):
        lo, la = tahmin(P, *p)
        e = np.concatenate([da(lo - lon), la - lat])
        J = np.zeros((len(e), 3))
        for k in range(3):
            q = p.copy(); q[k] += 1e-4
            l2, a2 = tahmin(P, *q)
            J[:, k] = (np.concatenate([da(l2 - lon), a2 - lat]) - e) / 1e-4
        w = 1.0 / np.maximum(1.0, np.abs(e) / 1.5)          # Huber
        Jw = J * w[:, None]
        dp = np.linalg.lstsq(Jw.T @ J + 1e-6 * np.eye(3), -(Jw.T @ e), rcond=None)[0]
        p += dp
        if np.abs(dp).max() < 1e-4: break
    lo, la = tahmin(P, *p)
    return p, np.hypot(da(lo - lon), la - lat)


rng = np.random.default_rng(0)


def yerles(q, Pm, Dm, oh, x0, z0, y0):
    """3B haritayla yer: başlangıç (x0, z0, y0) çevresinde RANSAC."""
    mm = oh.bf.knnMatch(q["oz"][0], Dm, k=2)
    iyi = [p[0] for p in mm if len(p) == 2 and p[0].distance < 0.8 * p[1].distance]
    if len(iyi) < 8: return None
    P = Pm[[m.trainIdx for m in iyi]]; lon = q["oz"][1][[m.queryIdx for m in iyi]]; lat = q["oz"][2][[m.queryIdx for m in iyi]]
    en = (0, None)
    for _ in range(60):
        k = rng.choice(len(P), 3, replace=False)
        p, _ = coz(P[k], lon[k], lat[k], x0, z0, y0, it=6)
        if math.hypot(p[0] - x0, p[1] - z0) > 1.0: continue
        lo, la = tahmin(P, *p)
        u = np.hypot(da(lo - lon), la - lat) < 2.0
        if u.sum() > en[0]: en = (int(u.sum()), u)
    if en[0] < 8: return None
    u = en[1]
    p, r = coz(P[u], lon[u], lat[u], x0, z0, y0)
    lo, la = tahmin(P, *p)
    u = np.hypot(da(lo - lon), la - lat) < 2.0
    p, r = coz(P[u], lon[u], lat[u], *p)
    return p, int(u.sum())


S, ohs = bilinen(SORU)
print("soru %s: yeri bilinen %d kare" % (os.path.basename(SORU), len(S)))
HK, NOK = [], []
for K in HAF:
    k_, oh_ = bilinen(K)
    hk, nk = harita_kur(K, k_, oh_); HK += hk; NOK += nk
Pm = np.array([n[0] for n in NOK]); Dm = np.array([n[1] for n in NOK])
oh = ohs
e1, e2, y1, y2, n2, sure = [], [], [], [], [], []
for q in S[::2]:
    # (a) anahtar kare: en çok uyan kare
    en = (0, None, 0.0)
    for k in HK:
        n, kay = oh._bir(q["oz"], k["oz"])
        if n > en[0]: en = (n, k, kay)
    if en[0] < 20: continue
    k = en[1]; yk = k["yaw"] + math.radians(en[2])
    e1.append(math.hypot(k["x"] - q["x"], k["z"] - q["z"])); y1.append(abs(da(math.degrees(yk - q["yaw"]))))
    t0 = time.time()
    r = yerles(q, Pm, Dm, oh, k["x"], k["z"], yk)
    sure.append(time.time() - t0)
    if r is None:
        e2.append(e1[-1]); y2.append(y1[-1]); n2.append(0); continue
    p, n = r
    e2.append(math.hypot(p[0] - q["x"], p[1] - q["z"])); y2.append(abs(da(math.degrees(p[2] - q["yaw"])))); n2.append(n)
e1, e2, y1, y2 = map(np.array, (e1, e2, y1, y2))
print("tanima: %d / %d soru karesinde en iyi anahtar kare >= 20 uyan nokta" % (len(e1), len(S[::2])))
print("soru %d kare | anahtar kare: yer ortanca %.2f %%90 %.2f m, yon ortanca %.1f %%90 %.1f der" % (len(e1), np.median(e1), np.percentile(e1, 90), np.median(y1), np.percentile(y1, 90)))
print("           | 3B harita   : yer ortanca %.2f %%90 %.2f m, yon ortanca %.1f %%90 %.1f der | cozulen %%%d, uyan nokta ortanca %d, sure %.0f ms" % (
    np.median(e2), np.percentile(e2, 90), np.median(y2), np.percentile(y2, 90), 100 * np.mean(np.array(n2) > 0), np.median([n for n in n2 if n] or [0]), 1000 * np.median(sure)))
