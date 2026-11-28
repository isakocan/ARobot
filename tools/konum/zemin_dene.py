"""Zemin bölütleme ile engel mesafesi denemesi (PC; Gemini raporu 28 Kas).

Model: NVIDIA jetson-inference FCN-ResNet18-SUN-RGBD-512x400 (21 sınıf, "floor" = 2), onnxruntime ile.
Giriş: robotun gittiği yöne bakan, 15 derece aşağı eğik tek düz pencere (512x400, 90 derece yatay görüş) — 360
panoramada robotun dibindeki ilk ~36 cm görünmüyor. Gövde: robotun kendi parçaları (teta.DinamikMaske, hareketli
karelerden öğrenilir) ve yere 18 cm'den yakın vuran ışınlar (şasi) kullanılmaz.
Her sütunda alttan yukarı kesintisiz zemin yürünür; zeminin bittiği ilk pikselin ışını yere (h = 0,17 m) indirilir
-> o yöndeki ilk engele uzaklık. Karşılaştırma: aynı karede robotun konumu biliniyorsa (işaret yakınken), telefon
haritasının engel hücrelerine aynı ışınlar boyunca bakılır (haritadaki ilk engel uzaklığı).
Çıktı: <çıktı>/zemin_<kare>.jpg (pencere + zemin boyası + engel çizgisi + harita karşılaştırması) ve özet tablo.
Kullanım: python zemin_dene.py <robot_kaydı> <telefon_ev_klasörü (ev.json, zemin.json)> <çıktı_klasörü> [ADIM=20]"""
import json, math, os, sys
import cv2
import numpy as np
import onnxruntime as ort

BURASI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(BURASI, "..", "..", "robot", "jetson", "konum"))
import teta, konum

K, EVK, CIK = sys.argv[1:4]
ADIM = int(os.environ.get("ADIM", "20"))
MODEL = os.environ.get("MODEL", r"C:/Users/PC/Desktop/ARobot/modeller/fcn_sun/FCN-ResNet18-SUN-RGBD-512x400/fcn_resnet18.onnx")
H_KAM = 0.17
os.makedirs(CIK, exist_ok=True)
ort.set_default_logger_severity(3)
OT = ort.InferenceSession(MODEL, providers=["CPUExecutionProvider"])
W, Hh, GORUS, EGIM = 512, 400, 90.0, -15.0
KAM = teta.SanalKamera(0.0, GORUS, W, Hh, EGIM)
# her pikselin robot çerçevesinde ışını (x sağ, y yukarı, z ileri)
v, u = np.mgrid[0:Hh, 0:W]
a = (u + 0.5 - KAM.cx) / KAM.f; b = (v + 0.5 - KAM.cy) / KAM.f
ISIN = np.stack([a, b, np.ones_like(a)], -1).reshape(-1, 3) @ KAM.R.T
ISIN /= np.linalg.norm(ISIN, axis=1, keepdims=True); ISIN = ISIN.reshape(Hh, W, 3)
asagi = -ISIN[..., 1]
YER = np.where(asagi > 1e-3, H_KAM / np.maximum(asagi, 1e-3), np.inf)          # ışın boyunca yere uzaklık
YATAY = YER * np.hypot(ISIN[..., 0], ISIN[..., 2])                                 # yerde yatay uzaklık
SASI = YATAY < 0.18                                                                 # robotun kendi kasası
SINIF = [l.strip() for l in open(os.path.join(os.path.dirname(MODEL), "classes.txt"))]
RENK = np.array([[int(c) for c in l.split()[:3]] for l in open(os.path.join(os.path.dirname(MODEL), "colors.txt"))], np.uint8)
MEAN = np.array([0.485, 0.456, 0.406], np.float32); STD = np.array([0.229, 0.224, 0.225], np.float32)


def bolutle(rgb):
    x = ((rgb.astype(np.float32) / 255.0 - MEAN) / STD).transpose(2, 0, 1)[None]
    o = OT.run(None, {"input_0": x})[0][0]                                          # 21 x 13 x 16 (1/32 ızgara)
    o = np.stack([cv2.resize(c, (W, Hh), interpolation=cv2.INTER_LINEAR) for c in o])
    return o.argmax(0)


