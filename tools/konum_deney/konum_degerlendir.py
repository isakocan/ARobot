"""Varyantlar (A-D) x kayıtlar: poz CSV'leri, konum/yaw ölçütleri, döngü (yeniden ziyaret) sayıları, harita ölçütleri ve görüntüleri, tablo.

Kullanım:  python konum_degerlendir.py [--varyant A B C D] [--girdi theta sim] [--kayit ...]
Çıktı (kayitlar/sonuclar/konum/):
  tahmin/<varyant>_<girdi>_<kayit>.csv      kare,t_ms,x_m,y_m,yon_derece
  olcut/<varyant>_<girdi>_<kayit>/          olc.py'nin sonuc.json + grafik.png
  harita/<varyant>_<girdi>_<kayit>.png      gerçek | bilinen-poz haritası | tahmin-poz haritası (+ yörüngeler)
  tablo.csv, tablo.md, sonuc.json
Harita ölçütleri harita_olc.py ile (gözlenebilir bölge R gerçek yörüngeden), bilinen-poz haritası aynı taramalarla gerçek pozlardan."""
import argparse
import csv
import json
import os
import sys
import time

import cv2
import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import konum_ortak as ko  # noqa: E402
import konum_slam as ks  # noqa: E402
import konum_tarama as kt  # noqa: E402
import harita_kur as hk  # noqa: E402
import harita_olc as ho  # noqa: E402
import olc  # noqa: E402
from kayit import Kayit  # noqa: E402

CIKTI = ko.KONUM_CIKTI


def yeniden_ziyaret(gk, mesafe_m=0.35, en_az_yol_m=4.0):
    """Gerçek yörüngeden: robotun, aralarında en az en_az_yol_m yol gidilmiş eski bir konuma mesafe_m'den yaklaştığı İLK kare. None ya da kare sırası."""
    x, y = gk["x_m"], gk["y_m"]
    yol = np.r_[0.0, np.cumsum(np.hypot(np.diff(x), np.diff(y)))]
    for i in range(1, len(x)):
        j = np.searchsorted(yol, yol[i] - en_az_yol_m, side="right") - 1
        if j < 0:
            continue
        if np.hypot(x[:j + 1] - x[i], y[:j + 1] - y[i]).min() < mesafe_m:
            return i
    return None


def referans_harita(ad, tur, tar, menzil):
    """Bilinen (gerçek) pozlarla kurulan harita (aynı taramalar): karşılaştırma ölçüsü."""
    klasor = os.path.join(ko.KAYITLAR, "sim", ad)
    iz = hk.Izgara.gercekten(klasor)
    gk = Kayit(klasor).gercek_konum()
    satir = {int(k): i for i, k in enumerate(gk["kare"])}
    for j, kr in enumerate(tar["kare"]):
        i = satir[int(kr)]
        iz.tara_ekle(gk["x_m"][i], gk["y_m"][i], gk["yon_derece"][i], tar["mesafe"][j], tar["guven"][j], menzil, guven_min=0.2)
    return iz


def harita_goruntusu(T, Ek, Ee, R, gk_xy, tahmin_xy, iz_t, yol, baslik, olcek=4):
    """gerçek | bilinen-poz | tahmin-poz (yan yana). Yörünge: gerçek (yeşil) ve tahmin (kırmızı) her panelde ince çizgi."""
    H, W = T.shape

    def renk(M):
        im = np.full((H, W, 3), 200, np.uint8)
        im[M == 255] = (255, 255, 255)
        im[M == 0] = (30, 30, 30)
        return im
    paneller = []
    for M, ad in ((T, "gercek harita"), (Ek, "bilinen poz"), (Ee, "tahmin poz")):
        im = cv2.resize(renk(M), (W * olcek, H * olcek), interpolation=cv2.INTER_NEAREST)
        for xy, c in ((gk_xy, (60, 160, 40)), (tahmin_xy if M is Ee else None, (40, 40, 230))):
            if xy is not None:
                cc = (xy[:, 0] - iz_t.x0) / iz_t.h
                rr = iz_t.Hh - (xy[:, 1] - iz_t.y0) / iz_t.h
                pts = np.stack([cc * olcek, rr * olcek], 1).astype(np.int32)
                cv2.polylines(im, [pts], False, c, 1, cv2.LINE_AA)
        cv2.putText(im, ad, (6, 18), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 0, 0), 1, cv2.LINE_AA)
        paneller.append(im)
    ys, xs = np.where(T != 128)
    y0, y1, x0, x1 = max(ys.min() - 4, 0) * olcek, (ys.max() + 5) * olcek, max(xs.min() - 4, 0) * olcek, (xs.max() + 5) * olcek
    paneller = [p[y0:y1, x0:x1] for p in paneller]
    ara = np.full((paneller[0].shape[0], 4, 3), 255, np.uint8)
    out = np.hstack([paneller[0], ara, paneller[1], ara, paneller[2]])
    cv2.putText(out, baslik, (6, out.shape[0] - 8), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 0, 160), 1, cv2.LINE_AA)
    os.makedirs(os.path.dirname(yol), exist_ok=True)
    cv2.imwrite(yol, out)


