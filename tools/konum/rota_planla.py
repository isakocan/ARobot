"""Telefon haritasının kesin açık zemininden robot için deneme rotası (PC).

Girdi: robotun köprüsünden ev haritası (GET :8080/api/ev ve /api/ev/zemin; tarama uygulaması "Robota" ile yollar) ya da
dosyalar. Açık zemin hücreleri (5 cm, "acik" = zemin kanıtı var, engel yok) engel / bilinmeyen her hücreden PAY metre
uzak olacak şekilde daraltılır. Rota: 1 numaralı işaretin 1 m önü (başlangıç) -> en uzak noktalar ve diğer işaretlerin
önü (açık zeminde birbirinden uzak) -> başlangıç. Ayaklar duvardan uzak tutan A* ile çizilir, görüş hattıyla düz
parçalara indirilir. Çıktı: rota.json (noktalar, ev koordinatı) ve rota.png (onay için resim).
Kullanım: python rota_planla.py <robot_adresi | ev.json zemin.json> <çıktı_klasörü>   (PAY=0.30, UZAK=4)"""
import heapq, json, math, os, sys, urllib.request
import cv2
import numpy as np

PAY = float(os.environ.get("PAY", "0.30"))       # robot merkezinden engele / bilinmeyene en az (m)
UZAK = int(os.environ.get("UZAK", "4"))           # uzak nokta sayısı
ON = 1.0                                          # işaretin önünde başlangıç uzaklığı (m)

if len(sys.argv) == 3:
    adr, CIK = sys.argv[1:3]
    EV = json.load(urllib.request.urlopen("http://%s:8080/api/ev" % adr, timeout=8))
    Z = json.load(urllib.request.urlopen("http://%s:8080/api/ev/zemin" % adr, timeout=15))
else:
    EV = json.load(open(sys.argv[1], encoding="utf-8")); Z = json.load(open(sys.argv[2], encoding="utf-8")); CIK = sys.argv[3]
os.makedirs(CIK, exist_ok=True)
json.dump(EV, open(os.path.join(CIK, "ev.json"), "w")); json.dump(Z, open(os.path.join(CIK, "zemin.json"), "w"))
HC = float(Z.get("hucre", 0.05))
a = np.array(Z["acik"], int).reshape(-1, 2)
x0, z0 = a.min(0) - 10; x1, z1 = a.max(0) + 10
W, H = x1 - x0 + 1, z1 - z0 + 1
acik = np.zeros((H, W), np.uint8); acik[a[:, 1] - z0, a[:, 0] - x0] = 1
uzak = cv2.distanceTransform(acik, cv2.DIST_L2, 5) * HC          # en yakın açık olmayan hücreye (m)
serbest = uzak >= PAY


def hucre(x, z): return int(round(x / HC)) - x0, int(round(z / HC)) - z0
def dunya(c, r): return (c + x0) * HC, (r + z0) * HC


def en_yakin_serbest(x, z):
    c, r = hucre(x, z); ys, xs = np.nonzero(serbest)
    if not len(xs): sys.exit("PAY %.2f m ile hiç serbest zemin yok" % PAY)
    k = np.argmin((xs - c) ** 2 + (ys - r) ** 2); return int(xs[k]), int(ys[k])


def jeodezik(bas):
    d = np.full((H, W), np.inf); d[bas[1], bas[0]] = 0; q = [(0.0, bas)]
    while q:
        dd, (c, r) = heapq.heappop(q)
        if dd > d[r, c]: continue
        for dc, dr, w in ((1, 0, 1), (-1, 0, 1), (0, 1, 1), (0, -1, 1), (1, 1, 1.414), (1, -1, 1.414), (-1, 1, 1.414), (-1, -1, 1.414)):
            cc, rr = c + dc, r + dr
            if 0 <= cc < W and 0 <= rr < H and serbest[rr, cc] and dd + w < d[rr, cc]:
                d[rr, cc] = dd + w; heapq.heappush(q, (dd + w, (cc, rr)))
    return d


def astar(s, g):
    ceza = 1.0 + 3.0 * np.clip(0.6 - uzak, 0, None)                 # duvara yakın hücre pahalı: ortadan gitsin
    d = {s: 0.0}; geri = {s: None}; q = [(0.0, s)]
    while q:
        _, u = heapq.heappop(q)
        if u == g: break
        c, r = u
        for dc, dr, w in ((1, 0, 1), (-1, 0, 1), (0, 1, 1), (0, -1, 1), (1, 1, 1.414), (1, -1, 1.414), (-1, 1, 1.414), (-1, -1, 1.414)):
            v = (c + dc, r + dr)
            if not (0 <= v[0] < W and 0 <= v[1] < H) or not serbest[v[1], v[0]]: continue
            nd = d[u] + w * ceza[v[1], v[0]]
            if nd < d.get(v, np.inf):
                d[v] = nd; geri[v] = u; heapq.heappush(q, (nd + math.hypot(v[0] - g[0], v[1] - g[1]), v))
    if g not in geri: return None
    yol = [g]
    while geri[yol[-1]] is not None: yol.append(geri[yol[-1]])
    return yol[::-1]


