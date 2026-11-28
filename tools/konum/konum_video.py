"""İşaretsiz konum bulmayı kayıt üzerinde gösterir (PC): video + yön grafiği + AR yönü ölçümü.

Harita, harita_genislet.py'deki gibi M kaydından kurulur ve demet ayarıyla düzeltilir. Sonra T kaydı (jiroskoplu)
işaret HİÇ kullanılmadan oynatılır; her ADIM karede bir harita ölçümü. Her kare için:
  üstte robotun açılmış 360 görüntüsü, yeşil = haritadaki 3B noktalarla eşleşen noktalar;
  altta solda üstten harita (gri 3B noktalar, mavi = haritadan bulunan yer, kırmızı = bugünkü sistem);
  altta sağda yön kadranı: yeşil jiroskop (gerçek), mavi harita, kırmızı bugünkü sistem.
AR: uygulamanın kuralı (robotun ~0,4 sn geç gelen yönü + o andan beri telefon pusulası / komut) iki yön kaynağıyla.
Kullanım: python konum_video.py <kök> <harita_kaydı> <test_kaydı> <çıktı_klasörü>   (ADIM=2 ortam değişkeni, VIDEO=0)"""
import json, math, os, subprocess, sys
import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

KOK, M, T, CIK = sys.argv[1:5]
ADIM = int(os.environ.get("ADIM", "2")); VIDEO = os.environ.get("VIDEO", "1") == "1"
os.makedirs(CIK, exist_ok=True)
_kod = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "harita_genislet.py"), encoding="utf-8").read()
sys.argv = [sys.argv[0], KOK, M, T]
exec(_kod.split("# ------------------------------------------------------------------ 2. test")[0])
harita.duzelt()
import teta

R, EV, GY = yukle(T)
G = {}
# bugünkü sistem (işaretli) ve işarete uzaklık
ref = konum.KonumBulucu(); ref.ev_ayarla(EV); NOR, UZK, GER = {}, {}, {}
for i, f in enumerate(R):
    g = cv2.imread(os.path.join(T, f["jpg"]), cv2.IMREAD_GRAYSCALE)
    if g is None: continue
    G[i] = g
    ref.isle(g, f["t"], (f["sol"], f["sag"]))
    UZK[i] = min([q[1] for q in ref.gorulen], default=None)
    if ref.bilinen: NOR[i] = (ref.x, ref.z, math.degrees(ref.yaw))
    if ref.bilinen and [q for q in ref.gorulen if q[1] <= 1.3] and math.degrees(math.sqrt(ref.P[2, 2])) < 4 and i in GY and abs(da(math.degrees(ref.yaw) - GY[i])) < 4:
        GER[i] = (ref.x, ref.z)

# işaretsiz, yalnız harita
kb = konum.KonumBulucu(); kb.ev_ayarla(EV); kb.ev = {}
oh = orb_hafiza.OrbHafiza(); HAR, UY, KAB = {}, {}, {}
for i, f in enumerate(R):
    if i not in G: continue
    g = G[i]
    kb.isle(g, f["t"], (f["sol"], f["sag"])); oh.gozle(g, f["sol"] != 0 or f["sag"] != 0)
    if i % ADIM == 0:
        oz = Y.cikar(nh.panorama(g), kb.gorulen, oh.govde.kullan())
        r = harita.yerles(oz, *((kb.x, kb.z, kb.yaw) if kb.bilinen else (None, None, None)), yaricap=0.8)
        if r is not None and r[3] >= 25:
            k_ = max(1.0, math.sqrt(40.0 / r[3]))
            o = kb.harita_olcumu(r[0], r[1], r[2], 0.05 * k_, math.radians(2.0) * k_, r[3], f["t"])
            UY[i] = harita.son; KAB[i] = bool(o[4])
    if kb.bilinen: HAR[i] = (kb.x, kb.z, math.degrees(kb.yaw))


def ozet(a):
    a = np.abs(np.array(a))
    return "ortanca %4.1f  %%90 %5.1f  en cok %5.1f" % (np.median(a), np.percentile(a, 90), a.max()) if len(a) else "-"