BASLANGIC = 24


def engel_tarama(zemin, gecerli, en_az=6, bosluk=3):
    """Her sütunda alttaki geçerli pikselden yukarı kesintisiz zemin; -> (sütun başına engel uzaklığı m, sınır satırı)."""
    d = np.full(W, np.nan, np.float32); sat = np.full(W, -1, int)
    for x in range(W):
        rows = np.flatnonzero(gecerli[:, x])
        if not len(rows): continue
        y0 = rows[-1]; y = y0; son = None; gap = 0; say = 0
        # gövde kenarındaki gölge / kenar şeridi: alttan en çok BASLANGIC piksel zemin aranır
        while y >= 0 and gecerli[y, x] and not zemin[y, x] and y0 - y < BASLANGIC: y -= 1
        while y >= 0 and gecerli[y, x]:
            if zemin[y, x]: son = y; gap = 0; say += 1
            else:
                gap += 1
                if gap > bosluk: break
            y -= 1
        if son is None or say < en_az:
            d[x] = YATAY[y0, x]; sat[x] = y0                                        # dibinde zemin yok: çok yakın engel
        else:
            d[x] = YATAY[son, x]; sat[x] = son
    return d, sat


# telefon haritası: engel hücreleri
Z = json.load(open(os.path.join(EVK, "zemin.json"), encoding="utf-8")); HC = Z.get("hucre", 0.05)
ENG = set(zip(Z["engel"][0::2], Z["engel"][1::2])); ACK = set(zip(Z["acik"][0::2], Z["acik"][1::2]))


def harita_mesafe(px, pz, yaw_der, kolon_yon):
    """Robot (px, pz, yaw) -> her sütun yönünde haritadaki ilk engel / bilinmeyen hücre (m)."""
    out = np.full(len(kolon_yon), np.nan, np.float32)
    for j, yn in enumerate(kolon_yon):
        r = math.radians(yaw_der + yn); sx, sz = math.sin(r), math.cos(r)
        for t in np.arange(0.18, 2.5, 0.025):
            c = (int(round((px + sx * t) / HC)), int(round((pz + sz * t) / HC)))
            if c in ENG: out[j] = t; break
            if c not in ACK: out[j] = t; break                                     # taranmamış: bilinmiyor (sınır)
    return out


R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
EV = json.load(open(os.path.join(K, "ev.json")))["isaretler"]
kb = konum.KonumBulucu(); kb.ev_ayarla(EV)
# Gövde maskesi (ön geçiş): robot hareket ederken pencerede hiç değişmeyen pikseller robotun kendisidir (ön plaka,
# kablo). Gövdenin üst kenarı zemini ~0,35 m'ye kadar örter; bu pikseller engel sayılmamalı.
toplam = np.zeros((Hh, W), np.float64); kare_t = np.zeros((Hh, W), np.float64); n_ = 0; onceki = None
for f in R[::3]:
    if f["sol"] == 0 and f["sag"] == 0: continue
    g = cv2.imread(os.path.join(K, f["jpg"]), cv2.IMREAD_GRAYSCALE)
    if g is None: continue
    p_ = cv2.GaussianBlur(cv2.remap(g, KAM.mx, KAM.my, cv2.INTER_LINEAR).astype(np.float32), (0, 0), 2.0)
    if onceki is not None:
        d_ = np.abs(p_ - onceki); toplam += d_; n_ += 1
    onceki = p_
