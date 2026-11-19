"""ArUco algilayici testi icin goruntu uretir: 960x540 gri, dizin/ icinde .gray (ham) + gercek.json."""
import cv2, numpy as np, json, os, sys, random
cikti = sys.argv[1] if len(sys.argv) > 1 else "veri"
os.makedirs(cikti, exist_ok=True)
W, H = 960, 540
rng = np.random.default_rng(7); random.seed(7)
d = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_4X4_50)

def arka():
    im = np.zeros((H, W), np.float32) + rng.uniform(80, 200)
    # dusuk frekansli doku + karisik sekiller
    n = rng.normal(0, 1, (H // 24, W // 24)).astype(np.float32)
    im += cv2.resize(n, (W, H), interpolation=cv2.INTER_CUBIC) * 35
    for _ in range(rng.integers(15, 40)):
        x, y = int(rng.integers(0, W)), int(rng.integers(0, H)); w, h = int(rng.integers(10, 200)), int(rng.integers(10, 120))
        c = float(rng.integers(0, 255)); t = rng.integers(0, 3)
        if t == 0: cv2.rectangle(im, (x, y), (x + w, y + h), c, -1)
        elif t == 1: cv2.line(im, (x, y), (x + w, y + h), c, int(rng.integers(1, 6)))
        else: cv2.ellipse(im, (x, y), (w // 2, h // 2), float(rng.integers(0, 180)), 0, 360, c, -1)
    for _ in range(rng.integers(0, 4)):  # sahte kareler (siyah cerceveli, beyaz icli) yanlis pozitif icin
        s = int(rng.integers(30, 120)); x, y = int(rng.integers(0, W - s)), int(rng.integers(0, H - s))
        cv2.rectangle(im, (x, y), (x + s, y + s), 20, -1)
        k = int(rng.integers(0, 3))
        if k == 0: cv2.rectangle(im, (x + s // 6, y + s // 6), (x + s - s // 6, y + s - s // 6), 230, -1)
        elif k == 1:
            for _ in range(10): cv2.circle(im, (x + int(rng.integers(0, s)), y + int(rng.integers(0, s))), int(rng.integers(2, s // 6 + 3)), float(rng.choice([20, 230])), -1)
    return im

def isaret_ekle(im, mid, boyut, gercek, kullanilan):
    px = 60
    m = cv2.aruco.generateImageMarker(d, mid, px).astype(np.float32)
    q = int(px * 0.25)  # sessiz bolge (beyaz kagit kenari)
    kagit = np.full((px + 2 * q, px + 2 * q), 235, np.float32); kagit[q:q + px, q:q + px] = m
    S = boyut / px * (px + 2 * q)  # kagit kenari
    ang = rng.uniform(0, 2 * np.pi)
    egim = rng.uniform(0, 0.55) if rng.random() < 0.7 else 0.0  # perspektif
    ex, ey = rng.uniform(0.15, 0.3), rng.uniform(0.15, 0.3)
    src = np.float32([[0, 0], [px + 2 * q, 0], [px + 2 * q, px + 2 * q], [0, px + 2 * q]])
    c = np.float32([[-.5, -.5], [.5, -.5], [.5, .5], [-.5, .5]]) * S
    # perspektif: bir kenari kucult
    ax = rng.uniform(0, 2 * np.pi)
    ux, uy = np.cos(ax), np.sin(ax)
    for i in range(4):
        z = 1 + egim * (c[i, 0] * ux + c[i, 1] * uy) / S * 0.9
        c[i] /= max(z, 0.4)
    R = np.array([[np.cos(ang), -np.sin(ang)], [np.sin(ang), np.cos(ang)]], np.float32)
    c = c @ R.T
    mg = S * 0.75
    cxm = rng.uniform(mg, W - mg); cym = rng.uniform(mg, H - mg)
    c += np.float32([cxm, cym])
    Hm = cv2.getPerspectiveTransform(src, c.astype(np.float32))
    warped = cv2.warpPerspective(kagit, Hm, (W, H), flags=cv2.INTER_LINEAR, borderValue=-1)
    mask = cv2.warpPerspective(np.ones_like(kagit), Hm, (W, H), flags=cv2.INTER_NEAREST) > 0
    if (mask & kullanilan).any(): return False
    im2 = im.copy(); im2[mask] = warped[mask]
    # marker koseleri (TL,TR,BR,BL) goruntude
    mk = np.float32([[q, q], [q + px, q], [q + px, q + px], [q, q + px]]).reshape(-1, 1, 2)
    pts = cv2.perspectiveTransform(mk, Hm).reshape(4, 2)
    if (pts.min() < 5) or pts[:, 0].max() > W - 5 or pts[:, 1].max() > H - 5: return False
    im[:] = im2; kullanilan |= cv2.dilate(mask.astype(np.uint8), np.ones((9, 9), np.uint8)) > 0
    gercek.append({"id": int(mid), "kose": pts.flatten().tolist(),
                   "kenar": float(np.mean([np.linalg.norm(pts[i] - pts[(i + 1) % 4]) for i in range(4)]))})
    return True

def bozucu(im):
    s = rng.uniform(0, 1.6)
    if s > 0.2: im = cv2.GaussianBlur(im, (0, 0), s)
    # isik gradyani
    gx = np.linspace(rng.uniform(0.6, 1.0), rng.uniform(0.9, 1.2), W, dtype=np.float32)[None, :]
    im = im * gx
    im = (im - 128) * rng.uniform(0.6, 1.1) + 128 + rng.uniform(-25, 25)
    im += rng.normal(0, rng.uniform(0, 9), im.shape).astype(np.float32)
    return np.clip(im, 0, 255).astype(np.uint8)

adet_var, adet_yok = 400, 100
meta = []
for i in range(adet_var + adet_yok):
    im = arka(); gercek = []; kullanilan = np.zeros((H, W), bool)
    if i < adet_var:
        for _ in range(int(rng.integers(1, 4))):
            for deneme in range(10):
                if isaret_ekle(im, int(rng.integers(0, 50)), float(np.exp(rng.uniform(np.log(40), np.log(200)))), gercek, kullanilan): break
    im = bozucu(im)
    ad = f"t{i:04d}"
    im.tofile(os.path.join(cikti, ad + ".gray"))
    if i < 3: cv2.imwrite(os.path.join(cikti, ad + ".png"), im)
    meta.append({"ad": ad, "w": W, "h": H, "isaretler": gercek})
json.dump(meta, open(os.path.join(cikti, "gercek.json"), "w"))
print(len(meta), "goruntu")
