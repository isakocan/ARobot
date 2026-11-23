"""Telefon Dev zincirinin uçtan uca yeniden oynatılması (kayıt: files/dev/<zaman>/ + ev.json).
Kayıttaki kareler yeni pusula (RobotGorselIz kopyası, motor tahmini X-Motor/X-Zaman ile) ve yeni birleştirmeden
(RobotDev.KonumGeldi kopyası) geçirilir. Doğruluk, karelerin kendisinde bulunan işarete göre ölçülür (robottan bağımsız):
işaretin ekranda olması gereken yön (telefonun gösterdiği konumdan) ile karede görüldüğü yön arasındaki fark, yatay ve dikey.
Pembe çerçevenin gerçek işarete oturup oturmadığının sayısal karşılığı.

python tools/konum/uctan_uca.py <dev_kaydi> <ev.json>
"""
import csv
import json
import math
import os
import sys

import cv2
import numpy as np

KOK = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import isaret  # noqa: E402
import pusula_dene as pd  # noqa: E402


def da(a):
    return (a + 180.0) % 360.0 - 180.0


def f(r, k, v=float("nan")):
    try:
        return float(r[k])
    except (KeyError, ValueError, TypeError):
        return v


def don(yaw, x, z):
    c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    return x * c + z * s, -x * s + z * c


