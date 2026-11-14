"""Örnek görüntüler: sorgu panoraması | en iyi eşleşen anahtar kare panoraması | harita üstünde (gerçek konum yeşil, sistemin cevabı kırmızı,
eşleşen anahtar kare mavi). Başarılar ve her tür başarısızlık; kayitlar/sonuclar/yeniden_bul/ornek/ altına.
Kullanım: python yeniden_bul_ornek.py"""
import os
import sys

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import konum_ortak as ko  # noqa: E402
import yeniden_bul_ortak as yo  # noqa: E402
import yeniden_bul_cekirdek as yc  # noqa: E402
import yeniden_bul_tablo as yt  # noqa: E402
import yeniden_bul_olc as ol  # noqa: E402
from kayit import Kayit  # noqa: E402
from kamera import Kamera  # noqa: E402

FONT = "C:/Windows/Fonts/arial.ttf"
PW, PH = 560, 280
_KAYIT = {}


def pano(ad, kare):
    if ad not in _KAYIT:
        k = Kayit(yo.KAYITLAR_TUM[ad]["klasor"])
        _KAYIT[ad] = (k, Kamera.bilgiden(k.bilgi))
    k, cam = _KAYIT[ad]
    e, _ = cam.balikgozu_to_esdikdortgen(k.resim(int(kare)), PW * 2, PH * 2)
    return cv2.resize(e, (PW, PH), interpolation=cv2.INTER_AREA)


def harita_paneli(H, gercek, cevap, kf_poz, boyut=560):
    iz = H.iz
    img = np.full((iz.Hh, iz.W, 3), 120, np.uint8)
    img[iz.lo <= -0.4] = (245, 245, 245)
    img[H.alan & (iz.lo <= -0.4)] = (255, 232, 214)          # BGR: açık mavi
    img[iz.lo >= 0.4] = (30, 30, 30)
    # kırp: bilinen alan sınırları
    ys, xs = np.where(np.abs(iz.lo) > 0.05)
    r0, r1, c0, c1 = ys.min() - 4, ys.max() + 4, xs.min() - 4, xs.max() + 4
    img = img[r0:r1, c0:c1]
    S = min(boyut / img.shape[1], boyut / img.shape[0])
    img = cv2.resize(img, None, fx=S, fy=S, interpolation=cv2.INTER_NEAREST)

    def px(x, y):
        r, c = iz.hucre_idx(np.array(x), np.array(y))
        return int((c - c0 + 0.5) * S), int((r - r0 + 0.5) * S)

    for p in H.kf_poz:
        cv2.circle(img, px(p[0], p[1]), 3, (200, 120, 0), 1)
    if kf_poz is not None:
        cv2.circle(img, px(kf_poz[0], kf_poz[1]), 8, (255, 0, 0), 2)
    def ok(p, renk, r):
        a = px(p[0], p[1])
        b = px(p[0] + 0.5 * np.sin(np.radians(p[2])), p[1] + 0.5 * np.cos(np.radians(p[2])))
        cv2.circle(img, a, r, renk, -1)
        cv2.line(img, a, b, renk, 2)
    ok(gercek, (0, 170, 0), 6)
    if cevap is not None:
        ok(cevap, (0, 0, 230), 4)
    out = np.full((boyut, boyut, 3), 255, np.uint8)
    out[:img.shape[0], :img.shape[1]] = img
    return out


