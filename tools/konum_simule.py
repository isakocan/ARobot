"""Robot konum kaydını (robotta ~/konum_kayit/<zaman>/) bilgisayarda oynatır ve videoya çevirir:
üstte robotun 360 derece görüntüsü + robot uygulamasındaki gibi AR yaratıkları, altta evin açık zemin haritasında
robotun konumu, izi, işaretler ve yaratıklar. Konum, Jetson'daki kodla (robot/jetson/konum) baştan hesaplanır.

Kullanım:
    python tools/konum_simule.py <kayit_klasoru> <ev_klasoru> [cikti.mp4]
    (ev_klasoru: telefondaki Android/data/com.ekip.artarama/files/evler/<ev>, adb pull ile; içinde zemin.json)
"""
import json
import math
import os
import random
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "robot", "jetson", "konum"))
import konum  # noqa: E402
import teta   # noqa: E402

KAMERA_Y = 0.17
PAN_GEN, PAN_YUK = 1280, 400            # eş dikdörtgen: boylam -180..180, enlem -45..+30
LAT_ALT, LAT_UST = -45.0, 30.0
HARITA = 560                             # harita paneli (px, kare)
RENKLER = [(64, 158, 255), (191, 217, 89), (255, 128, 191)]   # BGR: turuncu, turkuaz, mor


class Zemin:
    def __init__(self, yol):
        m = json.load(open(yol, encoding="utf-8"))
        h = m["hucre"]
        self.h = h
        hucre = lambda k: set(zip(m.get(k, [])[0::2], m.get(k, [])[1::2]))
        self.acik, self.engel, self.duvar = hucre("acik"), hucre("engel"), hucre("duvar")
        self.acik -= hucre("silinen")
        xs = [c[0] for c in self.acik | self.engel]; zs = [c[1] for c in self.acik | self.engel]
        self.x0, self.x1 = min(xs) * h - 0.3, max(xs) * h + 0.3
        self.z0, self.z1 = min(zs) * h - 0.3, max(zs) * h + 0.3

    def acik_mi(self, x, z, marj=0.12):
        r = int(math.ceil(marj / self.h))
        ix, iz = int(math.floor(x / self.h)), int(math.floor(z / self.h))
        return all((ix + a, iz + b) in self.acik for a in range(-r, r + 1) for b in range(-r, r + 1) if a * a + b * b <= r * r)


class Yaratik:
    """Robot uygulamasındaki RobotAR ile aynı davranış; yer yalnız açık zemin (haritadan)."""

    def __init__(self, zemin, x, z, renk, boy):
        self.z_, self.ev, self.pos, self.hedef = zemin, (x, z), [x, z], (x, z)
        self.yon = random.uniform(0, 360); self.renk = renk; self.boy = boy
        self.faz = random.uniform(0, 10); self.sonraki = 0.0

    def adim(self, t, dt):
        if t > self.sonraki or math.hypot(self.hedef[0] - self.pos[0], self.hedef[1] - self.pos[1]) < 0.03:
            for _ in range(20):
                a, r = random.uniform(0, 2 * math.pi), 0.45 * math.sqrt(random.random())
                h = (self.ev[0] + math.sin(a) * r, self.ev[1] + math.cos(a) * r)
                if self.z_.acik_mi(*h):
                    self.hedef = h; break
            self.sonraki = t + random.uniform(4, 9)
        dx, dz = self.hedef[0] - self.pos[0], self.hedef[1] - self.pos[1]
        ist = math.degrees(math.atan2(dx, dz))
        self.yon += max(-70 * dt, min(70 * dt, (ist - self.yon + 180) % 360 - 180))
        if abs((ist - self.yon + 180) % 360 - 180) < 40 and math.hypot(dx, dz) > 0.03:
            a = math.radians(self.yon); s = min(0.06 * dt, math.hypot(dx, dz))
            self.pos[0] += math.sin(a) * s; self.pos[1] += math.cos(a) * s


def panorama_haritasi():
    v, u = np.mgrid[0:PAN_YUK, 0:PAN_GEN]
    lon = ((u + 0.5) / PAN_GEN - 0.5) * 2 * math.pi
    lat = np.radians(LAT_UST - (v + 0.5) / PAN_YUK * (LAT_UST - LAT_ALT))
    d = np.stack([np.cos(lat) * np.sin(lon), np.sin(lat), np.cos(lat) * np.cos(lon)], -1).reshape(-1, 3)
    p = teta.yon_pikseller(d)
    return p[:, 0].reshape(PAN_YUK, PAN_GEN).copy(), p[:, 1].reshape(PAN_YUK, PAN_GEN).copy()


