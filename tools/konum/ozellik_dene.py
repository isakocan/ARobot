"""Nokta yöntemlerini farklı günler arası yer tanımada karşılaştırır (PC, torch + LightGlue + XFeat).

Yöntemler: ORB (robottaki), XFeat (hafif öğrenilmiş), SuperPoint+LightGlue, ALIKED+LightGlue, DISK+LightGlue.
Düzen orb_harita_dene.py ile aynı: hafıza kaydının yeri bilinen karelerinden 10 cm'de bir anahtar kare + 3B nokta haritası
(aynı kayıttaki anahtar kare çiftlerinden üçgenleme); soru kaydının yeri bilinen kareleri (işaretten, jiroskop varsa
ondan da süzülmüş) sorulur. İşaretin üstündeki ve robotun gövdesindeki noktalar atılır. Panorama enlem -25..85.
Ölçüm: tanınan soru oranı (en iyi anahtar karede >= ESIK uyan nokta), anahtar kare yöntemiyle yer/yön hatası, 3B
haritayla çözülen oran ve yer/yön hatası, kare başına süre.
Kullanım: python ozellik_dene.py <kök> <yöntemler,virgülle> <soru_kaydı> <hafıza_kaydı> [...]"""
import json, math, os, sys, time
import cv2
import numpy as np
import torch

KOK, YONTEMLER, SORU, HAF = sys.argv[1], sys.argv[2].split(","), sys.argv[3], sys.argv[4:]
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
import konum, orb_hafiza, isaret

PX, ALT, UST = 4, -25.0, 85.0
GEN, YUK = 360 * PX, int((UST - ALT) * PX)
H = 0.17; ARALIK = 0.10; TOL = 6.0; ENLEM_TOL = 4.0
ONBELLEK = os.environ.get("ONBELLEK", os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".onbellek_konum"))
CIHAZ = "cuda" if torch.cuda.is_available() else "cpu"
import teta
MX, MY = teta.es_dikdortgen_haritasi(GEN, YUK, ALT, UST)


def da(a): return (a + 180.0) % 360.0 - 180.0


# ------------------------------------------------------------------ kayıt -> yeri bilinen kareler (önbellekli)
def bilinen(K):
    os.makedirs(ONBELLEK, exist_ok=True)
    f_ = os.path.join(ONBELLEK, os.path.basename(K) + "_bilinen.npz")
    if os.path.exists(f_):
        z = np.load(f_, allow_pickle=True)
        return list(z["kareler"]), z["govde"]
    R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
    EV = json.load(open(os.path.join(K, "ev.json")))["isaretler"]
    gy = os.path.join(K, "gercek_yon.json"); GY = {}
    if os.path.exists(gy):
        g_ = json.load(open(gy)); GY = dict(zip(g_["kare"], g_["yaw"]))
    konum.HAFIZA_ORB = False
    kb = konum.KonumBulucu(); kb.ev_ayarla(EV)
    oh = orb_hafiza.OrbHafiza()                    # yalnız gövde maskesi için
    out = []
    for i, f in enumerate(R):
        g = cv2.imread(os.path.join(K, f["jpg"]), cv2.IMREAD_GRAYSCALE)
        if g is None: continue
        kb.isle(g, f["t"], (f["sol"], f["sag"]))
        oh.gozle(g, f["sol"] != 0 or f["sag"] != 0)
        if not (kb.bilinen and [q for q in kb.gorulen if q[1] <= 1.3] and math.degrees(math.sqrt(kb.P[2, 2])) < 4): continue
        if GY and (i not in GY or abs(da(math.degrees(kb.yaw) - GY[i])) > 4): continue
        gor = [(q[0], q[1], q[2], q[3]) for q in kb.gorulen]       # id, mesafe, yon, y
        out.append(dict(i=i, x=kb.x, z=kb.z, yaw=kb.yaw, pan=cv2.remap(g, MX, MY, cv2.INTER_LINEAR), gor=gor))
    gm = oh.govde.kullan().astype(np.uint8)
    np.savez_compressed(f_, kareler=np.array(out, dtype=object), govde=gm)
    return out, gm


