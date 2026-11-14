"""Yeniden bulma deneyleri E1-E4: sorgu_<ad>.pkl önbelleğinden tablolar (kayitlar/sonuclar/yeniden_bul/sonuc.md, sonuc.json, *.csv).
Kullanım: python yeniden_bul_degerlendir.py   (önce yeniden_bul_sorgu_calistir.py)
Kümeler (haritalı alan: yeniden_bul_ortak.haritali_alan; sınıf 0 = alan içi, 1 = alana <= 1 m, 2 = alandan > 1 m uzak):
  E1a  ev_turu'nun kendi kareleri, +-3 sn anahtar kareler dışlanmış
  E1b  başka kayıtlar (normal ışık), gerçek konum sınıf 0
  E2a  sınıf 2 (alanın 1 m ötesi: derin yatak odası)           E2b  sınıf 0/1 ama en yakın anahtar kare >= 2 m (yalnız kapıdan görülmüş)
  E3   sinama_los / far / duz_zemin (sınıf 0 ve 1)"""
import csv
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import konum_ortak as ko  # noqa: E402
import yeniden_bul_ortak as yo  # noqa: E402
import yeniden_bul_cekirdek as yc  # noqa: E402
import yeniden_bul_tablo as yt  # noqa: E402
import yeniden_bul_olc as ol  # noqa: E402

NORMAL_DIGER = ["salon_turu", "duz_git", "yerinde_donus_salon", "yerinde_donus_koridor", "yerinde_donus_banyo", "yerinde_donus_mutfak",
                "yerinde_donus_calisma_odasi", "yerinde_donus_yatak_odasi", "sinama_ters_tur", "sinama_hizli", "sinama_yeni_rota"]
DEGISEN = ["sinama_los", "sinama_far", "sinama_duz_zemin"]


def kumeler(T):
    k = {}
    k["E1a"] = [s for s in T if s["kayit"] == yo.HARITA_KAYDI]
    k["E1b"] = [s for s in T if s["kayit"] in NORMAL_DIGER and s["sinif"] == 0]
    k["E2a"] = [s for s in T if s["kayit"] in NORMAL_DIGER and s["sinif"] == 2]
    k["E2b"] = [s for s in T if s["kayit"] in NORMAL_DIGER and s["sinif"] in (0, 1) and s["d_kf_tum"] >= 2.0]
    k["E2c"] = [s for s in T if s["kayit"] in NORMAL_DIGER and s["sinif"] == 1]
    k["E3"] = [s for s in T if s["kayit"] in DEGISEN and s["sinif"] in (0, 1)]
    return k


def vo_adimlari(ad, kareler):
    """Ardışık sorgu kareleri arasında KENDİ görsel odometrisi (varyant C: yalnız kamera, harita düzeltmesiz): (d_sag, d_ileri, d_yaw)."""
    C = yo.vo_oku(ad)
    sira = {int(k): i for i, k in enumerate(C["kare"])}
    out = [None]
    for a, b in zip(kareler[:-1], kareler[1:]):
        i, j = sira[int(a)], sira[int(b)]
        p0 = (C["x"][i], C["y"][i], C["yaw"][i])
        sag, ileri = ko.dunya_to_robot(p0, C["x"][j], C["y"][j])
        out.append((float(sag), float(ileri), float(ko.sar(C["yaw"][j] - C["yaw"][i]))))
    return out


def dogru_mu(poz, g, esik=ol.ESIK2):
    if poz is None:
        return False
    return np.hypot(poz[0] - g[0], poz[1] - g[1]) <= esik[0] and abs(ko.sar(poz[2] - g[2])) <= esik[1]


def dizi_calistir(satirlar, bas=0, adim=None, e=None):
    """satirlar: aynı kaydın zaman sıralı sorguları. bas'tan adim kadar canlı dizi. Dönüş: liste (tanindi, poz, kaynak, dogru)."""
    e = e or yc.ESIK
    S = satirlar[bas:bas + adim] if adim else satirlar[bas:]
    vo = vo_adimlari(S[0]["kayit"], [s["kare"] for s in S])
    dz = yc.Dizi(e)
    cik = []
    for s, v in zip(S, vo):
        t, p, g, kaynak = dz.adim(s["ham"], v)
        cik.append((t, p, kaynak, dogru_mu(p, (s["gx"], s["gy"], s["gyaw"])) if t else False, g))
    return cik


def yol_uzunlugu(S):
    x = np.array([s["gx"] for s in S])
    y = np.array([s["gy"] for s in S])
    return np.concatenate([[0.0], np.cumsum(np.hypot(np.diff(x), np.diff(y)))])