def kart(H, s, baslik, notlar):
    ham = s["ham"]
    kf = None
    if ham["dog"]:
        kf = max(ham["dog"], key=lambda d: d[1])[0]
    elif "en" in ham:
        kf = ham["en"]["kf"]
    sol1 = pano(s["kayit"], s["kare"])
    sol2 = pano(yo.HARITA_KAYDI, H.kf_kare[kf]) if kf is not None else np.full((PH, PW, 3), 60, np.uint8)
    t = (s["gx"], s["gy"], s["gyaw"])
    cevap = s["poz"] if s.get("tanindi") else None
    harita = harita_paneli(H, t, cevap if cevap is not None else None, H.kf_poz[kf] if kf is not None else None)
    sol = np.vstack([sol1, sol2])
    govde = np.hstack([sol, harita])
    ust = np.full((92, govde.shape[1], 3), 255, np.uint8)
    im = Image.fromarray(np.vstack([ust, govde])[:, :, ::-1])
    d = ImageDraw.Draw(im)
    f1, f2 = ImageFont.truetype(FONT, 20), ImageFont.truetype(FONT, 15)
    d.text((10, 6), baslik, fill=(0, 0, 0), font=f1)
    for i, n in enumerate(notlar):
        d.text((10, 34 + 19 * i), n, fill=(40, 40, 40), font=f2)
    d.text((10, 92 + 4), "SORGU (%s kare %d)" % (s["kayit"], s["kare"]), fill=(255, 255, 0), font=f2, stroke_width=2, stroke_fill=(0, 0, 0))
    d.text((10, 92 + PH + 4), "EN İYİ ANAHTAR KARE (ev_turu %s)" % (("kare %d" % H.kf_kare[kf]) if kf is not None else "yok"), fill=(255, 255, 0), font=f2,
           stroke_width=2, stroke_fill=(0, 0, 0))
    d.text((PW + 8, 92 + 6), "yeşil: gerçek   kırmızı: cevap   mavi halka: eşleşen anahtar kare", fill=(0, 0, 0), font=f2)
    return np.array(im)[:, :, ::-1]


def aciklama(s):
    n = ["gerçek (%.2f, %.2f, %.0f°) %s | en yakın anahtar kare %.2f m | iç nokta %d, uyumlu anahtar kare %d, çizgi uyumu %.2f" % (
        s["gx"], s["gy"], s["gyaw"], s["oda"], s["d_kf"], s["en_icerik"], s["n_uyumlu"], s["cizgi"])]
    if s["tanindi"]:
        n.append("cevap (%.2f, %.2f, %.0f°) %s | hata %.2f m, %.1f°" % (s["poz"][0], s["poz"][1], s["poz"][2], yo.oda_adi(s["poz"][0], s["poz"][1]),
                                                                       s["hata_m"], s["hata_y"]))
    else:
        n.append("cevap: TANIMIYORUM")
    return n


