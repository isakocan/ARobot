"""Telefondaki görüntü pusulasının (ar_tarama RobotGorselIz.Pusula) Python kopyası: Dev kaydındaki (files/dev/<zaman>/)
kareleri yeniden oynatır, eski ve yeni yöntemin saydığı dönüşü robotun konumuyla karşılaştırır.

python tools/konum/pusula_dene.py <dev_kaydi> [--bas N --son M]

Robotun yönü (iz.csv k_yaw) gecikmeli gelir ama robot durduktan sonra doğrudur: karşılaştırma duruşlar arasında yapılır
(her duruşta: telefon ne kadar döndü dedi, robot ne kadar döndü dedi).
"""
import csv
import math
import os
import sys

import cv2
import numpy as np

KOK = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
import teta  # noqa: E402

BW, BH = 720, 24
PX = BW / 360.0


def bant_haritasi():
    y, x = np.mgrid[0:BH, 0:BW]
    yaw = ((x + 0.5) / BW - 0.5) * 2 * math.pi
    lat = np.radians(-5.0 + (y + 0.5) / BH * 36.0)
    d = np.stack([np.sin(yaw) * np.cos(lat), np.sin(lat), np.cos(yaw) * np.cos(lat)], -1).reshape(-1, 3)
    p = teta.yon_pikseller(d)
    return p[:, 0].reshape(BH, BW).copy(), p[:, 1].reshape(BH, BW).copy()


def maske():
    b = np.frombuffer(open(os.path.join(KOK, "ar_tarama", "Assets", "Resources", "PusulaMaske.bytes"), "rb").read(), np.uint8)
    return (b.reshape(BH, BW) > 127).astype(np.float32) if b.size == BW * BH else np.ones((BH, BW), np.float32)


PENCERE = (0.5 - 0.5 * np.cos(2 * np.pi * (np.arange(BH) + 0.5) / BH)).astype(np.float32)[:, None]


def eslestir(a, b, merkez, aralik):
    c0 = int(round(merkez)); n = 2 * aralik + 1
    ea = float((a * a).sum()); eb = float((b * b).sum())
    k = np.zeros(n + 2)
    for q in range(-1, n + 1):
        m = (c0 - aralik + q) % BW
        k[q + 1] = float((np.roll(a, -m, axis=1) * b).sum())
    en = int(np.argmax(k[1:n + 1]))
    y0, y1, y2 = k[en], k[en + 1], k[en + 2]; p = y0 - 2 * y1 + y2
    s = c0 - aralik + en + (max(-0.5, min(0.5, 0.5 * (y0 - y2) / p)) if abs(p) > 1e-6 else 0.0)
    return s, k[en + 1] / (math.sqrt(ea * eb) + 1e-6)


def _da(a):
    return (a + 180.0) % 360.0 - 180.0


class Pusula:
    def __init__(self, yeni):
        self.yeni = yeni; self.var = False; self.yaw = 0.0; self.ref_yaw = 0.0; self.rel = 0.0; self.son = 0.0
        self.red = 0; self.tahmin_say = 0

    def kare(self, cur, tahmin):
        if (cur * cur).sum() < 1:
            return 0.0
        once = self.yaw
        if not self.var:
            self.ref = cur.copy(); self.prev = cur.copy(); self.var = True; return 0.0
        if not self.yeni:
            s, c = eslestir(self.ref, cur, self.rel * PX, 24)
            if c >= 0.3:
                self.rel = s / PX; self.yaw = self.ref_yaw + self.rel
                if abs(self.rel) > 25 or c < 0.45:
                    self.ref = cur.copy(); self.ref_yaw = self.yaw; self.rel = 0
            else:
                s, c = eslestir(self.prev, cur, self.son * PX, 70)
                if c >= 0.24:
                    self.yaw += s / PX
                self.ref = cur.copy(); self.ref_yaw = self.yaw; self.rel = 0
        else:
            self.son_tahmin = False
            merkez = tahmin if abs(tahmin) > 0.5 else self.son
            s, c = eslestir(self.ref, cur, (self.rel + merkez) * PX, 24)
            refle = c >= 0.3
            if not refle:
                aralik = int(min(200, max(70, 70 + round(abs(merkez) * PX * 0.5))))
                s, c = eslestir(self.prev, cur, merkez * PX, aralik)
            adim = _da(self.ref_yaw + s / PX - once) if refle else s / PX
            tuttu = refle or c >= 0.24
            MOD = os.environ.get("MOD", "tam")
            if MOD == "merkez":
                red = False
            elif MOD == "ters2":
                red = abs(tahmin) > 4 and ((not tuttu and abs(tahmin) > 8) or (np.sign(adim) != np.sign(tahmin) and abs(adim) > 2) or
                                           (abs(tahmin) > 12 and abs(adim - tahmin) > max(10.0, 0.6 * abs(tahmin))))
            elif MOD == "ters":
                red = abs(tahmin) > 4 and ((not tuttu and abs(tahmin) > 8) or (np.sign(adim) != np.sign(tahmin) and abs(adim) > 2))
            else:
                red = abs(tahmin) > 3 and (not tuttu or (np.sign(adim) != np.sign(tahmin) and abs(adim) > 1) or
                                           abs(adim - tahmin) > max(8.0, 0.6 * abs(tahmin)))
            if red:
                if tuttu:
                    self.red += 1
                self.tahmin_say += 1; self.son_tahmin = True
                self.yaw = once + tahmin
                self.ref = cur.copy(); self.ref_yaw = self.yaw; self.rel = 0
            elif refle:
                self.rel = s / PX; self.yaw = self.ref_yaw + self.rel
                if abs(self.rel) > 25 or c < 0.45:
                    self.ref = cur.copy(); self.ref_yaw = self.yaw; self.rel = 0
            else:
                if tuttu:
                    self.yaw += adim
                self.ref = cur.copy(); self.ref_yaw = self.yaw; self.rel = 0
        self.son = _da(self.yaw - once)
        self.prev = cur.copy()
        return self.son