degisim = toplam / max(n_, 1)
GOVDE = degisim < 0.25 * np.median(degisim)
GOVDE = cv2.dilate(cv2.morphologyEx(GOVDE.astype(np.uint8), cv2.MORPH_OPEN, np.ones((5, 5), np.uint8)), np.ones((9, 9), np.uint8)) > 0
# yalnız pencerenin alt yarısındaki ve alttan bağlı gövde (üstte duran düz duvar "değişmiyor" görünebilir)
_, et = cv2.connectedComponents(GOVDE.astype(np.uint8))
alt = set(np.unique(et[-3:, :])) - {0}
GOVDE = np.isin(et, list(alt)) if alt else np.zeros_like(GOVDE)
cv2.imwrite(os.path.join(CIK, "govde_maskesi.png"), (GOVDE * 255).astype(np.uint8))
print("govde maskesi: %d hareketli kare, pencerenin %%%d" % (n_, 100 * GOVDE.mean()))
kolon_yon = np.degrees(np.arctan2(ISIN[Hh - 1, :, 0], ISIN[Hh - 1, :, 2]))           # sütunun yatay yönü (der)
ozet = []
for i, f in enumerate(R):
    g = cv2.imread(os.path.join(K, f["jpg"]), cv2.IMREAD_COLOR)
    if g is None: continue
    kb.isle(cv2.cvtColor(g, cv2.COLOR_BGR2GRAY), f["t"], (f["sol"], f["sag"]))
    pen = cv2.remap(g, KAM.mx, KAM.my, cv2.INTER_LINEAR)
    if i % ADIM: continue
    sinif = bolutle(cv2.cvtColor(pen, cv2.COLOR_BGR2RGB))
    gecerli = ~SASI & ~GOVDE & np.isfinite(YATAY)
    d, sat = engel_tarama(sinif == 2, gecerli)
    hm = None
    yakin = kb.bilinen and [q for q in kb.gorulen if q[1] <= 1.5] and math.degrees(math.sqrt(kb.P[2, 2])) < 5
    if yakin:
        hm = harita_mesafe(kb.x, kb.z, math.degrees(kb.yaw), kolon_yon)
        ok = np.isfinite(d) & np.isfinite(hm) & (hm < 1.5)
        if ok.sum() > 20: ozet.append((i, float(np.median(np.abs(d[ok] - hm[ok]))), float(np.mean(np.abs(d[ok] - hm[ok]) < 0.15)), int(ok.sum())))
    # resim
    boya = RENK[sinif][..., ::-1]
    im = cv2.addWeighted(pen, 0.6, boya, 0.4, 0)
    im[~gecerli] = (im[~gecerli] * 0.35).astype(np.uint8)
    for x in range(W):
        if sat[x] >= 0: cv2.circle(im, (x, int(sat[x])), 1, (0, 0, 255), -1)
    # alt şerit: üstten görünüş (kamera ileri yukarı), yeşil = bölütleme engeli, mavi = harita engeli
    ust = np.full((260, W, 3), 25, np.uint8); S = 120.0
    for m_ in (0.5, 1.0, 1.5, 2.0): cv2.circle(ust, (W // 2, 250), int(m_ * S), (70, 70, 70), 1)
    for x in range(0, W, 2):
        r = math.radians(kolon_yon[x])
        if np.isfinite(d[x]): cv2.circle(ust, (int(W / 2 + math.sin(r) * d[x] * S), int(250 - math.cos(r) * d[x] * S)), 2, (60, 220, 60), -1)
        if hm is not None and np.isfinite(hm[x]): cv2.circle(ust, (int(W / 2 + math.sin(r) * hm[x] * S), int(250 - math.cos(r) * hm[x] * S)), 2, (230, 150, 40), -1)
    cv2.putText(ust, "ustten: yesil = bolutlemeden engel, mavi = telefon haritasinda engel; halkalar 0,5 m", (6, 16), cv2.FONT_HERSHEY_SIMPLEX, 0.42, (220, 220, 220), 1, cv2.LINE_AA)
    cv2.putText(im, "kare %d  zemin %%%d" % (i, 100 * (sinif[gecerli] == 2).mean() if gecerli.any() else 0), (6, 18), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 2, cv2.LINE_AA)
    cv2.imwrite(os.path.join(CIK, "zemin_%04d.jpg" % i), np.vstack([im, ust]))
print("kare | bolutleme - harita engel uzakligi farki ortanca (m) | 15 cm icinde oran | sutun")
for i, e, o, n in ozet: print("%5d  %.2f  %%%2d  %d" % (i, e, 100 * o, n))
if ozet: print("GENEL: ortanca fark %.2f m, 15 cm icinde %%%d" % (np.median([e for _, e, _, _ in ozet]), 100 * np.mean([o for _, _, o, _ in ozet])))