yakin = lambda i: UZK.get(i) is not None and UZK[i] <= 1.5
print("ADIM %d (harita olcumu %.1f Hz), %d olcum, %d kabul" % (ADIM, 5.0 / ADIM, len(UY), sum(KAB.values())))
for ad, S in (("harita (isaretsiz)", HAR), ("bugunku (isaretli)", NOR)):
    e = {i: da(S[i][2] - GY[i]) for i in S if i in GY}
    print("  robot yonu %-20s yakin: %s | uzak: %s" % (ad, ozet([v for i, v in e.items() if yakin(i)]), ozet([v for i, v in e.items() if not yakin(i)])))
ep = [math.hypot(HAR[i][0] - GER[i][0], HAR[i][1] - GER[i][1]) for i in GER if i in HAR]
if ep: print("  harita yer hatasi (isaret yakindan gorulen %d karede): ortanca %.2f m" % (len(ep), np.median(ep)))

# ---------------------------------------------------------------- AR yönü (uygulama kuralı)
teta.Pusula.DINAMIK = False
pus = teta.Pusula(); idx = sorted(G); C = {idx[0]: 0.0}; GUV = {idx[0]: 0.0}
for a, b in zip(idx, idx[1:]):
    d, guv = pus.guncelle(G[b]); C[b] = C[a] + d; GUV[b] = guv
GEC = 2
AR = {"harita": {}, "bugunku": {}}
for n, i in enumerate(idx):
    if n < GEC or i not in GY: continue
    dol = 0.0
    for j0, j in zip(idx[n - GEC:n], idx[n - GEC + 1:n + 1]):
        p_ = C[j] - C[j0]; dtj = R[j]["t"] - R[j0]["t"]
        km = 127.0 * (R[j0]["sol"] - R[j0]["sag"]) / 510.0 * dtj
        dol += km if (abs(p_) < 1 and abs(km) > 4) or GUV[j] < 0.3 else p_
    k = idx[n - GEC]
    for ad, S in (("harita", HAR), ("bugunku", NOR)):
        if k in S: AR[ad][i] = da(S[k][2] + dol - GY[i])
for ad in AR:
    e = AR[ad]
    print("  AR yonu %-10s yakin: %s | uzak: %s" % (ad, ozet([v for i, v in e.items() if yakin(i)]), ozet([v for i, v in e.items() if not yakin(i)])))
json.dump({"ADIM": ADIM, "HAR": HAR, "NOR": NOR, "GY": GY, "UZK": UZK, "AR": AR}, open(os.path.join(CIK, "konum_seri_%d.json" % ADIM), "w"), default=float)

# ---------------------------------------------------------------- grafik: yön hatası zamanla
t0 = R[0]["t"]; W_, H_ = 1400, 520; im = np.full((H_, W_, 3), 255, np.uint8)
x0_, x1_, y0_, y1_ = 70, W_ - 20, 80, H_ - 50; tmax = R[-1]["t"] - t0; emax = 40.0
gx = lambda t: int(x0_ + (t - t0) / tmax * (x1_ - x0_)); gy = lambda e: int(y1_ - min(e, emax) / emax * (y1_ - y0_))
for i in idx:
    if yakin(i): cv2.rectangle(im, (gx(R[i]["t"]), y0_), (gx(R[i]["t"]) + 4, y1_), (225, 240, 225), -1)
for e in range(0, 41, 10):
    cv2.line(im, (x0_, gy(e)), (x1_, gy(e)), (225, 225, 225), 1)
for ad, S, renk in (("bugunku", NOR, (60, 60, 220)), ("harita", HAR, (200, 120, 30))):
    pts = [(gx(R[i]["t"]), gy(abs(da(S[i][2] - GY[i])))) for i in idx if i in S and i in GY]
    for a, b in zip(pts, pts[1:]): cv2.line(im, a, b, renk, 2, cv2.LINE_AA)