def pan_piksel(x, y, z):
    """Robot koordinatındaki nokta -> panorama pikseli (u, v) ve uzaklık."""
    lon = math.atan2(x, z); lat = math.atan2(y, math.hypot(x, z))
    u = (lon / (2 * math.pi) + 0.5) * PAN_GEN
    v = (math.radians(LAT_UST) - lat) / math.radians(LAT_UST - LAT_ALT) * PAN_YUK
    return u, v, math.sqrt(x * x + y * y + z * z)


def yaratik_ciz(img, yr, rx, rz, ryaw):
    """Yaratığı robotun görüntüsüne çiz (gölge + gövde + gözler). Robot -> yaratık yerel koordinat."""
    c, s = math.cos(math.radians(ryaw)), math.sin(math.radians(ryaw))
    dx, dz = yr.pos[0] - rx, yr.pos[1] - rz
    sag, ileri = dx * c - dz * s, dx * s + dz * c
    pxd = PAN_GEN / 360.0
    # gölge (zemin)
    u, v, d = pan_piksel(sag, -KAMERA_Y, ileri)
    if d < 0.15:
        return
    ry = math.degrees(math.atan(yr.boy * 0.55 / d)) * pxd
    for du in (-PAN_GEN, 0, PAN_GEN):
        cv2.ellipse(img, (int(u + du), int(v)), (int(ry * 1.1), max(2, int(ry * 0.35 * KAMERA_Y / max(d, 0.2) * 3))), 0, 0, 360, (25, 25, 30), -1, cv2.LINE_AA)
    zip_ = abs(math.sin((yr.t + yr.faz) * 3.2))
    yuk = -KAMERA_Y + yr.boy * (0.55 + 0.18 * zip_)
    u, v, d = pan_piksel(sag, yuk, ileri)
    r = math.degrees(math.atan(yr.boy * 0.5 / d)) * pxd
    # yaratığın robota göre baktığı yön: gözler kameraya dönükse görünür
    bakis = math.radians(yr.yon - ryaw) - math.atan2(sag, ileri)
    on = math.cos(bakis)
    for du in (-PAN_GEN, 0, PAN_GEN):
        m = (int(u + du), int(v))
        koyu = tuple(int(k * 0.55) for k in yr.renk)
        cv2.circle(img, m, int(r), koyu, -1, cv2.LINE_AA)
        cv2.circle(img, (int(m[0] - r * 0.15), int(m[1] - r * 0.15)), int(r * 0.85), yr.renk, -1, cv2.LINE_AA)
        cv2.circle(img, (int(m[0] - r * 0.35), int(m[1] - r * 0.4)), max(1, int(r * 0.18)), (255, 255, 255), -1, cv2.LINE_AA)
        for kulak in (-1, 1):
            k = (int(m[0] + kulak * r * 0.55), int(m[1] - r * 0.95))
            cv2.ellipse(img, k, (max(1, int(r * 0.18)), max(2, int(r * 0.38))), kulak * 20, 0, 360, koyu, -1, cv2.LINE_AA)
        if on > 0.1:
            kirp = 0.15 if ((yr.t + yr.faz) % 3.7) < 0.12 else 1.0
            for g in (-1, 1):
                e = (int(m[0] + (g * 0.3 + math.sin(bakis) * 0.3) * r), int(m[1] - r * 0.1))
                cv2.ellipse(img, e, (max(1, int(r * 0.2 * on)), max(1, int(r * 0.22 * kirp))), 0, 0, 360, (255, 255, 255), -1, cv2.LINE_AA)
                cv2.ellipse(img, e, (max(1, int(r * 0.1 * on)), max(1, int(r * 0.12 * kirp))), 0, 0, 360, (40, 30, 30), -1, cv2.LINE_AA)


