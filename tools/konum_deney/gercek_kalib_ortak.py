"""Kalibre gercek kamera yonlendirmesi (gercek_ortak tarzi): YONTEM DOSYALARINI DEGISTIRMEDEN kamera geometrisini kamera_gercek.json'a (egim, yan egim, yon) cevirir.
  kur(maske='gercek', kamera='gercek'|'nominal', etiket='kalib', json_yolu=None, ozel_maske=None)
    * kamera='gercek': kamera.Kamera.bilgiden (birlesik_bev, birlesik_yon, zemin_ortak, konum_bev ... hepsi bunu cagirir) KameraGercek(kamera_gercek.json) dondurur.
      Onbellekler AYRI klasorde: kayitlar/sonuclar/gercek/onbellek_<maske>_<etiket>/, harita_<maske>_<etiket>/ (tarama), kos_<maske>_<etiket>/ -> eski (nominal) onbellekler korunur.
    * kamera='nominal': hicbir sey degismez (gercek_ortak.kur ile ayni; eski onbellekler).
    * ozel_maske: gövde maskesi olarak kamera_maske_gercek.png yerine bu PNG kullanilir (ornegin sabit noktalar eklenmis maske).
KameraGercek, kamera.Kamera'nin alt sinifidir (yalniz montaj matrisine yan_egim ekler). Yontem dosyalarina dokunulmaz."""
import json
import os
import sys

import cv2

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_ortak as go  # noqa: E402
import gercek_kalib_model as km  # noqa: E402

KLASOR = go.KLASOR
_OZEL = {"maske": None}


def kur(maske="gercek", kamera="gercek", etiket="kalib", json_yolu=None, ozel_maske=None):
    bo, ko, zo = go.kur(maske)
    if ozel_maske:
        onceki = cv2.imread

        def imread(yol, *a, **k):
            if str(yol).replace("\\", "/").endswith("tools/konum_deney/kamera_maske.png"):
                yol = ozel_maske
            return onceki(yol, *a, **k)

        # go._imread zaten 'kamera_maske.png' -> kamera_maske_gercek.png; yeni sarmalayici ondan once calisir
        cv2.imread = imread
    if kamera != "gercek":
        return bo, ko, zo
    from kamera import Kamera
    yol = json_yolu or os.path.join(KLASOR, "kamera_gercek.json")
    ayar = json.load(open(yol, encoding="utf-8"))
    Kamera.bilgiden = staticmethod(lambda bilgi=None: km.KameraGercek(ayar))
    ob = os.path.join(go.SONUC, "onbellek_%s_%s" % (maske, etiket))
    os.makedirs(ob, exist_ok=True)
    orig = bo.spec

    def spec(ad):
        s = orig(ad)
        if ad in go.ADLAR or ad in go.OLCUM:
            s = dict(s)
            s["onb"] = ob
            s["tar"] = os.path.join(go.SONUC, "harita_%s_%s" % (maske, etiket), "tarama", "model_gorunum", "theta", ad + ".npz")
            s["tahmin"] = os.path.join(ob, "tahmin")
        return s

    bo.ONB = ob
    bo.CIKTI = os.path.join(go.SONUC, "birlesik_%s_%s" % (maske, etiket))
    bo.spec = spec
    ko.ONBELLEK = ob
    ko.KONUM_CIKTI = os.path.join(go.SONUC, "konum_%s_%s" % (maske, etiket))
    ko.TAHMIN = os.path.join(ko.KONUM_CIKTI, "tahmin")
    zo.CIKTI = os.path.join(go.SONUC, "harita_%s_%s" % (maske, etiket))
    ucluk = (bo, ko, zo)
    go.kur = lambda maske="gercek", *a, **k: ucluk      # gercek_kos.kos vb. icerde go.kur cagirir: yonlendirmeyi SIFIRLAMASIN
    return ucluk


def kos_klasoru(maske, etiket):
    return os.path.join(go.SONUC, "kos_%s_%s" % (maske, etiket)) if etiket else os.path.join(go.SONUC, "kos_" + maske)