def gorur(p, q):
    n = int(max(abs(q[0] - p[0]), abs(q[1] - p[1]))) + 1
    for t in np.linspace(0, 1, n + 1):
        c, r = int(round(p[0] + t * (q[0] - p[0]))), int(round(p[1] + t * (q[1] - p[1])))
        if not serbest[r, c]: return False
    return True


def sadelestir(yol):
    s = [yol[0]]; i = 0
    while i < len(yol) - 1:
        j = len(yol) - 1
        while j > i + 1 and not gorur(yol[i], yol[j]): j -= 1
        s.append(yol[j]); i = j
    return s


isaretler = sorted(EV.get("isaretler", []), key=lambda e: e["id"])
on = [(e["id"], e["x"] + ON * math.sin(e["aci"]), e["z"] + ON * math.cos(e["aci"])) for e in isaretler]
bas = en_yakin_serbest(on[0][1], on[0][2])
d = jeodezik(bas); ulasilir = np.isfinite(d)
hedef = [("isaret %d onu" % i, en_yakin_serbest(x, z)) for i, x, z in on[1:]]
hedef = [h for h in hedef if ulasilir[h[1][1], h[1][0]]]
MOD = os.environ.get("MOD", "test")


def ayak(a, b, etiket, noktalar):
    """a -> b A* ile, düz parçalara indirilmiş; her nokta o parçanın test etiketini taşır. -> uzunluk"""
    yol = astar(a, b)
    if yol is None: return 0.0
    L = 0.0
    for c, r in sadelestir(yol)[1:]:
        x, z = dunya(c, r); L += math.hypot(x - noktalar[-1]["x"], z - noktalar[-1]["z"])
        noktalar.append(dict(ad="", x=x, z=z, test=etiket, tur=False))
    return L


if MOD == "test":
    # Her parça ayrı bir şeyi sınar: kısa düz (işaret görünürken), yerinde tam tur (işaret yanında / uzakta), uzun düz,
    # zikzak (bol dönüş), dönüş yolu ve başlangıca dönüş (biriken kaymayı işaret 0 gösterir).
    uzak_puan = np.where(ulasilir, d, -1); r, c = np.unravel_index(np.argmax(uzak_puan), uzak_puan.shape); F = (int(c), int(r))
    noktalar = [dict(ad="başlangıç", x=dunya(*bas)[0], z=dunya(*bas)[1], test="", tur=True)]; toplam = 0.0; onceki = bas
    if hedef:
        toplam += ayak(onceki, hedef[0][1], "kısa düz (işaret 2'ye)", noktalar); noktalar[-1].update(ad="işaret 2 önü", tur=True); onceki = hedef[0][1]
    toplam += ayak(onceki, F, "uzun düz", noktalar); noktalar[-1].update(ad="uzak köşe", tur=True)
    # zikzak: uzak köşeden başlangıca giden yolun ilk ~4 m'si boyunca, yola dik +-0,45 m, 0,7 m arayla
    geri = astar(F, bas); cizgi = [dunya(*q) for q in sadelestir(geri)] if geri else []
    ornek = []; birikim = 0.0
    for (ax, az), (bx, bz) in zip(cizgi, cizgi[1:]):
        L = math.hypot(bx - ax, bz - az)
        for t in np.arange(0.7 - (birikim % 0.7), L, 0.7):
            if birikim + t > 4.2: break
            ux, uz = (bx - ax) / L, (bz - az) / L; ornek.append((ax + ux * t, az + uz * t, uz, -ux))
        birikim += L
        if birikim > 4.2: break
    son = F; isaret = 1
    for x, z, nx, nz in ornek:
        q = hucre(x + isaret * 0.45 * nx, z + isaret * 0.45 * nz)
        if 0 <= q[0] < W and 0 <= q[1] < H and serbest[q[1], q[0]] and gorur(son, q):
            px_, pz_ = dunya(*q); toplam += math.hypot(px_ - noktalar[-1]["x"], pz_ - noktalar[-1]["z"])
            noktalar.append(dict(ad="", x=px_, z=pz_, test="zikzak", tur=False)); son = q; isaret = -isaret
    if son != F: noktalar[-1]["ad"] = "zikzak sonu"
    toplam += ayak(son, bas, "başlangıca dönüş", noktalar); noktalar[-1].update(ad="bitiş (başlangıç)")
else:
    # uzak noktalar: başlangıçtan ve seçilenlerden en uzak ulaşılabilir hücre (jeodezik)
    secilen = [bas] + [h[1] for h in hedef]
    for k in range(UZAK):
        en = np.full((H, W), np.inf)
        for s_ in secilen:
            en = np.minimum(en, np.hypot(*np.meshgrid(np.arange(W) - s_[0], np.arange(H) - s_[1])))
        puan = np.where(ulasilir, en, -1)
        r, c = np.unravel_index(np.argmax(puan), puan.shape)
        if puan[r, c] * HC < 0.6: break
        hedef.append(("uzak %d" % (k + 1), (int(c), int(r)))); secilen.append((int(c), int(r)))
    sira = []; simdi = bas; kalan = hedef[:]
    while kalan:
        k = min(range(len(kalan)), key=lambda j: math.hypot(kalan[j][1][0] - simdi[0], kalan[j][1][1] - simdi[1]))
        sira.append(kalan.pop(k)); simdi = sira[-1][1]
    sira.append(("bitiş (başlangıç)", bas))
    noktalar = [dict(ad="başlangıç", x=dunya(*bas)[0], z=dunya(*bas)[1], test="", tur=True)]; onceki = bas; toplam = 0.0
    for ad, h in sira:
        toplam += ayak(onceki, h, ad, noktalar); noktalar[-1]["ad"] = ad; onceki = h