class HaritaCizer:
    def __init__(self, zemin, isaretler):
        self.zm = zemin
        gen = zemin.x1 - zemin.x0; yuk = zemin.z1 - zemin.z0
        self.olcek = (HARITA - 40) / max(gen, yuk)
        self.ox = 20 + ((HARITA - 40) - gen * self.olcek) / 2
        self.oy = 20 + ((HARITA - 40) - yuk * self.olcek) / 2
        self.taban = np.full((HARITA, HARITA, 3), (34, 30, 28), np.uint8)
        h = zemin.h
        for kume, renk in ((zemin.acik, (150, 135, 50)), (zemin.engel, (90, 90, 96)), (zemin.duvar, (150, 150, 160))):
            for ix, iz in kume:
                a = self.p(ix * h, iz * h); b = self.p((ix + 1) * h, (iz + 1) * h)
                cv2.rectangle(self.taban, (int(a[0]), int(b[1])), (int(b[0]), int(a[1])), renk, -1)
        self.taban = cv2.GaussianBlur(self.taban, (3, 3), 0)
        for i, x, z, aci in isaretler:
            n = (math.sin(aci), math.cos(aci)); sag = (n[1], -n[0])
            a = self.p(x - sag[0] * 0.15, z - sag[1] * 0.15); b = self.p(x + sag[0] * 0.15, z + sag[1] * 0.15)
            cv2.line(self.taban, (int(a[0]), int(a[1])), (int(b[0]), int(b[1])), (60, 60, 255), 4, cv2.LINE_AA)
            q = self.p(x + n[0] * 0.25, z + n[1] * 0.25)
            cv2.putText(self.taban, "#%d" % i, (int(q[0]) - 12, int(q[1]) + 5), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (90, 90, 255), 1, cv2.LINE_AA)
        # 1 m ölçek çubuğu
        a = self.p(zemin.x0 + 0.2, zemin.z0 + 0.15)
        cv2.line(self.taban, (int(a[0]), int(a[1])), (int(a[0] + self.olcek), int(a[1])), (220, 220, 220), 2)
        cv2.putText(self.taban, "1 m", (int(a[0]), int(a[1]) - 6), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (220, 220, 220), 1, cv2.LINE_AA)

    def p(self, x, z):
        return self.ox + (x - self.zm.x0) * self.olcek, HARITA - (self.oy + (z - self.zm.z0) * self.olcek)

    def ciz(self, iz, robot, bilinen, yaratiklar):
        img = self.taban.copy()
        for i in range(1, len(iz)):
            a, b = self.p(*iz[i - 1]), self.p(*iz[i])
            cv2.line(img, (int(a[0]), int(a[1])), (int(b[0]), int(b[1])), (80, 200, 240), 2, cv2.LINE_AA)
        for y in yaratiklar:
            q = self.p(*y.pos)
            cv2.circle(img, (int(q[0]), int(q[1])), 6, y.renk, -1, cv2.LINE_AA)
            cv2.circle(img, (int(q[0]), int(q[1])), 6, (20, 20, 20), 1, cv2.LINE_AA)
        if bilinen:
            x, z, yaw = robot
            q = self.p(x, z); a = math.radians(yaw)
            uc = self.p(x + math.sin(a) * 0.32, z + math.cos(a) * 0.32)
            # bakış konisi (önü)
            for s in (-35, 35):
                k = self.p(x + math.sin(a + math.radians(s)) * 0.6, z + math.cos(a + math.radians(s)) * 0.6)
                cv2.line(img, (int(q[0]), int(q[1])), (int(k[0]), int(k[1])), (200, 200, 200), 1, cv2.LINE_AA)
            cv2.circle(img, (int(q[0]), int(q[1])), 9, (255, 255, 255), -1, cv2.LINE_AA)
            cv2.arrowedLine(img, (int(q[0]), int(q[1])), (int(uc[0]), int(uc[1])), (30, 30, 30), 3, cv2.LINE_AA, tipLength=0.4)
        return img


