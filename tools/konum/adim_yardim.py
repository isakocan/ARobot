"""Adım adım sürüşte karar yardımcısı (PC): rota arayüzünden anlık konum (kamera + jiroskop simgesi), telefon haritasında
engele uzaklık, sıradaki rota noktalarına yön / uzaklık ve düz çizgi boyunca en dar boşluk.
Kullanım: python adim_yardim.py <rota_klasörü> [hedef_no ...]"""
import json, math, sys, urllib.request
import cv2
import numpy as np

KL = sys.argv[1]
Z = json.load(open(KL + "/zemin.json", encoding="utf-8")); R = json.load(open(KL + "/rota.json", encoding="utf-8"))["noktalar"]
a = np.array(Z["acik"]).reshape(-1, 2); x0, z0 = a.min(0) - 10; x1, z1 = a.max(0) + 10
A = np.zeros((z1 - z0 + 1, x1 - x0 + 1), np.uint8); A[a[:, 1] - z0, a[:, 0] - x0] = 1
U = cv2.distanceTransform(A, cv2.DIST_L2, 5) * 0.05


def bosluk(x, z):
    c, r = int(round(x / 0.05)) - x0, int(round(z / 0.05)) - z0
    return float(U[r, c]) if 0 <= r < U.shape[0] and 0 <= c < U.shape[1] else 0.0


def cizgi(p, q): return min(bosluk(p[0] + (q[0] - p[0]) * s, p[1] + (q[1] - p[1]) * s) for s in np.linspace(0, 1, 40))
def da(v): return (v + 180) % 360 - 180


d = json.load(urllib.request.urlopen("http://127.0.0.1:8770/durum", timeout=3))
k, j = d["poz"], d["jiro"]
print("kamera  x %.2f z %.2f yön %4.0f | görülen %s" % (k["x"], k["z"], da(k["yaw"]), [(g[0], g[1]) for g in k.get("gorulen") or []]))
print("jiroskop x %.2f z %.2f yön %4.0f | canlı %s" % (j["x"], j["z"], j["yaw"], j["canli"]))
for ad, p in (("kamera", (k["x"], k["z"])), ("jiroskop", (j["x"], j["z"]))):
    print("  %s yerinde engele / bilinmeyene: %.2f m" % (ad, bosluk(*p)))
hedefler = [int(v) for v in sys.argv[2:]] or range(len(R))
p = (j["x"], j["z"])
for i in hedefler:
    q = (R[i]["x"], R[i]["z"]); yon = math.degrees(math.atan2(q[0] - p[0], q[1] - p[1]))
    print("  hedef %2d (%5.2f, %5.2f) %-18s: %.2f m, yön %4.0f (dönülecek %+4.0f), çizgide en dar %.2f m"
          % (i, q[0], q[1], R[i].get("test", "")[:18], math.hypot(q[0] - p[0], q[1] - p[1]), yon, da(yon - j["yaw"]), cizgi(p, q)))
