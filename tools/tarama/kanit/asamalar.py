import json, os, sys, numpy as np
import matplotlib; matplotlib.use("Agg"); import matplotlib.pyplot as plt
from matplotlib.colors import ListedColormap
K, OUT = sys.argv[1], sys.argv[2]
n = "kayit_20261124_194526"
h = np.load(os.path.join(K, "ref_%s.npz" % n))["h"]
x0, z0 = int(h[:,0].min())-3, int(h[:,1].min())-3; W, H = int(h[:,0].max())-x0+4, int(h[:,1].max())-z0+4
F = np.zeros((H,W)); O = np.zeros((H,W)); N = np.zeros((H,W))
for r in h: F[int(r[1])-z0, int(r[0])-x0] = r[2]; O[int(r[1])-z0, int(r[0])-x0] = r[3]; N[int(r[1])-z0, int(r[0])-x0] = r[5]
engel = (O >= 3) & (O >= 0.3*N); acik0 = (F >= 2) & ~engel
def bilesen(m, mn, k8=True):
    from scipy import ndimage
    lab, k = ndimage.label(m, structure=np.ones((3,3)) if k8 else None)
    sz = ndimage.sum(m, lab, range(1, k+1)); keep = np.zeros(k+1, bool); keep[1:] = sz >= mn
    return keep[lab]
acik = bilesen(acik0, 100); atilan = acik0 & ~acik
from scipy import ndimage
disk = lambda r: np.array([[dx*dx+dz*dz <= r*r for dx in range(-r,r+1)] for dz in range(-r,r+1)])
yasak = ndimage.binary_dilation(engel, structure=disk(3))   # (uygulamada engel kumesi + 3 hucre)
o1 = acik & ~yasak & (F >= 4)
o2 = ndimage.binary_opening(o1, structure=disk(2)); o3 = bilesen(o2, 100)
ext = [x0*0.05, (x0+W)*0.05, z0*0.05, (z0+H)*0.05]
fig, ax = plt.subplots(2, 3, figsize=(21, 14))
def goster(a, img, baslik, **kw):
    a.imshow(img, origin="lower", extent=ext, interpolation="nearest", **kw); a.set_title(baslik, fontsize=13); a.set_aspect("equal"); a.tick_params(labelsize=8)
gor = N > 0
im = np.ma.masked_where(~gor, np.minimum(F, 30))
goster(ax[0,0], im, "1) Her 5 cm hucrede: zemin kac ayri karede goruldu (F)\nkoyu = cok kez, beyaz = hic nokta dusmedi", cmap="Blues", vmin=0, vmax=30)
oran = np.ma.masked_where(~gor, np.where(N > 0, O/np.maximum(N,1), 0))
goster(ax[0,1], oran, "2) Bakilan karelerin yuzde kacinda zeminden 15 cm-2 m\nyukarida bir sey goruldu (O/N); %30 ustu = engel", cmap="Oranges", vmin=0, vmax=1)
k = np.zeros((H,W)); k[gor] = 1; k[acik0] = 2; k[engel] = 3
goster(ax[0,2], np.ma.masked_where(k == 0, k), "3) Karar: yesil acik (F>=2, engel degil), turuncu engel,\ngri goruldu ama karar yok (zemin tek karede)", cmap=ListedColormap(["#cfcfcf","#2bd19a","#ff8a1f"]), vmin=1, vmax=3)
k2 = np.zeros((H,W)); k2[acik] = 1; k2[atilan] = 2; k2[engel] = 3
goster(ax[1,0], np.ma.masked_where(k2 == 0, k2), "4) Adacik silme: 0,25 m2'den kucuk, ana zemine\ndegmeyen acik parcalar (kirmizi) atilir", cmap=ListedColormap(["#2bd19a","#e8202a","#ffd2a8"]), vmin=1, vmax=3)
k3 = np.zeros((H,W)); k3[acik] = 1; k3[acik & yasak] = 2; k3[o1 & ~o3] = 3; k3[o3] = 4; k3[engel] = 5
goster(ax[1,1], np.ma.masked_where(k3 == 0, k3), "5) Oyun alani: acik zeminden engele 15 cm pay (sari),\nince serit/kucuk parca atilir (mor), kalan koyu mavi", cmap=ListedColormap(["#2bd19a","#ffd84a","#b06ad8","#1c3cea","#ffd2a8"]), vmin=1, vmax=5)
# 6) uygulamanin son hali
a = ax[1,2]; D = os.path.join(K, n + "-dog")
z = json.load(open(os.path.join(D, "zemin.json"))); u = json.load(open(os.path.join(D, "ucgen.json")))
from matplotlib.collections import PolyCollection
def ucg(L, renk, al):
    t = np.array(L).reshape(-1,3,2); a.add_collection(PolyCollection(t, facecolors=renk, edgecolors=renk, linewidths=0.2, alpha=al))
E = np.array(z["engel"]).reshape(-1,2)*0.05
a.scatter(E[:,0]+0.025, E[:,1]+0.025, s=3, c="#9aa0a6", marker="s", linewidths=0)
ucg(u["ucgen"], "#2bd19a", 0.85); ucg(u["oyun"], "#1c3cea", 0.9)
iz = np.array(z["iz"]).reshape(-1,2); a.plot(iz[:,0], iz[:,1], "-", color="#d11", lw=1.2)
a.set_xlim(ext[0], ext[1]); a.set_ylim(ext[2], ext[3]); a.set_aspect("equal"); a.tick_params(labelsize=8)
a.set_title("6) Uygulamanin son 2B haritasi: yesil acik zemin %.1f m2, koyu mavi\noyun alani, gri engel, kirmizi senin yurudugun yol" % z["alan_m2"], fontsize=13)
fig.suptitle("24 Kasim 19:45 taramasi, adim adim (eksenler metre)", fontsize=16)
fig.tight_layout(rect=(0,0,1,0.96)); fig.savefig(OUT, dpi=60)
print("acik0", acik0.sum()*0.0025, "acik", acik.sum()*0.0025, "atilan", atilan.sum()*0.0025, "engel", engel.sum()*0.0025, "oyun", o3.sum()*0.0025, "yasak bandi", (acik&yasak).sum()*0.0025)
# ornek hucreler
for ad, sec in (("acik zemin", acik & (F > 30)), ("esya kenari ama acik", acik & (O > 0) & (O < 0.3*N) & (O >= 3)), ("engel (masa/sandalye)", engel & (F >= 2)), ("adacik", atilan)):
    ys, xs = np.nonzero(sec)
    if len(xs): i = len(xs)//2; yy, xx = ys[i], xs[i]; print(ad, "x=%.2f z=%.2f F=%d O=%d N=%d oran=%.2f" % ((xx+x0)*0.05, (yy+z0)*0.05, F[yy,xx], O[yy,xx], N[yy,xx], O[yy,xx]/max(N[yy,xx],1)))
