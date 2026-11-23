"""Robotun konum kodu kaç kare/sn'de çalışırsa hızlı dönüşte yönü kaybetmez? Telefonun Dev kaydındaki kareler (robotun
kendi kamerası, ~7 kare/sn) robot konum koduna her karede / 2 karede bir / 3 karede bir verilir; işarete göre hata ölçülür.

python tools/konum/robot_hiz_dene.py <dev_kaydi> <ev.json>
"""
import csv
import json
import os
import sys

import cv2
import numpy as np

KOK = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
import konum  # noqa: E402


def main():
    K, evj = sys.argv[1], sys.argv[2]
    iz = list(csv.DictReader(open(os.path.join(K, "iz.csv"))))
    ev = json.load(open(evj))["isaretler"]
    for adim in (1, 2, 3):
        kb = konum.KonumBulucu(); kb.ev_ayarla(ev)
        hata = []
        for r in iz[::adim]:
            g = cv2.imread(os.path.join(K, r["jpg"]), cv2.IMREAD_GRAYSCALE)
            if g is None:
                continue
            ms = int(r["kare_motor_sol"]) if r.get("kare_motor_sol", "-999") not in ("", "-999") else int(r["motor_sol"])
            mg = int(r["kare_motor_sag"]) if r.get("kare_motor_sag", "-999") not in ("", "-999") else int(r["motor_sag"])
            t = float(r["robot_zaman_ms"]) / 1000.0 if float(r.get("robot_zaman_ms") or 0) > 0 else float(r["t"])
            kb.isle(g, t, (ms, mg))
            if kb.hata:
                hata.append(abs(kb.hata[0]))
        h = np.array(hata)
        print("her %d karede bir (~%.1f kare/sn): işaret gözleminde düzeltme öncesi yön hatası ortanca %.1f, %%90 %.1f, en çok %.1f derece; yeniden kilit %d" % (
            adim, 7.0 / adim, np.median(h), np.percentile(h, 90), h.max(), kb.kilit_sayisi))


if __name__ == "__main__":
    main()
