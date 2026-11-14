"""Kalibrasyon/teshis ortak analiz fonksiyonlari: eslesme tablosu (azimut, yaricap, normalize hiz), oylama benzetimi (B'nin en yogun kume kurali), sektor hizlari."""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_kalib_model as km  # noqa: E402

R_MIN, R_MAX = 0.32, 1.45
SEKTORLER = {"on": 0.0, "sag": 90.0, "arka": 180.0, "sol": 270.0}


def aci_fark(a, b):
    return (a - b + 180.0) % 360.0 - 180.0


def tablo(E, kam, h=km.H, r_min=R_MIN, r_max=R_MAX):
    """Eslesme basina: azimut(0 on, saat yonu), r1, yer degistirmesi D=(dx, dz) m, hiz s (m/sn, hareket YONUNDE: zemin hareketin tersine kayar), pixel yer degistirmesi,
    cift indeksi, lens, gecerli. Referans hiz vref (cift basina, on sektor +-45 derece medyani) ve s/vref."""
    c = E["c"]
    Y = km.yer_cift(kam, {k: E[k][...] for k in ("lens", "u1", "v1", "u2", "v2")}, h)
    dt = E["dt"][c]
    sg = E["isaret"][c]
    D = np.stack([Y["x2"] - Y["x1"], Y["z2"] - Y["z1"]], 1)
    az = np.degrees(np.arctan2(Y["x1"], Y["z1"])) % 360.0
    s = -D[:, 1] * sg / dt                                   # hareket yonunde (ileri kare icin + ) hiz
    yan = -D[:, 0] * sg / dt                                 # (saga +) yanal; ileri gidiste zemin sola kayar -> sag hareket +
    gec = Y["gec"] & (Y["r1"] > r_min) & (Y["r1"] < r_max) & (Y["r2"] > 0.2)
    pix = np.hypot(E["u2"] - E["u1"], E["v2"] - E["v1"])
    n = len(E["cift"])
    vref = np.full(n, np.nan)
    for i in range(n):
        m = (c == i) & gec & (np.abs(aci_fark(az, 0.0 if sg[c == i][0:1].sum() >= 0 else 0.0)) < 45)
        if m.sum() >= 15:
            vref[i] = np.median(s[m])
    return dict(az=az, r=Y["r1"], D=D, s=s, yan=yan, gec=gec, pix=pix, c=c, dt=dt, sg=sg, vref=vref, q=s / vref[c], lens=E["lens"], u1=E["u1"], v1=E["v1"])


def oylama(D, tol=0.035):
    """B'nin kural benzetimi (konum_bev.BevOtelemeci): oy = yer degistirme vektoru, en cok komsusu olan oy ve ortalamasi. Dondurur: merkez (2,), ic sayisi."""
    if len(D) < 8:
        return None, 0
    if len(D) > 700:
        D = D[np.random.default_rng(0).choice(len(D), 700, replace=False)]
    fark = np.linalg.norm(D[:, None, :] - D[None, :, :], axis=2) < tol
    j = int(np.argmax(fark.sum(1)))
    m = fark[j]
    t = D[m].mean(0)
    m2 = np.linalg.norm(D - t, axis=1) < tol
    return D[m2].mean(0), int(m2.sum())


def sektor_hiz(T, secim, genislik=50.0, min_n=40):
    """Secim (bool, eslesme) icinde 4 sektorun medyan hizi (m/sn, hareket yonunde) ve yanal; cift basina medyan sonra toplam yol/sure (agirlik dt) degil basit: tum ciftlerin sektor medyanlarinin dt-agirlikli ortalamasi."""
    out = {}
    for ad, a0 in SEKTORLER.items():
        # geri gidiste sektor tanimi robot cercevesinde sabit (on = robotun onu), hareket yonu isaretle gelir
        m = secim & T["gec"] & (np.abs(aci_fark(T["az"], a0)) < genislik)
        vs, ws, ys = [], [], []
        for i in np.unique(T["c"][m]):
            mi = m & (T["c"] == i)
            if mi.sum() >= min_n:
                vs.append(np.median(T["s"][mi]))
                ys.append(np.median(T["yan"][mi]))
                ws.append(T["dt"][mi][0])
        out[ad] = (float(np.average(vs, weights=ws)), float(np.average(ys, weights=ws)), len(vs)) if vs else (np.nan, np.nan, 0)
    return out


def yayilim(sh):
    v = np.array([sh[k][0] for k in SEKTORLER])
    return float((v.max() - v.min()) / v.mean())
