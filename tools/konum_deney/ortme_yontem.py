"""Örtme (gizleme) kararı yöntemleri. Her yöntem, nesnenin 7 dikey diliminin her biri için "gizli mi" kararı verir (True = gizli).

  HARITA   : kestirilen kameradan dilim orta noktasına görüş hattı, robotun KENDİ doluluk haritasında (o ana kadar kurulan) dolu hücreden geçiyor mu.
             (gosterim_ar.Harita.engelli; mevcut gösterimdeki yöntemle aynı)
  TARAMA   : canlı zemin sınırı taraması. Tarama = robota göre azimut k (derece) için görünen zeminin bittiği mesafe s[k] (360 değer) ve güven g[k].
             Dilimin zemine değdiği nokta tarama kamerasından d uzaklıkta ve azimut a ise: s[a] (3 komşu kutunun ortancası) < d - pay(d) ise
             dilim bir şeyin ARKASINDA -> gizli. pay(d) = 0,10 + 0,05 d (m): zemin sınırının mesafe hatası uzaklıkla büyür.
             Güvensiz değerler (güven < 0,2, sınır < 0,8 m (kör bölge kenarı) ya da sınır güvenilir menzilin (3,2 m) ötesi) KARAR VERMEZ ("bilinmiyor"): HARITA'sız TARAMA'da gösterilir.
             Güvenilir menzilin ötesindeki bir nesne, sınır güvenilir menzil içindeyse yine gizlenebilir; sınır menzil dışındaysa karar verilemez.
  BIRLESIK : TARAMA'nın karar verebildiği dilimlerde TARAMA, veremediklerinde HARITA.
Tarama kaynağı: "taze" = o karenin kendi taraması (üst sınır); "gercekci" = 3 Hz (her 5. kare) + 0,2 sn işleme gecikmesi, son gelen tarama
(nesne konumu tarama anındaki kestirilen kameradan hesaplanır: dünya noktası -> tarama kamerası çerçevesi)."""
import os
import sys

import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
import konum_ortak as ko  # noqa: E402
import ortme_veri as ov  # noqa: E402

PAY0, PAY1 = 0.10, 0.05
KOMSU = "ortanca"           # 3 komşu kutudan sınır değeri: ortanca (tek kutuluk hataya dayanıklı) | merkez | en_kucuk
S_MIN = 0.80                  # kör bölge: zemin en yakın 0,71 m'de başlıyor (gövde maskesi); bundan kısa sınır = 'zemin görünmüyor', konumu güvenilmez


def pay(d):
    return PAY0 + PAY1 * d


def tarama_karar(tarama, poz_tarama, noktalar, menzil=None):
    """tarama = (mesafe(360), guven(360), kare) ya da None. poz_tarama = tarama anının (x, y, yaw) kestirimi (kamera). noktalar (K,2) dünya.
    Dönüş: (gizli (K,), bilinen (K,)). Bilinmeyen dilimde gizli=False."""
    K = len(noktalar)
    if tarama is None:
        return np.zeros(K, bool), np.zeros(K, bool)
    m, g, _ = tarama
    menzil = menzil if menzil is not None else 3.2132
    sag, ileri = ko.dunya_to_robot(poz_tarama, noktalar[:, 0], noktalar[:, 1])
    d = np.hypot(sag, ileri)
    az = np.degrees(np.arctan2(sag, ileri)) % 360.0
    k = np.round(az).astype(int) % 360
    tri = np.stack([m[(k - 1) % 360], m[k], m[(k + 1) % 360]], 1)
    s = {"ortanca": lambda t: np.nanmedian(t, 1), "merkez": lambda t: t[:, 1], "en_kucuk": lambda t: np.nanmin(t, 1)}[KOMSU](tri)
    guven_ok = (g[k] >= ov.GUVEN_MIN) & np.isfinite(s)
    guven_ok &= s >= S_MIN                                         # kör bölge kenarına düşen sınır: duvar mı, bölütleme hatası mı ayırt edilemez
    sinir_ici = s <= menzil                                        # sınır güvenilir menzil içinde
    gizli = guven_ok & sinir_ici & (s < d - pay(d))
    bos_bilinir = guven_ok & ~sinir_ici & (d + pay(d) < menzil)    # sınır menzil ötesinde ve nesne menzil içinde: nesneye kadar zemin görünüyor
    onde = guven_ok & sinir_ici & (s >= d - pay(d))                # zemin nesneye kadar görünüyor
    bilinen = gizli | bos_bilinir | onde
    return gizli, bilinen


def birlesik(gizli_t, bilinen_t, gizli_h):
    """Tarama bildiğinde tarama, bilmediğinde harita."""
    return np.where(bilinen_t, gizli_t, gizli_h)


def birlesik_uyum(gizli_t, bilinen_t, gizli_h):
    """Alternatif: tarama bildiğinde, haritayla ÇELİŞİRSE nesne gösterilir (gizli yalnızca iki yöntem de gizliyorsa); bilmediğinde harita."""
    return np.where(bilinen_t, gizli_t & gizli_h, gizli_h)
