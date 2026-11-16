"""Yoğun (stella_vslam_dense) koşusunun yolunu, iyi olduğu bilinen seyrek koşuyla karşılaştırır: yol sağlam mı, bozulmuş mu?

  python tools/slam_deneme/yogun_yol_kontrol.py kayitlar/slam/yogun/frame_trajectory.txt
  python tools/slam_deneme/yogun_yol_kontrol.py <frame_trajectory.txt> --bulut <yogun.ply> --referans kayitlar/slam/sonuc/h1/poz.jsonl

Ölçüt: 10 sn'lik dilimlerde kat edilen yolun referansa oranı. Sağlam koşuda bu oran sabittir (iki haritanın birim farkı).
Oran dilimden dilime büyüyorsa birim kayıyor demektir. Kamera yüksekliği (y) de sabit kalmalıdır.
Girdi: stella'nın TUM biçimli kare yolu (t x y z qx qy qz qw; konum = kamera merkezi). Referans: köprü poz.jsonl (poz_cw alanı T_wc taşır)."""
import argparse
import json
import sys

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def yol_uzunlugu(t, p, a, b):
    k = (t >= a) & (t < b)
    if k.sum() < 7:
        return float("nan")
    q = p[k][::5]
    return float(np.sum(np.linalg.norm(np.diff(q, axis=0), axis=1)))


def referans_oku(yol, fps=15.0):
    t, p = [], []
    for s in open(yol, encoding="utf-8"):
        j = json.loads(s)
        m = j.get("poz_wc") or j.get("poz_cw")
        if m:
            t.append(j["i"] / fps)
            p.append(np.array(m, dtype=float).reshape(4, 4)[:3, 3])
    return np.array(t), np.array(p)


def ana():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("yol", help="frame_trajectory.txt (TUM)")
    ap.add_argument("--referans", default="kayitlar/slam/sonuc/h1/poz.jsonl")
    ap.add_argument("--bulut", default=None, help="yoğun nokta bulutu (ASCII PLY, x y z r g b)")
    ap.add_argument("--dilim", type=float, default=10.0)
    ap.add_argument("--duragan", type=float, default=0.25, help="referansta bundan kısa yol (birim) olan dilim oran hesabına girmez")
    a = ap.parse_args()

    d = np.loadtxt(a.yol)
    td, pd_ = d[:, 0], d[:, 1:4]
    th, ph = referans_oku(a.referans)
    son = min(td[-1], th[-1])
    print("yoğun: %d kare, %.1f-%.1f sn | referans: %d kare, %.1f-%.1f sn" % (len(td), td[0], td[-1], len(th), th[0], th[-1]))
    print("dilim (sn)  yoğun yol  referans yol   oran   yoğun y ort")
    oranlar = []
    for bas in np.arange(0.0, son, a.dilim):
        yd, yh = yol_uzunlugu(td, pd_, bas, bas + a.dilim), yol_uzunlugu(th, ph, bas, bas + a.dilim)
        k = (td >= bas) & (td < bas + a.dilim)
        oran = yd / yh if yh == yh and yh > a.duragan else float("nan")
        if oran == oran:
            oranlar.append(oran)
        print("%4.0f-%-4.0f   %8.2f   %9.2f   %6s   %8.3f" % (bas, bas + a.dilim, yd, yh, ("%.2f" % oran) if oran == oran else "  -", pd_[k, 1].mean() if k.any() else float("nan")))
    oranlar = np.array(oranlar)
    y_yayilim = float(pd_[:, 1].max() - pd_[:, 1].min())
    en_buyuk = float(oranlar.max() / oranlar.min()) if len(oranlar) else float("nan")
    print("")
    print("oranın en büyük/en küçük değeri: %.2f (sağlam koşuda 1'e yakın)" % en_buyuk)
    print("kamera yüksekliği oynaması: %.3f birim (sağlam koşuda 0'a yakın)" % y_yayilim)
    print("yoğun yol kapsamı: x %.1f..%.1f, z %.1f..%.1f" % (pd_[:, 0].min(), pd_[:, 0].max(), pd_[:, 2].min(), pd_[:, 2].max()))
    print("SONUÇ: %s" % ("yol SAĞLAM" if en_buyuk < 2.0 else "yol BOZUK (birim kayıyor)"))

    if a.bulut:
        satir = 0
        with open(a.bulut, "r", encoding="ascii", errors="replace") as f:
            for satir, s in enumerate(f, 1):
                if s.strip() == "end_header":
                    break
        P = np.loadtxt(a.bulut, skiprows=satir)
        X = P[:, :3]
        cy = float(np.median(pd_[td < td[0] + 30.0, 1]))   # ilk 30 sn: yol bozulsa bile başlangıç genelde sağlam
        alt = X[X[:, 1] > cy + 0.03, 1]           # y aşağı doğru artar: kameranın altındakiler
        print("")
        print("bulut: %d nokta; x %.1f..%.1f, y %.1f..%.1f, z %.1f..%.1f (yüzde 1-99)" % ((len(X),) + tuple(np.percentile(X, [1, 99], axis=0).T.ravel())))
        if len(alt):
            h = float(np.median(alt) - cy)
            print("kamera altındaki nokta: %d; kamera yüksekliği %.3f birim → 17 cm kabulüyle %.2f m/birim" % (len(alt), h, 0.17 / h))


if __name__ == "__main__":
    ana()
