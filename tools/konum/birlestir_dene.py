"""Telefondaki konum birleştirmesinin (RobotDev.KonumGeldi / PozGuncelle) Python kopyası: Dev kaydında (iz.csv + konum.csv)
telefonun görüntü ölçümü ve robotun konumu aynen kullanılıp birleştirme farklı ayarlarla yeniden yapılır; AR hatası
(işaretin beklenen - görülen açısı) yeniden hesaplanır.

python tools/konum/birlestir_dene.py <dev_kaydi> [ev.json]

Görülen açı kayıttan geri çıkarılır: kayıtta ar_hata = beklenen(o anki ekran konumu) - görülen.
"""
import csv
import json
import math
import os
import sys

import numpy as np


def da(a):
    return (a + 180.0) % 360.0 - 180.0


def f(r, k):
    try:
        return float(r[k])
    except (KeyError, ValueError):
        return float("nan")


def don(yaw, x, z):
    c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    return x * c + z * s, -x * s + z * c


def aci_bekle(px, pz, pyaw, ex, ez):
    c, s = math.cos(math.radians(pyaw)), math.sin(math.radians(pyaw))
    dx, dz = ex - px, ez - pz
    return math.degrees(math.atan2(dx * c - dz * s, dx * s + dz * c))


def calis(iz, kn, ev, ayar):
    # olaylar zaman sırasında: kare (görüntü adımı) ve konum
    olay = [(f(r, "t"), 0, r) for r in iz] + [(f(r, "t") - 1e-4, 1, r) for r in kn]
    olay.sort(key=lambda q: q[0])
    gecmis = []; fark = None; vY = 25.0; vP = 0.01; onceki = None; d = None; son_t = None
    hatalar = []
    for t, tur, r in olay:
        if tur == 0:
            gy, gx, gz = f(r, "g_yaw"), f(r, "g_x"), f(r, "g_z")
            if onceki is not None:
                ad = abs(da(gy - onceki[0])); yol = math.hypot(gx - onceki[1], gz - onceki[2])
                s = ayar["yon"] * ad + ayar["yon0"]; vY += s * s
                sp = ayar["poz"] * yol + 0.002; vP += sp * sp
            onceki = (gy, gx, gz)
            if fark is not None:
                # ekran farkı tahmine yetişir
                dt = 0.0 if son_t is None else t - son_t
                a = 1 - math.exp(-dt / ayar["tau"]) if ayar["tau"] > 0 else 1.0
                d = [d[0] + da(fark[0] - d[0]) * a, d[1] + (fark[1] - d[1]) * a, d[2] + (fark[2] - d[2]) * a]
                rx, rz = don(d[0], gx, gz)
                poz = (rx + d[1], rz + d[2], gy + d[0])
            else:
                poz = (float("nan"),) * 3
            son_t = t
            gecmis.append((int(f(r, "robot_kare")), gy, gx, gz, poz, r))
            continue
        kk = int(f(r, "k_kare"))
        g = next((q for q in reversed(gecmis) if 0 <= q[0] <= kk), None)
        if g is None:
            continue
        # işaret ölçümü (kayıttan geri çıkar): görülen = beklenen(kayıttaki ekran) - ar_hata
        if r.get("ar_hata", "") != "" and fark is not None:
            rr = g[5]
            eski = aci_bekle(f(rr, "poz_x"), f(rr, "poz_z"), f(rr, "poz_yaw"), ev[0], ev[1])
            gorulen = eski - f(r, "ar_hata")
            yeni = aci_bekle(g[4][0], g[4][1], g[4][2], ev[0], ev[1])
            hatalar.append(da(yeni - gorulen))
        ky_, kx_, kz_, sy, sp = f(r, "k_yaw"), f(r, "k_x"), f(r, "k_z"), f(r, "k_sy"), f(r, "k_sp")
        hy = da(ky_ - g[1])
        ry, rp = max(sy, 0.8), max(sp, 0.015)
        if fark is None:
            rx, rz = don(hy, g[2], g[3]); fark = [hy, kx_ - rx, kz_ - rz]; d = list(fark); vY = ry * ry; vP = rp * rp
            continue
        k1 = vY / (vY + ry * ry)
        if sy < 6:
            k1 = max(k1, ayar["kmin"])
        fark[0] += da(hy - fark[0]) * k1; vY *= 1 - k1
        rx, rz = don(fark[0], g[2], g[3])
        k2 = vP / (vP + rp * rp)
        if sp < 0.15:
            k2 = max(k2, ayar["kmin"])
        fark[1] += (kx_ - rx - fark[1]) * k2; fark[2] += (kz_ - rz - fark[2]) * k2; vP *= 1 - k2
    return np.abs(hatalar)


def main():
    K = sys.argv[1]
    iz = list(csv.DictReader(open(os.path.join(K, "iz.csv"))))
    kn = list(csv.DictReader(open(os.path.join(K, "konum.csv"))))
    ev = (0.0, 0.0)
    if len(sys.argv) > 2:
        e = json.load(open(sys.argv[2]))["isaretler"][0]; ev = (e["x"], e["z"])
    kayit = np.abs([f(r, "ar_hata") for r in kn if r.get("ar_hata", "") != ""])
    print("kayıttaki (telefonda olan): ortanca %.1f, %%90 %.1f, en çok %.1f derece (%d)" % (np.median(kayit), np.percentile(kayit, 90), kayit.max(), len(kayit)))
    for ad, ayar in [("eski ayar (yeniden)", dict(yon=0.05, yon0=0.05, poz=0.08, kmin=0.0, tau=0.25)),
                     ("yeni ayar", dict(yon=0.3, yon0=0.1, poz=0.3, kmin=0.5, tau=0.15)),
                     ("robotu aynen al", dict(yon=0.3, yon0=0.1, poz=0.3, kmin=1.0, tau=0.0))]:
        h = calis(iz, kn, ev, ayar)
        print("%-20s ortanca %.1f, %%90 %.1f, en çok %.1f derece (%d)" % (ad, np.median(h), np.percentile(h, 90), h.max(), len(h)))


if __name__ == "__main__":
    main()
