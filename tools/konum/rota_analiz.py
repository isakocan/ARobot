"""Rota test sürüşünü parça parça değerlendirir (PC).

Girdi: rota klasörü (rota.json + rota_sur.py'nin surus_*.csv kaydı) ve robotun konum kaydı (~/konum_kayit/<zaman>/,
gercek_yon.json ile: jiro_eslestir.py telefonun jiroskop CSV'sinden üretir).
Her robot karesi, sürücünün o anki parçasıyla (kısa düz, tur, uzun düz, zikzak, dönüş...) etiketlenir. Parça başına:
  - bugünkü sistemin (robotta canlı çalışan) yön hatası, jiroskoba göre,
  - işaretsiz tahmin (ilk kilitten sonra işaret yok sayılır: yalnız pusula + zemin izi) yön hatası,
  - YENI=1 ise yeni yöntem (XFeat nokta haritası, harita bu sürüşün işaret yakını karelerinden; işaretsiz) yön hatası.
Kontrol noktaları (işaret 2'ye varış, başlangıca dönüş): işaretsiz tahminin o ana kadar biriktirdiği yer / yön kayması
(işaretin yakından görüldüğü ilk karedeki işaret konumuna göre).
Çıktı: <rota klasörü>/analiz.png (üstten yol + zamanla yön hatası) ve ekrana tablo.
Kullanım: python rota_analiz.py <kök> <rota_klasörü> <robot_kaydı>"""
import csv, glob, json, math, os, subprocess, sys
import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

KOK, RK, K = sys.argv[1:4]
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
import konum


def da(a): return (a + 180.0) % 360.0 - 180.0


ROTA = json.load(open(os.path.join(RK, "rota.json"), encoding="utf-8"))
CSV = sorted(glob.glob(os.path.join(RK, "surus_*.csv")))[-1]
S = [r for r in csv.DictReader(open(CSV, encoding="utf-8"))]
su = np.array([float(r["utc"]) for r in S]); sev = [r["evre"] for r in S]
R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
EV = [e if isinstance(e, dict) else dict(id=e[0], x=e[1], z=e[2], aci=e[3]) for e in json.load(open(os.path.join(RK, "ev.json"), encoding="utf-8"))["isaretler"]]
gy = os.path.join(K, "gercek_yon.json")
GY = dict(zip(*[json.load(open(gy))[k] for k in ("kare", "yaw")])) if os.path.exists(gy) else {}
if not GY: print("UYARI: gercek_yon.json yok (jiro_eslestir.py çalıştırılmadı): yön hatası hesaplanamaz")


def parca(t):
    j = np.searchsorted(su, t) - 1
    if j < 0 or t - su[max(j, 0)] > 1.0: return "sürüş dışı"
    e = sev[j]
    for on in ("git: ", "don: "):
        if e.startswith(on): return e[len(on):] + (" (dönerek hizalanma)" if on == "don: " else "")
    return e


ET = [parca(f["t"]) for f in R]
# işaretli yeniden oynatma (kontrol noktası gerçeği) ve işaretsiz tahmin (ilk kilitten sonra işaret yok)
ref = konum.KonumBulucu(); ref.ev_ayarla(EV); od = konum.KonumBulucu(); od.ev_ayarla(EV); kur = False
REF, OD, YAK = {}, {}, {}
for i, f in enumerate(R):
    g = cv2.imread(os.path.join(K, f["jpg"]), cv2.IMREAD_GRAYSCALE)
    if g is None: continue
    ref.isle(g, f["t"], (f["sol"], f["sag"]))
    if kur: od.ev = {}
    od.isle(g, f["t"], (f["sol"], f["sag"])); kur = kur or od.bilinen
    if ref.bilinen: REF[i] = (ref.x, ref.z, math.degrees(ref.yaw), math.degrees(math.sqrt(ref.P[2, 2])))
    if od.bilinen: OD[i] = (od.x, od.z, math.degrees(od.yaw))
    YAK[i] = min([q[1] for q in ref.gorulen], default=None)
CANLI = {i: (f["x"], f["z"], f["yaw"]) for i, f in enumerate(R) if f.get("x") is not None}
KAYNAK = {"canlı (bugünkü sistem)": CANLI, "işaretsiz tahmin": OD}
if os.environ.get("YENI") == "1":
    on = os.path.join(RK, "yeni")
    subprocess.run([sys.executable, os.path.join(KOK, "tools", "konum", "harita_genislet.py"), KOK, K, K], env=dict(os.environ, KAYDET=on), check=True)
    y = json.load(open(on + "_demet ayarli.json"))["yon"]
    KAYNAK["yeni yöntem (işaretsiz)"] = {int(k): (None, None, GY[int(k)] + v) for k, v in y.items() if int(k) in GY}   # yalnız hata büyüklüğü var


def ozet(a):
    a = np.abs(np.array(a, float))
    return "%5.1f %5.1f %5.1f" % (np.median(a), np.percentile(a, 90), a.max()) if len(a) else "   -     -     -"


sira = [];
for e in ET:
    if e not in sira and e != "sürüş dışı": sira.append(e)
print("\nYÖN HATASI (derece, jiroskoba göre): ortanca / %90 / en çok")
print("%-36s %5s  " % ("parça", "kare") + "  ".join("%-17s" % k[:17] for k in KAYNAK))
for e in sira + ["HEPSİ"]:
    ks = [i for i, x in enumerate(ET) if (x == e or (e == "HEPSİ" and x != "sürüş dışı")) and i in GY]
    print("%-36s %5d  " % (e[:36], len(ks)) + "  ".join("%-17s" % ozet([da(S_[i][2] - GY[i]) for i in ks if i in S_]) for S_ in KAYNAK.values()))

