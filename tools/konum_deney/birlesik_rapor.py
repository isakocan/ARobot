"""E, A, C, D karşılaştırma tablosu + kaynak güven oranları + konum güveni analizi + grafikler.

Kullanım:
  python birlesik_rapor.py --kume gel sin --cikti gelistirme      (15 geliştirme kaydı; A/C/D mevcut tahmin CSV'lerinden okunur)
  python birlesik_rapor.py --kume sin2 --cikti sinama2            (taze held-out; A/C/D birlesik_sinama2.py abcd ile üretilmiş olmalı)
Çıktı: kayitlar/sonuclar/birlesik/<cikti>/  tablo.md, tablo.csv, sonuc.json, guven_*.png, zaman_*.png, kaynak_*.csv"""
import argparse
import csv
import json
import os
import sys
import time

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import birlesik_ortak as bo  # noqa: E402
import birlesik_degerlendir as bd  # noqa: E402
import birlesik_kestirici as bk  # noqa: E402
import konum_ortak as ko  # noqa: E402
import harita_olc as ho  # noqa: E402
from kayit import Kayit  # noqa: E402

KUME = {"gel": bo.GEL, "sin": bo.SIN, "sin2": bo.SIN2}


def harita_olc_E(g, iz, tar_yolu):
    """E'nin kendi pozlarıyla kurulmuş harita (iz) için konum_degerlendir.kos ile aynı harita ölçütleri + bilinen-poz referansı."""
    import cv2
    import konum_slam as ks
    import harita_kur as hk
    tar = bo.npz(tar_yolu)
    menzil = float(tar["max_menzil"])
    gk = g.gk
    satir = {int(k): i for i, k in enumerate(gk["kare"])}
    ref = hk.Izgara.gercekten(g.s["sim"])
    for j, kr in enumerate(tar["kare"]):
        i = satir[int(kr)]
        ref.tara_ekle(gk["x_m"][i], gk["y_m"][i], gk["yon_derece"][i], tar["mesafe"][j], tar["guven"][j], menzil, guven_min=0.2)
    T = cv2.imread(os.path.join(g.s["sim"], "gercek_harita.png"), cv2.IMREAD_GRAYSCALE)
    ii = [satir[int(kr)] for kr in tar["kare"]]
    kon = np.stack([gk["x_m"][ii], gk["y_m"][ii], gk["yon_derece"][ii]], 1)
    R = ho.gozlenebilir_bolge(T, ref, kon, 4.0)
    se = ho.olc(T, ks.izgara_kirp(iz), R, 0.05)
    sk = ho.olc(T, ref.durum(), R, 0.05)
    return se, sk


def tablo_oku(kume):
    """A/C/D için kayıtlı harita ölçütleri (konum_degerlendir tablo.csv)."""
    yol = {"gel": os.path.join(bo.SONUC, "konum", "tablo.csv"), "sin": os.path.join(bo.SONUC, "sinama", "konum", "tablo.csv"),
           "sin2": os.path.join(bo.CIKTI, "sinama2", "konum", "tablo.csv")}[kume]
    out = {}
    if os.path.exists(yol):
        with open(yol, encoding="utf-8") as f:
            for r in csv.DictReader(f):
                if r.get("girdi", "theta") == "theta":
                    out[(r["varyant"], r["kayit"])] = r
    return out


def kume_adi(ad):
    return bo.spec(ad)["kume"]


def f(v, nd=3):
    return "-" if v is None or (isinstance(v, float) and not np.isfinite(v)) else ("%.*f" % (nd, v)).replace(".", ",")