def ana():
    os.makedirs(yo.ORNEK, exist_ok=True)
    H = yc.hafiza_kur()
    T = yt.tablo_kur(H)
    for s in T:
        s["oda"] = yo.oda_adi(s["gx"], s["gy"])
    ol.karar_uygula(T)
    dogru = lambda s: s["tanindi"] and s["hata_m"] <= 0.5 and s["hata_y"] <= 15
    sec = []                      # (dosya_adı, baslik, satır)
    # başarılar: üç farklı kayıt
    for kay, kosul in (("salon_turu", lambda s: s["d_kf"] > 0.8 and dogru(s) and s["sinif"] == 0),
                       ("duz_git", lambda s: s["d_kf"] > 0.5 and dogru(s) and s["sinif"] == 0),
                       ("sinama_ters_tur", lambda s: s["d_kf"] > 0.4 and dogru(s) and s["oda"] == "KORIDOR")):
        k = [s for s in T if s["kayit"] == kay and kosul(s)]
        if k:
            s = k[len(k) // 2]
            sec.append(("basari_%s" % kay, "BAŞARI: %s kare %d" % (kay, s["kare"]), s))
    # başarı: ev_turu içi
    k = [s for s in T if s["kayit"] == "ev_turu" and dogru(s) and s["d_kf"] > 0.6]
    if k:
        sec.append(("basari_ev_turu_dislanmis", "BAŞARI: ev_turu kendisi, ±3 sn anahtar kareler dışlı", k[len(k) // 2]))
    # yanlış-kesin
    for i, s in enumerate(sorted([s for s in T if s["tanindi"] and not dogru(s)], key=lambda s: -s["hata_m"])[:4]):
        sec.append(("yanlis_kesin_%d" % (i + 1), "YANLIŞ-KESİN (tanıdım dedi, yanlış yer): %s kare %d" % (s["kayit"], s["kare"]), s))
    # hiç görmediği yer: reddedildi (doğru ret) ve tanıdığı örnek (varsa)
    for i, s in enumerate([s for s in T if s["sinif"] == 2 and not s["tanindi"]][::12][:3]):
        sec.append(("dogru_ret_gorulmemis_%d" % (i + 1), "DOĞRU RET: hiç görmediği yer, 'tanımıyorum' (%s kare %d)" % (s["kayit"], s["kare"]), s))
    yanlis_tanima = [s for s in T if s["sinif"] == 2 and s["tanindi"]]
    for i, s in enumerate(yanlis_tanima[:2]):
        sec.append(("yanlis_tanima_gorulmemis_%d" % (i + 1), "YANLIŞ TANIMA: hiç görmediği yeri tanıdığını söyledi (%s kare %d)" % (s["kayit"], s["kare"]), s))
    # kaçırma: anahtar kareye yakın ama tanımadı
    k = [s for s in T if not s["tanindi"] and s["d_kf"] < 0.7 and s["kayit"] not in ("sinama_far", "sinama_los")]
    for i, s in enumerate(k[:2]):
        sec.append(("kacirma_yakin_%d" % (i + 1), "KAÇIRMA: anahtar kareye yakın ama tanımadı (%s kare %d)" % (s["kayit"], s["kare"]), s))
    # uzak: anahtar kareden uzak, tanımadı (kapsam sınırı)
    k = [s for s in T if not s["tanindi"] and 1.2 < s["d_kf"] < 1.8 and s["sinif"] == 0 and s["kayit"] == "salon_turu"]
    for i, s in enumerate(k[::20][:2]):
        sec.append(("kacirma_uzak_%d" % (i + 1), "KAÇIRMA: haritalı alanda ama anahtar kare 1,2-1,8 m uzak (%s kare %d)" % (s["kayit"], s["kare"]), s))
    # değişen koşullar
    for kay, ad in (("sinama_los", "loş"), ("sinama_far", "yalnız far"), ("sinama_duz_zemin", "düz zemin")):
        k = [s for s in T if s["kayit"] == kay and s["d_kf"] < 0.5]
        if k:
            s = k[len(k) // 2]
            sec.append(("kosul_%s" % kay, "DEĞİŞEN KOŞUL (%s): %s" % (ad, "tanıdı" if s["tanindi"] else "tanımadı"), s))
    # benzer yer: en güçlü yanlış-yer eşleşmesi
    en = None
    for s in T:
        if s["kayit"] in ("sinama_los", "sinama_far", "sinama_duz_zemin"):
            continue
        for (kf, ic, esl, dy, ph) in s["ham"]["dog"]:
            d = float(np.hypot(H.kf_poz[kf][0] - s["gx"], H.kf_poz[kf][1] - s["gy"]))
            if d > 2.0 and (en is None or ic > en[0]):
                en = (ic, s, kf, d)
    if en:
        s = dict(en[1])
        s["ham"] = dict(en[1]["ham"])
        s["ham"]["dog"] = [d for d in s["ham"]["dog"] if d[0] == en[2]]
        sec.append(("benzer_yer_en_guclu", "BENZER YER: en güçlü yanlış-yer eşleşmesi (anahtar kare %d, %.1f m uzak, %d iç nokta) - reddedildi" % (en[2], en[3], en[0]), s))
    for ad, baslik, s in sec:
        g = kart(H, s, baslik, aciklama(s))
        cv2.imwrite(os.path.join(yo.ORNEK, ad + ".jpg"), g, [cv2.IMWRITE_JPEG_QUALITY, 88])
        print("yazıldı", ad)


if __name__ == "__main__":
    ana()
