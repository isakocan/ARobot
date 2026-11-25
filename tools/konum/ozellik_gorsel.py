"""ozellik_dene.py'deki bir yöntemin farklı gün eşleşmesini çizer (yeşil = aynı kaymaya uyan, kırmızı = reddedilen).
Kullanım: python ozellik_gorsel.py <kök> <yöntem> <soru_kaydı> <hafıza_kaydı> <çıktı.jpg>"""
import math, os, sys
import cv2
import numpy as np

KOK, AD, SORU, HAFK, CIKTI = sys.argv[1:6]
kod = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "ozellik_dene.py"), encoding="utf-8").read()
sys.argv = [sys.argv[0], KOK, AD, SORU, HAFK]
exec(kod.split("# ------------------------------------------------------------------ deney")[0])

SB, SG = bilinen(SORU); (HB, HG) = bilinen(HAFK)
Y = Yontem(AD)
en = None
for q in SB[::6]:
    for k in HB[::4]:
        dy = abs(da(math.degrees(q["yaw"] - k["yaw"])))
        if math.hypot(q["x"] - k["x"], q["z"] - k["z"]) < 0.2 and 30 < dy < 150:
            en = (q, k); break
    if en: break
q, k = en
q["oz"] = Y.cikar(q["pan"], q["gor"], SG); k["oz"] = Y.cikar(k["pan"], k["gor"], HG)
ia, ib = Y.esle(q["oz"], k["oz"]); n, kay, um = kayma(q["oz"], k["oz"], ia, ib)


def xy(lon, lat): return int((lon / 360.0 + 0.5) * GEN), int((UST - lat) * PX)


ust = cv2.cvtColor(k["pan"], cv2.COLOR_GRAY2BGR); alt = cv2.cvtColor(q["pan"], cv2.COLOR_GRAY2BGR)
im = np.vstack([ust, np.zeros((40, GEN, 3), np.uint8), alt])
for j, (a, b) in enumerate(zip(ia, ib)):
    p1 = xy(k["oz"]["lon"][b], k["oz"]["lat"][b]); p2 = xy(q["oz"]["lon"][a], q["oz"]["lat"][a]); p2 = (p2[0], p2[1] + YUK + 40)
    if abs(p1[0] - p2[0]) > GEN / 2: continue
    cv2.line(im, p1, p2, (60, 220, 60) if um is not None and um[j] else (60, 60, 230), 1, cv2.LINE_AA)
tah = math.degrees(k["yaw"]) + kay
cv2.putText(im, "Anahtar kare (%s), yon %.0f" % (os.path.basename(HAFK)[:8], da(math.degrees(k["yaw"]))), (10, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2, cv2.LINE_AA)
cv2.putText(im, "Simdiki kare (%s, baska gun), gercek yon %.0f" % (os.path.basename(SORU)[:8], da(math.degrees(q["yaw"]))), (10, YUK + 68), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2, cv2.LINE_AA)
cv2.putText(im, "%s: %d eslesme, %d uyan (yesil) -> tahmini yon %.0f, hata %.1f der" % (AD, len(ia), n, da(tah), da(tah - math.degrees(q["yaw"]))),
            (10, YUK + 28), cv2.FONT_HERSHEY_SIMPLEX, 0.75, (0, 215, 255), 2, cv2.LINE_AA)
cv2.imwrite(CIKTI, im)
print("%s: eslesme %d, uyan %d, hata %.1f" % (AD, len(ia), n, da(tah - math.degrees(q["yaw"]))))