def md_tablo(basliklar, satirlar):
    s = ["| " + " | ".join(basliklar) + " |", "|" + "---|" * len(basliklar)]
    for r in satirlar:
        s.append("| " + " | ".join(str(c) for c in r) + " |")
    return "\n".join(s)


def ana():
    os.makedirs(yo.CIKTI, exist_ok=True)
    H = yc.hafiza_kur()
    T = yt.tablo_kur(H)
    sonuc_json = {}
    md = []

    def yaz(*a):
        md.append(" ".join(str(x) for x in a))

    # ---- sorgu tablosu CSV
    alanlar = ["kayit", "kare", "t_ms", "gx", "gy", "gyaw", "oda", "sinif", "d_alan", "d_kf", "d_kf_tum", "en_icerik", "n_uyumlu",
               "toplam_icerik", "cizgi", "tri_skor", "genel_skor", "e_kf", "e_kume", "e_tri", "y_tri", "e_tarama", "e_son", "e_genel", "y_genel", "ms"]
    for s in T:
        s["oda"] = yo.oda_adi(s["gx"], s["gy"])
    with open(os.path.join(yo.CIKTI, "sorgu_tablosu.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(alanlar)
        for s in T:
            w.writerow([("%.3f" % s[a]) if isinstance(s[a], float) else s[a] for a in alanlar])

    e = yc.ESIK
    yaz("# Yeniden bulma (kidnapped robot) sonuçları\n")
    yaz("Eşikler (TEK küme, tüm deneylerde): en iyi anahtar kare M2D iç nokta >= %d, yaw'ı uyumlu doğrulanan anahtar kare >= %d, çizgi uyumu >= %.2f."
        % (e.en_icerik, e.n_uyumlu, e.cizgi))
    yaz("Başarı = tanıdım VE poz (konum, yaw) hataya eşik içinde. Yanlış-kesin = tanıdım ama poz (0,5 m, 15°) dışında. Oranların paydası = kümedeki TÜM sorgular.\n")
    K = kumeler(T)
    for ad, k in K.items():
        ol.karar_uygula(k, e)
    # ---- harita / küme sayıları
    yaz("## Haritalı alan ve küme boyutları\n")
    sinif_say = {a: [sum(1 for s in T if s["kayit"] == a and s["sinif"] == c) for c in (0, 1, 2)] for a in yo.KAYITLAR_TUM}
    yaz("Haritalı alan = robotun kendi haritasında boş bilinen ve haritalama yolunun bir noktasından görüş hattıyla <= 2 m erişilen hücreler "
        "(%.1f m2). Sorgu konumunun sınıfı: 0 içeride, 1 alana <= 1 m, 2 alandan > 1 m." % (H.alan.sum() * H.iz.h ** 2))
    yaz(md_tablo(["kayıt", "sınıf 0 (alan içi)", "sınıf 1 (<=1 m)", "sınıf 2 (>1 m)", "toplam sorgu"],
                 [[a, *v, sum(v)] for a, v in sinif_say.items()]))
    yaz("")
    yaz(md_tablo(["küme", "tanım", "sorgu"], [
        ["E1a", "ev_turu kendi kareleri (±3 sn anahtar kareler dışlı)", len(K["E1a"])],
        ["E1b", "diğer kayıtlar, sınıf 0", len(K["E1b"])],
        ["E2a", "sınıf 2 (alanın >1 m ötesi)", len(K["E2a"])],
        ["E2b", "sınıf 0/1 ama en yakın anahtar kare >= 2 m", len(K["E2b"])],
        ["E2c", "sınıf 1 (alana <= 1 m)", len(K["E2c"])],
        ["E3", "sinama_los/far/duz_zemin, sınıf 0/1", len(K["E3"])]]))
    sonuc_json["kume_boyutlari"] = {a: len(v) for a, v in K.items()}

    # ---- E1
    yaz("\n## E1 - Haritasını çıkardığı yere ışınlanma\n")
    satir = [ol.ozet_satir("E1a ev_turu (±3 sn dışlı)", ol.ozet(K["E1a"]))]
    for a in NORMAL_DIGER:
        k = [s for s in K["E1b"] if s["kayit"] == a]
        if k:
            satir.append(ol.ozet_satir("E1b " + a, ol.ozet(k)))
    satir.append(ol.ozet_satir("**E1b toplam**", ol.ozet(K["E1b"])))
    d15 = [s for s in K["E1b"] if s["d_kf"] <= 1.0]
    satir.append(ol.ozet_satir("E1b, en yakın anahtar kare <= 1 m", ol.ozet(d15)))
    yaz(ol.BASLIK)
    yaz("\n".join(satir))
    sonuc_json["E1a"] = ol.ozet(K["E1a"])
    sonuc_json["E1b"] = ol.ozet(K["E1b"])
    yaz("\n### E1: en yakın anahtar kareye uzaklığa göre (E1a + E1b)\n")
    yaz(ol.dilim_tablosu(K["E1a"] + K["E1b"]))
    yaz("\nE1b yalnız:\n")
    yaz(ol.dilim_tablosu(K["E1b"]))

    # ---- bileşen karşılaştırması
    yaz("\n### Bileşenler ne kattı (E1a + E1b, tüm sorgular; konum yalnız tanıdığı sorgularda ölçülür)\n")
    tum = K["E1a"] + K["E1b"]
    tan = [s for s in tum if s["tanindi"]]
    sat = []
    for v, ad in (("kf", "K: en iyi anahtar karenin konumu"), ("kume", "K+P: doğrulanan anahtar karelerin ağırlıklı ortalaması"),
                  ("tri", "K+Ü: öteleme yönü çizgilerinin üçgenlemesi"), ("tarama", "K+Ü+T: üçgenleme + tarama-harita terimi + 10 cm inceltme (her zaman)"), ("son", "SEÇİLEN: çizgiler >= 20° yayılıyorsa K+Ü, değilse K+Ü+T")):
        h = np.array([s["e_" + v] for s in tan])
        yv = np.array([s["y_" + v] for s in tan])
        ok3 = np.mean((h <= 0.3) & (yv <= 10))
        ok5 = np.mean((h <= 0.5) & (yv <= 15))
        sat.append([ad, "%.0f%%" % (100 * ok3), "%.0f%%" % (100 * ok5), "%.2f" % np.median(h), "%.2f" % np.percentile(h, 90), "%.1f" % np.median(yv)])
    yaz("Tanınan %d sorgu üzerinde:" % len(tan))
    yaz(md_tablo(["konum kaynağı", "<=0,3 m ve 10°", "<=0,5 m ve 15°", "medyan m", "p90 m", "medyan yaw °"], sat))
    # G: genel tarama araması tek başına
    yaz("\nG: genel tarama araması (görüntüsüz; sorgu taraması haritanın tüm bilinen boş hücrelerinde ve 360° yaw'da aranır) tek başına, tüm sorgular:")
    g_sat = []
    for esikg in (0.5, 0.6, 0.7):
        tg = [s for s in tum if np.isfinite(s["genel_skor"]) and s["genel_skor"] >= esikg]
        okg = [s for s in tg if s["e_genel"] <= 0.5 and s["y_genel"] <= 15]
        g_sat.append(["skor >= %.1f" % esikg, len(tg), "%.0f%%" % (100 * len(okg) / len(tum)), "%.0f%%" % (100 * (len(tg) - len(okg)) / len(tum)),
                      "%.0f%%" % (100 * len(okg) / max(len(tg), 1))])
    yaz(md_tablo(["G eşiği", "tanıdığı", "başarı (0,5 m, 15°)", "yanlış-kesin", "tanıdıklarının doğru oranı"], g_sat))
    # G ikinci görüş: seçilen yöntemin kesin cevabıyla örtüşme
    ag = [s for s in tan if s["p_genel"] is not None]
    ortus = [s for s in ag if np.hypot(s["p_genel"][0] - s["poz"][0], s["p_genel"][1] - s["poz"][1]) < 0.5 and abs(ko.sar(s["p_genel"][2] - s["poz"][2])) < 15]
    dg = [s for s in ag if s["hata_m"] <= 0.5 and s["hata_y"] <= 15]
    yg = [s for s in ag if not (s["hata_m"] <= 0.5 and s["hata_y"] <= 15)]
    yaz("\nG ikinci görüş olarak: tanınan %d sorgunun %d'sinde G'nin en iyi adayı da (0,5 m, 15°) aynı yeri gösteriyor. Doğru tanınanlarda örtüşme %d/%d, "
        "yanlış tanınanlarda %d/%d." % (len(ag), len(ortus), sum(1 for s in dg if s in ortus), len(dg), sum(1 for s in yg if s in ortus), len(yg)))
    sonuc_json["G_ikinci_goruş"] = dict(tanilan=len(ag), ortusen=len(ortus), dogru_ortusen=sum(1 for s in dg if s in ortus), dogru=len(dg),
                                        yanlis_ortusen=sum(1 for s in yg if s in ortus), yanlis=len(yg))
    # tarama skoru süzgeç olarak
    ts = [s for s in tan if np.isfinite(s["tri_skor"])]
    if ts:
        dts = [s["tri_skor"] for s in ts if s["hata_m"] <= 0.5 and s["hata_y"] <= 15]
        yts = [s["tri_skor"] for s in ts if not (s["hata_m"] <= 0.5 and s["hata_y"] <= 15)]
        yaz("\nSeçilen pozdaki tarama skoru (haritayla uyum, 0..1): doğru tanınanlarda medyan %.2f (n=%d), yanlış tanınanlarda %s (n=%d)." % (
            np.median(dts), len(dts), "%.2f" % np.median(yts) if yts else "-", len(yts)))

    # ---- E2
    yaz("\n## E2 - Hiç görmediği yere ışınlanma\n")
    yaz("Beklenen cevap: tanımıyorum. 'Yanlış tanıma' = tanıdığını söyledi (konum doğru olsa bile sınıf 2'de alanın dışında; doğruysa ayrıca belirtildi).\n")
    sat = []
    for ad, aciklama in (("E2a", "alanın >1 m ötesi"), ("E2c", "alana <= 1 m"), ("E2b", "yalnız kapıdan görülmüş, anahtar kare >= 2 m")):
        k = K[ad]
        tn = [s for s in k if s["tanindi"]]
        dgr = [s for s in tn if dogru_mu(s["poz"], (s["gx"], s["gy"], s["gyaw"]))]
        sat.append([ad + " " + aciklama, len(k), len(tn), "%.1f%%" % (100 * len(tn) / max(len(k), 1)), len(dgr), len(tn) - len(dgr)])
        sonuc_json[ad] = dict(n=len(k), tanindi=len(tn), dogru=len(dgr), yanlis=len(tn) - len(dgr))
    yaz(md_tablo(["küme", "sorgu", "tanıdım", "yanlış tanıma oranı", "tanıdıklarından konumu doğru", "tanıdıklarından konumu yanlış"], sat))
    yaz("\nOdalara göre (E2: sınıf 0/1/2 sorgular, anahtar kare >= 1,5 m):")
    sat = []
    for oda in ["BANYO", "YATAK ODASI", "CALISMA ODASI", "MUTFAK", "SALON", "KORIDOR"]:
        k = [s for s in T if s["kayit"] in NORMAL_DIGER and s["oda"] == oda and s["d_kf_tum"] >= 1.5]
        ol.karar_uygula(k, e)
        tn = [s for s in k if s["tanindi"]]
        yn = [s for s in tn if not dogru_mu(s["poz"], (s["gx"], s["gy"], s["gyaw"]))]
        sat.append([oda, len(k), len(tn), len(yn)])
    yaz(md_tablo(["gerçek oda", "sorgu (anahtar kare >= 1,5 m)", "tanıdım", "tanıdığı ama yanlış"], sat))

    # ---- E2 canlı dizi
    yaz("\n### E2 devamı: canlı dizi (sinama_ters_tur banyodan başlar; sinama_yeni_rota çalışma odasında kapalı tur)\n")
    zaman = {}
    for ad in ("sinama_ters_tur", "sinama_yeni_rota"):
        S = [s for s in T if s["kayit"] == ad]
        S.sort(key=lambda s: s["kare"])
        ol.karar_uygula(S, e)
        cik = dizi_calistir(S)
        L = yol_uzunlugu(S)
        t0 = S[0]["t_ms"]
        # girişler
        def ilk(kosul):
            for i, s in enumerate(S):
                if kosul(i, s):
                    return i
            return None
        i_koridor = ilk(lambda i, s: s["oda"] == "KORIDOR")
        i_yakin = ilk(lambda i, s: s["d_kf_tum"] <= 1.0)
        i_tek = ilk(lambda i, s: s["tanindi"] and dogru_mu(s["poz"], (s["gx"], s["gy"], s["gyaw"])))
        i_dizi = ilk(lambda i, c: cik[i][0] and cik[i][3])
        i_yanl_tek = [i for i, s in enumerate(S) if s["tanindi"] and not dogru_mu(s["poz"], (s["gx"], s["gy"], s["gyaw"]))]
        i_yanl_dizi = [i for i, c in enumerate(cik) if c[0] and not c[3]]
        def bilgi(i):
            return "-" if i is None else "%.1f sn, %.1f m, kare %d (%s)" % ((S[i]["t_ms"] - t0) / 1000.0, L[i], S[i]["kare"], S[i]["oda"])
        yaz("**%s** (%d sorgu, %.0f sn, %.1f m)" % (ad, len(S), (S[-1]["t_ms"] - t0) / 1000.0, L[-1]))
        yaz("- koridora (haritalı alana) ilk girdiği an: %s" % bilgi(i_koridor))
        yaz("- en yakın anahtar kareye <= 1 m olunan ilk an: %s" % bilgi(i_yakin))
        yaz("- TEK KARE doğru poz bildirdiği ilk an: %s" % bilgi(i_tek))
        yaz("- DİZİ süzgeci doğru poz bildirdiği ilk an: %s" % bilgi(i_dizi))
        yaz("- tek kare yanlış-kesin bildirdiği sorgu: %d (%s); dizi süzgeci yanlış-kesin: %d (%s)" % (
            len(i_yanl_tek), ",".join(str(S[i]["kare"]) for i in i_yanl_tek[:12]), len(i_yanl_dizi), ",".join(str(S[i]["kare"]) for i in i_yanl_dizi[:12])))
        # hiç görülmemiş bölümde (d_kf_tum >= 1.5) yanlış bildirim
        yaz("- anahtar kareye >= 1,5 m uzak sorgularda tek kare/dizi bildirimi: %d/%d sorguda tek kare, %d/%d dizi" % (
            sum(1 for s in S if s["d_kf_tum"] >= 1.5 and s["tanindi"]), sum(1 for s in S if s["d_kf_tum"] >= 1.5),
            sum(1 for i, s in enumerate(S) if s["d_kf_tum"] >= 1.5 and cik[i][0]), sum(1 for s in S if s["d_kf_tum"] >= 1.5)))
        yaz("")
        zaman[ad] = dict(kare=[s["kare"] for s in S], t=[(s["t_ms"] - t0) / 1000.0 for s in S], yol=L.tolist(), oda=[s["oda"] for s in S],
                         d_kf=[s["d_kf_tum"] for s in S], tek=[bool(s["tanindi"]) for s in S],
                         tek_dogru=[bool(s["tanindi"] and dogru_mu(s["poz"], (s["gx"], s["gy"], s["gyaw"]))) for s in S],
                         dizi=[bool(c[0]) for c in cik], dizi_dogru=[bool(c[3]) for c in cik],
                         hata_m=[float(s["hata_m"]) if s["tanindi"] else None for s in S])
        sonuc_json["canli_" + ad] = dict(ilk_tek=bilgi(i_tek), ilk_dizi=bilgi(i_dizi), ilk_koridor=bilgi(i_koridor), ilk_yakin=bilgi(i_yakin),
                                         yanlis_tek=len(i_yanl_tek), yanlis_dizi=len(i_yanl_dizi))
        with open(os.path.join(yo.CIKTI, "zaman_%s.csv" % ad), "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f)
            w.writerow(["kare", "t_sn", "yol_m", "oda", "en_yakin_anahtar_kare_m", "tek_tanidi", "tek_dogru", "dizi_tanidi", "dizi_dogru", "hata_m", "en_icerik"])
            for i, s in enumerate(S):
                w.writerow([s["kare"], "%.2f" % ((s["t_ms"] - t0) / 1000.0), "%.2f" % L[i], s["oda"], "%.2f" % s["d_kf_tum"], int(s["tanindi"]),
                            int(zaman[ad]["tek_dogru"][i]), int(cik[i][0]), int(cik[i][3]), "%.2f" % s["hata_m"] if np.isfinite(s["hata_m"]) else "", s["en_icerik"]])
    with open(os.path.join(yo.CIKTI, "zaman.json"), "w", encoding="utf-8") as f:
        json.dump(zaman, f)

    # ---- ışınlanma sonrası kaç adımda: her kayıt, her başlangıç noktası
    yaz("\n### Işınlanma sonrası kaç adım (3 Hz) gerek: her sorgu karesinden dizi başlatılır (E1: haritalı alanda başlayanlar)\n")
    sat = []
    for ad in NORMAL_DIGER + [yo.HARITA_KAYDI]:
        S = sorted([s for s in T if s["kayit"] == ad], key=lambda s: s["kare"])
        ol.karar_uygula(S, e)
        if len(S) < 8:
            continue
        ilk_t, ilk_d, ilk_yt, ilk_yd, n_b = [], [], 0, 0, 0
        for b in range(0, len(S) - 6, 2):
            if S[b]["sinif"] != 0 or S[b]["d_kf"] > 1.0:
                continue
            cik = dizi_calistir(S, b, 8)
            n_b += 1
            a_t = next((i for i, c in enumerate(cik) if c[0] and c[3] and c[2] == "tek"), None)
            a_d = next((i for i, c in enumerate(cik) if c[0] and c[3]), None)
            ilk_t.append(a_t if a_t is not None else 99)
            ilk_d.append(a_d if a_d is not None else 99)
            ilk_yd += int(any(c[0] and not c[3] for c in cik))
        if n_b:
            f = lambda L, k: "%.0f%%" % (100 * np.mean(np.array(L) <= k))
            sat.append([ad, n_b, f(ilk_t, 0), f(ilk_d, 0), f(ilk_d, 2), f(ilk_d, 7), "%.0f%%" % (100 * ilk_yd / n_b)])
    yaz("Başlangıç = o sorgu karesi (gerçek konumu haritalı alanda ve en yakın anahtar kare <= 1 m). Sütunlar: ilk karede tek kare doğru, ilk karede dizi doğru, "
        "3 adım (1 sn) içinde, 8 adım içinde doğru poz bildirenlerin oranı ve 8 adım içinde herhangi bir yanlış-kesin bildirim oranı.")
    yaz(md_tablo(["kayıt", "başlangıç sayısı", "ilk kare (tek)", "ilk kare (dizi)", "<=3 adım (dizi)", "<=8 adım (dizi)", "8 adımda yanlış-kesin"], sat))

    # ışınlanma sonrası: başlangıç uzaklık dilimine göre (hareketli kayıtlar)
    yaz("\nAynısı, başlangıç noktasının en yakın anahtar kareye uzaklığına göre (hareketli kayıtlar: salon_turu, duz_git, sinama_*, ev_turu; başlangıç her sınıftan):")
    dil = {(0.0, 0.5): [], (0.5, 1.0): [], (1.0, 1.5): [], (1.5, 9.0): []}
    for ad in ["salon_turu", "duz_git", "sinama_ters_tur", "sinama_hizli", "sinama_yeni_rota", yo.HARITA_KAYDI]:
        S = sorted([s for s in T if s["kayit"] == ad], key=lambda s: s["kare"])
        ol.karar_uygula(S, e)
        for b in range(0, len(S) - 9, 2):
            cik = dizi_calistir(S, b, 10)
            a_t = next((i for i, c in enumerate(cik) if c[0] and c[3] and c[2] == "tek"), None)
            a_d = next((i for i, c in enumerate(cik) if c[0] and c[3]), None)
            yan = any(c[0] and not c[3] for c in cik)
            for (lo, hi), L in dil.items():
                if lo <= S[b]["d_kf"] < hi:
                    L.append((a_t, a_d, yan))
    sat = []
    for (lo, hi), L in dil.items():
        if not L:
            continue
        f = lambda idx, k: "%.0f%%" % (100 * np.mean([(x[idx] is not None and x[idx] <= k) for x in L]))
        sat.append(["%.1f-%s" % (lo, ("%.1f" % hi) if hi < 9 else "+"), len(L), f(0, 0), f(0, 9), f(1, 2), f(1, 9), "%.0f%%" % (100 * np.mean([x[2] for x in L]))])
    yaz(md_tablo(["başlangıçtaki anahtar kare uzaklığı m", "başlangıç sayısı", "tek kare: ilk karede", "tek kare: 10 adımda", "dizi: <=3 adım", "dizi: 10 adımda", "10 adımda yanlış-kesin"], sat))

    # ---- E3
    yaz("\n## E3 - Koşullar değişti (harita normal ışıkta, sorgu loş / far / düz zemin)\n")
    ref = [s for s in T if s["kayit"] == "sinama_yeni_rota" and s["sinif"] == 0]
    ol.karar_uygula(ref, e)
    yaz("Üç kayıt, sinama_yeni_rota ile AYNI rotadır (aynı gerçek konumlar). Karşılaştırma için normal ışıklı yeni_rota da gösterilir.\n")
    sat = [ol.ozet_satir("normal ışık (sinama_yeni_rota)", ol.ozet(ref))]
    for a in DEGISEN:
        k = [s for s in K["E3"] if s["kayit"] == a and s["sinif"] == 0]
        sat.append(ol.ozet_satir(a + " (sınıf 0)", ol.ozet(k)))
    yaz(ol.BASLIK)
    yaz("\n".join(sat))
    yaz("\nYeni_rota ile aynı rotada, aynı kare no'su, sınıf 0: normal ışıkta tanınan sorgulardan (E1 koşulu) değişen koşulda da tanınanlar:")
    sat = []
    base = {s["kare"]: s for s in ref if s["sinif"] == 0}
    for a in DEGISEN:
        k = {s["kare"]: s for s in K["E3"] if s["kayit"] == a and s["sinif"] == 0}
        ortak = [kr for kr in base if kr in k]
        nt = [kr for kr in ortak if base[kr]["tanindi"]]
        sat.append([a, len(ortak), len(nt), sum(1 for kr in nt if k[kr]["tanindi"]), sum(1 for kr in nt if dogru_mu(k[kr]["poz"], (k[kr]["gx"], k[kr]["gy"], k[kr]["gyaw"]))),
                    sum(1 for kr in ortak if not base[kr]["tanindi"] and k[kr]["tanindi"])])
    yaz(md_tablo(["kayıt", "ortak kare", "normalde tanınan", "değişende de tanıdı", "...ve poz doğru", "normalde tanımadığı, değişende tanıdı"], sat))
    yaz("\nE3 uzaklık dilimleri (üç kayıt birlikte, sınıf 0/1):\n")
    yaz(ol.dilim_tablosu(K["E3"]))
    yaz("\nPanorama özellik sayısı (ORB, 800 tavanlı) ve medyan en_icerik: ")
    sat = []
    for a in ["sinama_yeni_rota"] + DEGISEN:
        S = [s for s in T if s["kayit"] == a]
        sat.append([a, "%.0f" % np.median([s["ham"]["sayi_max"] for s in S]), "%.0f" % np.median([s["en_icerik"] for s in S]), "%.0f" % np.mean([s["en_icerik"] >= e.en_icerik for s in S]) if False else "%.0f%%" % (100 * np.mean([s["en_icerik"] >= e.en_icerik for s in S]))])
    yaz(md_tablo(["kayıt", "ilk sıralamada en iyi eşleşme sayısı (medyan)", "M2D iç nokta (medyan)", "iç nokta eşiğini geçen sorgu"], sat))
    for a in DEGISEN:
        S = sorted([s for s in T if s["kayit"] == a], key=lambda s: s["kare"])
        ol.karar_uygula(S, e)
        yaz("- %s: yanlış tanınan (E1+E2 birlikte, tüm sorgular): %d / %d" % (a, sum(1 for s in S if s["tanindi"] and not dogru_mu(s["poz"], (s["gx"], s["gy"], s["gyaw"]))), len(S)))

    # ---- E4
    yaz("\n## E4 - Benzer yerler, sınırlar, maliyet\n")
    # karışıklık: tanınıp yanlış olan
    yaz("### Yanlış-kesin cevaplar (tüm kümeler, normal + değişen koşul): gerçek yer -> cevap\n")
    ol.karar_uygula(T, e)
    yan = [s for s in T if s["tanindi"] and not dogru_mu(s["poz"], (s["gx"], s["gy"], s["gyaw"]))]
    sat = []
    for s in sorted(yan, key=lambda s: -s["hata_m"]):
        sat.append([s["kayit"], s["kare"], "(%.2f, %.2f) %s" % (s["gx"], s["gy"], s["oda"]), "(%.2f, %.2f) %s" % (s["poz"][0], s["poz"][1], yo.oda_adi(s["poz"][0], s["poz"][1])),
                    "%.2f m, %.0f°" % (s["hata_m"], s["hata_y"]), s["en_icerik"], "%.2f" % s["d_kf"]])
    yaz(md_tablo(["kayıt", "kare", "gerçek", "cevap", "hata", "iç nokta", "en yakın anahtar kare m"], sat[:40]))
    yaz("\nToplam yanlış-kesin: %d / %d sorgu (%.1f%%). Gerçek oda ile cevap odası farklı olanlar: %d." % (
        len(yan), len(T), 100 * len(yan) / len(T), sum(1 for s in yan if yo.oda_adi(s["gx"], s["gy"]) != yo.oda_adi(s["poz"][0], s["poz"][1]))))
    # ilk sıralama (ORB eşleşme sayısı) benzerliği: yanlış anahtar kareye en yüksek eşleşme
    # en güçlü YANLIŞ YER eşleşmesi: bir anahtar kare gerçek konumdan > 2 m uzaktayken M2D iç nokta sayısı
    yaz("\n### Benzer yer eşleşmeleri: sorgudan > 2 m uzak anahtar karelerle M2D iç nokta sayısı (doğrulanan <= 8 anahtar kare içinde)\n")
    yanyer, dogyer = [], []
    for s in T:
        if s["kayit"] in DEGISEN:
            continue
        t = (s["gx"], s["gy"])
        for (kf, ic, esl, dy, ph) in s["ham"]["dog"]:
            d = float(np.hypot(H.kf_poz[kf][0] - t[0], H.kf_poz[kf][1] - t[1]))
            (yanyer if d > 2.0 else dogyer).append((ic, s["kayit"], s["kare"], kf, d))
    ic_y = np.array([a[0] for a in yanyer]) if yanyer else np.array([0])
    ic_d = np.array([a[0] for a in dogyer if a[4] < 1.0]) if dogyer else np.array([0])
    yaz("Doğrulamaya giren (anahtar kare, sorgu) çifti: %d uzak (> 2 m), %d yakın/orta. Uzak çiftlerde iç nokta: medyan %.0f, p95 %.0f, EN BÜYÜK %d; eşik %d'yi aşan uzak çift: %d. "
        "Yakın (< 1 m) çiftlerde medyan %.0f." % (len(yanyer), len(dogyer), np.median(ic_y), np.percentile(ic_y, 95), ic_y.max(), e.en_icerik,
                                                  int((ic_y >= e.en_icerik).sum()), np.median(ic_d)))
    en_k = sorted(yanyer, key=lambda a: -a[0])[:8]
    sat = [[a[1], a[2], yo.oda_adi(*[T_[k] for k in ("gx", "gy")]) if False else "", a[3], "%.1f" % a[4], a[0]] for a in en_k]
    sat = []
    for ic, kay, kare, kf, d in en_k:
        sq = next(s for s in T if s["kayit"] == kay and s["kare"] == kare)
        sat.append([kay, kare, yo.oda_adi(sq["gx"], sq["gy"]), "kf %d (%s)" % (kf, yo.oda_adi(H.kf_poz[kf][0], H.kf_poz[kf][1])), "%.1f" % d, ic])
    yaz(md_tablo(["sorgu kaydı", "kare", "sorgu odası", "eşleşen anahtar kare", "gerçek uzaklık m", "iç nokta"], sat))
    sonuc_json["benzer_yer"] = dict(uzak_cift=len(yanyer), uzak_ic_max=int(ic_y.max()), uzak_ic_p95=float(np.percentile(ic_y, 95)), esigi_asan=int((ic_y >= e.en_icerik).sum()))
    # maliyet
    yaz("\n### Saklanan veri ve süre\n")
    b = H.bayt()
    yaz("- Haritalama yolu (ev_turu, 22 m): %d anahtar kare (>= 0,30 m aralık, mevcut anahtar kareye < 0,20 m yakın olanlar atlanır)." % b["anahtar_kare"])
    yaz("- Anahtar kare özellikleri: %.2f MB (ışın float32 + 32 bayt tanımlayıcı), ışınlar float16 saklanırsa %.2f MB; kare başına ortalama %d nokta. Poz: %d bayt. Harita (1 bayt/hücre, "
        "%dx%d): %.0f KB." % (b["kf_bayt"] / 1e6, b["kf_bayt_f16"] / 1e6, int(np.mean([len(r) for r in H.kf_R])), b["poz_bayt"], H.iz.W, H.iz.Hh, b["harita_bayt"] / 1e3))
    ms = np.array([s["ms"] for s in T])
    yaz("- Sorgu süresi bu PC'de (4 sorgu paralel koştuğu için bir sorgu tek başına koşandan yavaş ölçüldü): medyan %.0f ms (p95 %.0f ms), bunun içinde: ilk sıralama (33 anahtar kareyle kaba kuvvet "
        "Hamming eşleştirme) %.0f ms, M2D doğrulama (<=8 anahtar kare) %.0f ms, konum çözümü %.0f ms, genel tarama araması (G, isteğe bağlı) %.0f ms." % (
        np.median(ms), np.percentile(ms, 95), np.median([s["ms_parca"]["sira"] for s in T]), np.median([s["ms_parca"]["dogrula"] for s in T]),
        np.median([s["ms_parca"]["konum"] for s in T]), np.median([s["ms_parca"]["genel"] for s in T])))
    sonuc_json["maliyet"] = dict(bayt=b, ms_medyan=float(np.median(ms)), ms_p95=float(np.percentile(ms, 95)))
    with open(os.path.join(yo.CIKTI, "sonuc.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(md) + "\n")
    with open(os.path.join(yo.CIKTI, "sonuc.json"), "w", encoding="utf-8") as f:
        json.dump(sonuc_json, f, indent=1, ensure_ascii=False, default=float)
    print("\n".join(md))


if __name__ == "__main__":
    ana()