def nokta_maskesi(lon, lat, gor, govde):
    """True = kullan: işaretin üstü ve robotun gövdesi değil."""
    tut = np.ones(len(lon), bool)
    for _, mes, yon, y in gor:
        r = math.degrees(math.atan2(0.18, max(0.1, mes))) + 3.0
        ey = math.degrees(math.atan2(abs(y), max(0.1, mes)))
        tut &= ~((np.abs(da(lon - yon)) < r) & (np.abs(lat) < ey + r))
    gh, gw = govde.shape                                         # gövde maskesi enlem ALT..65 (orb_hafiza)
    gx = np.clip(((lon / 360.0 + 0.5) * gw).astype(int), 0, gw - 1)
    gy = ((orb_hafiza.UST - lat) * gh / (orb_hafiza.UST - orb_hafiza.ALT)).astype(int)
    ic = (gy >= 0) & (gy < gh)
    g = np.ones(len(lon), bool); g[ic] = govde[gy[ic], gx[ic]] > 0
    return tut & g


# ------------------------------------------------------------------ yöntemler
class Yontem:
    def __init__(self, ad):
        self.ad = ad; self.lg = None
        if ad == "orb":
            self.orb = cv2.ORB_create(nfeatures=1500, scaleFactor=1.2, nlevels=6, edgeThreshold=15, patchSize=21)
            self.bf = cv2.BFMatcher(cv2.NORM_HAMMING)
        elif ad == "xfeat":
            self.m = torch.hub.load("verlab/accelerated_features", "XFeat", pretrained=True, top_k=1500, trust_repo=True, verbose=False)
        else:
            from lightglue import SuperPoint, ALIKED, DISK, LightGlue
            F = {"superpoint": SuperPoint, "aliked": ALIKED, "disk": DISK}[ad]
            self.m = F(max_num_keypoints=1500).eval().to(CIHAZ)
            self.lg = LightGlue(features=ad).eval().to(CIHAZ)

    @torch.no_grad()
    def cikar(self, pan, gor, govde):
        if self.ad == "orb":
            kp, d = self.orb.detectAndCompute(pan, None)
            xy = np.array([k.pt for k in kp], np.float32); ek = None
        elif self.ad == "xfeat":
            t = torch.from_numpy(pan).float()[None, None].repeat(1, 3, 1, 1).to(CIHAZ) / 255.0
            o = self.m.detectAndCompute(t, top_k=1500)[0]
            xy = o["keypoints"].cpu().numpy(); d = o["descriptors"].cpu().numpy(); ek = None
        else:
            t = torch.from_numpy(pan).float()[None, None].to(CIHAZ) / 255.0
            if self.ad != "superpoint": t = t.repeat(1, 3, 1, 1)
            o = self.m.extract(t)
            xy = o["keypoints"][0].cpu().numpy(); d = o["descriptors"][0].cpu().numpy()
            ek = {k: v for k, v in o.items()}
        lon = (xy[:, 0] / GEN - 0.5) * 360.0; lat = UST - xy[:, 1] / PX
        m = nokta_maskesi(lon, lat, gor, govde)
        r = dict(lon=lon[m].astype(np.float32), lat=lat[m].astype(np.float32), d=d[m])
        if ek is not None:
            idx = torch.from_numpy(np.where(m)[0]).to(CIHAZ)
            r["lg"] = {"keypoints": ek["keypoints"][:, idx], "descriptors": ek["descriptors"][:, idx],
                       "image_size": ek["image_size"]}
            if "keypoint_scores" in ek: r["lg"]["keypoint_scores"] = ek["keypoint_scores"][:, idx]
        return r

    @torch.no_grad()
    def esle(self, a, b):
        """-> (a indeksleri, b indeksleri)"""
        if len(a["d"]) < 8 or len(b["d"]) < 8: return np.zeros(0, int), np.zeros(0, int)
        if self.ad == "orb":
            mm = self.bf.knnMatch(a["d"], b["d"], k=2)
            iyi = [p[0] for p in mm if len(p) == 2 and p[0].distance < 0.8 * p[1].distance]
            return np.array([m.queryIdx for m in iyi], int), np.array([m.trainIdx for m in iyi], int)
        if self.lg is not None:
            o = self.lg({"image0": a["lg"], "image1": b["lg"]})
            m = o["matches"][0].cpu().numpy()
            return m[:, 0], m[:, 1]
        da_ = torch.from_numpy(a["d"]).to(CIHAZ); db_ = torch.from_numpy(b["d"]).to(CIHAZ)   # XFeat: karşılıklı en yakın
        s = da_ @ db_.T
        i1 = s.argmax(1); i2 = s.argmax(0)
        ok = i2[i1] == torch.arange(len(i1), device=CIHAZ)
        ok &= s.max(1).values > 0.82
        ia = torch.where(ok)[0]
        return ia.cpu().numpy(), i1[ia].cpu().numpy()


