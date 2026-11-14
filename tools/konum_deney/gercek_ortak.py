"""Gerçek robot kayıtları (kayitlar/gercek/<ad>) için ortak ayar: mevcut sim hattını YÖNTEM DOSYALARINI DEĞİŞTİRMEDEN gerçek kayıtlara yönlendirir.
Yalnız YOL sabitleri ve gövde maskesi dosyası çalışma zamanında yönlendirilir (sinama_calistir.py ile aynı yöntem):
  kur(maske='eski'|'gercek'):
    * cv2.imread('.../kamera_maske.png') çağrıları 'gercek' seçilirse kamera_maske_gercek.png'ye yönlenir (konum_bev, pusula_ortak, zemin_ortak kendi yoluyla okur).
    * birlesik_ortak.spec(ad) gerçek kayıtlar için yol sözlüğü döndürür; önbellek: kayitlar/sonuclar/gercek/onbellek_<maske>/ .
    * zemin_ortak / konum_ortak KAYITLAR, CIKTI, ONBELLEK yolları.
  KAM_H (0,255) sabiti konum_bev.BevTablo varsayılan bağımsız değişkeni (tanım anında bağlanmış) ve konum_ortak.KAM_H olarak durur, DOSYALAR DEĞİŞTİRİLMEDİ:
    birlesik hattı (birlesik_bev.bev_lcn) yüksekliği kayıt bilgi.json'undaki kamera_yuksekligi_m'den okur (0,17: doğrudan kullanılır);
    ham konum_bev.bev_hesapla kullanılırsa BevTablo(h=...) bağımsız değişkeni gerekir (burada kullanılmıyor).
    zemin_ortak.KayitGirdisi h'yi bilgi.json'dan okur (0,17). zemin_degerlendir.py:154,201 ve harita_calistir.py:89'daki sabit 0,255'ler yalnız sim
    değerlendirme/ölçüt yolunda; gerçek hatta KULLANILMIYOR."""
import os
import sys

import cv2

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
KOK = os.path.abspath(os.path.join(KLASOR, "..", ".."))
KAYITLAR = os.path.join(KOK, "kayitlar")
GERCEK = os.path.join(KAYITLAR, "gercek")
SONUC = os.path.join(KAYITLAR, "sonuclar", "gercek")
ADLAR = ["1_bekleme", "2a_vitessiz_duz", "2b_otomatik_duz", "3_vitessiz_donus", "4_parke_vitessiz", "5_saksi_turu",
         "6a_far_isik_acik", "6b_far_karanlik", "6c_karanlik_surus"]

OLCUM = ["olcum_ham"]          # yer gerçeği taşıyan ham JPEG kaydı (20-12-25); ADLAR varsayılan döngülere girmesin diye ayrı
_imread0 = cv2.imread
_MASKE = {"mod": "eski"}


def _imread(yol, *a, **k):
    if _MASKE["mod"] == "gercek" and str(yol).replace("\\", "/").endswith("tools/konum_deney/kamera_maske.png"):
        yol = os.path.join(KLASOR, "kamera_maske_gercek.png")
    return _imread0(yol, *a, **k)


def onbellek(maske):
    return os.path.join(SONUC, "onbellek_" + maske)


def kur(maske="eski"):
    _MASKE["mod"] = maske
    cv2.imread = _imread
    import birlesik_ortak as bo
    import konum_ortak as ko
    import zemin_ortak as zo
    ob = onbellek(maske)
    os.makedirs(ob, exist_ok=True)
    bo.ONB = ob
    bo.CIKTI = os.path.join(SONUC, "birlesik_" + maske)
    orig = bo.spec

    def spec(ad):
        if ad in ADLAR or ad in OLCUM:
            return dict(ad=ad, kume="gercek", theta=os.path.join(GERCEK, ad), sim=None, onb=ob,
                        tar=os.path.join(SONUC, "harita_" + maske, "tarama", "model_gorunum", "theta", ad + ".npz"), tahmin=os.path.join(ob, "tahmin"))
        return orig(ad)

    bo.spec = spec
    ko.KAYITLAR = KAYITLAR
    ko.KONUM_CIKTI = os.path.join(SONUC, "konum_" + maske)
    ko.ONBELLEK = ob
    ko.TAHMIN = os.path.join(ko.KONUM_CIKTI, "tahmin")
    ko.KAYIT_ADLARI = list(ADLAR)
    zo.KAYITLAR = KAYITLAR
    zo.CIKTI = os.path.join(SONUC, "harita_" + maske)
    K0 = zo.Kayit

    def K(p):                       # zemin_ortak.KayitGirdisi KAYITLAR/theta/<ad> açar: gerçek kayıtlara yönlendir
        ad = os.path.basename(p)
        if (ad in ADLAR or ad in OLCUM) and os.path.basename(os.path.dirname(p)) == "theta":
            p = os.path.join(GERCEK, ad)
        return K0(p)

    zo.Kayit = K
    return bo, ko, zo