pil = Image.fromarray(cv2.cvtColor(im, cv2.COLOR_BGR2RGB)); dr = ImageDraw.Draw(pil)
F = lambda s: ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", s)
dr.text((x0_, 12), "Robotun yön hatası (jiroskopa göre), 25 Kasım sürüşü, harita %s kaydından" % os.path.basename(M)[:8], fill=(0, 0, 0), font=F(22))
dr.text((x0_, 46), "mavi: harita (işaret hiç kullanılmadan)    kırmızı: bugünkü sistem (işaretli)    yeşil zemin: işaret 1,5 m'den yakın", fill=(90, 90, 90), font=F(15))
for e in range(0, 41, 10): dr.text((20, gy(e) - 10), "%d°" % e, fill=(90, 90, 90), font=F(15))
for s in range(0, int(tmax) + 1, 20): dr.text((gx(t0 + s) - 10, y1_ + 8), "%d sn" % s, fill=(90, 90, 90), font=F(15))
pil.save(os.path.join(CIK, "yon_hatasi_zaman_%d.png" % ADIM))
if not VIDEO: sys.exit()

# ---------------------------------------------------------------- video
KAREDIR = os.path.join(CIK, "kareler_%d" % ADIM); os.makedirs(KAREDIR, exist_ok=True)
S_ = 105; OX, OZ = 3.3, 1.3; MW, MH = int(6.6 * S_), 470
def mp(x, z): return int((x + OX) * S_), int(MH - (z + OZ) * S_)
taban = np.full((MH, MW, 3), 255, np.uint8)
for x in range(-3, 4): cv2.line(taban, mp(x, -1.3), mp(x, 3.2), (238, 238, 238), 1)
for z in range(-1, 4): cv2.line(taban, mp(-3.3, z), mp(3.3, z), (238, 238, 238), 1)
for P in harita.Pn: cv2.circle(taban, mp(P[0], P[2]), 1, (185, 185, 185), -1)
for k in harita.kareler: cv2.circle(taban, mp(k["x"], k["z"]), 3, (215, 200, 170), 1)
cv2.rectangle(taban, mp(-0.1, 0.03), mp(0.1, -0.03), (0, 0, 0), -1)


def ok(im, x, z, yaw, renk, L=0.45, k=3):
    a = mp(x, z); b = mp(x + L * math.sin(math.radians(yaw)), z + L * math.cos(math.radians(yaw)))
    cv2.circle(im, a, 7, renk, -1); cv2.arrowedLine(im, a, b, renk, k, cv2.LINE_AA, tipLength=0.25)