on1 = [e for e in isaretler if e["id"] == on[0][0]][0]
bas_yon = math.degrees(math.atan2(on1["x"] - noktalar[0]["x"], on1["z"] - noktalar[0]["z"]))   # başta işarete bak
json.dump(dict(pay=PAY, toplam_m=round(toplam, 2), bas_yon=bas_yon, noktalar=noktalar), open(os.path.join(CIK, "rota.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

# resim
S = 6; im = np.full((H * S, W * S, 3), 255, np.uint8)
renk = np.full((H, W, 3), 255, np.uint8); renk[acik == 1] = (235, 215, 190); renk[serbest] = (200, 150, 90)
for ad, c_ in (("engel", (60, 60, 60)), ("duvar", (0, 0, 0))):
    if Z.get(ad):
        e = np.array(Z[ad], int).reshape(-1, 2); m = (e[:, 0] - x0 >= 0) & (e[:, 0] - x0 < W) & (e[:, 1] - z0 >= 0) & (e[:, 1] - z0 < H)
        renk[e[m, 1] - z0, e[m, 0] - x0] = c_
im = cv2.resize(renk[::-1], (W * S, H * S), interpolation=cv2.INTER_NEAREST)
def px(x, z): c, r = hucre(x, z); return int(c * S + S / 2), int((H - 1 - r) * S + S / 2)
RENK = {"zikzak": (0, 140, 255), "uzun düz": (40, 40, 220), "başlangıca dönüş": (160, 40, 160)}
for p, q in zip(noktalar, noktalar[1:]):
    cv2.arrowedLine(im, px(p["x"], p["z"]), px(q["x"], q["z"]), RENK.get(q.get("test"), (40, 160, 40)), 3, cv2.LINE_AA, tipLength=0.04)
for j, p in enumerate(noktalar):
    cv2.circle(im, px(p["x"], p["z"]), 7, (40, 40, 220), -1)
    if p.get("tur"): cv2.circle(im, px(p["x"], p["z"]), 16, (40, 40, 220), 2, cv2.LINE_AA)
    if p["ad"] and j < len(noktalar) - 1: cv2.putText(im, "%d %s" % (j, p["ad"].translate(str.maketrans("şçığüöŞÇİĞÜÖ", "sciguoSCIGUO"))), (px(p["x"], p["z"])[0] + 9, px(p["x"], p["z"])[1] - 6), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (20, 20, 120), 2, cv2.LINE_AA)
for e in isaretler:
    a_ = px(e["x"], e["z"]); b_ = px(e["x"] + 0.3 * math.sin(e["aci"]), e["z"] + 0.3 * math.cos(e["aci"]))
    cv2.rectangle(im, (a_[0] - 7, a_[1] - 7), (a_[0] + 7, a_[1] + 7), (0, 0, 0), -1); cv2.arrowedLine(im, a_, b_, (0, 150, 0), 2, tipLength=0.3)
    cv2.putText(im, "ArUco %d" % e["id"], (a_[0] + 9, a_[1] + 18), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 0, 0), 2, cv2.LINE_AA)
for k in range(int(x0 * HC) - 1, int(x1 * HC) + 2):
    cv2.line(im, px(k, z0 * HC), px(k, z1 * HC), (225, 225, 225), 1)
for k in range(int(z0 * HC) - 1, int(z1 * HC) + 2):
    cv2.line(im, px(x0 * HC, k), px(x1 * HC, k), (225, 225, 225), 1)
cv2.putText(im, "koyu mavi: robotun merkezi girebilir (engelden %.0f cm uzak), acik mavi: acik zemin, siyah/gri: duvar/engel, 1 m izgara" % (PAY * 100),
            (10, 22), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 0, 0), 1, cv2.LINE_AA)
cv2.putText(im, "rota %.1f m: yesil kisa duz, kirmizi uzun duz, turuncu zikzak, mor donus; halka = yerinde tam tur" % toplam, (10, 44), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (20, 20, 120), 2, cv2.LINE_AA)
cv2.imwrite(os.path.join(CIK, "rota.png"), im)
print("rota: %d nokta, %.1f m, serbest alan %.1f m2 (PAY %.2f m)" % (len(noktalar), toplam, serbest.sum() * HC * HC, PAY))
for j, p in enumerate(noktalar): print("  %2d %6.2f %6.2f %-22s %s%s" % (j, p["x"], p["z"], p.get("test", ""), p["ad"], "  +tur" if p.get("tur") else ""))