def main():
    if len(sys.argv) < 3:
        print(__doc__); return 2
    K, E = sys.argv[1], sys.argv[2]
    cikti = sys.argv[3] if len(sys.argv) > 3 else os.path.join(K, "simulasyon.mp4")
    random.seed(3)
    R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl"))]
    ev = json.load(open(os.path.join(K, "ev.json")))["isaretler"]
    zm = Zemin(os.path.join(E, "zemin.json"))
    kb = konum.KonumBulucu(); kb.ev_ayarla(ev)
    mx, my = panorama_haritasi()
    hc = HaritaCizer(zm, ev)
    W, H = PAN_GEN, PAN_YUK + HARITA
    vw = cv2.VideoWriter(cikti, cv2.VideoWriter_fourcc(*"mp4v"), 10, (W, H))
    iz, yaratiklar = [], []
    t0 = R[0]["t"]; onceki_t = None
    goster = None                          # gösterilen (yumuşatılmış) robot konumu
    resimler = {}
    for n, r in enumerate(R):
        ham = cv2.imread(os.path.join(K, r["jpg"]))
        gri = cv2.cvtColor(ham, cv2.COLOR_BGR2GRAY)
        gor = kb.isle(gri, r["t"], (r["sol"], r["sag"]))
        o = kb.ozet(); t = r["t"] - t0
        dt = 0.2 if onceki_t is None else r["t"] - onceki_t; onceki_t = r["t"]
        if o["var"]:
            if goster is None:
                goster = [o["x"], o["z"], o["yaw"]]
                # yaratıklar robotun önündeki açık zemine (0,7-1,3 m, 100 derecelik yay)
                for i in range(3):
                    for _ in range(200):
                        a = o["yaw"] + (i - 1) * 50 + random.uniform(-15, 15); d = random.uniform(0.7, 1.3)
                        x = o["x"] + math.sin(math.radians(a)) * d; z = o["z"] + math.cos(math.radians(a)) * d
                        if zm.acik_mi(x, z, 0.15):
                            yaratiklar.append(Yaratik(zm, x, z, RENKLER[i], random.uniform(0.11, 0.16))); break
            goster[0] += (o["x"] - goster[0]) * 0.6; goster[1] += (o["z"] - goster[1]) * 0.6
            goster[2] += ((o["yaw"] - goster[2] + 180) % 360 - 180) * 0.8
            iz.append((goster[0], goster[1]))
        # 10 fps video: her kayıt karesi (5 Hz) iki kez, yaratıklar arada da hareket eder
        pan0 = cv2.remap(ham, mx, my, cv2.INTER_LINEAR)
        for alt in range(2):
            for y in yaratiklar:
                y.t = t + alt * dt / 2
                y.adim(y.t, dt / 2)
            pan = pan0.copy()
            if goster is not None:
                for y in sorted(yaratiklar, key=lambda y: -math.hypot(y.pos[0] - goster[0], y.pos[1] - goster[1])):
                    yaratik_ciz(pan, y, goster[0], goster[1], goster[2])
            # işaretler: görüntüde çerçeve
            for g in gor:
                u, v, _ = pan_piksel(g["x"], g["y"], g["z"])
                cv2.rectangle(pan, (int(u) - 14, int(v) - 14), (int(u) + 14, int(v) + 14), (60, 60, 255), 2)
                cv2.putText(pan, "#%d %.1fm" % (g["id"], g["mesafe"]), (int(u) - 24, int(v) - 20), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (90, 90, 255), 1, cv2.LINE_AA)
            for yon, ad in ((0, "ON"), (90, "SAG"), (-90, "SOL"), (180, "ARKA")):
                u = (yon / 360.0 + 0.5) * PAN_GEN
                cv2.putText(pan, ad, (int(u) - 14 if yon != 180 else PAN_GEN - 46, 18), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (235, 235, 235), 1, cv2.LINE_AA)
            harita = hc.ciz(iz, goster or (0, 0, 0), goster is not None, yaratiklar)
            kare = np.zeros((H, W, 3), np.uint8)
            kare[:PAN_YUK] = pan
            kare[PAN_YUK:, :HARITA] = harita
            # bilgi paneli
            bx = HARITA + 30
            satirlar = ["%.1f sn" % t,
                        "konum: " + ("biliniyor" if o["var"] else "aranıyor (yakın işaret bekleniyor)"),
                        ]
            if o["var"]:
                satirlar += ["x %.2f m   z %.2f m   yön %.0f" % (o["x"], o["z"], (o["yaw"] + 180) % 360 - 180),
                             "son düzeltme: " + ({"isaret": "işaret", "hafiza": "hafıza (anahtar kare)"}.get(o["kaynak"], "-")) +
                             ("  %.1f sn önce" % (r["t"] - kb.son_isaret) if kb.son_isaret else "")]
            satirlar += ["görülen işaret: " + (", ".join("#%d %.2f m" % (g["id"], g["mesafe"]) for g in gor) or "yok"),
                         "motor: sol %d  sağ %d" % (r["sol"], r["sag"]),
                         "hafıza: %d anahtar kare" % o["hafiza"]]
            tr = str.maketrans("ıİşŞğĞüÜöÖçÇ", "iIsSgGuUoOcC")         # OpenCV yazısı Türkçe harf basamıyor
            for i, s in enumerate(satirlar):
                cv2.putText(kare, s.translate(tr), (bx, PAN_YUK + 40 + i * 30), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (230, 230, 230), 1, cv2.LINE_AA)
            aciklama = ["harita: turkuaz = acik zemin (telefon taramasi), gri = esya/engel",
                        "kirmizi cizgi = isaret, sari = robotun izi, beyaz ok = robot",
                        "renkli noktalar = AR yaratiklari (acik zeminde dolasir)"]
            for i, s in enumerate(aciklama):
                cv2.putText(kare, s, (bx, H - 80 + i * 24), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (170, 170, 170), 1, cv2.LINE_AA)
            vw.write(kare)
            if alt == 0 and n in (int(len(R) * f) for f in (0.08, 0.3, 0.55, 0.75, 0.95)):
                resimler[n] = kare.copy()
    vw.release()
    for n, k in resimler.items():
        cv2.imwrite(os.path.splitext(cikti)[0] + "_%03d.jpg" % n, k, [cv2.IMWRITE_JPEG_QUALITY, 88])
    print("yazildi:", cikti, len(R), "kare")


if __name__ == "__main__":
    sys.exit(main() or 0)