def kayma(q, k, ia, ib):
    """Boylam kayması doğrulaması -> (uyan sayısı, kayma derece: anahtar kareden şimdiye sağa +, uyan maskesi)"""
    if len(ia) < 6: return 0, 0.0, None
    dl = da(q["lon"][ia] - k["lon"][ib]); db = q["lat"][ia] - k["lat"][ib]
    en, kay, um = 0, 0.0, None
    for c in np.unique(np.round(dl / 2.0) * 2.0):
        u = (np.abs(da(dl - c)) < TOL) & (np.abs(db) < ENLEM_TOL)
        if u.sum() > en: en, kay, um = int(u.sum()), float(np.median(dl[u])), u
    return en, -kay, um


def isin(lon, lat, yaw):
    lo, la = np.radians(lon), np.radians(lat)
    r, u, f = np.cos(la) * np.sin(lo), np.sin(la), np.cos(la) * np.cos(lo)
    c, s = math.cos(yaw), math.sin(yaw)
    return np.stack([r * c + f * s, u, -r * s + f * c], 1)


def ucgenle(Y, a, b):
    ia, ib = Y.esle(b["oz"], a["oz"])                # b soru gibi
    n, _, um = kayma(b["oz"], a["oz"], ia, ib)
    if n < 8: return []
    ia, ib = ia[um], ib[um]
    qa, qb = a["oz"], b["oz"]
    da_ = isin(qa["lon"][ib], qa["lat"][ib], a["yaw"]); db_ = isin(qb["lon"][ia], qb["lat"][ia], b["yaw"])
    pa = np.array([a["x"], H, a["z"]]); pb = np.array([b["x"], H, b["z"]]); w = pb - pa
    A_ = (da_ * da_).sum(1); B_ = (da_ * db_).sum(1); C_ = (db_ * db_).sum(1); D_ = (da_ * w).sum(1); E_ = (db_ * w).sum(1)
    den = A_ * C_ - B_ * B_; ok = den > 1e-6
    s = np.where(ok, (D_ * C_ - B_ * E_) / np.where(ok, den, 1), 0); t = np.where(ok, (D_ * B_ - A_ * E_) / np.where(ok, den, 1), 0)
    P1 = pa + da_ * s[:, None]; P2 = pb + db_ * t[:, None]; P = (P1 + P2) / 2
    ayrik = np.linalg.norm(P1 - P2, axis=1); paralaks = np.degrees(np.arccos(np.clip(B_, -1, 1)))
    ok &= (s > 0.3) & (t > 0.3) & (s < 8) & (t < 8) & (paralaks > 2.0) & (ayrik < 0.02 + 0.02 * s) & (P[:, 1] > -0.05) & (P[:, 1] < 3.0)
    return [(P[k], b["oz"]["d"][ia[k]], b, ia[k]) for k in np.where(ok)[0]]


def tahmin(P, x, z, yaw):
    v = P - np.array([x, H, z]); c, s = math.cos(yaw), math.sin(yaw)
    r = v[:, 0] * c - v[:, 2] * s; f = v[:, 0] * s + v[:, 2] * c
    return np.degrees(np.arctan2(r, f)), np.degrees(np.arctan2(v[:, 1], np.hypot(r, f)))


def coz(P, lon, lat, x0, z0, y0, it=12):
    p = np.array([x0, z0, y0], float)
    for _ in range(it):
        lo, la = tahmin(P, *p); e = np.concatenate([da(lo - lon), la - lat]); J = np.zeros((len(e), 3))
        for k in range(3):
            q = p.copy(); q[k] += 1e-4; l2, a2 = tahmin(P, *q)
            J[:, k] = (np.concatenate([da(l2 - lon), a2 - lat]) - e) / 1e-4
        w = 1.0 / np.maximum(1.0, np.abs(e) / 1.5); Jw = J * w[:, None]
        dp = np.linalg.lstsq(Jw.T @ J + 1e-6 * np.eye(3), -(Jw.T @ e), rcond=None)[0]; p += dp
        if np.abs(dp).max() < 1e-4: break
    return p


rng = np.random.default_rng(0)