# kontrol noktaları: işaret yakından (<= 1,3 m, yön belirsizliği < 4 der) görülen bölümlerin ilk karesi
print("\nKONTROL NOKTALARI: işaretsiz tahminin biriktirdiği kayma (işaretin gösterdiği yere göre)")
onceki = -99
for i in sorted(REF):
    if YAK.get(i) is not None and YAK[i] <= 1.3 and REF[i][3] < 4 and i in OD and i - onceki > 25:
        r, o = REF[i], OD[i]
        print("  %5.0f. sn  %-32s yer %.2f m, yön %.1f der" % (R[i]["t"] - R[0]["t"], ET[i][:32], math.hypot(r[0] - o[0], r[1] - o[1]), abs(da(r[2] - o[2]))))
    if YAK.get(i) is not None and YAK[i] <= 1.3: onceki = i

# resim: üstten yol + zamanla yön hatası
PAL = {}; renkler = [(46, 160, 67), (214, 39, 40), (255, 127, 14), (148, 103, 189), (31, 119, 180), (140, 86, 75), (227, 119, 194), (23, 190, 207)]
for j, e in enumerate(sira): PAL[e] = renkler[j % len(renkler)]
xs = [p["x"] for p in ROTA["noktalar"]] + [v[0] for v in CANLI.values()]; zs = [p["z"] for p in ROTA["noktalar"]] + [v[1] for v in CANLI.values()]
x0, x1, z0, z1 = min(xs) - 0.8, max(xs) + 0.8, min(zs) - 0.8, max(zs) + 0.8; SC = min(900 / (x1 - x0), 700 / (z1 - z0))
W1, H1 = int((x1 - x0) * SC), int((z1 - z0) * SC)
im = Image.new("RGB", (max(W1, 900), H1 + 420), "white"); dr = ImageDraw.Draw(im)
F = lambda s: ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", s)
P = lambda x, z: ((x - x0) * SC, H1 - (z - z0) * SC)
for k in range(int(x0) - 1, int(x1) + 2): dr.line([P(k, z0), P(k, z1)], fill=(235, 235, 235))
for k in range(int(z0) - 1, int(z1) + 2): dr.line([P(x0, k), P(x1, k)], fill=(235, 235, 235))
nk = ROTA["noktalar"]
for a, b in zip(nk, nk[1:]): dr.line([P(a["x"], a["z"]), P(b["x"], b["z"])], fill=(170, 170, 170), width=6)
od_ = [P(*OD[i][:2]) for i in sorted(OD)]
if len(od_) > 1: dr.line(od_, fill=(120, 120, 120), width=2)
ks = sorted(CANLI)
for a, b in zip(ks, ks[1:]):
    if ET[b] in PAL: dr.line([P(*CANLI[a][:2]), P(*CANLI[b][:2])], fill=PAL[ET[b]], width=3)
for e in EV:
    p = P(e["x"], e["z"]); dr.rectangle([p[0] - 6, p[1] - 6, p[0] + 6, p[1] + 6], fill="black"); dr.text((p[0] + 8, p[1] - 8), "ArUco %d" % e["id"], fill="black", font=F(15))
dr.text((10, 6), "Üstten: kalın gri = planlanan rota, renkli = robotun kendi bildiği yol (parçaya göre), ince gri = işaretsiz tahmin", fill="black", font=F(15))
# zaman grafiği
y0 = H1 + 40; t0 = R[0]["t"]; T = R[-1]["t"] - t0; gx = lambda t: 50 + (t - t0) / T * (im.width - 70); gyy = lambda e: y0 + 280 - min(e, 40) / 40 * 280
for i, e in enumerate(ET):
    if e in PAL: dr.line([gx(R[i]["t"]), y0, gx(R[i]["t"]), y0 + 280], fill=tuple(int(c * 0.25 + 255 * 0.75) for c in PAL[e]))
for e in range(0, 41, 10): dr.line([50, gyy(e), im.width - 20, gyy(e)], fill=(225, 225, 225)); dr.text((10, gyy(e) - 9), "%d°" % e, fill=(90, 90, 90), font=F(13))
for ad, S_, renk in (("canlı (bugünkü sistem)", CANLI, (200, 40, 40)), ("işaretsiz tahmin", OD, (110, 110, 110))):
    pts = [(gx(R[i]["t"]), gyy(abs(da(S_[i][2] - GY[i])))) for i in sorted(S_) if i in GY]
    if len(pts) > 1: dr.line(pts, fill=renk, width=2)
dr.text((50, y0 - 24), "Zamanla yön hatası (jiroskoba göre): kırmızı canlı sistem, gri işaretsiz tahmin; arka renk = parça", fill="black", font=F(15))
yy = y0 + 290
for j, e in enumerate(sira):
    dr.rectangle([50 + 230 * (j % 4), yy + 22 * (j // 4), 64 + 230 * (j % 4), yy + 14 + 22 * (j // 4)], fill=PAL[e]); dr.text((70 + 230 * (j % 4), yy - 3 + 22 * (j // 4)), e[:30], fill="black", font=F(13))
im = im.crop((0, 0, im.width, yy + 22 * ((len(sira) + 3) // 4) + 10))
im.save(os.path.join(RK, "analiz.png")); print("\nresim:", os.path.join(RK, "analiz.png"))
