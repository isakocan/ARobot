"""Anahtar karelerle yer tanıma ne kadar tutuyor (PC).

Kayıtlar konum.py ile yeniden oynatılır. İşaretin yakından (<= YAKIN m) ve yön belirsizliği küçükken görüldüğü kareler
"yeri bilinen" karelerdir. Bir kaydın bu karelerinden anahtar kare hafızası kurulur (konum.py'nin kullandığı öznitelik:
hafiza.Hafiza.ozellik, 360 derece ufuk şeridi), öbür kaydın yeri bilinen kareleri sorulur: en iyi eşleşmenin yeri ve yönü
gerçeğe (işaretten) ne kadar yakın? Aynı kayıt içinde de zamanca uzak karelerle (>= 20 sn) denenir.
Kullanım: python hafiza_olc.py <kök> <kayıt_A> [<kayıt_B> ...]"""
import json, math, os, sys
import cv2
import numpy as np

KOK = sys.argv[1]; KAYITLAR = sys.argv[2:]
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
import konum, hafiza

YAKIN = 1.3
hafiza.ARALIK = float(os.environ.get("ARALIK", "0.10"))   # deney: robottaki 0,30 yerine sik kare


def oku(K):
    R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
    EV = json.load(open(os.path.join(K, "ev.json")))["isaretler"]
    kb = konum.KonumBulucu(); kb.ev_ayarla(EV); out = []
    for f in R:
        g = cv2.imread(os.path.join(K, f["jpg"]), cv2.IMREAD_GRAYSCALE)
        if g is None: continue
        kb.isle(g, f["t"], (f["sol"], f["sag"]))
        yakin = [q for q in kb.gorulen if q[1] <= YAKIN]
        if kb.bilinen and yakin and math.degrees(math.sqrt(kb.P[2, 2])) < 4:
            out.append((f["t"], kb.x, kb.z, kb.yaw, hafiza.Hafiza.ozellik(kb.pusula.onceki)))
    return out


def da(a): return (a + 180) % 360 - 180


V = {K: oku(K) for K in KAYITLAR}
for K, v in V.items():
    print("%s: yeri bilinen kare %d" % (os.path.basename(K), len(v)))


def dene(hafiza_kareleri, sorular, ad):
    h = hafiza.Hafiza()
    for t, x, z, yaw, F in hafiza_kareleri:
        h.ekle(F, x, z, yaw)
    ey, ep, dogru, ben_d, ben_y, en_iyi, rastgele = [], [], 0, [], [], [], []
    for t, x, z, yaw, F in sorular:
        m = h.eslestir(F)
        if not m: continue
        s, mx, mz, myaw = m[0]
        e = math.hypot(mx - x, mz - z); ep.append(e); ey.append(abs(da(math.degrees(myaw - yaw))))
        dd = np.hypot(h.poz[:, 0] - x, h.poz[:, 1] - z); en_iyi.append(dd.min()); rastgele.append(np.median(dd))
        (ben_d if e < 0.4 else ben_y).append(s)
    ep, ey = np.array(ep), np.array(ey)
    if not len(ep): print(ad, "soru yok"); return
    print("%-34s hafiza %3d kare, soru %3d | yer hatasi ortanca %.2f m, %%90 %.2f m, <0,4 m: %%%d | yon hatasi ortanca %.1f, %%90 %.1f der | benzerlik dogru %.2f / yanlis %.2f" % (
        ad, len(h), len(ep), np.median(ep), np.percentile(ep, 90), 100 * np.mean(ep < 0.4), np.median(ey), np.percentile(ey, 90),
        np.median(ben_d) if ben_d else -1, np.median(ben_y) if ben_y else -1))
    print("%34s karsilastirma: en yakin anahtar kare ortanca %.2f m (olabilecek en iyi), rastgele kare ortanca %.2f m (sans)" % ("", np.median(en_iyi), np.median(rastgele)))
    yerler = np.array([[x, z] for _, x, z, _, _ in sorular])
    print("%34s sorularin yayildigi alan: x %.1f..%.1f, z %.1f..%.1f m; isarete uzaklik ortanca %.2f m" % (
        "", yerler[:, 0].min(), yerler[:, 0].max(), yerler[:, 1].min(), yerler[:, 1].max(), np.median(np.hypot(yerler[:, 0], yerler[:, 1]))))


ks = list(V)
for a in ks:
    v = V[a]
    # ayni kayit: ilk yaridan hafiza, ikinci yaridan (20 sn sonrasi) soru
    if len(v) > 20:
        tm = v[len(v) // 2][0]
        dene([q for q in v if q[0] < tm], [q for q in v if q[0] > tm + 20], "%s icinde (yari/yari)" % os.path.basename(a))
    for b in ks:
        if a != b:
            dene(V[a], V[b], "%s -> %s" % (os.path.basename(a), os.path.basename(b)))
