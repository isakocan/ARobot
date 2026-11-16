"""Eski (yanlış) 'kamera_konum' alanı ile doğru alanın (poz_cw/poz_wc sütun 3 = T_wc konumu) yan yana karşılaştırması. Aynı koşu dosyası, aynı ölçüt kodu."""
import json, os, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import parametre_olcut as po

def kos(ad):
    kl = os.path.join(po.KLASOR, "..", "..", "kayitlar", "slam", "sonuc", ad)
    P = [json.loads(s) for s in open(os.path.join(kl, "poz.jsonl"), encoding="utf-8")]
    t = np.array([p["t_ms"] for p in P]) / 1000.0
    izl = np.array([bool(p.get("izleniyor")) for p in P])
    out = {}
    for ad_alan in ("eski_kamera_konum", "dogru_poz_wc_sutun3"):
        c = np.full((len(P), 3), np.nan)
        for i, p in enumerate(P):
            if izl[i]:
                c[i] = p["kamera_konum"] if ad_alan.startswith("eski") else np.array(p["poz_cw"]).reshape(4, 4)[:3, 3]
        ok = np.where(izl)[0]
        i0 = ok[np.argmax(t[ok] >= 3.4)]; ib = ok[np.argmin(abs(t[ok] - 14.4))]; it = ok[np.argmin(abs(t[ok] - 82.4))]
        s = 2.0 / np.linalg.norm(c[ib] - c[i0])
        seg = ok[(t[ok] >= t[ok][0]) & (t[ok] <= 85)]
        yol85 = np.linalg.norm(np.diff(c[seg], axis=0), axis=1).sum() * s
        tur = ok[(t[ok] >= 22.8) & (t[ok] <= 82.4)]
        yol_tur = np.linalg.norm(np.diff(c[tur], axis=0), axis=1).sum() * s
        rs = []
        for a, b in po.donus_araliklari(t[-1]):
            m = ok[(t[ok] >= a) & (t[ok] <= b)]
            if len(m) >= 8:
                xz = c[m][:, [0, 2]] * s
                rs.append(round(float(np.median(np.linalg.norm(xz - xz.mean(0), axis=1))), 3))
        # ölçek kayması: ilk düz gidiş hızı / tur sırasında komutla normalize SLAM hızı değil; basit gösterge: bant ölçeği (birim başına m) ilk 3,4-14,4 ve 14,4-85 yol/komut-ileri-PWM oranı
        tt, kom = po.komutlar()
        pw = np.array([kom[np.argmin(abs(tt - x)), :].sum() / 2 for x in t[ok]])
        def oran(a, b):
            m = (t[ok] >= a) & (t[ok] <= b)
            seg = ok[m]
            d = np.linalg.norm(np.diff(c[seg], axis=0), axis=1).sum() * s
            w = np.abs(pw[m][:-1]).sum() / 15.0
            return d / max(w, 1e-9) * 255
        out[ad_alan] = dict(bant_olcek_m_birim=round(float(s), 3), yol_0_85_m=round(float(yol85), 1), tur_yolu_m=round(float(yol_tur), 1),
                            tur_sonu_baslangic_m=round(float(np.linalg.norm(c[it] - c[i0]) * s), 2),
                            donus_yaricaplari_m=rs, hiz_m_sn_tam_gaz_ilk_gidis=round(float(oran(4, 14)), 3), hiz_m_sn_tam_gaz_tur=round(float(oran(22.8, 82.4)), 3))
    return out

if __name__ == "__main__":
    for ad in sys.argv[1:]:
        print(ad, json.dumps(kos(ad), ensure_ascii=False, indent=1))
