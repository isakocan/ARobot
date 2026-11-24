"""Dönüş kaynaklarını gerçek yöne (telefon jiroskobu, gercek_yon.json) karşı kare kare ölçer (PC).
Kaynaklar: görsel pusula (teta.Pusula), zemin izi (zemin_izi), motor komutu. Kullanım: python donus_olc.py <kök> <robot_kaydı>"""
import json, math, os, sys
import cv2
import numpy as np

KOK, K = sys.argv[1:3]
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
import teta, zemin_izi

R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
g = json.load(open(os.path.join(K, "gercek_yon.json"))); GY = dict(zip(g["kare"], g["yaw"]))
pus = teta.Pusula(); izi = zemin_izi.ZeminIzi(); son = None; satir = []; onceki_kom = (0, 0)
def da(a): return (a + 180) % 360 - 180
for i, f in enumerate(R):
    gri = cv2.imread(os.path.join(K, f["jpg"]), cv2.IMREAD_GRAYSCALE)
    dt = 0 if son is None else f["t"] - son; son = f["t"]
    komut = 140.0 * (f["sol"] - f["sag"]) / 510.0 * dt
    komut_gec = 140.0 * (onceki_kom[0] - onceki_kom[1]) / 510.0 * dt      # goruntu ~1 kare gec: bir onceki karenin komutu
    onceki_kom = (f["sol"], f["sag"])
    d, guv = pus.guncelle(gri, komut)
    od = izi.guncelle(gri)
    if i - 1 in GY and i in GY:
        gercek = da(GY[i] - GY[i - 1])
        a, b = R[i - 1]["sol"], R[i - 1]["sag"]
        tur = "dur" if a == 0 and b == 0 else "yerinde" if a * b < 0 else "duz" if abs(a - b) <= 0.15 * max(abs(a), abs(b)) else "giderken"
        satir.append((tur, gercek, d, guv, od[2] if od else np.nan, komut, gercek / max(dt, 1e-3), komut_gec))
A = satir
print("tur        n   gercek hiz ort/maks (der/sn) | pusula hata ortanca/%90 | zemin izi hata ort/%90 (var %) | komut hata ort/%90")
for t in ("dur", "duz", "giderken", "yerinde"):
    s = [x for x in A if x[0] == t]
    if not s: continue
    g_ = np.array([x[1] for x in s]); p = np.array([x[2] for x in s]); z = np.array([x[4] for x in s]); k = np.array([x[5] for x in s]); h = np.array([abs(x[6]) for x in s])
    ep = np.abs(p - g_); ez = np.abs(z - g_); ek = np.abs(k - g_); zv = ~np.isnan(z)
    print("%-9s %4d  %5.0f / %5.0f              | %5.1f / %5.1f            | %5.1f / %5.1f (%3d%%)        | %5.1f / %5.1f" % (
        t, len(s), h.mean(), h.max(), np.median(ep), np.percentile(ep, 90), np.nanmedian(ez) if zv.any() else -1, np.nanpercentile(ez, 90) if zv.any() else -1, 100 * zv.mean(), np.median(ek), np.percentile(ek, 90)))
# hiz kovalarina gore pusula
g_ = np.array([x[1] for x in A]); p = np.array([x[2] for x in A]); h = np.array([abs(x[6]) for x in A])
print("\ngercek donus hizina gore pusula hatasi (kare basina):")
for lo, hi in ((0, 10), (10, 30), (30, 60), (60, 100), (100, 1000)):
    m = (h >= lo) & (h < hi)
    if m.sum(): print("  %3d-%4d der/sn: %3d kare, ortanca hata %.1f, %%90 %.1f der" % (lo, hi, m.sum(), np.median(np.abs(p[m] - g_[m])), np.percentile(np.abs(p[m] - g_[m]), 90)))
# toplam: surus boyunca birikim
print("\ntoplam donus: gercek %.0f, pusula %.0f, komut %.0f der (mutlak toplam gercek %.0f)" % (g_.sum(), p.sum(), sum(x[5] for x in A), np.abs(g_).sum()))

# birikim: her kaynakla yön zinciri, gerçeğe göre en büyük / son sapma
def zincir(sec):
    e = 0.0; en = 0.0; seri = []
    for x in A:
        e += sec(x) - x[1]; en = max(en, abs(e)); seri.append(e)
    return en, e, np.array(seri)
kurallar = {
    "pusula": lambda x: x[2],
    "komut": lambda x: x[5],
    "zemin izi (yoksa pusula)": lambda x: x[4] if not np.isnan(x[4]) else x[2],
    "birlesik: yerinde komut, digerinde zemin izi": lambda x: x[5] if x[0] == "yerinde" else (x[4] if not np.isnan(x[4]) else x[2]),
    "birlesik2: yerinde komut+pusula ort.": lambda x: 0.5 * (x[5] + x[2]) if x[0] == "yerinde" else (x[4] if not np.isnan(x[4]) else x[2]),
}
print("\nbirikmis yon hatasi (surus boyunca, isaretsiz):")
for ad, f in kurallar.items():
    en, son_, _ = zincir(f)
    print("  %-46s en buyuk %5.1f der, sonda %6.1f der" % (ad, en, son_))

