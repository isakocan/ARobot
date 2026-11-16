"""SLAM çıktısı (kopru toplu kip: poz.jsonl + harita.json) değerlendirme, ölçek ve grafikler.
Dünya çerçevesi stella_vslam: kamera x sağ, y aşağı, z ileri (ilk anahtar kare = dünya). Kare yerçekimine hizalı olduğundan yer düzlemi ~ y = sabit.
Kullanım: python slam_degerlendir.py <sonuc_klasoru> <kayit_klasoru> <ad> [--bas_t 3.4] ...
Yer gerçeği (kullanıcı, olcum_ham): ilk düz gidiş 3,4-14,4 sn, 14,4'te ön kenar 2,00 m ilerideki bantta; tur 22,8-82,4 sn, 82,4'te başlangıca dönüş (<=0,02 m)."""
import argparse
import json
import os
import sys

import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(KLASOR, "..", "konum_deney"))
from kayit import Kayit  # noqa: E402

H_KAM = 0.17
T_BAS, T_BANT, T_TUR_BAS, T_TUR_SON = 3.4, 14.4, 22.8, 82.4


def yukle(sonuc):
    P = []
    for s in open(os.path.join(sonuc, "poz.jsonl"), encoding="utf-8"):
        d = json.loads(s)
        P.append(d)
    n = len(P)
    t = np.array([d["t_ms"] for d in P]) / 1000.0
    izl = np.array([bool(d.get("izleniyor")) for d in P])
    durum = [d["durum"] for d in P]
    Rwc = np.full((n, 3, 3), np.nan)
    c = np.full((n, 3), np.nan)
    for i, d in enumerate(P):
        if d.get("poz_cw") is not None and izl[i]:
            T = np.array(d["poz_cw"]).reshape(4, 4)     # alan adı yanıltıcı: gerçekte T_wc (kamera->dünya)
            Rwc[i] = T[:3, :3]
            c[i] = T[:3, 3]
    with open(os.path.join(sonuc, "harita.json")) as f:
        hm = json.load(f)
    return dict(P=P, t=t, izl=izl, durum=durum, Rwc=Rwc, c=c, noktalar=np.array(hm["noktalar"]), kf=hm["anahtar_kareler"])


def yon_derece(R):
    f = R @ np.array([0, 0, 1.0])
    return np.degrees(np.arctan2(f[0], f[2]))


def at_t(D, ts):
    """en yakın izlenen kare indeksi"""
    ok = np.where(D["izl"])[0]
    return ok[np.argmin(np.abs(D["t"][ok] - ts))]


def zemin_olcegi(D, esik_m=None, yineleme=3000, rng=np.random.default_rng(0)):
    """Kamera merkezlerinin altındaki harita noktalarına RANSAC düzlem; normal y eksenine <=15 derece yakın.
    Dönüş: dict(olcek, yukseklik_birimi, ic_sayisi, ...) veya None. Ölçek = 0,17 / (kamera-düzlem uzaklığı)."""
    X = D["noktalar"][:, :3]
    C = D["c"][D["izl"]]
    ort_y = np.median(C[:, 1])
    # kameranın aşağısındaki noktalar (y büyük = aşağı); yatay uzaklık sınırı yok
    alt = X[X[:, 1] > ort_y]
    if len(alt) < 30:
        return None
    # yükseklik (y) histogramı: yerden yansıyan nokta öbeği tepe verir. RANSAC düzlem: y = a x + b z + d
    en_iyi = None
    esik = esik_m if esik_m else 0.05 * (np.median(alt[:, 1]) - ort_y)   # göreli: ölçeksiz birim
    for _ in range(yineleme):
        idx = rng.choice(len(alt), 3, replace=False)
        p = alt[idx]
        n = np.cross(p[1] - p[0], p[2] - p[0])
        nn = np.linalg.norm(n)
        if nn < 1e-9:
            continue
        n = n / nn
        if n[1] < 0:
            n = -n
        if n[1] < np.cos(np.radians(15)):
            continue
        d = alt @ n - p[0] @ n
        ic = np.abs(d) < esik
        sc = ic.sum()
        if en_iyi is None or sc > en_iyi[0]:
            en_iyi = (sc, n, p[0] @ n, ic)
    if en_iyi is None:
        return None
    sc, n, off, ic = en_iyi
    # içlerle yeniden uydur (SVD)
    Q = alt[ic]
    cen = Q.mean(0)
    u, s, vt = np.linalg.svd(Q - cen)
    n = vt[2]
    if n[1] < 0:
        n = -n
    off = cen @ n
    mesafe = (C @ n) - off          # işaretli: kamera düzlemin "üst" tarafında (n aşağı doğru => negatif)
    h_birim = np.abs(np.median(mesafe))
    return dict(olcek=float(H_KAM / h_birim), yukseklik_birim=float(h_birim), ic_sayisi=int(sc), aday=int(len(alt)),
                normal=n.tolist(), egim_derece=float(np.degrees(np.arccos(np.clip(n[1], -1, 1)))), esik=float(esik),
                mesafe_std=float(np.std(mesafe)), duzlem_off=float(off), ic_maske_alt=ic)