def kos(varyant, tur, ad, par=None, resim=True, bev_olcek=1.0, etiket=None):
    etiket = etiket or varyant
    k = ks.Kosucu(ad, tur, varyant, par=par, bev_olcek=bev_olcek)
    t0 = time.time()
    P, iz, log = k.kos()
    sure = time.time() - t0
    yol = ks.tahmin_yol(etiket, tur, ad)
    ks.tahmin_yaz(yol, k.kay.kare_numaralari, k.kay.t_ms, P)
    r = olc.degerlendir(k.kay, yol, os.path.join(CIKTI, "olcut", "%s_%s_%s" % (etiket, tur, ad)), ad="%s %s %s" % (etiket, tur, ad))
    h = r["_hizali"]
    s = {"varyant": etiket, "girdi": tur, "kayit": ad, "kare": r["kare_sayisi"], "yol_m": r.get("gidilen_mesafe_m")}
    for a in ("konum_rmse_m", "konum_maks_m", "konum_son_m", "konum_son_yuzde_mesafe", "konum_rmse_yuzde_mesafe",
              "yaw_ort_mutlak_derece", "yaw_maks_derece", "yaw_surukleme_derece_360_basina"):
        s[a] = r.get(a)
    hata = np.hypot(h["tx"] - h["gx"], h["ty"] - h["gy"])
    s["_hata"] = hata
    # yeniden ziyaret
    gk = k.gk
    rv = yeniden_ziyaret(gk) if ad in ("salon_turu", "ev_turu", "duz_git") else None
    s["yeniden_ziyaret_kare"] = rv
    if rv is not None:
        s["hata_once_m"] = float(hata[max(rv - 45, 0)])
        s["hata_ziyaret_m"] = float(hata[rv])
        s["hata_sonra_m"] = float(hata[min(rv + 90, len(hata) - 1)])
        s["hata_sonra_maks_m"] = float(hata[rv:min(rv + 300, len(hata))].max())
    # harita
    menzil = k.menzil
    iz_k = referans_harita(ad, tur, k.tar, menzil)
    T = cv2.imread(os.path.join(ko.KAYITLAR, "sim", ad, "gercek_harita.png"), cv2.IMREAD_GRAYSCALE)
    satir = {int(kk): i for i, kk in enumerate(gk["kare"])}
    ii = [satir[int(kr)] for kr in k.tar["kare"]]
    kon = np.stack([gk["x_m"][ii], gk["y_m"][ii], gk["yon_derece"][ii]], 1)
    R = ho.gozlenebilir_bolge(T, iz_k, kon, 4.0)
    Ek = iz_k.durum()
    Ee = ks.izgara_kirp(iz)
    sk = ho.olc(T, Ek, R, 0.05)
    se = ho.olc(T, Ee, R, 0.05)
    for a in ("bos_iou", "dolu_kesinlik_1", "dolu_geri_cagirma_1", "tahmin_dolu_gercek_dolu_cm", "gercek_dolu_tahmin_dolu_cm", "bos_dolu_sanildi_tol1"):
        s["harita_" + a] = se[a]
        s["referans_" + a] = sk[a]
    if resim:
        harita_goruntusu(T, Ek, Ee, R, np.stack([gk["x_m"], gk["y_m"]], 1), P[:, :2], iz_k,
                         os.path.join(CIKTI, "harita", "%s_%s_%s.png" % (etiket, tur, ad)),
                         "%s %s %s  IoU bilinen %.2f tahmin %.2f" % (etiket, tur, ad, sk["bos_iou"], se["bos_iou"]))
    # süre
    yz = k.yz
    s["ms_m2d"] = float(np.mean(yz["ms"]))
    s["ms_bev"] = float(np.mean(k.bz["ms"])) if k.bz is not None else None
    s["ms_esle"] = float(np.mean(log["esle_ms"])) if len(log["esle_ms"]) else None
    s["ms_harita"] = float(np.mean(log["harita_ms"]))
    s["ms_seg"] = float(np.mean(k.tar["sure_ms"]))
    s["esle_kabul"] = "%d/%d" % (log["kabul"], log["deneme"])
    s["calisma_sn"] = sure
    return s, P, log


