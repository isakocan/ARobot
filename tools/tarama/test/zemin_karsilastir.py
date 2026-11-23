"""Zemin haritasi regresyon karsilastirmasi: her kayit icin derinlik bulutu + her surumun haritasi yan yana.

Kullanim: python zemin_karsilastir.py <cikti.png> <kayit_klasoru> [<kayit_klasoru> ...]
Her kayit oda_test.sh ile iki kez oynatilir: eski kural (ZEMIN_ESKI=1) ve simdiki. Bir kayda gore ayar yapip
digerini bozmamak icin her degisiklikte hepsine birlikte bakilir (23 Kasim).
"""
import json, os, random, subprocess, sys
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt

HERE = os.path.dirname(os.path.abspath(__file__))
cikti, kayitlar = sys.argv[1], sys.argv[2:]
# ESKI=hist: "eski" 23 Kasim derinlik kurali (histerezis ve pay yok), yoksa derinlikten onceki cogunluk kurali
surumler = [("eski", {"ZEMIN_HIST": "0"} if os.environ.get("ESKI") == "hist" else {"ZEMIN_ESKI": "1"}), ("simdi", {})]
fig, ax = plt.subplots(len(kayitlar), 1 + len(surumler), figsize=(6 * (1 + len(surumler)), 6 * len(kayitlar)), squeeze=False)
for r, K in enumerate(kayitlar):
    yol = [(json.loads(l)["px"], json.loads(l)["pz"]) for l in open(os.path.join(K, "kareler.jsonl"))]
    sonuc = {}
    for ad, env in surumler:
        o = os.path.join(os.path.dirname(cikti), "zk_" + os.path.basename(K) + "_" + ad)
        e = dict(os.environ); e.update(env)
        subprocess.run(["bash", os.path.join(HERE, "oda_test.sh"), K, o], env=e, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
        sonuc[ad] = json.load(open(os.path.join(o, "zemin.json")))
    zy = sonuc["simdi"]["zemin_y"]
    pts = []
    with open(os.path.join(K, "noktalar.ply")) as f:
        for s in f:
            if s.startswith("end_header"): break
        for s in f:
            v = s.split()
            if len(v) == 3: pts.append(tuple(map(float, v)))
    random.seed(1); pts = random.sample(pts, min(80000, len(pts)))
    for c, k in (("c", lambda h: abs(h) < 0.05), ("orange", lambda h: 0.08 <= h < 0.6), ("red", lambda h: 0.6 <= h < 2.0)):
        q = [p for p in pts if k(p[1] - zy)]; ax[r][0].scatter([p[0] for p in q], [p[2] for p in q], s=0.2, c=c, lw=0)
    ax[r][0].set_title(os.path.basename(K) + ": derinlik")
    for i, (ad, _) in enumerate(surumler):
        zj = sonuc[ad]; V = zj["hucre"]
        import numpy as np
        hepsi = zj["engel"] + zj["acik"]
        x0, x1 = min(hepsi[0::2]), max(hepsi[0::2]); z0, z1 = min(hepsi[1::2]), max(hepsi[1::2])
        img = np.ones((z1 - z0 + 1, x1 - x0 + 1, 3))
        for key, c in (("engel", (0.6, 0.6, 0.6)), ("acik", (0.26, 0.85, 0.75))):
            a = zj[key]
            for j in range(0, len(a), 2): img[a[j + 1] - z0, a[j] - x0] = c
        ax[r][1 + i].imshow(img, origin="lower", extent=[x0 * V, (x1 + 1) * V, z0 * V, (z1 + 1) * V], interpolation="nearest")
        ax[r][1 + i].set_title("%s: %.1f m2" % (ad, zj["alan_m2"]))
    xs = [p[0] for p in yol]; zs = [p[1] for p in yol]
    for a in ax[r]:
        a.plot(xs, zs, "k-", lw=.6); a.set_aspect("equal"); a.grid(alpha=.3)
        a.set_xlim(min(xs) - 2.5, max(xs) + 2.5); a.set_ylim(min(zs) - 2.5, max(zs) + 2.5)
plt.tight_layout(); plt.savefig(cikti, dpi=45)
print("yazildi", cikti)
