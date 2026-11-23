"""Tarama teşhis çizimi: oda_test.sh çıktısındaki durum.csv + zemin.json ile kaydın derinlik noktaları yan yana.
Zemini görülüp boyanmayan hücreler nerede, neden (engel mi, bağlı değil mi) ve engel kanıtı ne kadar güçlü.

python tools/tarama/test/durum_ciz.py <kayit> <oda_test_cikti> [cikti.png]
"""
import csv
import json
import os
import random
import sys

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

K, O = sys.argv[1], sys.argv[2]
cikti = sys.argv[3] if len(sys.argv) > 3 else os.path.join(O, "durum.png")
rows = list(csv.DictReader(open(os.path.join(O, "durum.csv"))))
z = json.load(open(os.path.join(O, "zemin.json")))
ac = set(zip(z["acik"][0::2], z["acik"][1::2])); zy = z["zemin_y"]
fig, ax = plt.subplots(1, 3, figsize=(21, 7))
pts = []
with open(os.path.join(K, "noktalar.ply")) as f:
    for s in f:
        if s.startswith("end_header"):
            break
    for s in f:
        v = s.split()
        if len(v) == 3:
            pts.append(tuple(map(float, v)))
random.seed(1); pts = random.sample(pts, min(80000, len(pts)))
for c, k in (("c", lambda h: abs(h) < 0.05), ("orange", lambda h: 0.08 <= h < 0.6), ("red", lambda h: 0.6 <= h < 2.0)):
    q = [p for p in pts if k(p[1] - zy)]; ax[0].scatter([p[0] for p in q], [p[2] for p in q], s=0.2, c=c, lw=0)
ax[0].set_title("derinlik (turkuaz zemin, turuncu 8-60 cm, kirmizi 60 cm+)")


def ciz(a, filt, col, lab):
    q = [r for r in rows if filt(r)]
    a.scatter([int(r["ix"]) * 0.05 for r in q], [int(r["iz"]) * 0.05 for r in q], s=4, c=col, marker="s", lw=0, label="%s %d" % (lab, len(q)))


ciz(ax[1], lambda r: (int(r["ix"]), int(r["iz"])) in ac, "c", "boyali")
ciz(ax[1], lambda r: int(r["zemind"]) >= 15 and r["durum"] == "2" and (int(r["ix"]), int(r["iz"])) not in ac, "red", "zemin gorulmus ama engel")
ciz(ax[1], lambda r: int(r["zemind"]) >= 15 and r["durum"] == "1" and (int(r["ix"]), int(r["iz"])) not in ac, "orange", "acik ama boyanmamis")
ciz(ax[1], lambda r: int(r["zemind"]) >= 15 and r["durum"] == "0", "gray", "kararsiz")
ax[1].legend(markerscale=4); ax[1].set_title("son durum")
ax[2].hist([int(r["encok"]) for r in rows if int(r["zemind"]) >= 15 and r["durum"] == "2"], bins=40)
ax[2].set_title("zemini gorulmus engel hucrelerinde en dolu kup (18: iki carpma, 35: doygun)")
for a in ax[:2]:
    a.set_aspect("equal")
fig.tight_layout(); fig.savefig(cikti, dpi=80)
print("yazildi", cikti)
