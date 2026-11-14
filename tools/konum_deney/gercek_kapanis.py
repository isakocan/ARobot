"""Kapanış (ilk-son kare doğrudan eşleşmesi) ve hareketsiz sürüklenme denetimi, M2D ile (yontem_pusula_nokta.Yontem, parametreler aynen).
  python gercek_kapanis.py [maske=eski]
1) Her kayıt: ilk karedeki özellikler ile son (ve son-60 kare .. son) karedeki özellikler doğrudan eşleştirilir (pencere ±180 derece, komutsuz);
   sonuç doğrudan dönüş farkı. Birikimli M2D yaw'ı (kare kare, anahtar kareli) mod 360 ile karşılaştırılır: fark = kapanış tutarlılığı.
   (Robot aynı yere/aynı yöne dönmüş olmak ZORUNDA değil; konum farkı ilk-son eşleşmeyi bozar. İç nokta sayısı eşleşmenin güvenini söyler.)
2) 1_bekleme, 6a, 6b (motor hep 0): her 5. kare doğrudan 0. kareyle zorla eşleştirilir (durgunluk kısa devresi YOK): yaw gürültüsü/sürüklenme."""
import json
import os
import sys

import cv2
import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_ortak as go  # noqa: E402


def ana(maske="eski"):
    go.kur(maske)
    import yontem_pusula_nokta as ypn
    from kayit import Kayit
    from kamera import Kamera
    from olc import sar
    sonuc = {}
    for ad in go.ADLAR:
        kay = Kayit(os.path.join(go.GERCEK, ad))
        kam = Kamera.bilgiden(kay.bilgi)
        y = ypn.Yontem(dict(kay.bilgi), kam, kip="duzlem", komut_oncel=False)
        n = len(kay)

        def oz(i):
            gri, gec = y.don(kay.resim(i))
            return y._cikar(gri, gec)
        o0 = oz(0)
        z = np.load(os.path.join(go.onbellek(maske), "yon_komutsuz_gercek_%s.npz" % ad))
        yacc = float(z["yon"][-1])
        satir = {"birikimli_son_mod360": float(((yacc + 180) % 360) - 180)}
        dd = []
        for j in (n - 1, n - 31, n - 61):
            dy, kal, tamam, b = y._esle(oz(j), o0, 0.0, 180.0)
            dd.append((j, float(dy), float(kal), bool(tamam), int(b.get("icerik", 0)), int(b.get("eslesme", 0))))
        satir["dogrudan_ilk_son"] = dd
        # kapanis farki: ilk-son dogrudan (kare n-1) ile birikimli
        satir["kapanis_farki_derece"] = float(sar(dd[0][1] - yacc))
        print("%-20s birikimli son %7.1f (mod360) | doğrudan ilk-son %7.1f (iç %d, eşleşme %d, kalite %.2f) -> fark %6.1f derece" % (
            ad, satir["birikimli_son_mod360"], dd[0][1], dd[0][4], dd[0][5], dd[0][2], satir["kapanis_farki_derece"]), flush=True)
        if ad in ("1_bekleme", "6a_far_isik_acik", "6b_far_karanlik"):
            ds, ics = [], []
            for j in range(5, n, 5):
                dy, kal, tamam, b = y._esle(oz(j), o0, 0.0, 30.0)
                ds.append(float(dy) if tamam else np.nan)
                ics.append(int(b.get("icerik", 0)))
            ds = np.array(ds)
            satir["zorla_tamam_orani"] = float(np.mean(np.isfinite(ds)))
            satir["zorla_dyaw_ort"] = float(np.nanmean(ds)) if np.isfinite(ds).any() else None
            satir["zorla_dyaw_std"] = float(np.nanstd(ds)) if np.isfinite(ds).any() else None
            satir["zorla_dyaw_maks_mutlak"] = float(np.nanmax(np.abs(ds))) if np.isfinite(ds).any() else None
            satir["zorla_ic_ort"] = float(np.mean(ics))
            satir["zorla_dyaw_son"] = float(ds[np.isfinite(ds)][-1]) if np.isfinite(ds).any() else None
            print("    zorla eşleştirme (durgun): tamam %.2f, dyaw ort %.3f std %.3f maks|.| %.3f son %.3f derece, iç ort %.0f" % (
                satir["zorla_tamam_orani"], satir["zorla_dyaw_ort"] or np.nan, satir["zorla_dyaw_std"] or np.nan,
                satir["zorla_dyaw_maks_mutlak"] or np.nan, satir["zorla_dyaw_son"] or np.nan, satir["zorla_ic_ort"]), flush=True)
        sonuc[ad] = satir
    json.dump(sonuc, open(os.path.join(go.SONUC, "kapanis_%s.json" % maske), "w", encoding="utf-8"), indent=1, ensure_ascii=False)


if __name__ == "__main__":
    ana(sys.argv[1] if len(sys.argv) > 1 else "eski")
