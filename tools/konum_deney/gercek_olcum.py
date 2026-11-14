"""Yer gercegi tasiyan ham JPEG kaydi (olcum_ham: 2,00 m bant araligi) analizi. Parametre ayari YOK; h = 0,17 m, gercek maske.
  python gercek_olcum.py ilk      ilk duz gidis (3,4-14,4 sn): kusbakisi zemin yolu, olcek, hiz, h kalibrasyonu (tarama maskesiz ve maskeli)
  python gercek_olcum.py don      yerinde donusler (150-165, 175-182 sn): M2D birikimli aci, don hizi
  python gercek_olcum.py kapanis  ilk-son/benzer kare dogrudan M2D eslesmeleri
Girdi onbellekleri: onbellek_gercek (yonL_komutlu_olcum_ham, yon_komutsuz_gercek_olcum_ham, tarama, bev)."""
import json
import os
import sys

import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_ortak as go  # noqa: E402

plt.rcParams["font.family"] = "DejaVu Sans"
AD = "olcum_ham"
CIKTI = os.path.join(go.SONUC, "olcum")
H = 0.17
os.makedirs(CIKTI, exist_ok=True)


def yukle():
    from kayit import Kayit
    k = Kayit(os.path.join(go.GERCEK, AD))
    t = k.t_ms / 1000.0
    kom = np.array([k.komut(int(x)) for x in k.t_ms])
    return k, t, kom


def bev_maskesiz(maske="gercek", t0=0.0, t1=20.0):
    """LCN BEV artimlari, tarama maskesi OLMADAN (zemin taramasindan bagimsiz). yaw = M2D-LCN komutlu. Onbellek: onbellek_<maske>/bevMaskesiz_olcum_ham_<t0>_<t1>.npz"""
    bo, ko, zo = go.kur(maske)
    p = os.path.join(go.onbellek(maske), "bevMaskesiz_%s_%d_%d.npz" % (AD, t0, t1))
    if os.path.exists(p):
        z = np.load(p)
        return {k: z[k] for k in z.files}
    import cv2
    cv2.setNumThreads(2)
    import birlesik_bev as bb
    import birlesik_yon as by
    import konum_bev as kb
    from kayit import Kayit
    from kamera import Kamera
    kay = Kayit(os.path.join(go.GERCEK, AD))
    kam = Kamera.bilgiden(kay.bilgi)
    yz = by.yukle(AD, True)
    tab = bb.LcnTablo("theta", kam, lcn_par=bb.LcnParam, h=H)
    oz = kb.BevOtelemeci(tab)
    oz.orb = cv2.ORB_create(nfeatures=500, scaleFactor=1.2, nlevels=3, edgeThreshold=13, fastThreshold=bb.LcnParam.fast_esik, patchSize=21)
    t = kay.t_ms / 1000.0
    idx = np.where((t >= t0) & (t <= t1))[0]
    D = np.full((len(idx), 2), np.nan)
    OK = np.zeros(len(idx), bool)
    IC = np.zeros(len(idx), int)
    for j, i in enumerate(idx):
        ds, di, ok, ic = oz.guncelle(kay.resim(i), float(yz["yon"][i]), None)
        D[j] = (ds, di)
        OK[j] = ok
        IC[j] = ic
    z = dict(idx=idx, t=t[idx], d=D, ok=OK, ic=IC)
    np.savez_compressed(p, **z)
    return z