def f(v, nd=3):
    return "-" if v is None or (isinstance(v, float) and not np.isfinite(v)) else ("%.*f" % (nd, v)).replace(".", ",")


def tablo_yaz(satirlar, dosya_ek=""):
    os.makedirs(CIKTI, exist_ok=True)
    alanlar = [k for k in satirlar[0].keys() if not k.startswith("_")]
    for s in satirlar:
        for k in s:
            if not k.startswith("_") and k not in alanlar:
                alanlar.append(k)
    with open(os.path.join(CIKTI, "tablo%s.csv" % dosya_ek), "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(alanlar)
        for s in satirlar:
            w.writerow([s.get(a, "") for a in alanlar])
    md = ["| varyant | girdi | kayıt | yol m | RMSE m | en çok m | uç nokta m (%) | yaw ort/en çok ° | harita IoU (bilinen) | dolu K/G 1 hücre | duvar cm tah→gerç | düzeltme kabul |",
          "|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for s in satirlar:
        md.append("| %s | %s | %s | %s | %s | %s | %s (%s) | %s / %s | %s (%s) | %s / %s | %s | %s |" % (
            s["varyant"], s["girdi"], s["kayit"], f(s["yol_m"], 1), f(s["konum_rmse_m"]), f(s["konum_maks_m"]), f(s["konum_son_m"]),
            f(s["konum_son_yuzde_mesafe"], 1) + "%" if s["konum_son_yuzde_mesafe"] is not None else "-",
            f(s["yaw_ort_mutlak_derece"], 2), f(s["yaw_maks_derece"], 2), f(s["harita_bos_iou"], 2), f(s["referans_bos_iou"], 2),
            f(s["harita_dolu_kesinlik_1"], 2), f(s["harita_dolu_geri_cagirma_1"], 2), f(s["harita_tahmin_dolu_gercek_dolu_cm"], 1),
            s.get("esle_kabul", "-")))
    with open(os.path.join(CIKTI, "tablo%s.md" % dosya_ek), "w", encoding="utf-8") as fh:
        fh.write("\n".join(md) + "\n")
    return "\n".join(md)


def ana():
    ap = argparse.ArgumentParser()
    ap.add_argument("--varyant", nargs="*", default=["A", "B", "C", "D"])
    ap.add_argument("--girdi", nargs="*", default=["theta"])
    ap.add_argument("--kayit", nargs="*", default=None)
    ap.add_argument("--ek", default="")
    a = ap.parse_args()
    adlar = a.kayit or ko.KAYIT_ADLARI
    satirlar = []
    for tur in a.girdi:
        for ad in adlar:
            if tur == "sim" and ad not in ("salon_turu", "ev_turu"):
                continue
            for v in a.varyant:
                s, P, log = kos(v, tur, ad)
                satirlar.append(s)
                print("%s %-5s %-28s RMSE %.3f maks %.3f son %.3f yaw %.2f/%.2f IoU %.2f (bilinen %.2f) kabul %s" % (
                    v, tur, ad, s["konum_rmse_m"], s["konum_maks_m"], s["konum_son_m"], s["yaw_ort_mutlak_derece"], s["yaw_maks_derece"],
                    s["harita_bos_iou"], s["referans_bos_iou"], s["esle_kabul"]), flush=True)
                tablo_yaz(satirlar, a.ek)
    with open(os.path.join(CIKTI, "sonuc%s.json" % a.ek), "w", encoding="utf-8") as fh:
        json.dump([{k: v for k, v in s.items() if not k.startswith("_")} for s in satirlar], fh, indent=1, ensure_ascii=False, default=float)
    print(tablo_yaz(satirlar, a.ek))


if __name__ == "__main__":
    ana()
