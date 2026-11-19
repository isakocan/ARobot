"""stella_vslam_dense çıktısını resme döker: kuşbakışı harita (yörünge ile) ve yandan görünüş.
Kullanım: python tools/haritalama/bulut_goster.py <kayit_klasoru>
Okur: <kayit>/harita/yogun.ply, <kayit>/harita/izler/frame_trajectory.txt (TUM: t x y z qx qy qz qw)
Yazar: <kayit>/harita/kusbakisi.png, <kayit>/harita/yandan.png
stella koordinatları: x sağ, y aşağı, z ileri (ilk kareye göre). Yukarı = -y kabul edilir
(kamera düz takılı, ilk kare yerçekimine hizalı varsayımı). Ölçek birimsizdir (tek kamera)."""
import sys
from pathlib import Path

import cv2
import numpy as np


def ply_oku(yol):
    with open(yol, "rb") as f:
        n = 0
        while True:
            s = f.readline().decode().strip()
            if s.startswith("element vertex"):
                n = int(s.split()[-1])
            if s == "end_header":
                break
        veri = np.loadtxt(f, max_rows=n) if n else np.zeros((0, 6))
    return veri[:, :3].astype(np.float32), veri[:, 3:6].astype(np.uint8)


def ciz(p, renk, yol_p, eksen, boyut=1400, kenar=40):
    """p: Nx3 noktalar, eksen: (yatay, dikey, derinlik) sütun indeksleri ve işaretleri"""
    (a, sa), (b, sb) = eksen
    u, v = p[:, a] * sa, p[:, b] * sb
    tum_u = np.concatenate([u, yol_p[:, a] * sa]); tum_v = np.concatenate([v, yol_p[:, b] * sb])
    lo_u, hi_u = np.percentile(tum_u, [1, 99]); lo_v, hi_v = np.percentile(tum_v, [1, 99])
    pay = 0.1 * max(hi_u - lo_u, hi_v - lo_v)
    lo_u -= pay; hi_u += pay; lo_v -= pay; hi_v += pay
    olcek = (boyut - 2 * kenar) / max(hi_u - lo_u, hi_v - lo_v)
    G = int((hi_u - lo_u) * olcek) + 2 * kenar; Y = int((hi_v - lo_v) * olcek) + 2 * kenar
    img = np.full((Y, G, 3), 24, np.uint8)
    x = ((u - lo_u) * olcek + kenar).astype(int); y = ((hi_v - v) * olcek + kenar).astype(int)
    ok = (x >= 0) & (x < G) & (y >= 0) & (y < Y)
    img[y[ok], x[ok]] = renk[ok][:, ::-1]           # RGB -> BGR
    yu = ((yol_p[:, a] * sa - lo_u) * olcek + kenar).astype(int)
    yv = ((hi_v - yol_p[:, b] * sb) * olcek + kenar).astype(int)
    cv2.polylines(img, [np.stack([yu, yv], 1).reshape(-1, 1, 2)], False, (0, 200, 255), 2, cv2.LINE_AA)
    cv2.circle(img, (int(yu[0]), int(yv[0])), 7, (0, 255, 0), -1)
    cv2.circle(img, (int(yu[-1]), int(yv[-1])), 7, (0, 0, 255), -1)
    return img


def main():
    h = Path(sys.argv[1]) / "harita"
    p, renk = ply_oku(h / "yogun.ply")
    yol = np.loadtxt(h / "izler" / "frame_trajectory.txt")[:, 1:4].astype(np.float32)
    print(f"{len(p)} yoğun nokta, {len(yol)} kare konumu")
    # kuşbakışı: x sağa, z yukarı (ileri); tavanı gizle: kamera yüksekliğinin üstündeki noktalar çizilmez
    yuk = -p[:, 1]
    alt = yuk < np.median(-yol[:, 1]) + 0.5 * np.ptp(yol[:, [0, 2]])   # tavana yakın olanları at
    cv2.imwrite(str(h / "kusbakisi.png"), ciz(p[alt], renk[alt], yol, ((0, 1), (2, 1))))
    cv2.imwrite(str(h / "yandan.png"), ciz(p, renk, yol, ((0, 1), (1, -1))))
    print("yazıldı:", h / "kusbakisi.png", h / "yandan.png", "(yeşil = başlangıç, kırmızı = bitiş)")


if __name__ == "__main__":
    main()
