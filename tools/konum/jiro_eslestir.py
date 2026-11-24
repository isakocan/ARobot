"""Telefon jiroskop kaydı (robotun üstünde düz) -> robot karelerinde gerçek yön dayanağı (PC).

1. Dikey eksen etrafındaki dönüş hızı: r = -(w . g/|g|) (rad/sn), sapma (bias) duruş anlarından çıkarılır.
2. Zaman kayması: robotun dönüş komutu (sol - sag) ile r arasındaki en iyi ilinti (±3 sn).
3. Yön: r'nin integrali; başlangıç açısı, yakın (≤1,2 m) işaret görülen karelerdeki işaret yüzeyi yönüyle
   (çok gözlemin ortancası; tek tek ±10 derece oynuyor) hizalanır.
Çıktı: <robot_kaydı>/gercek_yon.json {"kare": [...], "yaw": [...] (derece, robot yaw ile aynı yön: sağa +), "kayma": sn}

Kullanım: python jiro_eslestir.py <robot_kaydı> <jiro.csv>
"""
import json, math, os, sys
import numpy as np

K, CSV = sys.argv[1:3]
R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
EV = {int(m[0]): (m[1], m[2], m[3]) for m in json.load(open(os.path.join(K, "ev.json")))["isaretler"]}
J = np.genfromtxt(CSV, delimiter=",", names=True)
J = J[np.abs(J["gz"]) + np.abs(J["gx"]) + np.abs(J["gy"]) > 0.5]
t = J["utc_sn"]; w = np.stack([J["wx"], J["wy"], J["wz"]], 1); g = np.stack([J["gx"], J["gy"], J["gz"]], 1)
gu = g / np.linalg.norm(g, axis=1, keepdims=True)
r = -(w * gu).sum(1)

rt = np.array([f["t"] for f in R]); kom = np.array([(f["sol"] - f["sag"]) / 510.0 for f in R])
# sapma: robot dururken ve jiroskop sakinken
dur_r = np.interp(t, rt, (np.abs(kom) < 1e-6).astype(float)) > 0.99
sak = dur_r & (np.abs(w).sum(1) < 0.05)
bias = float(np.median(r[sak])) if sak.sum() > 50 else 0.0
r = r - bias


def ilinti(tau):
    rr = np.interp(rt + tau, t, r)
    m = (rt + tau > t[0]) & (rt + tau < t[-1])
    a, b = rr[m], kom[m]
    return float(np.corrcoef(a, b)[0, 1]) if m.sum() > 20 else -1


taus = np.arange(-3, 3.001, 0.02); c = np.array([ilinti(x) for x in taus])
tau = float(taus[int(np.argmax(c))]); isaret_yonu = 1.0
if abs(c.min()) > abs(c.max()):
    tau = float(taus[int(np.argmin(c))]); isaret_yonu = -1.0
r = r * isaret_yonu
tau_komut = tau
# goruntu zamani: kameranin gordugu donus (gorsel pusula) komuttan gec gelir (25 Kasim: ~1 kare); gercek yon goruntuye
# hizalanir (konum goruntuden hesaplanir). Komut hizasinin cevresinde pusula adimlariyla ince ayar.
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "robot", "jetson", "konum"))
import cv2, teta
pus = teta.Pusula(); pa = [0.0]
for f in R:
    d, _ = pus.guncelle(cv2.imread(os.path.join(K, f["jpg"]), cv2.IMREAD_GRAYSCALE)); pa.append(d)
pa = np.array(pa[1:])
Yt = np.concatenate([[0], np.cumsum(0.5 * (r[1:] + r[:-1]) * np.diff(t))])
def hata(x):
    yy = np.degrees(np.interp(rt + x, t, Yt)); gd = np.diff(yy); m = (rt[1:] + x > t[0]) & (rt[1:] + x < t[-1])
    return float(np.mean(np.abs(pa[1:][m] - gd[m])))
tt = np.arange(tau - 1.0, tau + 0.501, 0.01); hh = [hata(x) for x in tt]
tau = float(tt[int(np.argmin(hh))])
print("zaman: komuta gore %.2f sn, goruntuye gore %.2f sn (goruntu %.2f sn gec); pusula kare basi hata %.2f der" % (tau_komut, tau, tau_komut - tau, min(hh)))
# integral (yamuk), robot karelerinde
Y = np.concatenate([[0], np.cumsum(0.5 * (r[1:] + r[:-1]) * np.diff(t))])
yk = np.degrees(np.interp(rt + tau, t, Y))
# başlangıç: yakın işaretlerin yüzey yönünden robot yönü = E.aci - gaci
fark = []
for i, f in enumerate(R):
    for q in f["gorulen"]:
        if q[0] in EV and math.hypot(q[1], q[2]) < 1.2:
            yaw = math.degrees(EV[q[0]][2] - q[3])
            fark.append(((yaw - yk[i] + 180) % 360) - 180)
y0 = float(np.median(fark)) if fark else 0.0
yaw = yk + y0
ic = (rt + tau > t[0]) & (rt + tau < t[-1])
sapma_isaret = np.array(fark) - y0 if fark else np.array([])
print("jiro %d olcum %.0f sn, %.0f Hz | sapma %.4f der/sn | isaret yonu %+d | zaman kaymasi %.2f sn (ilinti %.2f)" % (
    len(t), t[-1] - t[0], len(t) / (t[-1] - t[0]), math.degrees(bias), isaret_yonu, tau, c.max() if isaret_yonu > 0 else -c.min()))
print("yakin isaret gozlemi %d: jiro yonu - isaret yuzey yonu: ortanca 0 (hizalandi), mutlak ortanca %.1f, %%90 %.1f der" % (
    len(fark), np.median(np.abs(sapma_isaret)) if len(fark) else -1, np.percentile(np.abs(sapma_isaret), 90) if len(fark) else -1))
json.dump({"kare": [i for i in range(len(R)) if ic[i]], "yaw": [round(float(yaw[i]), 2) for i in range(len(R)) if ic[i]], "kayma": tau, "kayma_komut": tau_komut,
           "sapma_der_sn": math.degrees(bias)}, open(os.path.join(K, "gercek_yon.json"), "w"))
print("yazildi", os.path.join(K, "gercek_yon.json"), int(ic.sum()), "kare")
