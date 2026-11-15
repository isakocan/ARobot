"""Doluluk haritası: ESKİ geometri (kamera.json) ile KALİBRE geometri (kamera_gercek.json + sabit maske) yan yana, AYNI konum kaynağıyla.
  python gercek_e2_harita.py [kayit ...]
Satır 1: konum = E (aynı koşu), sol eski geometri, sağ kalibre geometri.  Satır 2: konum = E2, sol eski, sağ kalibre.
Her harita için duvar tutarlılığı: dünya koordinatındaki tüm sınır noktaları (her taramanın "dolu" ışın uçları) üstünde sıralı RANSAC ile en baskın 3 doğru
(inlier eşiği 0,06 m, en az 80 nokta; menzil kenarındaki ufuk halkası ve 0,5 m'den yakın noktalar hariç), doğru başına kuşaktaki (±0,15 m) noktaların dik uzaklık sapması (MAD*1,4826, m), kuşak nokta sayısı ve toplam sınır noktasına oranı.
Çıktı: kayitlar/sonuclar/gercek/e2/harita_once_sonra_<kayit>.png, harita_sayilar.json."""
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
import harita_kur as hk  # noqa: E402

plt.rcParams["font.family"] = "DejaVu Sans"
E2D = os.path.join(go.SONUC, "e2")
TAR = {"eski": os.path.join(go.SONUC, "harita_gercek", "tarama", "model_gorunum", "theta"),
       "kalib": os.path.join(go.SONUC, "harita_gercek_kalibS", "tarama", "model_gorunum", "theta")}
ADLAR = ["5_saksi_turu", "olcum_ham", "2a_vitessiz_duz"]
X0, Y0, W, H, HUC = -20.0, -20.0, 800, 800, 0.05
GUVEN_MIN = 0.2


def kos_oku(ad):
    return np.load(os.path.join(E2D, "kos", ad + ".npz"))


def harita_kur(P, tar, kare_no):
    iz = hk.Izgara(X0, Y0, W, H, HUC)
    sira = {int(k): i for i, k in enumerate(tar["kare"])}
    menzil = float(tar["max_menzil"])
    nokta = []
    for i in range(len(P)):
        j = sira.get(int(kare_no[i]))
        if j is None:
            continue
        iz.tara_ekle(P[i, 0], P[i, 1], P[i, 2], tar["mesafe"][j], tar["guven"][j], menzil, guven_min=GUVEN_MIN)
        mes, gv = np.asarray(tar["mesafe"][j], float), np.asarray(tar["guven"][j], float)
        k = np.arange(360)
        ok = np.isfinite(mes) & (mes > 0.5) & (mes <= menzil - 0.12) & (gv >= GUVEN_MIN)      # menzil kenarındaki ufuk halkası duvar sayılmaz
        if ok.any():
            teta = np.radians(P[i, 2] + k[ok])
            nokta.append(np.c_[P[i, 0] + np.sin(teta) * mes[ok], P[i, 1] + np.cos(teta) * mes[ok]])
    nokta = np.vstack(nokta) if nokta else np.zeros((0, 2))
    return iz, nokta, menzil


def duvarlar(nokta, n_dogru=3, esik=0.06, kusak=0.15, rng_tohum=3):
    """Sıralı RANSAC doğrular; döndürür [(sapma_mad_m, n_kusak, uzunluk_m, aci_derece)]."""
    rng = np.random.RandomState(rng_tohum)
    kalan = nokta.copy()
    out = []
    for _ in range(n_dogru):
        if len(kalan) < 200:
            break
        ornek = kalan[rng.choice(len(kalan), min(6000, len(kalan)), replace=False)]
        en, ed = 0, None
        for _t in range(400):
            a, b = ornek[rng.choice(len(ornek), 2, replace=False)]
            d = b - a
            L = np.hypot(*d)
            if L < 0.5:
                continue
            nrm = np.array([-d[1], d[0]]) / L
            say = int((np.abs((ornek - a) @ nrm) < esik).sum())
            if say > en:
                en, ed = say, (a, nrm)
        if ed is None or en < 80:
            break
        a, nrm = ed
        # tüm noktalarda doğruyu yeniden uydur (inlier'lar üzerinde PCA)
        ic = np.abs((kalan - a) @ nrm) < esik
        q = kalan[ic]
        m = q.mean(0)
        u, s, vt = np.linalg.svd(q - m, full_matrices=False)
        yon = vt[0]
        nrm = np.array([-yon[1], yon[0]])
        dik = (kalan - m) @ nrm
        boy = (kalan - m) @ yon
        bant = np.abs(dik) < kusak
        # doğru boyunca uzunluk: inlier boy aralığının 2-98 yüzdelik farkı
        bi = boy[np.abs(dik) < esik]
        out.append(dict(sapma=float(1.4826 * np.median(np.abs(dik[bant] - np.median(dik[bant])))), n=int(bant.sum()), uzunluk=float(np.percentile(bi, 98) - np.percentile(bi, 2)),
                        aci=float(np.degrees(np.arctan2(yon[1], yon[0])) % 180)))
        out[-1]["oran"] = float(bant.sum()) / len(nokta)
        kalan = kalan[~bant]
    return out


def durum_img(lo):
    d = np.full(lo.shape, 128, np.uint8)
    d[lo <= -0.4] = 255
    d[lo >= 0.4] = 0
    return d


