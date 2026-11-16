"""PatchMatch sürücüsü için girdi klasörü hazırlar (h1 haritasının anahtar kareleri, zaman sırasıyla).
  python tools/slam_deneme/yogun_girdi_hazirla.py [--harita kayitlar/slam/sonuc/h1] [--cikti kayitlar/slam/yogun/girdi_h1]
Her görünüm NNN için: NNN.png (640x320 BGR), NNN_maske.png (0 = kullanma), NNN_poz.txt (R_cw satır sırası 9 sayı + t_cw 3 sayı, dünya->kamera),
NNN_seyrek.txt (karenin geçerli landmark'ları, dünya xyz), index.txt (NNN kf_id kare_no t_sn).
rot_cw dördeyi (x,y,z,w) sırasındadır (Eigen coeffs; kimlik = [0,0,0,1]); harita.json'daki poz_wc ile çapraz doğrulanır."""
import argparse
import os
import sys

import cv2
import msgpack
import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
from derinlik_ortak import harita_yukle, kare_numarasi  # noqa: E402


def dordey_matris(q):
    x, y, z, w = q
    n = np.sqrt(x * x + y * y + z * z + w * w)
    x, y, z, w = x / n, y / n, z / n, w / n
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def ana():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--harita", default="kayitlar/slam/sonuc/h1")
    ap.add_argument("--kareler", default="kayitlar/slam/ekdortgen_olcum_ham")
    ap.add_argument("--maske", default=None, help="1280x640 maske (255 = kullan); öntanım <kareler>/maske.png")
    ap.add_argument("--cikti", default="kayitlar/slam/yogun/girdi_h1")
    ap.add_argument("--en", type=int, default=640)
    ap.add_argument("--boy", type=int, default=320)
    a = ap.parse_args()
    os.makedirs(a.cikti, exist_ok=True)
    m = msgpack.unpackb(open(os.path.join(a.harita, "harita.msg"), "rb").read(), raw=False, strict_map_key=False)
    _, KF = harita_yukle(os.path.join(a.harita, "harita.json"))
    kare = kare_numarasi(KF, os.path.join(a.kareler, "zamanlar.csv"))
    maske = cv2.imread(a.maske or os.path.join(a.kareler, "maske.png"), cv2.IMREAD_GRAYSCALE)
    maske = np.where(maske > 127, 255, 0).astype(np.uint8)
    h = maske.shape[0]
    maske[: int(0.06 * h)] = 0          # kutup bantları (ayar_yogun.yaml mask_rectangles ile aynı)
    maske[int(0.94 * h):] = 0
    maske_k = cv2.resize(maske, (a.en, a.boy), interpolation=cv2.INTER_NEAREST)
    sira = sorted(range(len(KF)), key=lambda i: KF[i]["t"])
    L = m["landmarks"]
    hata_max, dt_max = 0.0, 0.0
    idx = []
    for n, i in enumerate(sira):
        k = KF[i]
        e = m["keyframes"][str(k["id"])]
        R = dordey_matris(e["rot_cw"]); t = np.array(e["trans_cw"], dtype=float)
        # çapraz doğrulama: poz_wc = T_wc ; R_wc = R_cw^T, c = -R_cw^T t
        Rwc, c = k["T_wc"][:3, :3], k["T_wc"][:3, 3]
        hata_max = max(hata_max, np.abs(Rwc - R.T).max(), np.abs(c + R.T @ t).max())
        dt_max = max(dt_max, abs(float(e["ts"]) - k["t"]))
        kn, fark = kare[i]
        img = cv2.imread(os.path.join(a.kareler, "%06d.jpg" % kn))
        img = cv2.resize(img, (a.en, a.boy), interpolation=cv2.INTER_AREA)
        ad = "%03d" % n
        cv2.imwrite(os.path.join(a.cikti, ad + ".png"), img)
        cv2.imwrite(os.path.join(a.cikti, ad + "_maske.png"), maske_k)
        np.savetxt(os.path.join(a.cikti, ad + "_poz.txt"), np.concatenate([R.ravel(), t])[None], fmt="%.17g")
        ids = [j for j in e["lm_ids"] if j >= 0]
        X = np.array([L[str(j)]["pos_w"] if str(j) in L else L[j]["pos_w"] for j in ids], dtype=float)
        np.savetxt(os.path.join(a.cikti, ad + "_seyrek.txt"), X, fmt="%.17g")
        idx.append("%s %d %d %.4f" % (ad, k["id"], kn, k["t"]))
    open(os.path.join(a.cikti, "index.txt"), "w").write("\n".join(idx) + "\n")
    print("görünüm: %d; poz çapraz-doğrulama en büyük sapma (poz_wc vs dördey): %.2e; ts-t farkı en çok %.4f sn" % (len(sira), hata_max, dt_max))


if __name__ == "__main__":
    ana()