iz = []; son_uy = None; son_uy_i = -99
for n, i in enumerate(idx):
    pan = cv2.cvtColor(nh.panorama(G[i]), cv2.COLOR_GRAY2BGR)
    if i in UY: son_uy, son_uy_i = UY[i], i
    if son_uy is not None and i - son_uy_i <= ADIM:
        for lo, la in zip(*son_uy):
            cv2.circle(pan, (int((lo / 360.0 + 0.5) * nh.GEN), int((nh.UST - la) * nh.PX)), 5, (40, 220, 40), 2, cv2.LINE_AA)
    m = taban.copy()
    if i in HAR: iz.append(mp(HAR[i][0], HAR[i][1]))
    for a, b in zip(iz, iz[1:]): cv2.line(m, a, b, (230, 190, 140), 2, cv2.LINE_AA)
    if i in GER: cv2.drawMarker(m, mp(*GER[i]), (0, 0, 0), cv2.MARKER_CROSS, 18, 2)
    if i in NOR: ok(m, NOR[i][0], NOR[i][1], NOR[i][2], (60, 60, 220), k=2)
    if i in HAR:
        if i in GY: ok(m, HAR[i][0], HAR[i][1], GY[i], (40, 170, 40), L=0.6, k=2)
        ok(m, HAR[i][0], HAR[i][1], HAR[i][2], (200, 120, 30))
    sag = np.full((MH, nh.GEN - MW, 3), 255, np.uint8); cx, cy, rr = 190, 250, 150
    cv2.circle(sag, (cx, cy), rr, (210, 210, 210), 2, cv2.LINE_AA)
    for ad, S, renk, L in (("g", None, (40, 170, 40), 1.0), ("b", NOR, (60, 60, 220), 0.8), ("h", HAR, (200, 120, 30), 0.9)):
        y = GY.get(i) if S is None else (S[i][2] if i in S else None)
        if y is None: continue
        cv2.arrowedLine(sag, (cx, cy), (int(cx + L * rr * math.sin(math.radians(y))), int(cy - L * rr * math.cos(math.radians(y)))), renk, 4, cv2.LINE_AA, tipLength=0.12)
    alt = np.hstack([m, sag]); ara = np.full((8, nh.GEN, 3), 40, np.uint8)
    kare = np.vstack([pan, ara, alt])
    pil = Image.fromarray(cv2.cvtColor(kare, cv2.COLOR_BGR2RGB)); dr = ImageDraw.Draw(pil); y0 = nh.YUK + 8
    def yaz(xy, s, renk=(0, 0, 0), b=20): dr.text(xy, s, fill=renk, font=F(b))
    dr.rectangle((0, 0, 980, 34), fill=(0, 0, 0))
    yaz((10, 3), "Robotun 360° görüntüsü (açılmış).  Yeşil halka: haritadaki 3B noktalardan biri olarak tanınan nokta (%d)" % (len(son_uy[0]) if son_uy is not None and i - son_uy_i <= ADIM else 0), (255, 255, 255), 19)
    dr.rectangle((0, y0, MW - 1, y0 + 32), fill=(255, 255, 255)); dr.rectangle((0, y0 + MH - 64, MW - 1, y0 + MH), fill=(255, 255, 255))
    yaz((10, y0 + 6), "Üstten harita (gri: 3B noktalar, siyah: ArUco, 1 m ızgara)", (60, 60, 60), 18)
    yaz((10, y0 + MH - 58), "mavi ok: haritadan bulunan yer ve yön (işaret HİÇ kullanılmıyor)", (30, 120, 200), 17)
    yaz((10, y0 + MH - 36), "yeşil ok: gerçek yön (telefon jiroskobu)     kırmızı ok: bugünkü sistem (işaretli)", (40, 140, 40), 17)
    yaz((MW + 10, y0 + 6), "Yön kadranı", (60, 60, 60), 18)
    sx = MW + 400; yy = y0 + 60
    uz = UZK.get(i); yaz((sx, yy), "%4.0f. saniye" % (R[i]["t"] - t0), b=22); yy += 40
    yaz((sx, yy), "İşaret: " + ("görünmüyor" if uz is None else "%.1f m uzakta" % uz), b=20); yy += 50
    if i in GY:
        if i in HAR: yaz((sx, yy), "Harita yön hatası:  %4.1f°" % abs(da(HAR[i][2] - GY[i])), (30, 120, 200), 22); yy += 36
        if i in NOR: yaz((sx, yy), "Bugünkü sistem:     %4.1f°" % abs(da(NOR[i][2] - GY[i])), (200, 60, 60), 22); yy += 36
    if i in GER and i in HAR:
        yaz((sx, yy + 10), "Yer hatası: %.0f cm" % (100 * math.hypot(HAR[i][0] - GER[i][0], HAR[i][1] - GER[i][1])), (0, 0, 0), 20)
        yaz((sx, yy + 38), "(siyah artı: işaretten bilinen gerçek yer)", (90, 90, 90), 15)
    pil.save(os.path.join(KAREDIR, "%05d.jpg" % n), quality=88)

import imageio_ffmpeg
ff = imageio_ffmpeg.get_ffmpeg_exe()
subprocess.run([ff, "-y", "-loglevel", "error", "-framerate", "10", "-i", os.path.join(KAREDIR, "%05d.jpg"), "-vf", "scale=1280:-2",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "24", os.path.join(CIK, "isaretsiz_konum.mp4")], check=True)
print("video:", os.path.join(CIK, "isaretsiz_konum.mp4"))
