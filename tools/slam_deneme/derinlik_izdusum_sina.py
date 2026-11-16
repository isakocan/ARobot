"""Harita noktasını gözlendiği anahtar kareye geri izdüşürüp stella'nın kayıtlı 2B gözlem pikseline göre piksel hatasını ölçer.
Kullanım: python tools/slam_deneme/derinlik_izdusum_sina.py kayitlar/slam/sonuc/h1/harita.json kayitlar/slam/sonuc/h1/harita.msg
Not: harita.json gözlem listesi yalnız nokta kimliği içerir (kopru.cc:115-121); piksel gözlemi harita.msg içindeki undist_keypts + lm_ids'tedir."""
import sys, json
import numpy as np, msgpack
sys.path.insert(0, __import__("os").path.dirname(__file__))
from derinlik_ortak import *

P, KF = harita_yukle(sys.argv[1])
msg = msgpack.unpack(open(sys.argv[2], "rb"), raw=False, strict_map_key=False)
pid = {int(p[4]): p[:3] for p in P}
rng = np.random.default_rng(0)
tum, ornek = [], []
for k in KF:
    kf = msg["keyframes"][str(k["id"])]
    ts = kf["ts"]
    assert abs(ts - k["t"]) < 1e-6, (ts, k["t"])
    R_cw, t_cw = np.array(kf["rot_cw"]), np.array(kf["trans_cw"])   # quaternion xyzw, çeviri
    # poz_wc ile msg poz tutarlılığı
    from scipy.spatial.transform import Rotation as Rot
    Rm = Rot.from_quat(R_cw).as_matrix()
    Tm = np.linalg.inv(np.vstack([np.hstack([Rm, t_cw[:, None]]), [0, 0, 0, 1]]))
    poz_fark = np.abs(Tm - k["T_wc"]).max()
    kp = np.array([q["pt"] for q in kf["undist_keypts"]], dtype=np.float64)
    lm = np.array(kf["lm_ids"], dtype=np.int64)
    hata = []
    for i, l in enumerate(lm):
        if l in pid:
            pc = kameraya(k["T_wc"], pid[l][None])[0]
            b = pc / np.linalg.norm(pc)
            u, v = yon_to_piksel(b)
            du = (u - kp[i][0] + KOLON / 2) % KOLON - KOLON / 2      # kolon sarma
            hata.append((np.hypot(du, v - kp[i][1]), l, i))
    k["hata"] = hata; k["poz_fark"] = poz_fark
    tum += [h[0] for h in hata]
tum = np.array(tum)
print("kullanilan gozlem sayisi", len(tum))
print("piksel hatasi: medyan %.3f  p90 %.3f  p99 %.3f  maks %.3f" % (np.median(tum), np.percentile(tum, 90), np.percentile(tum, 99), tum.max()))
print("poz_wc (json) ile msg poz tutarsizligi (maks mutlak fark): %.2e" % max(k["poz_fark"] for k in KF))
print("--- 20 rastgele nokta (anahtar kare id, nokta id, hata px) ---")
for _ in range(20):
    k = KF[rng.integers(len(KF))]
    h = k["hata"][rng.integers(len(k["hata"]))]
    print(k["id"], h[1], "%.3f" % h[0])
