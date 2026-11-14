"""SegFormer-B0 (ADE20K, olduğu gibi) zemin bölütlemesini GERÇEK robot karelerinde görselleştirir (nitel değerlendirme; yer gerçeği yok).
  python gercek_zemin_goruntu.py [maske=eski]
Her örnek: (üst) 1280x640 panorama + zemin olasılığı (yeşil) + gövde maskesi (mavi taralı) + zemin sınırı (kırmızı çizgi);
          (alt sol) zemin ham olasılık; (alt sağ) kuşbakışı zemin sınırı noktaları, h=0,17 m (dolu) ve h=0,255 m (boş, sim sabiti) ile; halkalar 1 m aralık.
Ayrıca sayılar: sınır mesafesi min/medyan/maks (h=0,17), sınırı bulunan azimut oranı, zemin olasılığı ortalaması."""
import json
import os
import sys

import cv2
import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_ortak as go  # noqa: E402

ORNEKLER = [("2a_vitessiz_duz", 250, "hali, ilk"), ("2a_vitessiz_duz", 520, "hali, surus"), ("2b_otomatik_duz", 400, "hali, otomatik vites"),
            ("3_vitessiz_donus", 150, "hali, donus"), ("4_parke_vitessiz", 250, "parke"), ("4_parke_vitessiz", 480, "parke, ileri"),
            ("5_saksi_turu", 300, "parke, esyalar"), ("5_saksi_turu", 900, "parke, esyalar"), ("5_saksi_turu", 1500, "parke, esyalar"),
            ("5_saksi_turu", 2100, "parke, tur sonu"), ("6a_far_isik_acik", 150, "far acik, los"), ("6b_far_karanlik", 150, "karanlik, far kapali"),
            ("6c_karanlik_surus", 300, "karanlik surus"), ("6c_karanlik_surus", 700, "karanlik surus")]


def yaz(im, s, org, renk=(255, 255, 255), boy=0.6):
    cv2.putText(im, s, org, cv2.FONT_HERSHEY_SIMPLEX, boy, (0, 0, 0), 3, cv2.LINE_AA)
    cv2.putText(im, s, org, cv2.FONT_HERSHEY_SIMPLEX, boy, renk, 1, cv2.LINE_AA)


def ana(maske="eski"):
    go.kur(maske)
    import zemin_ortak as zo
    import zemin_model as zm
    b = zm.Bolutleyici()
    cikis = os.path.join(go.SONUC, "zemin_%s" % maske)
    os.makedirs(cikis, exist_ok=True)
    ozet = []
    for ad, sira, etiket in ORNEKLER:
        kg = zo.KayitGirdisi(ad, "theta")
        sira = min(sira, kg.n - 1)
        g = kg.girdi(sira)
        Pk, gec = b.zemin_olasiligi(g)
        r = zm.p_den_sinir(Pk, gec, g, "kesim")
        H, G = g.yuk, g.gen
        v0 = H // 2
        pan = g.pan.copy()
        # panoramaya bindirme: Pk (H2, 360) -> (H2, G)
        P = cv2.resize(Pk, (G, H - v0), interpolation=cv2.INTER_LINEAR)
        yesil = np.zeros_like(pan)
        yesil[..., 1] = 255
        a = np.zeros((H, G), np.float32)
        a[v0:] = np.clip(P, 0, 1) * 0.5
        ov = (pan * (1 - a[..., None]) + yesil * a[..., None]).astype(np.uint8)
        # gövde maskesi (mavi)
        mav = g.gecersiz.astype(bool)
        ov[mav] = (0.55 * ov[mav] + 0.45 * np.array([255, 80, 0])).astype(np.uint8)
        # sınır
        rr = r.ek["r"]
        xs = (np.arange(360) + 0.5) * G / 360.0
        ys = v0 + rr + 0.0
        pts = np.stack([xs, ys], 1).astype(np.int32)
        cv2.polylines(ov, [pts], False, (0, 0, 255), 2, cv2.LINE_AA)
        cv2.line(ov, (0, v0), (G, v0), (255, 255, 0), 1)
        yaz(ov, "%s kare %d (%s)  |  yesil: SegFormer zemin olasiligi, mavi: govde maskesi, kirmizi: zemin siniri, sari: ufuk" % (ad, sira, etiket), (8, 22), boy=0.5)
        # kuşbakışı
        S = 400
        kb = np.zeros((S, S, 3), np.uint8) + 30
        ppm = S / 2 / 3.0       # 3 m yarıçap
        for m in (1, 2, 3):
            cv2.circle(kb, (S // 2, S // 2), int(m * ppm), (90, 90, 90), 1)
            yaz(kb, "%d m" % m, (S // 2 + int(m * ppm) - 34, S // 2 - 4), boy=0.4)
        mes = r.mesafe
        gv = r.guven
        mes255 = mes * 0.255 / g.h
        for k in range(360):
            az = np.radians(k)
            for d, renk, dolu in ((mes255[k], (120, 120, 255), -1), (mes[k], (0, 255, 255), -1)):
                if np.isfinite(d) and d < 3.0:
                    p = (int(S // 2 + d * ppm * np.sin(az)), int(S // 2 - d * ppm * np.cos(az)))
                    cv2.circle(kb, p, 2 if renk[1] == 255 and renk[0] == 0 else 1, renk, dolu)
        cv2.arrowedLine(kb, (S // 2, S // 2), (S // 2, S // 2 - 40), (255, 255, 255), 2)
        yaz(kb, "on", (S // 2 + 6, S // 2 - 44), boy=0.45)
        yaz(kb, "sari: h=0.17 m (gercek)  salmon: h=0.255 m (sim sabiti)", (6, S - 8), boy=0.4)
        # ham olasılık
        pr = (np.clip(P, 0, 1) * 255).astype(np.uint8)
        pr = cv2.applyColorMap(pr, cv2.COLORMAP_VIRIDIS)
        pr = cv2.resize(pr, (G - S, S), interpolation=cv2.INTER_AREA)
        yaz(pr, "zemin olasiligi (alt yarim, 0-1)", (6, 20), boy=0.5)
        alt = np.hstack([pr, kb])
        im = np.vstack([ov, alt])
        im = cv2.resize(im, (1280, int(im.shape[0] * 1280 / im.shape[1])))
        cv2.imwrite(os.path.join(cikis, "%s_%04d.jpg" % (ad, sira)), im, [cv2.IMWRITE_JPEG_QUALITY, 88])
        ok = np.isfinite(mes) & (mes < r.max_menzil * 0.999)
        ozet.append(dict(ad=ad, kare=int(sira), etiket=etiket, zemin_orani=float((Pk > 0.5)[:, :].mean()), min_m=float(np.nanmin(mes)),
                         medyan_m=float(np.nanmedian(mes)), maks_m=float(np.nanmax(mes[np.isfinite(mes)])), menzil_ici_oran=float(ok.mean()),
                         guven_ort=float(gv.mean()), max_menzil=float(r.max_menzil), h=g.h))
        s = ozet[-1]
        print("%-18s %4d %-22s sınır mesafesi h=0,17: min %.2f medyan %.2f maks %.2f m | menzil içi %.2f güven %.2f menzil %.2f" % (
            ad, sira, etiket, s["min_m"], s["medyan_m"], s["maks_m"], s["menzil_ici_oran"], s["guven_ort"], s["max_menzil"]), flush=True)
    json.dump(ozet, open(os.path.join(cikis, "ozet.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)


if __name__ == "__main__":
    ana(sys.argv[1] if len(sys.argv) > 1 else "eski")