f = kurallar["birlesik2: yerinde komut+pusula ort."]
er = [(abs(f(x) - x[1]), j, x) for j, x in enumerate(A)]
er.sort(reverse=True)
print("\nen kotu 12 adim (birlesik2):")
for e, j, x in er[:12]:
    print("  adim %3d %-8s gercek %6.1f  pusula %6.1f (guven %.2f)  zemin %6.1f  komut %6.1f  -> hata %5.1f" % (j, x[0], x[1], x[2], x[3], x[4], x[5], e))
print("toplam hatanin buyuk adimlardan gelen kismi: |hata|>5 der adimlar %d, toplam %.0f der; digerleri toplam %.0f der" % (
    sum(1 for e, _, _ in er if e > 5), sum(e for e, _, _ in er if e > 5), sum(e for e, _, _ in er if e <= 5)))

# goruntu gecikmesi: pusula adimlari gercek adimlarla kac kare kaydirilinca en iyi ortusuyor
g_ = np.array([x[1] for x in A]); p = np.array([x[2] for x in A]); z = np.array([x[4] if not np.isnan(x[4]) else x[2] for x in A]); k = np.array([x[5] for x in A])
print("\nkaydirma (kare; + = goruntu gercekten gec): pusula / zemin izi / komut ortalama mutlak hata")
for s in range(-3, 5):
    if s >= 0: a, b = g_[:len(g_) - s], (p[s:], z[s:], k[s:])
    else: a, b = g_[-s:], (p[:s], z[:s], k[:s])
    print("  %+d: %.2f / %.2f / %.2f" % (s, *[np.mean(np.abs(q - a)) for q in b]))

er = sorted([(abs(x[2] - x[1]), j, x) for j, x in enumerate(A)], reverse=True)
print("\nen kotu 10 adim (pusula):")
for e, j, x in er[:10]:
    print("  adim %3d %-8s gercek %7.1f  pusula %7.1f (guven %.2f)  zemin %7.1f  komut %7.1f  hata %5.1f" % (j, x[0], x[1], x[2], x[3], x[4], x[5], e))
print("pusula: |hata|>10 der adim %d, toplam %.0f der; geri kalanlarin toplami %.0f der" % (sum(1 for e, _, _ in er if e > 10), sum(e for e, _, _ in er if e > 10), sum(e for e, _, _ in er if e <= 10)))

iyi = [x for x in A if abs(x[2] - x[1]) < 5 and abs(x[1]) > 3]
eg = np.polyfit([x[1] for x in iyi], [x[2] for x in iyi], 1)
print("\npusula olcegi (iyi adimlar): pusula = %.3f * gercek %+.2f" % (eg[0], eg[1]))
def sec(x, olcek=1.0):
    p, z, k = x[2], x[4], x[5]
    zv = not np.isnan(z)
    # pusula sifira yapisti ama zemin izi / komut donuyor diyor: pusulaya guvenme
    if abs(p) < 1.0 and ((zv and abs(z) > 4) or abs(k) > 4):
        return z if zv else k
    return p / olcek
for ad, f in (("kural: sifir tepesinde zemin izi/komut", lambda x: sec(x)), ("kural + olcek", lambda x: sec(x, eg[0]))):
    en, son_, _ = zincir(f); print("  %-46s en buyuk %5.1f der, sonda %6.1f der" % (ad, en, son_))
_, _, seri = zincir(lambda x: sec(x))
print("\nbirikim (kural), her 20 adimda: ", " ".join("%d:%.0f" % (j, seri[j]) for j in range(0, len(seri), 20)))
d = np.diff(np.concatenate([[0], seri]))
big = np.argsort(-np.abs(d))[:12]
for j in sorted(big): x = A[j]; print("  adim %3d %-8s gercek %6.1f pusula %6.1f guven %.2f zemin %6.1f komut %6.1f -> %+.1f" % (j, x[0], x[1], x[2], x[3], x[4], x[5], d[j]))

def sec2(x):
    p, z, k = x[2], x[4], x[7]
    zv = not np.isnan(z)
    if abs(p) < 1.0 and ((zv and abs(z) > 4) or abs(k) > 4):
        return z if zv else k
    return p
ek = np.array([abs(x[7] - x[1]) for x in A]); e0 = np.array([abs(x[5] - x[1]) for x in A])
print("\nkomut hatasi: ayni kare %.2f, bir onceki kare %.2f der (ortalama)" % (e0.mean(), ek.mean()))
for ad, f in (("komut (bir kare gec)", lambda x: x[7]), ("kural: sifir tepesinde zemin izi / gec komut", sec2)):
    en, son_, _ = zincir(f); print("  %-46s en buyuk %5.1f der, sonda %6.1f der" % (ad, en, son_))