def main():
    K = sys.argv[1]
    rows = list(csv.DictReader(open(os.path.join(K, "iz.csv"))))
    mx, my = bant_haritasi(); mk = maske()
    eski, yeni = Pusula(False), Pusula(True)
    # dayanak: aynı kareler robotun konum koduyla (işaret + Kalman) işlenir; işaret göründükçe yön mutlak düzelir
    import konum, json as _json
    kb = konum.KonumBulucu()
    evj = os.environ.get("EV", os.path.join(os.path.dirname(K.rstrip("/")), "..", "..", "robot", "20261122_233211", "ev.json"))
    kb.ev_ayarla(_json.load(open(evj))["isaretler"] if os.path.exists(evj) else [[1, 0.0, 0.0, 0.0]])
    donus_k = 140.0
    kuyruk = []; eski_komut = (0, 0)
    GECIKME = float(os.environ.get("GECIKME", "0.35"))
    t_once = None; ozet = []
    dur_bas = None
    for i, r in enumerate(rows):
        g = cv2.imread(os.path.join(K, r["jpg"]), cv2.IMREAD_GRAYSCALE)
        if g is None:
            continue
        b = cv2.remap(g, mx, my, cv2.INTER_LINEAR).astype(np.float32)
        gx = (np.roll(b, -1, axis=1) - np.roll(b, 1, axis=1)) * mk * PENCERE
        t = float(r["t"]); dt = 0.0 if t_once is None else min(1.0, t - t_once); t_once = t
        # karenin çekildiği andaki motor: canlıda köprü her kareyle birlikte yollar (X-Motor). Eski kayıtta yok:
        # komut GECIKME sn geriye kaydırılarak yaklaşılır (komut robota ~0,05 sn, kare telefona ~0,3 sn'de ulaşıyor)
        while kuyruk and kuyruk[0][0] < t - GECIKME:
            eski_komut = kuyruk.pop(0)[1:]
        if os.environ.get("KAYNAK", "motor") == "motor":
            kuyruk.append((t, int(r["motor_sol"]), int(r["motor_sag"])))
        else:
            kuyruk.append((t, int(r["komut_sol"]), int(r["komut_sag"])))
        sol, sag = eski_komut
        tahmin = donus_k * (sol - sag) / 510.0 * dt
        eski.kare(gx, 0.0)
        a = yeni.kare(gx, tahmin)
        # dönüş hızı katsayısı ölçümden öğrenilir (telefonda da aynı)
        if dt > 0.03 and abs(sol - sag) > 150 and abs(tahmin) > 3 and not yeni.son_tahmin:
            olc = a / dt / ((sol - sag) / 510.0)
            if 40 < olc < 400:
                donus_k = donus_k * 0.95 + olc * 0.05
        ms, mg = int(r["motor_sol"]), int(r["motor_sag"])
        kb.isle(g, t, (ms, mg))
        ref = kb.ozet()["yaw"] if kb.bilinen else float("nan")
        ozet.append((t, eski.yaw, yeni.yaw, ref, int(r["komut_sol"]), int(r["komut_sag"]), float(r["g_yaw"])))
    # duruşlar: komut 0 ve 1,5 sn sonra robot yönü oturmuş
    print("kare %d, yeni: reddedilen ölçüm %d, tahminle geçilen kare %d, öğrenilen dönüş hızı %.0f derece/sn" % (len(ozet), yeni.red, yeni.tahmin_say, donus_k))
    duruslar = []
    i = 0
    while i < len(ozet):
        if ozet[i][4] == 0 and ozet[i][5] == 0:
            j = i
            while j + 1 < len(ozet) and ozet[j + 1][4] == 0 and ozet[j + 1][5] == 0:
                j += 1
            if ozet[j][0] - ozet[i][0] > 1.5:
                duruslar.append(j)
            i = j + 1
        else:
            i += 1
    print("duruşlar arası dönüş (derece): dayanak (aynı karelerde işaret + Kalman) | telefon eski (kayıtta) | eski (bu kopya) | yeni")
    for a, b in zip(duruslar, duruslar[1:]):
        dr = ozet[b][3] - ozet[a][3]
        if not abs(dr) >= 5:
            continue
        print("  t=%6.1f-%6.1f  robot %7.1f | kayıt %7.1f | eski %7.1f | yeni %7.1f" % (
            ozet[a][0], ozet[b][0], dr, ozet[b][6] - ozet[a][6], ozet[b][1] - ozet[a][1], ozet[b][2] - ozet[a][2]))


if __name__ == "__main__":
    main()
