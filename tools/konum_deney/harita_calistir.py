"""Bilinen konumlarla harita kurma deneyi: her sınır yöntemi x girdi (sim/theta) x kayıt için harita, ölçüt ve görüntü.

Tek komut (taramalar zemin_degerlendir.py ile üretilmiş olmalı; gerçek veri değişirse bu da yeniden koşulur):
    python tools/konum_deney/harita_calistir.py --hepsi
    python tools/konum_deney/harita_calistir.py --yontem klasik --tur sim --kayit salon_turu

Üst sınır haritası: gerçek tarama (gercek_mesafe_gorunen_zemin, yoksa eski zemin) aynı kod yolundan geçer. Güven=1;
'ust_sinirsiz' menzil 20 m, 'ust_menzilli' yöntemlerle aynı güvenilir menzil. Fark = harita kodu hatası ile sınır bulma hatasını ayırır.
Çıktı: kayitlar/sonuclar/harita/harita/<yontem>_<tur>/<kayit>.png, harita_olcut.json, harita_olcut.md"""
import argparse
import json
import os
import sys

import cv2
import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zemin_ortak as zo  # noqa: E402
import zemin_degerlendir as zd  # noqa: E402
import harita_kur as hk  # noqa: E402
import harita_olc as ho  # noqa: E402

HARITA_KAYITLARI = ["salon_turu", "ev_turu", "yerinde_donus_salon", "yerinde_donus_koridor", "yerinde_donus_yatak_odasi",
                    "yerinde_donus_banyo", "yerinde_donus_mutfak", "yerinde_donus_calisma_odasi", "duz_git"]


def gercek_harita_yukle(ad):
    return cv2.imread(os.path.join(zo.KAYITLAR, "sim", ad, "gercek_harita.png"), cv2.IMREAD_GRAYSCALE)


def konum_yukle(ad):
    return zo.poz_oku(ad)


def ust_sinir_taramasi(ad, adim=5, menzil=None):
    ts, kn, tr = zo.gercek_zemin_dosyasi(ad)
    idx = np.arange(0, len(kn), adim)
    return {"kare": kn[idx], "mesafe": tr[idx].astype(np.float32), "guven": np.ones((len(idx), 360), np.float32),
            "max_menzil": float(menzil if menzil is not None else 20.0)}, ts


def kaydi_isle(ad, tarama, menzil=None, gorus_m=4.0, **kw):
    konum = konum_yukle(ad)
    klasor = os.path.join(zo.KAYITLAR, "sim", ad)
    iz = hk.kayittan_izgara(ad, tarama, konum, None, klasor, max_menzil=menzil, **kw)
    E = iz.durum()
    T = gercek_harita_yukle(ad)
    satir = {int(k): i for i, k in enumerate(konum["kare"])}
    ii = [satir[int(k)] for k in tarama["kare"]]
    kon = np.stack([konum["x_m"][ii], konum["y_m"][ii], konum["yon_derece"][ii]], 1)
    R = ho.gozlenebilir_bolge(T, iz, kon, gorus_m)
    return iz, E, T, R, kon


