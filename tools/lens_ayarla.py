"""Theta S çift balık gözü: iki lensin örtüştüğü bantta görüntüler en iyi eşleşsin diye
lens merkezleri, ölçek (px/rad) ve 2. lensin küçük dönmesini bulur. Model surus.html ile aynı.

Kullanım (bilgisayarda; numpy, scipy, pillow gerekir):
    ssh jetson "wget -qO /tmp/k1.jpg http://localhost:8080/kare.jpg"; scp jetson:/tmp/k1.jpg .
    python tools/lens_ayarla.py k1.jpg [k2.jpg ...]
Çıkan m1/f1/m2/f2/don2 değerleri surus.html gölgelendiricisindeki M1, F1, M2, F2 ve DON2 olur.
Farklı yerlerde çekilmiş 2-3 kare daha iyi sonuç verir (tavan çizgileri, kapılar)."""
import sys, json
import numpy as np
from PIL import Image, ImageFilter
from scipy.ndimage import map_coordinates
from scipy.optimize import minimize

yollar = sys.argv[1:]
goruntuler = []
for y in yollar:
    g = Image.open(y).convert("L")
    goruntuler.append([np.asarray(g.filter(ImageFilter.GaussianBlur(s)), dtype=np.float32) for s in (4, 1.5)])

# örtüşme bandındaki yönler: ekvatordan (z=0 düzlemi) ±BANT derece
def yonler(bant, n_boylam=720, n_enlem=9):
    lon = np.linspace(-np.pi, np.pi, n_boylam, endpoint=False)
    lat = np.radians(np.linspace(-bant, bant, n_enlem))
    L, B = np.meshgrid(lon, lat)
    # z eksenine dik çember: x = cos(lon), y = sin(lon), z = sin(b)
    d = np.stack([np.cos(L) * np.cos(B), np.sin(L) * np.cos(B), np.sin(B)], -1).reshape(-1, 3)
    return d

def rot(rx, ry, rz):
    cx, sx, cy, sy, cz, sz = np.cos(rx), np.sin(rx), np.cos(ry), np.sin(ry), np.cos(rz), np.sin(rz)
    Rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    Ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    Rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    return Rz @ Ry @ Rx

def piksel(c, cx, cy, f):
    teta = np.arccos(np.clip(c[:, 2], -1, 1))
    n = np.maximum(np.hypot(c[:, 0], c[:, 1]), 1e-9)
    r = teta * f
    return cx + c[:, 0] / n * r, cy - c[:, 1] / n * r, teta

def maliyet(p, img, d, ret=False):
    cx1, cy1, f1, cx2, cy2, f2, rx, ry, rz = p
    u1, v1, t1 = piksel(d, cx1, cy1, f1)
    c2 = np.stack([-d[:, 0], d[:, 1], -d[:, 2]], -1) @ rot(np.radians(rx), np.radians(ry), np.radians(rz)).T
    u2, v2, t2 = piksel(c2, cx2, cy2, f2)
    a = map_coordinates(img, [v1, u1], order=1)
    b = map_coordinates(img, [v2, u2], order=1)
    # parlaklık farkını yok say: her iki örneği bant içinde normalize et
    a = (a - a.mean()) / (a.std() + 1e-6); b = (b - b.mean()) / (b.std() + 1e-6)
    e = np.mean(np.abs(a - b))
    return (e, a, b) if ret else e

d = yonler(6)
p = np.array([320, 320, 320 / np.radians(100), 960, 320, 320 / np.radians(100), 0, 0, 0], dtype=float)
toplam = lambda p, k: np.mean([maliyet(p, g[k], d) for g in goruntuler])
print("başlangıç hata:", round(toplam(p, 1), 4))
for k in (0, 1):
    for _ in range(2):
        s = minimize(toplam, p, args=(k,), method="Powell", options={"maxiter": 6000, "xtol": 1e-3, "ftol": 1e-5})
        p = s.x
    print("aşama", k, "hata:", round(toplam(p, 1), 4))

cx1, cy1, f1, cx2, cy2, f2, rx, ry, rz = p
sonuc = {"m1": [round(cx1, 2), round(cy1, 2)], "f1": round(f1, 2), "m2": [round(cx2, 2), round(cy2, 2)], "f2": round(f2, 2),
         "don2": [round(rx, 3), round(ry, 3), round(rz, 3)],
         "lens_derece_320px": [round(np.degrees(320 / f1) * 2, 1), round(np.degrees(320 / f2) * 2, 1)]}
print(json.dumps(sonuc, ensure_ascii=False))

# görüntü dairesinin kenarı (karanlık halka): merkezden dışa ortalama parlaklık
g = np.asarray(Image.open(yollar[0]).convert("L"), dtype=np.float32)
for ad, (cx, cy) in (("lens1", (cx1, cy1)), ("lens2", (cx2, cy2))):
    acilar = np.linspace(0, 2 * np.pi, 360, endpoint=False)
    profil = []
    for r in range(240, 330, 5):
        u = cx + r * np.cos(acilar); v = cy + r * np.sin(acilar)
        ok = (u >= 0) & (u < 1280) & (v >= 0) & (v < 720)
        profil.append((r, round(float(map_coordinates(g, [v[ok], u[ok]], order=1).mean()), 1)))
    print(ad, profil)
