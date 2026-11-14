"""olcum_ham: belirli anlardaki kareleri DOGRUDAN M2D ile eslestirir (ilk kareye, ve birbirine) ve birikimli M2D yaw'i ile karsilastirir (kapanis tutarliligi).
Gorunumden ayni yer/ayni yon oldugu gozle dogrulananlar: t=0,5 ile 82,5 / 100 / 133 (raf ayni yerde, ayni boyutta). Gercek maske."""
import json, os, sys
import numpy as np
if hasattr(sys.stdout, "reconfigure"): sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_ortak as go
go.kur("gercek")
import yontem_pusula_nokta as ypn
from kayit import Kayit
from kamera import Kamera
from olc import sar
kay = Kayit(os.path.join(go.GERCEK, "olcum_ham")); kam = Kamera.bilgiden(kay.bilgi)
y = ypn.Yontem(dict(kay.bilgi), kam, kip="duzlem", komut_oncel=False)
t = kay.t_ms / 1000.0
z = np.load(os.path.join(go.onbellek("gercek"), "yon_komutsuz_gercek_olcum_ham.npz"))
acc = z["yon"]
def oz(ts):
    i = int(np.argmin(abs(t - ts))); gri, gec = y.don(kay.resim(i)); return i, y._cikar(gri, gec)
base_i, base = oz(0.5)
out = []
for ts in (14.5, 22.7, 82.5, 100.0, 133.0, 150.0, 165.5, 173.0, 182.5, 190.0, 299.0):
    i, o = oz(ts)
    dy, kal, tamam, b = y._esle(o, base, 0.0, 180.0)
    fark = float(sar(dy - (acc[i] - acc[base_i])))
    out.append(dict(t=ts, dogrudan=float(dy), ic=int(b.get("icerik", 0)), kalite=float(kal), birikimli=float(acc[i] - acc[base_i]), birikimli_mod360=float(sar(acc[i] - acc[base_i])), fark=fark))
    print("t=%6.1f  doğrudan(t=0,5'e göre) %7.1f° (iç %3d, kalite %.2f) | birikimli %8.1f° (mod360 %7.1f) | fark %6.1f°" % (ts, dy, out[-1]["ic"], kal, acc[i] - acc[base_i], sar(acc[i] - acc[base_i]), fark))
json.dump(out, open(os.path.join(go.SONUC, "olcum", "kapanis.json"), "w", encoding="utf-8"), indent=1)