def olcek_bant(D):
    """Bant sınaması: 14,4 sn - 3,4 sn arası uzaklığı 2,00 m yapan ölçek (kamera yüksekliği ölçeğinden bağımsız ikinci yol)."""
    i0, i1 = at_t(D, max(T_BAS, D["t"][D["izl"]][0])), at_t(D, T_BANT)
    d = np.linalg.norm(D["c"][i1] - D["c"][i0])
    return 2.0 / d, d


def ozet(D, olcek):
    t, c, R = D["t"], D["c"] * olcek, D["Rwc"]
    ok = np.where(D["izl"])[0]
    i0 = at_t(D, max(T_BAS, t[ok][0]))
    i1 = at_t(D, T_BANT)
    i2 = at_t(D, T_TUR_BAS)
    i3 = at_t(D, T_TUR_SON)
    hiz_ilk = float(np.linalg.norm(c[i1] - c[i0]))
    # düzlem hareketi: x-z (yerçekimine dik)
    c2 = c[:, [0, 2]]
    seg = np.diff(c2[ok], axis=0)
    yol = float(np.linalg.norm(seg, axis=1).sum())
    yol_tur = float(np.linalg.norm(np.diff(c2[ok[(t[ok] >= T_TUR_BAS) & (t[ok] <= T_TUR_SON)]], axis=0), axis=1).sum())
    ilk_poz_t = float(t[ok][0])
    # başlangıçta robot T_BAS'e kadar duruyordu; ilk poz o andan sonra olabilir
    sonuc = dict(
        ilk_izlenen_t=ilk_poz_t, bant_i0_t=float(t[i0]), bant_i1_t=float(t[i1]),
        bant_uzaklik_m=float(np.linalg.norm(c2[i1] - c2[i0])), bant_uzaklik_3B_m=hiz_ilk,
        tur_bas_son_uzaklik_m=float(np.linalg.norm(c2[i3] - c2[i0])), tur_bas_son_uzaklik_3B_m=float(np.linalg.norm(c[i3] - c[i0])),
        tur_i3_t=float(t[i3]),
        yon_kapanis_derece=float((yon_derece(R[i3]) - yon_derece(R[i0]) + 180) % 360 - 180),
        toplam_yol_m=yol, tur_yol_m=yol_tur,
        son_kare_baslangic_uzaklik_m=float(np.linalg.norm(c2[ok[-1]] - c2[i0])),
    )
    return sonuc


def kayip_analiz(D, kay):
    """Kayıp = 'Tracking' dışı durum (başlatma sonrası) ya da poz yok. Her kayıp aralığı için komutlar."""
    t, izl = D["t"], D["izl"]
    ilk = np.where(izl)[0][0]
    aralik = []
    i = ilk
    n = len(t)
    while i < n:
        if not izl[i]:
            j = i
            while j < n and not izl[j]:
                j += 1
            aralik.append((i, j - 1))
            i = j
        else:
            i += 1
    cikti = []
    for a, b in aralik:
        k = np.array([kay.komut(int(x)) for x in (D["t"][max(a - 3, 0):b + 1] * 1000)])
        cikti.append(dict(bas_t=float(t[a]), son_t=float(t[b]), kare=int(b - a + 1), komut_sol_medyan=float(np.median(k[:, 0])), komut_sag_medyan=float(np.median(k[:, 1])),
                          tur=bool(np.median(k[:, 0]) * np.median(k[:, 1]) < 0)))
    return cikti, aralik


def kare_basina_hareket(D, kay):
    """Atlanan/kayıp kare yok; başlatma sonrası izlenen oran."""
    ilk = np.where(D["izl"])[0][0]
    return float(D["izl"][ilk:].mean()), float(D["izl"].mean())


