"""Hizli donus bittikten sonra yon: donusten hemen onceki ve hemen sonraki durgun karenin 360 ufuk seritleri dairesel
kaydirmayla karsilastirilir. Dayanak: ayni isaretin robota gore yonu (gorulen[2]) once / sonra (yerinde donuste
isaret yonu farki = -donus)."""
import json, math, os, sys, cv2, numpy as np
KOK = sys.argv[1]; K = sys.argv[2]
sys.path.insert(0, os.path.join(KOK, "tools", "konum")); sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
import pusula_dene as pd
mx, my = pd.bant_haritasi(); mk = pd.maske()
R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
_sabit = None
def ham(i):
    g = cv2.imread(os.path.join(K, R[i]["jpg"]), cv2.IMREAD_GRAYSCALE)
    b = cv2.remap(g, mx, my, cv2.INTER_LINEAR).astype(np.float32)
    return (np.roll(b, -1, axis=1) - np.roll(b, 1, axis=1)) * mk * pd.PENCERE
def serit(i):
    global _sabit
    if _sabit is None:
        idx = list(range(0, len(R), 7)); _sabit = np.mean([ham(k) for k in idx], 0)
    return ham(i) - _sabit
def serit_eski(i):
    g = cv2.imread(os.path.join(K, R[i]["jpg"]), cv2.IMREAD_GRAYSCALE)
    b = cv2.remap(g, mx, my, cv2.INTER_LINEAR).astype(np.float32)
    return (np.roll(b, -1, axis=1) - np.roll(b, 1, axis=1)) * mk * pd.PENCERE
def kaydir(a, b):
    # dairesel capraz iliski (FFT): b = a'nin s piksel kaymasi
    A = np.fft.rfft(a, axis=1); B = np.fft.rfft(b, axis=1)
    c = np.fft.irfft((np.conj(A) * B).sum(0), n=a.shape[1])
    c = c / (np.sqrt((a*a).sum() * (b*b).sum()) + 1e-6)
    i = int(np.argmax(c)); s = i if i <= a.shape[1] // 2 else i - a.shape[1]
    ikinci = np.max(np.delete(c, [(i + d) % len(c) for d in range(-20, 21)]))
    return -s / pd.PX, c[i], ikinci
def da(x): return (x + 180) % 360 - 180
donen = [abs(r["sol"] - r["sag"]) > 100 and r["sol"] * r["sag"] < 0 for r in R]
durgun = [r["sol"] == 0 and r["sag"] == 0 for r in R]
seg = []; i = 0
while i < len(R):
    if donen[i]:
        j = i
        while j + 1 < len(R) and (donen[j + 1] or not durgun[j + 1]): j += 1
        seg.append((i, j)); i = j + 1
    else: i += 1
sonuc = []
for s, e in seg:
    a = s - 1
    while a > 0 and not durgun[a]: a -= 1
    b = e + 1
    if b >= len(R) or a < 0: continue
    # komutla beklenen donus (isaretsiz, kaba): motor farki x sure
    sure = R[e]["t"] - R[s]["t"] + 0.2
    hiz = max(abs(R[k]["sol"] - R[k]["sag"]) for k in range(s, e + 1))
    son0 = b
    while son0 + 1 < len(R) and durgun[son0 + 1]: son0 += 1
    bk = min(b + int(os.environ.get('GEC', '3')), son0)
    sa, sb = serit(a), serit(bk)
    d, c, c2 = kaydir(sa, sb)
    # dayanak 1: ayni isaretin robota gore normal acisi (yerinde donuste fark = -donus); 2: robotun kaydettigi yon,
    # donusten sonraki durus bitmeden hemen once (isaretle yeniden kilitlenmis olur)
    ga = {g[0]: g for g in R[a]["gorulen"]}; ort = None
    son = b
    while son + 1 < len(R) and durgun[son + 1]: son += 1
    for bb in range(son, b - 1, -1):
        gb = {g[0]: g for g in R[bb]["gorulen"]}; ortak = set(ga) & set(gb)
        if ortak:
            k = next(iter(ortak)); ort = -math.degrees(math.atan2(math.sin(gb[k][3] - ga[k][3]), math.cos(gb[k][3] - ga[k][3]))); break
    kal = da(R[son]["yaw"] - R[a]["yaw"]); bek = son - b
    sonuc.append((s, e, a, b, sure, hiz, d, c, c2, ort, kal, bek))
print("donus sayisi", len(sonuc))
hat = []
for (s, e, a, b, sure, hiz, d, c, c2, ort, kal, bek) in sonuc:
    ref = ort if ort is not None else (kal if bek >= 10 else None)
    h = None if ref is None else abs(da(d - ref))
    if h is not None: hat.append((h, ort is not None, c, abs(ref)))
    print("kare %3d-%3d %.1f sn | serit %7.1f (benzerlik %.2f) | isaret %s | robot kaydi %7.1f (durus %d kare) | hata %s" % (s, e, sure, d, c, "%7.1f" % ort if ort is not None else "   -   ", kal, bek, "-" if h is None else "%.1f" % h))
h = np.array([q[0] for q in hat]); print("dayanakli donus", len(h), "| serit hatasi ortanca %.1f, %%90 %.1f, en cok %.1f derece | 5 derece alti %d/%d" % (np.median(h), np.percentile(h, 90), h.max(), (h < 5).sum(), len(h)))
print("donus buyuklugu ve hata:", sorted([(round(q[3]), round(q[0], 1)) for q in hat]))