def ciz_harita(ax, iz, P, baslik, kutu=None):
    D = durum_img(iz.lo)
    gor = np.argwhere(D != 128)
    if kutu is None:
        r0, c0 = gor.min(0)
        r1, c1 = gor.max(0)
        kutu = (X0 + c0 * HUC - 0.5, X0 + (c1 + 1) * HUC + 0.5, Y0 + (H - 1 - r1) * HUC - 0.5, Y0 + (H - r0) * HUC + 0.5)
    x0, x1, y0, y1 = kutu
    c0, c1 = int((x0 - X0) / HUC), int((x1 - X0) / HUC)
    r0, r1 = int(H - 1 - (y1 - Y0) / HUC), int(H - 1 - (y0 - Y0) / HUC)
    c0, r0 = max(c0, 0), max(r0, 0)
    c1, r1 = min(c1, W - 1), min(r1, H - 1)
    crop = D[r0:r1 + 1, c0:c1 + 1]
    ext = (X0 + c0 * HUC, X0 + (c1 + 1) * HUC, Y0 + (H - 1 - r1) * HUC, Y0 + (H - r0) * HUC)
    ax.imshow(crop, cmap="gray", vmin=0, vmax=255, extent=ext, origin="upper", interpolation="nearest")
    ax.plot(P[:, 0], P[:, 1], "-", color="tab:red", lw=0.7)
    ax.plot(P[0, 0], P[0, 1], "o", color="tab:green", ms=6)
    ax.plot(P[-1, 0], P[-1, 1], "s", color="tab:blue", ms=5)
    ax.set_xlim(x0, x1)
    ax.set_ylim(y0, y1)
    ax.set_aspect("equal")
    ax.set_title(baslik, fontsize=7)
    ax.set_xlabel("x (m)", fontsize=8)
    ax.tick_params(labelsize=7)
    # ölçek çubuğu 1 m
    bx, by = x0 + 0.1 * (x1 - x0), y0 + 0.06 * (y1 - y0)
    ax.plot([bx, bx + 1.0], [by, by], "-", color="tab:orange", lw=3)
    ax.text(bx + 0.5, by + 0.04 * (y1 - y0), "1 m", color="tab:orange", fontsize=8, ha="center", fontweight="bold")
    return D, kutu


def ana(adlar):
    sayilar = {}
    from kayit import Kayit
    for ad in adlar:
        pe = os.path.join(E2D, "kos", ad + ".npz")
        if not os.path.exists(pe):
            print("kos yok", ad)
            continue
        z = kos_oku(ad)
        kay = Kayit(os.path.join(go.GERCEK, ad))
        kare_no = kay.kare_numaralari
        tars = {}
        for g in ("eski", "kalib"):
            p = os.path.join(TAR[g], ad + ".npz")
            tars[g] = np.load(p) if os.path.exists(p) else None
        fig, ax = plt.subplots(2, 2, figsize=(14, 12))
        sayilar[ad] = {}
        for ri, (kad, P) in enumerate((("E", z["E_P"]), ("E2", z["E2_P"]))):
            kutu = None
            for ci, g in enumerate(("eski", "kalib")):
                if tars[g] is None:
                    ax[ri, ci].set_title("%s geometri: tarama yok (ölçülemedi)" % g)
                    continue
                iz, nok, menzil = harita_kur(P, tars[g], kare_no)
                dv = duvarlar(nok)
                D = durum_img(iz.lo)
                bil = float((D != 128).sum()) * HUC * HUC
                dolu = float((D == 0).sum()) * HUC * HUC
                sap = [d["sapma"] for d in dv]
                bas = "%s konumu, %s geometri\nbilinen alan %.1f m², dolu %.2f m², sınır noktası %d, menzil %.2f m\nduvar sapması (MAD, m): %s" % (
                    kad, "ESKİ (kamera.json)" if g == "eski" else "KALİBRE + sabit maske", bil, dolu, len(nok), menzil, ", ".join("%.3f (n=%d, %%%.0f)" % (d["sapma"], d["n"], 100 * d["oran"]) for d in dv) or "doğru bulunamadı")
                D, kutu = ciz_harita(ax[ri, ci], iz, P, bas, kutu)
                sayilar[ad]["%s_%s" % (kad, g)] = dict(bilinen_m2=bil, dolu_m2=dolu, nokta=int(len(nok)), menzil=menzil, duvarlar=dv,
                                                       medyan_sapma=float(np.median(sap)) if sap else None)
        fig.suptitle("%s: doluluk haritası, eski ve kalibre kamera geometrisi (beyaz boş, siyah dolu, gri bilinmiyor; kırmızı yol, yeşil başlangıç, mavi bitiş)" % ad, fontsize=10, y=0.995)
        fig.tight_layout(rect=(0, 0, 1, 0.97))
        fig.savefig(os.path.join(E2D, "harita_once_sonra_%s.png" % ad), dpi=95)
        plt.close(fig)
        for k, v in sayilar[ad].items():
            print(ad, k, "bilinen %.1f dolu %.2f nokta %d medyan sapma %s" % (v["bilinen_m2"], v["dolu_m2"], v["nokta"], v["medyan_sapma"]), [round(d["sapma"], 3) for d in v["duvarlar"]], flush=True)
    json.dump(sayilar, open(os.path.join(E2D, "harita_sayilar.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)


if __name__ == "__main__":
    ana(sys.argv[1:] or ADLAR)