def grafik(D, olcek, zem, yol, ad, bant_isaret=True, baslik_ek=""):
    c = D["c"] * olcek
    t = D["t"]
    X = D["noktalar"][:, :3] * olcek
    fig, ax = plt.subplots(1, 2, figsize=(16, 8))
    for k, a in enumerate(ax):
        # üstten bakış: x (sağ) yatay, z (ileri) dikey
        yer = np.ones(len(X), bool)
        if k == 1 and zem is not None:
            yer = zem_maske(D, zem, olcek)
        a.scatter(X[yer, 0], X[yer, 2], s=2, c="0.65", alpha=0.6, linewidths=0, label="harita noktaları" if k == 0 else "zemin ve alt noktalar")
        ok = D["izl"]
        sc = a.scatter(c[ok, 0], c[ok, 2], s=3, c=t[ok], cmap="viridis")
        a.plot(c[ok, 0], c[ok, 2], "-", lw=0.4, c="k", alpha=0.4)
        i0 = at_t(D, max(T_BAS, t[ok][0])); i1 = at_t(D, T_BANT); i3 = at_t(D, T_TUR_SON)
        a.plot(*c[i0, [0, 2]], "o", c="red", ms=9, label="bant 1 (başlangıç, t=%.1f sn)" % t[i0])
        a.plot(*c[i1, [0, 2]], "s", c="orange", ms=9, label="bant 2 (t=14,4 sn, gerçek 2,00 m)")
        a.plot(*c[i3, [0, 2]], "*", c="magenta", ms=14, label="tur sonu (t=82,4 sn, gerçek ~0)")
        a.set_aspect("equal")
        a.set_xlabel("x (m, sağ)"); a.set_ylabel("z (m, ileri)")
        a.grid(alpha=0.3)
        if k == 0:
            a.legend(loc="best", fontsize=8)
            a.set_title("%s %s" % (ad, baslik_ek))
        else:
            a.set_title("yalnız yer düzlemine yakın/alt noktalar (varsa)")
        mx = np.nanmax(np.abs(c[ok][:, [0, 2]])) * 1.3 + 1
        a.set_xlim(np.nanmin(c[ok, 0]) - 3, np.nanmax(c[ok, 0]) + 3)
        a.set_ylim(np.nanmin(c[ok, 2]) - 3, np.nanmax(c[ok, 2]) + 3)
    fig.colorbar(sc, ax=ax, label="t (sn)", shrink=0.6)
    fig.savefig(yol, dpi=110, bbox_inches="tight")
    plt.close(fig)


def zem_maske(D, zem, olcek):
    X = D["noktalar"][:, :3]
    n = np.array(zem["normal"])
    d = X @ n - zem["duzlem_off"]
    return np.abs(d) < zem["esik"] * 3


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("sonuc")
    ap.add_argument("kayit")
    ap.add_argument("ad")
    ap.add_argument("--grafik_klasoru", default=None)
    a = ap.parse_args()
    D = yukle(a.sonuc)
    kay = Kayit(a.kayit)
    zem = zemin_olcegi(D)
    s_bant, d_bant = olcek_bant(D)
    out = dict(kare=len(D["t"]), izlenen=int(D["izl"].sum()))
    out["izlenen_oran_baslatma_sonrasi"], out["izlenen_oran_tum"] = kare_basina_hareket(D, kay)
    kayip, aralik = kayip_analiz(D, kay)
    out["kayip_sayisi"] = len(kayip)
    out["kayiplar"] = kayip
    out["anahtar_kare"] = len(D["kf"])
    out["harita_nokta"] = len(D["noktalar"])
    out["olcek_bant"] = s_bant
    out["bant_ham_uzaklik_birim"] = d_bant
    if zem:
        z = {k: v for k, v in zem.items() if k != "ic_maske_alt"}
        out["zemin"] = z
        out["ozet_kamera_yuksekligi_olcekli"] = ozet(D, zem["olcek"])
    out["ozet_bant_olcekli"] = ozet(D, s_bant)
    os.makedirs(a.grafik_klasoru, exist_ok=True) if a.grafik_klasoru else None
    if a.grafik_klasoru:
        if zem:
            grafik(D, zem["olcek"], zem, os.path.join(a.grafik_klasoru, a.ad + "_kamera_yuksekligi_olcegi.png"), a.ad, baslik_ek="(ölçek: kamera yüksekliği 0,17 m)")
        grafik(D, s_bant, zem, os.path.join(a.grafik_klasoru, a.ad + "_bant_olcegi.png"), a.ad, baslik_ek="(ölçek: bant 2,00 m)")
    print(json.dumps(out, indent=1, ensure_ascii=False))
    with open(os.path.join(a.sonuc, "degerlendirme.json"), "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1, ensure_ascii=False)
