"""PatchMatch surucusu ciktisini cozumler: gorunum eslemesi, yogun-seyrek uyumu, kapsam, zemin, duvar kusagi, PLY, goruntuler.
  python tools/slam_deneme/yogun_analiz.py <cikti_klasoru> <girdi_klasoru> [--gorsel] [--ply yol]
Esleme: cikis j = kabul edilen gorunumlerin [j+OFS]'uncusu; OFS 0..8 denenir, en kucuk ortanca bagil fark secilir (--ofs ile zorlanabilir).
Olcek: --olcek m/birim (ontanim 3.349 = bant olcegi)."""
import argparse
import json
import os
import re
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
from derinlik_ortak import yon_to_piksel  # noqa: E402

ap = argparse.ArgumentParser()
ap.add_argument("cikti")
ap.add_argument("girdi")
ap.add_argument("--gorsel", action="store_true")
ap.add_argument("--ply")
ap.add_argument("--ofs", type=int)
ap.add_argument("--olcek", type=float, default=3.349)
ap.add_argument("--yol", default="kayitlar/slam/sonuc/h1/poz.jsonl")
a = ap.parse_args()
S = a.olcek
idx = [l.split() for l in open(a.girdi + "/index.txt")]
N = len(idx)
red = [int(m) for m in re.findall(r"reddedildi gorunum (\d+)", open(a.cikti + "/log.txt").read())]
kabul = [i for i in range(N) if i not in red]
cik = [l.split() for l in open(a.cikti + "/cikti.txt")]
J = len(cik)


def poz(i):
    v = np.loadtxt("%s/%s_poz.txt" % (a.girdi, idx[i][0]))
    return v[:9].reshape(3, 3), v[9:]


def seyrek(i):
    return np.loadtxt("%s/%s_seyrek.txt" % (a.girdi, idx[i][0])).reshape(-1, 3)


D = [np.fromfile("%s/cikti_%03d_depth.f32" % (a.cikti, j), dtype=np.float32).reshape(320, 640) for j in range(J)]


def uyum(j, v):
    R, t = poz(v)
    P = seyrek(v)
    pc = P @ R.T + t
    d = np.linalg.norm(pc, axis=1)
    b = pc / d[:, None]
    u, vv = yon_to_piksel(b, 640, 320)
    u = np.clip(u.astype(int), 0, 639)
    vv = np.clip(vv.astype(int), 0, 319)
    dd = D[j][vv, u]
    ok = dd > 0
    return (np.abs(dd[ok] - d[ok]) / d[ok]), ok.mean(), len(P)


print("giris gorunum %d, reddedilen %d, cikis %d" % (N, len(red), J))
print("(a) esleme sinamasi: OFS -> ortanca bagil fark / %90 / derinlik-landmark isabet orani (tum cikislar)")
en = None
for ofs in ([a.ofs] if a.ofs is not None else range(0, 9)):
    r, h = [], []
    for j in range(J):
        v = j + ofs
        if v >= len(kabul):
            break
        x, hh, _ = uyum(j, kabul[v])
        r.append(x)
        h.append(hh)
    r = np.concatenate(r)
    print("  OFS %d: ortanca %.3f  p90 %.3f  isabet %.2f  (n=%d)" % (ofs, np.median(r), np.percentile(r, 90), np.mean(h), len(r)))
    if en is None or np.median(r) < en[1]:
        en = (ofs, np.median(r), np.percentile(r, 90))
OFS = a.ofs if a.ofs is not None else en[0]
print("secilen OFS = %d" % OFS)
kap = []
for j in range(J):
    v = kabul[j + OFS]
    m = cv2.imread("%s/%s_maske.png" % (a.girdi, idx[v][0]), 0) > 0
    kap.append(((D[j] > 0) & m).sum() / m.sum())
kap = np.array(kap)
print("(c) kapsam: ortanca %.3f min %.3f maks %.3f" % (np.median(kap), kap.min(), kap.max()))
sey = [kabul[j + OFS] for j in range(J)]
dus = [(idx[sey[j]][3], round(float(kap[j]), 3)) for j in range(J) if kap[j] < 0.05]
print("    kapsami %5 alti cikislar (t sn, kapsam):", dus[:40])
PTS, CL, VID = [], [], []
for j in range(J):
    b = np.fromfile("%s/cikti_%03d_nokta.bin" % (a.cikti, j), dtype=np.uint8)
    n = len(b) // 15
    b = b[: n * 15].reshape(n, 15)
    PTS.append(np.ascontiguousarray(b[:, :12]).view(np.float32).reshape(n, 3))
    CL.append(b[:, 12:15])
    VID.append(np.full(n, j))
P = np.concatenate(PTS).astype(np.float64)
C = np.concatenate(CL)
V = np.concatenate(VID)
print("nokta sayisi %d" % len(P))
KC = np.array([-poz(i)[0].T @ poz(i)[1] for i in range(N)])
cy = float(np.median(KC[:, 1]))
alt = P[P[:, 1] > cy + 0.03, 1]
hst, ed = np.histogram(alt, bins=np.arange(cy, cy + 0.8, 0.01))
mod = ed[np.argmax(hst)] + 0.005
print("(b) kamera y(medyan) %.3f; kamera alti nokta %d; zemin tepe y %.3f -> yukseklik %.3f birim x %.3f = %.3f m (gercek 0.17); tepe +-0.03 komsulugunda nokta: %d"
      % (cy, len(alt), mod, mod - cy, S, (mod - cy) * S, ((alt > mod - .03) & (alt < mod + .03)).sum()))