def ana():
    ap = argparse.ArgumentParser()
    ap.add_argument("--yontem", nargs="*", default=None)
    ap.add_argument("--tur", nargs="*", default=["sim", "theta"])
    ap.add_argument("--kayit", nargs="*", default=None)
    ap.add_argument("--hepsi", action="store_true")
    ap.add_argument("--adim", type=int, default=5)
    ap.add_argument("--menzil_sim", type=float, default=None)
    ap.add_argument("--menzil_theta", type=float, default=None)
    ap.add_argument("--gorus", type=float, default=4.0)
    ap.add_argument("--ust_sinir_yok", action="store_true")
    ap.add_argument("--guven_min", type=float, default=0.2)
    ap.add_argument("--alt_ornek", type=int, default=1, help="yalnızca kare numarası N'in katı olan taramaları kullan (model taramaları seyrek: 20)")
    a = ap.parse_args()
    yontemler = a.yontem or zd.YONTEMLER
    adlar = a.kayit or HARITA_KAYITLARI
    sonuc = {}
    ts_ilk = zo.gercek_zemin_dosyasi(adlar[0])[0]
    if ts_ilk != "gorunen_zemin":
        print("UYARI: gorunen_zemin yok; eski KUTU gerçeği kullanılıyor.")
    kokmenzil = {"sim": a.menzil_sim, "theta": a.menzil_theta}
    for ad in adlar:
        # üst sınır
        if not a.ust_sinir_yok:
            for tur in a.tur:
                mz = kokmenzil[tur]
                tar_m = None
                for ust_ad, menzil in (("ust_sinirsiz", 20.0), ("ust_menzilli", None)):
                    if menzil is None:
                        # yöntemlerin menzili: ilk yöntemin tarama dosyasından
                        yol = zd.tarama_yolu(yontemler[0], tur, ad)
                        menzil = mz if mz else (float(np.load(yol)["max_menzil"]) if os.path.exists(yol) else zo.guvenilir_menzil(768 if tur == "sim" else 640, 0.255))
                    tar, ts = ust_sinir_taramasi(ad, max(a.adim, a.alt_ornek), menzil)
                    iz, E, T, R, kon = kaydi_isle(ad, tar, menzil, a.gorus)
                    s = ho.olc(T, E, R, iz.h)
                    s["menzil"] = menzil
                    sonuc["%s|%s|%s" % (ust_ad, tur, ad)] = s
                    ho.yan_yana(T, E, R, kon, iz, os.path.join(zo.CIKTI, "harita", "%s_%s" % (ust_ad, tur), ad + ".png"),
                                baslik="%s %s %s (gercek: %s)" % (ust_ad, tur, ad, ts))
        for y in yontemler:
            for tur in a.tur:
                yol = zd.tarama_yolu(y, tur, ad)
                if not os.path.exists(yol):
                    continue
                z = zd.tarama_yukle(y, tur, ad)
                if a.alt_ornek > 1:                          # yalnızca kare numarası N'in katı olan taramalar (modelle aynı seyreklik)
                    z = dict(z)
                    kp = (z["kare"] % a.alt_ornek) == 0
                    for kk in ("kare", "mesafe", "guven"):
                        z[kk] = z[kk][kp]
                menzil = kokmenzil[tur] or float(z["max_menzil"])
                iz, E, T, R, kon = kaydi_isle(ad, z, menzil, a.gorus, guven_min=a.guven_min)
                s = ho.olc(T, E, R, iz.h)
                s["menzil"] = menzil
                sonuc["%s|%s|%s" % (y, tur, ad)] = s
                ho.yan_yana(T, E, R, kon, iz, os.path.join(zo.CIKTI, "harita", "%s_%s" % (y, tur), ad + ".png"),
                            baslik="%s %s %s menzil %.1f m" % (y, tur, ad, menzil))
                print("%-20s %-6s %-28s IoU %.3f  dolu K/G(1 hucre) %.2f/%.2f  bos->dolu %.3f  dolu->bos %.3f" % (
                    y, tur, ad, s["bos_iou"], s["dolu_kesinlik_1"], s["dolu_geri_cagirma_1"], s["bos_dolu_sanildi_tol1"], s["dolu_bos_sanildi_tol1"]))
    os.makedirs(zo.CIKTI, exist_ok=True)
    with open(os.path.join(zo.CIKTI, "harita_olcut.json"), "w", encoding="utf-8") as f:
        json.dump(sonuc, f, indent=1, ensure_ascii=False)
    md = ozet_tablo(sonuc)
    with open(os.path.join(zo.CIKTI, "harita_olcut.md"), "w", encoding="utf-8") as f:
        f.write(md)
    print(md)


def ozet_tablo(sonuc):
    sat = ["| yöntem | girdi | kayıt | boş IoU | dolu K/G (1 hücre) | dolu K/G (2 hücre) | tah→gerç / gerç→tah (cm) | boş→dolu sanıldı (tol 1) | dolu→boş sanıldı (tol 1) |",
           "|---|---|---|---|---|---|---|---|---|"]
    for k, s in sonuc.items():
        y, t, ad = k.split("|")
        sat.append("| %s | %s | %s | %.3f | %.2f / %.2f | %.2f / %.2f | %.1f / %.1f | %.3f | %.3f |" % (
            y, t, ad, s["bos_iou"], s["dolu_kesinlik_1"], s["dolu_geri_cagirma_1"], s["dolu_kesinlik_2"], s["dolu_geri_cagirma_2"],
            s["tahmin_dolu_gercek_dolu_cm"], s["gercek_dolu_tahmin_dolu_cm"], s["bos_dolu_sanildi_tol1"], s["dolu_bos_sanildi_tol1"]))
    return "\n".join(sat)


if __name__ == "__main__":
    ana()