def ilk():
    k, t, kom = yukle()
    sonuc = {}
    z = bev_maskesiz("gercek", 0.0, 20.0)
    tt, d, ok, ic = z["t"], z["d"], z["ok"], z["ic"]
    kg = kom[z["idx"]]
    dt = np.clip(np.diff(tt, prepend=tt[0] - 1 / 15.0), 0.02, 0.3)
    di = np.where(ok & np.isfinite(d[:, 1]), d[:, 1], 0.0)
    ds = np.where(ok & np.isfinite(d[:, 0]), d[:, 0], 0.0)
    yol = np.cumsum(di)
    hareket = (np.abs(kg[:, 0]) + np.abs(kg[:, 1])) > 60
    # komutun nominal yolu (0,55 m/sn @255; SIM varsayimi) ve hareketli karelerde gecerlilik
    vnom = 0.55 * (kg[:, 0] + kg[:, 1]) / 2 / 255.0
    ynom = np.cumsum(vnom * dt)
    gecerli = float(ok[hareket].mean()) if hareket.any() else np.nan
    maks_ileri = float(yol.max())
    net = float(yol[-1])
    # tam gaz ileri kararli bolum: komut >= 240 hem sol hem sag, 0,5 sn sonrasi
    tam = (kg[:, 0] >= 240) & (kg[:, 1] >= 240)
    tam_k = np.zeros(len(tt), bool)
    for i in range(10, len(tt)):
        tam_k[i] = tam[i - 10:i + 1].all()
    v_tam = float(np.sum(d[tam_k & ok, 1]) / np.sum(dt[tam_k & ok])) if (tam_k & ok).sum() > 5 else np.nan
    sonuc.update(maskesiz=dict(gecerli_hareketli=gecerli, ic_ort=float(ic[ok & hareket].mean()), maks_ileri_m=maks_ileri, net_m=net, nominal_yol_net_m=float(ynom[-1]),
                              tam_gaz_hiz_m_sn=v_tam, tam_gaz_kare=int((tam_k & ok).sum()), yanal_net_m=float(np.sum(ds)),
                              h_gerekli_net_2m=float(H * 2.0 / net) if net > 0.1 else None, h_gerekli_maks_2m=float(H * 2.0 / maks_ileri) if maks_ileri > 0.1 else None))
    print("[maskesiz] hareketli karelerde B gecerli %.2f, ic ort %.0f | maks ileri %.2f m, net (14,4-20 sn dahil) %.2f m, nominal(0,55) net %.2f m | tam gaz hizi %.3f m/sn (%d kare) | yanal net %.2f m" % (
        gecerli, ic[ok & hareket].mean(), maks_ileri, net, ynom[-1], v_tam, (tam_k & ok).sum(), np.sum(ds)))
    fig, ax = plt.subplots(2, 1, figsize=(11, 6), sharex=True)
    ax[0].plot(tt, yol, "b", label="kuşbakışı zemin ölçümü (h=0,17 m), birikimli ileri yol")
    ax[0].plot(tt, ynom, "k--", label="komut x 0,55 m/sn (SİM varsayımı)")
    ax[0].axhline(2.0, color="r", ls=":", label="gerçek: bantlar arası 2,00 m")
    ax[0].set_ylabel("ileri yol (m)")
    ax[0].legend(fontsize=8)
    ax[0].grid(alpha=0.3)
    ax[0].set_title("olcum_ham, ilk düz gidiş: ölçülen yol / 2,00 m", fontsize=10)
    ax[1].plot(tt, kg[:, 0], label="sol")
    ax[1].plot(tt, kg[:, 1], label="sağ")
    ax[1].plot(tt, ok * 50 - 300, "g", lw=0.6, label="B geçerli")
    ax[1].legend(fontsize=7)
    ax[1].set_ylabel("PWM")
    ax[1].set_xlabel("zaman (sn)")
    fig.tight_layout()
    fig.savefig(os.path.join(CIKTI, "ilk_duz_gidis.png"), dpi=105)
    plt.close(fig)
    json.dump(sonuc, open(os.path.join(CIKTI, "ilk_duz_gidis.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    return sonuc


def don():
    k, t, kom = yukle()
    z = np.load(os.path.join(go.onbellek("gercek"), "yon_komutsuz_gercek_%s.npz" % AD))
    y = np.degrees(np.unwrap(np.radians(z["yon"])))
    ic = z["icerik"]
    out = {}
    for ad, (a, b) in {"yerinde_150_165": (148, 167), "yerinde_175_182": (174, 184), "tur_22_83": (22, 84), "tum": (0, t[-1])}.items():
        m = (t >= a) & (t <= b)
        i0, i1 = np.where(m)[0][[0, -1]]
        out[ad] = dict(yaw_basi=float(y[i0]), yaw_sonu=float(y[i1]), net=float(y[i1] - y[i0]), toplam_mutlak=float(np.abs(np.diff(y[i0:i1 + 1])).sum()),
                       ic_ort=float(ic[m][ic[m] > 0].mean()) if (ic[m] > 0).any() else 0)
        print("%-16s net dönüş %8.1f derece (toplam |dönüş| %7.1f), iç nokta ort %.0f" % (ad, out[ad]["net"], out[ad]["toplam_mutak"] if False else out[ad]["toplam_mutlak"], out[ad]["ic_ort"]))
    # donus hizi - PWM: yerinde donus kareleri (|sol+sag|<60, |sol-sag|>100), 3 kare gecikmeli komut; 8 karelik pencereler
    kg = np.vstack([np.zeros((3, 2)), kom[:-3]])
    pen = 8
    satir = []
    for i in range(pen, len(t)):
        w = kg[i - pen:i + 1]
        if (np.abs(w[:, 0] + w[:, 1]) < 70).all() and (np.abs(w[:, 0] - w[:, 1]) > 100).all() and np.sign(w[:, 0] - w[:, 1]).std() < 0.01 and (w.max(0) - w.min(0)).max() < 40:
            satir.append(((y[i] - y[i - pen]) / (t[i] - t[i - pen]), float(np.abs(w[:, 0] - w[:, 1]).mean() / 2), float(np.sign(w[:, 0].mean() - w[:, 1].mean()))))
    s = np.array(satir)
    if len(s):
        for lo, hi in ((50, 90), (90, 110), (110, 140), (140, 256)):
            m = (s[:, 1] >= lo) & (s[:, 1] < hi)
            if m.sum() > 3:
                print("   yerinde dönüş |PWM|(her tekerlek) %d-%d: ölçülen %.1f derece/sn (işaretli ort %.1f, n=%d pencere)" % (lo, hi, np.abs(s[m, 0]).mean(), np.mean(s[m, 0] * s[m, 2]), m.sum()))
                out["don_hizi_%d_%d" % (lo, hi)] = dict(mutlak=float(np.abs(s[m, 0]).mean()), n=int(m.sum()), pwm=float(s[m, 1].mean()))
    fig, ax = plt.subplots(2, 1, figsize=(11, 6), sharex=True)
    ax[0].plot(t, y, label="M2D birikimli yön (komutsuz)")
    ax[0].set_ylabel("derece")
    ax[0].grid(alpha=0.3)
    ax[0].legend(fontsize=8)
    ax[1].plot(t, kom[:, 0], label="sol")
    ax[1].plot(t, kom[:, 1], label="sağ")
    ax[1].set_ylabel("PWM")
    ax[1].set_xlabel("zaman (sn)")
    ax[1].legend(fontsize=7)
    fig.suptitle("olcum_ham: M2D yönü ve motor (tüm kayıt)", fontsize=10)
    fig.tight_layout()
    fig.savefig(os.path.join(CIKTI, "yon_zaman.png"), dpi=105)
    plt.close(fig)
    json.dump(out, open(os.path.join(CIKTI, "yon.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)


if __name__ == "__main__":
    {"ilk": ilk, "don": don}[sys.argv[1]]()