zy = mod
y_lo, y_hi = zy - 1.50 / S, zy - 0.10 / S
w = (P[:, 1] > y_lo) & (P[:, 1] < y_hi)
h = 0.20 / S
ij = np.floor(P[w][:, [0, 2]] / h).astype(np.int64)
key = ij[:, 0] * 100000 + ij[:, 1]
u, inv = np.unique(key, return_inverse=True)
gor = np.zeros(len(u), int)
pairs = np.unique(np.stack([inv, V[w]], 1), axis=0)
np.add.at(gor, pairs[:, 0], 1)
nokta_h = np.bincount(inv)
print("(d) duvar kusagi (zeminden 0.10-1.50 m): %d nokta, dolu hucre >=1 nokta %d, >=3 nokta %d, >=3 ayri gorunum %d, >=5 ayri gorunum %d"
      % (w.sum(), len(u), (nokta_h >= 3).sum(), (gor >= 3).sum(), (gor >= 5).sum()))
if a.ply:
    with open(a.ply, "wb") as f:
        f.write(("ply\nformat binary_little_endian 1.0\ncomment h1 pozlari + PatchMatch, birim harita birimi (x %.3f = metre)\nelement vertex %d\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nend_header\n" % (S, len(P))).encode())
        rec = np.zeros(len(P), dtype=[("p", "<f4", 3), ("c", "u1", 3)])
        rec["p"] = P
        rec["c"] = C[:, ::-1]
        f.write(rec.tobytes())
    print("PLY:", a.ply, "(renk BGR->RGB)")
if a.gorsel:
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    O = "kayitlar/gosterim/slam/"
    yol = []
    for s in open(a.yol):
        j = json.loads(s)
        m = j.get("poz_wc") or j.get("poz_cw")
        if m:
            yol.append(np.array(m).reshape(4, 4)[:3, 3])
    yol = np.array(yol)
    Cm = np.clip((C[:, ::-1] / 255.0) ** 0.6 * 1.25, 0, 1)
    X, Y, Z = P[:, 0] * S, P[:, 1] * S, P[:, 2] * S
    ZY = zy * S

    def ust(mask, fn, baslik):
        o = np.argsort(-Y[mask])
        fig, ax = plt.subplots(figsize=(13, 13), dpi=110, facecolor="#111")
        ax.set_facecolor("#111")
        ax.scatter(X[mask][o], Z[mask][o], c=Cm[mask][o], s=5, linewidths=0)
        ax.plot(yol[:, 0] * S, yol[:, 2] * S, "-", color="#ff5050", lw=.8, label="h1 yolu")
        ax.plot(0, 0, "*", color="yellow", ms=14)
        ax.set_aspect("equal")
        ax.grid(alpha=.25)
        ax.tick_params(colors="w")
        ax.legend(loc="upper right")
        ax.set_xlabel("x (m)", color="w")
        ax.set_ylabel("z (m)", color="w")
        ax.set_title(baslik, color="w")
        fig.savefig(O + fn)
        plt.close(fig)

    tavan = Y > ZY - 2.0
    ust(tavan, "yogun_ustten.png", "Yogun harita ustten, tavan atildi (zeminden >2.0 m yok), %d nokta" % tavan.sum())
    wm = (Y > ZY - 1.50) & (Y < ZY - 0.10)
    ust(wm, "yogun_duvar_kusagi.png", "Duvar/esya kusagi: zeminden 0.10-1.50 m, %d nokta" % wm.sum())
    fig, ax = plt.subplots(figsize=(14, 6), dpi=110, facecolor="#111")
    ax.set_facecolor("#111")
    o = np.argsort(-Z)
    ax.scatter(X[o], -(Y[o] - ZY), c=Cm[o], s=3, linewidths=0)
    ax.axhline(0, color="cyan", lw=.5)
    ax.set_aspect("equal")
    ax.tick_params(colors="w")
    ax.set_title("Yan gorunus: x - zeminden yukseklik (m)", color="w")
    fig.savefig(O + "yogun_yandan.png")
    plt.close(fig)
    az, el = np.radians(35), np.radians(40)
    mm = tavan
    Q = np.stack([X[mm] - np.median(X[mm]), Y[mm], Z[mm] - np.median(Z[mm])], 1)
    x = Q[:, 0] * np.cos(az) - Q[:, 2] * np.sin(az)
    z = Q[:, 0] * np.sin(az) + Q[:, 2] * np.cos(az)
    y = Q[:, 1]
    u_, v_ = x, -y * np.cos(el) + z * np.sin(el)
    dep = z * np.cos(el) + y * np.sin(el)
    o = np.argsort(-dep)
    fig, ax = plt.subplots(figsize=(14, 9), dpi=110, facecolor="#111")
    ax.set_facecolor("#111")
    ax.scatter(u_[o], v_[o], c=Cm[mm][o], s=5, linewidths=0)
    ax.set_aspect("equal")
    ax.axis("off")
    ax.set_title("Egik gorunus (tavan atildi)", color="w")
    fig.savefig(O + "yogun_perspektif.png")
    plt.close(fig)