def yerles(Y, q, harita, x0, z0, y0):
    """Harita noktaları anahtar karelerine göre gruplu: sorunun eşleştiği anahtar karelerin noktaları kullanılır."""
    P, lon, lat = [], [], []
    for k, noktalar in harita:
        if math.hypot(k["x"] - x0, k["z"] - z0) > 1.0 or not noktalar: continue
        ia, ib = Y.esle(q["oz"], k["oz"])
        idx = {kk: p for p, kk in noktalar}
        for a_, b_ in zip(ia, ib):
            if b_ in idx: P.append(idx[b_]); lon.append(q["oz"]["lon"][a_]); lat.append(q["oz"]["lat"][a_])
    if len(P) < 8: return None
    P, lon, lat = np.array(P), np.array(lon), np.array(lat)
    en = (0, None)
    for _ in range(60):
        k = rng.choice(len(P), 3, replace=False)
        p = coz(P[k], lon[k], lat[k], x0, z0, y0, it=6)
        if math.hypot(p[0] - x0, p[1] - z0) > 1.0: continue
        lo, la = tahmin(P, *p); u = np.hypot(da(lo - lon), la - lat) < 2.0
        if u.sum() > en[0]: en = (int(u.sum()), u)
    if en[0] < 8: return None
    p = coz(P[en[1]], lon[en[1]], lat[en[1]], x0, z0, y0)
    lo, la = tahmin(P, *p); u = np.hypot(da(lo - lon), la - lat) < 2.0
    if u.sum() < 8: return None
    return coz(P[u], lon[u], lat[u], *p), int(u.sum())


# ------------------------------------------------------------------ deney
SB, SG = bilinen(SORU)
HB = [(bilinen(K), K) for K in HAF]
print("soru %s: %d yeri bilinen kare; hafiza: %s" % (os.path.basename(SORU), len(SB), ", ".join("%s %d" % (os.path.basename(K), len(b[0])) for b, K in HB)))
ESIK = int(os.environ.get("ESIK", "20"))
for ad in YONTEMLER:
    Y = Yontem(ad); t0 = time.time()
    HK = []; harita = []
    for (kareler, govde), K in HB:
        hk = []
        for k in kareler:
            if all(math.hypot(k["x"] - o["x"], k["z"] - o["z"]) >= ARALIK for o in hk): hk.append(k)
        for k in hk: k["oz"] = Y.cikar(k["pan"], k["gor"], govde)
        nok = {id(k): [] for k in hk}
        for i in range(len(hk)):
            for j in range(i + 1, len(hk)):
                d = math.hypot(hk[i]["x"] - hk[j]["x"], hk[i]["z"] - hk[j]["z"])
                if 0.12 <= d <= 0.9:
                    for P, _, kk, idx in ucgenle(Y, hk[i], hk[j]): nok[id(kk)].append((P, idx))
        HK += hk; harita += [(k, nok[id(k)]) for k in hk]
    t_kur = time.time() - t0
    e1, y1, e2, y2, cz, sure = [], [], [], [], 0, []
    sorular = SB[::2]
    for q in sorular:
        t1 = time.time()
        q["oz"] = Y.cikar(q["pan"], q["gor"], SG)
        en = (0, None, 0.0)
        for k in HK:
            ia, ib = Y.esle(q["oz"], k["oz"]); n, kay, _ = kayma(q["oz"], k["oz"], ia, ib)
            if n > en[0]: en = (n, k, kay)
        if en[0] < ESIK: sure.append(time.time() - t1); continue
        k = en[1]; yk = k["yaw"] + math.radians(en[2])
        e1.append(math.hypot(k["x"] - q["x"], k["z"] - q["z"])); y1.append(abs(da(math.degrees(yk - q["yaw"]))))
        r = yerles(Y, q, harita, k["x"], k["z"], yk)
        sure.append(time.time() - t1)
        if r is None: e2.append(e1[-1]); y2.append(y1[-1]); continue
        cz += 1; p, _ = r
        e2.append(math.hypot(p[0] - q["x"], p[1] - q["z"])); y2.append(abs(da(math.degrees(p[2] - q["yaw"]))))
    f = lambda a, p: np.percentile(a, p) if len(a) else float("nan")
    print("%-10s taninan %3d/%3d (%%%2d) | anahtar kare: yer %.2f/%.2f m, yon %4.1f/%5.1f der | 3B cozulen %%%2d: yer %.2f/%.2f m, yon %4.1f/%5.1f | kurulum %.0f sn, soru basina %.0f ms (ortanca / %%90)" % (
        ad, len(e1), len(sorular), 100 * len(e1) / len(sorular), f(e1, 50), f(e1, 90), f(y1, 50), f(y1, 90),
        100 * cz / max(1, len(e1)), f(e2, 50), f(e2, 90), f(y2, 50), f(y2, 90), t_kur, 1000 * np.median(sure)), flush=True)
