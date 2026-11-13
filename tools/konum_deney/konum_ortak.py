"""Konum (x, y, yaw) kestirimi + harita (kamera tabanlı SLAM) deneyi: ortak yollar, poz sözleşmeleri, yardımcılar.

POZ SÖZLEŞMESİ (gercek_konum.csv ile aynı):
  (x, y) = KAMERANIN zemindeki izdüşümü, x doğu, y kuzey (m); yaw = kuzeyden SAAT YÖNÜNDE derece.
  Robotun ileri birim vektörü  f = (sin yaw, cos yaw);  sağ birim vektörü  s = (cos yaw, -sin yaw).
  Kamera dönme merkezinin KAM_GERI (0,07 m) ARKASINDA: dönme merkezi c = kamera + KAM_GERI * f.
  Yerinde dönmede kamera, merkez çevresinde bir daire çizer; bu poz bileşiminde (poz_ekle, merkez_ilerle) hesaba katılır.
Robota göre (kamera çerçevesi) nokta: (sag, ileri) metre. Dünya noktası = kamera + sag*s + ileri*f.
Tarama azimutu k derece: 0 = ileri, saat yönünde artar (ışın yönü = (sin(yaw+k), cos(yaw+k)))."""
import json
import os
import sys

import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
KOK = os.path.abspath(os.path.join(KLASOR, "..", ".."))
sys.path.insert(0, KLASOR)
import zemin_ortak as zo  # noqa: E402

KAYITLAR = zo.KAYITLAR
KONUM_CIKTI = os.path.join(KAYITLAR, "sonuclar", "konum")
ONBELLEK = os.path.join(KONUM_CIKTI, "onbellek")
TAHMIN = os.path.join(KONUM_CIKTI, "tahmin")
KAYIT_ADLARI = list(zo.KAYIT_ADLARI)
KAM_GERI = 0.07                  # kamera dönme merkezinin ne kadar ARKASINDA (m); bilgi.json kamera_ofseti_robot_m.ileri = -0.07
KAM_H = 0.255                    # mercek yüksekliği (m)


def klasor_yap(*yol):
    p = os.path.join(*yol)
    os.makedirs(p, exist_ok=True)
    return p


def ileri_vek(yon_derece):
    y = np.radians(yon_derece)
    return np.array([np.sin(y), np.cos(y)])


def sag_vek(yon_derece):
    y = np.radians(yon_derece)
    return np.array([np.cos(y), -np.sin(y)])


def robot_to_dunya(poz, sag, ileri):
    """poz = (x, y, yaw_derece); robota göre (sag, ileri) nokta/dizi -> dünya (x, y). Dizi girişte (N,) beklenir."""
    y = np.radians(poz[2])
    s, c = np.sin(y), np.cos(y)
    sag = np.asarray(sag, float)
    ileri = np.asarray(ileri, float)
    return poz[0] + sag * c + ileri * s, poz[1] - sag * s + ileri * c


def dunya_to_robot(poz, x, y):
    y0 = np.radians(poz[2])
    s, c = np.sin(y0), np.cos(y0)
    dx = np.asarray(x, float) - poz[0]
    dy = np.asarray(y, float) - poz[1]
    return dx * c - dy * s, dx * s + dy * c          # (sag, ileri)


def poz_ekle(poz, d_sag, d_ileri, d_yaw):
    """Kamera çerçevesinde görece hareket (önceki kameraya göre sağ/ileri öteleme, yaw değişimi) uygular.
    Dönüş yeni poz. (Öteleme, ÖNCEKİ yön çerçevesinde verilir.)"""
    x, y = robot_to_dunya(poz, d_sag, d_ileri)
    return (float(x), float(y), float((poz[2] + d_yaw) % 360.0))


def merkez_ilerle(poz, v_dt, d_yaw, geri=KAM_GERI):
    """Robot DÖNME MERKEZİNİN yönüne (kamera yönü ile aynı) v_dt kadar ilerlemesi ve d_yaw dönmesi sonrası kamera pozu.
    Kamera merkezin geri kadar arkasında olduğundan: c = kamera + geri*f0; merkez ilerler: c' = c + v_dt*(f ortalaması);
    kamera' = c' - geri*f1. Yay yerine orta açı (yarım dönüşle) ilerleme kullanılır (kare başına küçük adımlar)."""
    y0 = np.radians(poz[2])
    y1 = y0 + np.radians(d_yaw)
    ym = 0.5 * (y0 + y1)
    cx = poz[0] + geri * np.sin(y0)
    cy = poz[1] + geri * np.cos(y0)
    cx += v_dt * np.sin(ym)
    cy += v_dt * np.cos(ym)
    return (float(cx - geri * np.sin(y1)), float(cy - geri * np.cos(y1)), float(np.degrees(y1) % 360.0))


def sar(a):
    a = (np.asarray(a, float) + 180.0) % 360.0 - 180.0
    return np.where(a == -180.0, 180.0, a)


def kayit_bilgi(ad, tur):
    with open(os.path.join(KAYITLAR, tur, ad, "bilgi.json"), encoding="utf-8") as f:
        return json.load(f)


def nominal_hizlar(bilgi):
    """(v_tam m/s, w_tam derece/sn): tam PWM'de (sol=sag=255) ileri hız ve (255,-255) yerinde dönüş. yontem_komut ile aynı kural."""
    import yontem_komut
    return yontem_komut._nominal(bilgi)


def komut_hizlari(bilgi, sol, sag):
    """PWM (sol, sag) -> (v m/s ileri, w derece/sn saat yönü), nominal dönüşümle."""
    v_tam, w_tam = nominal_hizlar(bilgi)
    return v_tam * (sol + sag) / 2.0 / 255.0, w_tam * (sol - sag) / 510.0
