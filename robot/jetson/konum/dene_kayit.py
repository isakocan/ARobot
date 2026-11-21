"""Robotta kaydedilmis konum kaydini (~/konum_kayit/<zaman>/) PC'de tekrar oynatir.

python dene_kayit.py <kayit_klasoru>          # pusula + komut (isaretsiz) yonunu isaret olcumuyle karsilastirir
python dene_kayit.py <kayit_klasoru> tam      # tam birlestirme (isaretlerle), konum izi yazar
"""
import json
import math
import os
import sys

import cv2

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import konum  # noqa: E402


def main():
    K = sys.argv[1]; tam = len(sys.argv) > 2 and sys.argv[2] == "tam"
    R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl"))]
    ev = json.load(open(os.path.join(K, "ev.json")))["isaretler"]
    kb = konum.KonumBulucu()
    if tam:
        kb.ev_ayarla(ev)
    t0 = R[0]["t"]; ofs = None; farklar = []
    for r in R:
        g = cv2.imread(os.path.join(K, r["jpg"]), cv2.IMREAD_GRAYSCALE)
        gor = kb.isle(g, r["t"], (r["sol"], r["sag"]))
        o = kb.ozet()
        if tam:
            if int((r["t"] - t0) * 5) % 10 == 0:
                print("%5.1f  %s x=%5.2f z=%5.2f yon=%7.1f  %s" % (r["t"] - t0, "biliniyor" if o["var"] else "-", o["x"], o["z"], o["yaw"], o["gorulen"]))
            continue
        for gg in gor:
            if gg["id"] != 1 or gg["mesafe"] > 2.5 or gg.get("belirsiz"):
                continue
            ym = math.degrees(-gg["aci"])
            if ofs is None:
                ofs = ym - o["yaw"]
            f = ((o["yaw"] + ofs - ym + 180) % 360) - 180
            farklar.append((r["t"] - t0, f))
    if not tam:
        for t, f in farklar[::4]:
            print("%5.1f sn  yon hatasi %6.1f derece" % (t, f))
        print("donus katsayisi ogrenilen: %.0f derece/s (tam PWM farkinda)" % kb.donus_k)


if __name__ == "__main__":
    main()