def ana():
    ap = argparse.ArgumentParser()
    ap.add_argument("--kume", nargs="*", default=["gel", "sin"])
    ap.add_argument("--cikti", default="gelistirme")
    ap.add_argument("--par", nargs="*", default=[])
    a = ap.parse_args()
    par = bk.Param(**{k: float(v) for k, v in (x.split("=") for x in a.par)})
    adlar = sum([KUME[k] for k in a.kume], [])
    cikti = os.path.join(bo.CIKTI, a.cikti)
    os.makedirs(cikti, exist_ok=True)
    tablolar = {k: tablo_oku(k) for k in set(kume_adi(ad) for ad in adlar)}
    satirlar = []
    seriler = {}
    guven_X, guven_y, guven_id = [], [], []
    for ad in adlar:
        g = bd.Girdi(ad)
        P, seri, ms, iz, log = bd.kos_E(g, par, harita=True)
        r = bd.metrik(g, P)
        se, sk = harita_olc_E(g, iz, g.s["tar"])
        v = ko.nominal_hizlar(g.kay.bilgi)[0] * (g.komut[:, 0] + g.komut[:, 1]) / 2 / 255
        hareket = np.abs(v) > 0.05
        yol = r.get("gidilen_mesafe_m", 0.0)
        kume = g.s["kume"]
        rows = {"E": r}
        for vv in "ACD":
            q = bd.a_c_d_oku(ad, vv)
            if q is not None:
                rows[vv] = q
        for vv, q in rows.items():
            t = tablolar[kume].get((vv, ad))
            if vv == "E":
                iou, ref_iou = se["bos_iou"], sk["bos_iou"]
                kab = "-"
            else:
                iou = float(t["harita_bos_iou"]) if t and t.get("harita_bos_iou") not in (None, "") else None
                ref_iou = float(t["referans_bos_iou"]) if t and t.get("referans_bos_iou") not in (None, "") else None
            satirlar.append(dict(varyant=vv, kayit=ad, kume=kume, yol_m=yol, rmse=q["konum_rmse_m"], maks=q["konum_maks_m"],
                                 son=q["konum_son_m"], son_yuzde=(100 * q["konum_son_m"] / yol if yol > 0.3 else None),
                                 rmse_yuzde=(100 * q["konum_rmse_m"] / yol if yol > 0.3 else None),
                                 yaw_ort=q["yaw_ort_mutlak_derece"], yaw_maks=q["yaw_maks_derece"], harita_iou=iou, ref_iou=ref_iou))
        # kaynak güven oranları (hareketli karelerde)
        kay_say = dict(kayit=ad, kume=kume, kare=g.n, hareketli=int(hareket.sum()),
                       bev_kullan=float(seri["bev_kullan"][hareket].mean()) if hareket.any() else None,
                       komut_kaynak=float(((seri["bev_kullan"] == 0) & (seri["komut_sifir"] == 0))[hareket].mean()) if hareket.any() else None,
                       kare_red=float(seri["kare_red"][hareket].mean()) if hareket.any() else None,
                       bev_ok=float(g.bz["ok"][hareket].mean()) if hareket.any() else None,
                       pencere_guven=float(seri["guven_b"][hareket].mean()) if hareket.any() else None,
                       yaw_komut=float(seri["yaw_komut"].mean()), k_son=float(seri["k"][-1]),
                       ms_E_ort=float(ms.mean()), ms_E_p95=float(np.percentile(ms, 95)))
        seriler[ad] = dict(hata=r["_hata"], yhata=r["_yhata"], sigma=seri["sigma"], guven=seri["guven"], t=(g.t_ms - g.t_ms[0]) / 1000.0,
                           bev=seri["bev_kullan"], P=P, yol=np.r_[0, np.cumsum(np.hypot(np.diff(r["_hizali"]["gx"]), np.diff(r["_hizali"]["gy"])))],
                           ac={vv: rows[vv]["_hata"] for vv in "ACD" if vv in rows}, kay=kay_say)
        guven_X.append(seri["sigma"]); guven_y.append(r["_hata"]); guven_id += [ad] * g.n
        print("%-24s E RMSE %.3f maks %.3f son %.3f | yaw %.2f/%.2f | bev_kullan %.0f%% | IoU %.2f (bilinen %.2f)" % (
            ad, r["konum_rmse_m"], r["konum_maks_m"], r["konum_son_m"], r["yaw_ort_mutlak_derece"], r["yaw_maks_derece"],
            100 * (kay_say["bev_kullan"] or 0), se["bos_iou"], sk["bos_iou"]), flush=True)
        seriler[ad]["kay"]["kay_say"] = None
    # ---- tablolar
    alanlar = ["varyant", "kayit", "kume", "yol_m", "rmse", "maks", "son", "son_yuzde", "rmse_yuzde", "yaw_ort", "yaw_maks", "harita_iou", "ref_iou"]
    with open(os.path.join(cikti, "tablo.csv"), "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(alanlar)
        for s in satirlar:
            w.writerow([s.get(k, "") for k in alanlar])
    md = ["| kayıt | yöntem | yol m | RMSE m | en çok m | uç nokta m | uç %yol | RMSE %yol | yaw ort/en çok ° | harita IoU (bilinen) |", "|---|---|---|---|---|---|---|---|---|---|"]
    for ad in adlar:
        for s in [x for x in satirlar if x["kayit"] == ad]:
            md.append("| %s | %s | %s | %s | %s | %s | %s | %s | %s / %s | %s (%s) |" % (
                ad if s["varyant"] == "A" else "", s["varyant"], f(s["yol_m"], 1), f(s["rmse"]), f(s["maks"]), f(s["son"]),
                f(s["son_yuzde"], 1), f(s["rmse_yuzde"], 1), f(s["yaw_ort"], 2), f(s["yaw_maks"], 2), f(s["harita_iou"], 2), f(s["ref_iou"], 2)))
    with open(os.path.join(cikti, "tablo.md"), "w", encoding="utf-8") as fh:
        fh.write("\n".join(md) + "\n")
    # ---- kaynak güven oranları
    ks = [seriler[ad]["kay"] for ad in adlar]
    with open(os.path.join(cikti, "kaynak_oranlari.csv"), "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        kl = [k for k in ks[0].keys() if k != "kay_say"]
        w.writerow(kl)
        for k in ks:
            w.writerow([k.get(x, "") for x in kl])
    # ---- güven analizi
    X = np.concatenate(guven_X)
    y = np.concatenate(guven_y)
    ids = np.array(guven_id)
    from scipy.stats import spearmanr
    from sklearn.metrics import roc_auc_score
    ga = {"n_kare": int(len(y))}
    if len(y) > 10 and np.std(y) > 0:
        ga["spearman_sigma_hata"] = float(spearmanr(X, y)[0])
        for esik in (0.10, 0.15, 0.30):
            lab = y > esik
            ga["auc_hata>%.2f" % esik] = float(roc_auc_score(lab, X)) if 0 < lab.sum() < len(lab) else None
            ga["oran_hata>%.2f" % esik] = float(lab.mean())
        yol_all = np.concatenate([seriler[ad]["yol"] for ad in adlar])
        ga["spearman_yol_hata"] = float(spearmanr(yol_all, y)[0])
        lab = y > 0.15
        ga["auc_yol_tabani_hata>0.15"] = float(roc_auc_score(lab, yol_all)) if 0 < lab.sum() < len(lab) else None
        # kayıt içi (kayıt başına) Spearman
        ga["kayit_ici_spearman"] = {ad: float(spearmanr(seriler[ad]["sigma"], seriler[ad]["hata"])[0]) for ad in adlar if np.std(seriler[ad]["hata"]) > 0}
        # kalibrasyon: sigma dilimi -> ortalama gerçek hata
        qs = np.quantile(X, [0, .2, .4, .6, .8, 1.0])
        ga["kalibrasyon"] = [dict(sigma_araligi=[float(qs[i]), float(qs[i + 1])], ort_sigma=float(X[(X >= qs[i]) & (X <= qs[i + 1])].mean()),
                                  ort_hata=float(y[(X >= qs[i]) & (X <= qs[i + 1])].mean()), p90_hata=float(np.percentile(y[(X >= qs[i]) & (X <= qs[i + 1])], 90))) for i in range(5)]
    with open(os.path.join(cikti, "sonuc.json"), "w", encoding="utf-8") as fh:
        json.dump(dict(satirlar=satirlar, kaynak=ks, guven=ga), fh, indent=1, ensure_ascii=False, default=float)
    print("\n".join(md))
    print(json.dumps(ga, indent=1, ensure_ascii=False, default=float))
    # ---- grafikler
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        fig, ax = plt.subplots(1, 2, figsize=(12, 4.6))
        ax[0].scatter(X, y, s=2, alpha=0.25, color="#1f5fa8")
        mx = max(X.max(), y.max())
        ax[0].plot([0, mx], [0, mx], "--", color="#888", lw=1, label="sigma = hata")
        ax[0].set_xlabel("tahmin edilen belirsizlik sigma (m)")
        ax[0].set_ylabel("gerçek konum hatası (m)")
        ax[0].set_title("Konum güveni: sigma - hata (%d kare)" % len(y))
        ax[0].legend()
        ax[0].grid(alpha=0.3)
        if "kalibrasyon" in ga:
            c = ga["kalibrasyon"]
            xs = [q["ort_sigma"] for q in c]
            ax[1].plot(xs, [q["ort_hata"] for q in c], "o-", color="#1f5fa8", label="ortalama hata")
            ax[1].plot(xs, [q["p90_hata"] for q in c], "s--", color="#d1342f", label="%90 hata")
            ax[1].plot(xs, xs, ":", color="#888", label="sigma")
            ax[1].set_xlabel("ortalama sigma (beşte birlik dilim, m)")
            ax[1].set_ylabel("hata (m)")
            ax[1].set_title("Kalibrasyon")
            ax[1].legend()
            ax[1].grid(alpha=0.3)
        fig.tight_layout()
        fig.savefig(os.path.join(cikti, "guven_sigma_hata.png"), dpi=110)
        plt.close(fig)
        for ad in adlar:
            sr = seriler[ad]
            fig, ax = plt.subplots(2, 1, figsize=(9, 5.4), sharex=True)
            for vv, c in (("A", "#d98c1f"), ("C", "#7a4fa8"), ("D", "#2a9d8f")):
                if vv in sr["ac"]:
                    ax[0].plot(sr["t"], sr["ac"][vv], color=c, lw=1, label=vv)
            ax[0].plot(sr["t"], sr["hata"], color="#d1342f", lw=2, label="E")
            ax[0].set_ylabel("konum hatası (m)")
            ax[0].set_title(ad)
            ax[0].legend(ncol=4)
            ax[0].grid(alpha=0.3)
            ax[1].plot(sr["t"], sr["guven"], color="#1f5fa8", label="konum güveni")
            ax[1].fill_between(sr["t"], 0, sr["bev"], color="#2a9d8f", alpha=0.25, step="pre", label="B kullanılan kare")
            ax[1].set_xlabel("zaman (sn)")
            ax[1].set_ylabel("güven / B")
            ax[1].legend(ncol=2)
            ax[1].grid(alpha=0.3)
            fig.tight_layout()
            fig.savefig(os.path.join(cikti, "zaman_%s.png" % ad), dpi=100)
            plt.close(fig)
    except Exception as e:  # noqa: BLE001
        print("grafik hatası:", e)


if __name__ == "__main__":
    ana()