def main():
    K, evj = sys.argv[1], sys.argv[2]
    e = json.load(open(evj))["isaretler"][0]; EX, EZ = e["x"], e["z"]
    iz = list(csv.DictReader(open(os.path.join(K, "iz.csv"))))
    kn = list(csv.DictReader(open(os.path.join(K, "konum.csv"))))
    os.environ["MOD"] = "tam"
    mx, my = pd.bant_haritasi(); mk = pd.maske()
    pus = pd.Pusula(True)
    olay = [(f(r, "t"), 0, r) for r in iz] + [(f(r, "t") - 1e-4, 1, r) for r in kn]
    olay.sort(key=lambda q: q[0])
    gY = gX = gZ = 0.0; donus_k = 140.0; onceki = None
    fark = None; d = None; vY, vP = 25.0, 0.01; son_t = None
    gecmis = []
    sonuc = {"kayit": [], "yeni": []}
    duran = {"kayit": [], "yeni": []}
    say = 0
    for t, tur, r in olay:
        if tur == 1:
            kk = int(f(r, "k_kare", -1))
            g = next((q for q in reversed(gecmis) if 0 <= q[0] <= kk), None)
            if g is None:
                continue
            ky_, kx_, kz_, sy, sp = f(r, "k_yaw"), f(r, "k_x"), f(r, "k_z"), f(r, "k_sy"), f(r, "k_sp")
            hy = da(ky_ - g[1]); ry, rp = max(sy, 0.8), max(sp, 0.015)
            if fark is None:
                rx, rz = don(hy, g[2], g[3]); fark = [hy, kx_ - rx, kz_ - rz]; d = list(fark); vY, vP = ry * ry, rp * rp
                continue
            k1 = vY / (vY + ry * ry)
            if sy < 6:
                k1 = max(k1, 0.5)
            fark[0] += da(hy - fark[0]) * k1; vY *= 1 - k1
            rx, rz = don(fark[0], g[2], g[3])
            k2 = vP / (vP + rp * rp)
            if sp < 0.15:
                k2 = max(k2, 0.5)
            fark[1] += (kx_ - rx - fark[1]) * k2; fark[2] += (kz_ - rz - fark[2]) * k2; vP *= 1 - k2
            continue
        # kare: pusula (motor tahminli) + kayıttaki zemin adımı
        g = cv2.imread(os.path.join(K, r["jpg"]), cv2.IMREAD_GRAYSCALE)
        if g is None:
            continue
        b = cv2.remap(g, mx, my, cv2.INTER_LINEAR).astype(np.float32)
        gx_ = (np.roll(b, -1, axis=1) - np.roll(b, 1, axis=1)) * mk * pd.PENCERE
        zm = f(r, "robot_zaman_ms", 0); ms, mg = int(f(r, "kare_motor_sol", -999)), int(f(r, "kare_motor_sag", -999))
        dt = 0.0
        if onceki is not None:
            dt = (zm - onceki[0]) / 1000.0 if zm > 0 and onceki[0] > 0 and zm > onceki[0] else t - onceki[3]
        dt = min(max(dt, 0.0), 1.5)
        if ms == -999:
            ms, mg = int(f(r, "motor_sol", 0)), int(f(r, "motor_sag", 0))
        ps, pg = (onceki[1], onceki[2]) if onceki is not None else (ms, mg)
        sol, sag = (ms + ps) // 2, (mg + pg) // 2
        tahmin = donus_k * (sol - sag) / 510.0 * dt
        adim = pus.kare(gx_, tahmin)
        if not pus.son_tahmin and dt > 0.03 and abs(sol - sag) > 150 and abs(adim) > 3:
            o = adim / dt / ((sol - sag) / 510.0)
            if 40 < o < 400:
                donus_k = donus_k * 0.95 + o * 0.05
        onceki = (zm, ms, mg, t)
        gY += adim
        s = (0.5 if pus.son_tahmin else 0.3) * abs(adim) + 0.1; vY += s * s
        ai, asg = f(r, "adim_ileri", 0), f(r, "adim_sag", 0)
        if r.get("zemin_tuttu") == "1":
            sn, cs = math.sin(math.radians(gY)), math.cos(math.radians(gY))
            gX += sn * ai + cs * asg; gZ += cs * ai - sn * asg
            yol = math.hypot(ai, asg); vP += (0.3 * yol + 0.002) ** 2
        if fark is not None:
            a = 1 - math.exp(-(0 if son_t is None else t - son_t) / 0.15)
            d = [d[0] + da(fark[0] - d[0]) * a, d[1] + (fark[1] - d[1]) * a, d[2] + (fark[2] - d[2]) * a]
        son_t = t
        poz = None
        if d is not None:
            rx, rz = don(d[0], gX, gZ); poz = (rx + d[1], rz + d[2], gY + d[0])
        gecmis.append((int(f(r, "robot_kare", -1)), gY, gX, gZ))
        dur = r.get("komut_sol") == "0" and r.get("komut_sag") == "0"
        for ad, p in (("kayit", (f(r, "poz_x"), f(r, "poz_z"), f(r, "poz_yaw"))), ("yeni", poz)):
            if p is not None and not math.isnan(p[0]):
                duran[ad].append((t, dur, p))
        # işaret karede: her 2. kare
        say += 1
        if say % 2 or poz is None:
            continue
        for m in isaret.bul(g):
            if m["id"] != e["id"] or m["mesafe"] > 2.5:
                continue
            az_m = math.degrees(math.atan2(m["x"], m["z"])); el_m = math.degrees(math.atan2(m["y"], m["mesafe"]))
            for ad, p in (("kayit", (f(r, "poz_x"), f(r, "poz_z"), f(r, "poz_yaw"))), ("yeni", poz)):
                lx, lz = don(-p[2], EX - p[0], EZ - p[1])
                lx, lz = don(p[2], EX - p[0], EZ - p[1])
                c, s_ = math.cos(math.radians(p[2])), math.sin(math.radians(p[2]))
                dx, dz = EX - p[0], EZ - p[1]
                sag, ileri = dx * c - dz * s_, dx * s_ + dz * c
                az = math.degrees(math.atan2(sag, ileri)); el = math.degrees(math.atan2(m["y"], math.hypot(sag, ileri)))
                sonuc[ad].append((t, da(az - az_m), el - el_m, m["mesafe"]))
    print("== %s" % K)
    for ad in ("kayit", "yeni"):
        a = np.array(sonuc[ad])
        if not len(a):
            print(ad, "ölçüm yok"); continue
        h, v = np.abs(a[:, 1]), np.abs(a[:, 2])
        print("%-6s işarete göre yatay hata: ortanca %.1f°, %%90 %.1f°, en çok %.1f° | dikey: ortanca %.1f°, %%90 %.1f° (%d kare)" % (
            "telefondaki" if ad == "kayit" else "yeni", np.median(h), np.percentile(h, 90), h.max(), np.median(v), np.percentile(v, 90), len(a)))
        # durağan kayma (komut yokken 1 sn+ bloklar, ilk 0,8 sn hariç)
        L = duran[ad]; en = (0.0, 0.0); i = 0
        while i < len(L):
            j = i
            while j + 1 < len(L) and L[j + 1][1] and L[i][1]:
                j += 1
            if L[i][1] and L[j][0] - L[i][0] > 1.5:
                bl = [q for q in L[i:j + 1] if q[0] - L[i][0] > 0.8]
                if len(bl) > 2:
                    p0 = bl[0][2]
                    en = (max(en[0], max(math.hypot(q[2][0] - p0[0], q[2][1] - p0[1]) for q in bl)),
                          max(en[1], max(abs(da(q[2][2] - p0[2])) for q in bl)))
            i = j + 1
        print("         durağan kayma en çok %.0f cm / %.1f°" % (en[0] * 100, en[1]))
    if len(sys.argv) > 3:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        fig, ax = plt.subplots(2, 1, figsize=(13, 6), sharex=True)
        for ad in ("kayit", "yeni"):
            a = np.array(sonuc[ad])
            if len(a):
                ax[0].plot(a[:, 0] - a[0, 0], a[:, 1], ".", ms=3, label=ad); ax[1].plot(a[:, 0] - a[0, 0], a[:, 2], ".", ms=3, label=ad)
        ax[0].set_ylabel("yatay hata (°)"); ax[1].set_ylabel("dikey hata (°)")
        for q in ax:
            q.set_ylim(-30, 30); q.grid(alpha=0.3); q.legend(); q.axhline(0, color="k", lw=0.5)
        fig.tight_layout(); fig.savefig(sys.argv[3], dpi=90)


if __name__ == "__main__":
    main()
