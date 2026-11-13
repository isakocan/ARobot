"""Görsel pusula (Aşama 2) için ortak parçalar: giriş dönüştürücü ve anahtar kareli yaw kestirici iskeleti.

Fikir (docs/ar-konum-yol-haritasi.md, Aşama 2):
  * Dönme ekseni dikeydir: robot yerinde dönünce eşdikdörtgen panorama yatay (dairesel) kayar. İlerleyince sol ve sağ
    taraftaki cisimler ters yöne kayar; tüm 360 derece üstünden bakınca bu etkiler büyük ölçüde birbirini götürür.
  * ANAHTAR KARE: her kare bir öncekiyle değil, açısı bilinen saklı bir kareyle karşılaştırılır. Hata kare kare
    birikmez; yalnızca anahtar kare değişirken eklenir. Anahtar kare, eşleşme kalitesi düşünce ya da komuta göre
    kamera anahtar kareden bu yana uzağa gitmişse (varsayılan 0,3 m) değişir.
  * Yönün işareti: robot saat yönünde dönerse görüntü içeriği SOLA kayar. yon_simdi = yon_anahtar + kayma_sutun*360/G.
  * Motor komutu yalnızca zayıf ön bilgi: arama penceresini daraltır, "komut sıfır ve görüntü durgun" ise açıyı tutar,
    anahtar kare değişimi için mesafe kestirir. Ölçüm DEĞİLDİR. komut_oncel=False ile hepsi kapatılır (yalnızca görüntü:
    en çok dönüş hızından türeyen bir süreklilik penceresi kalır).
  * Genellik kuralı: oda/kayıt başına ayar yok. Parametreler sabit; sim ve Theta arasındaki tek fark giriş dönüşümüdür.

Girdi türü bilgi.json'dan anlaşılır: kamera.tur == "cift_balikgozu" ise kamera.py ile eşdikdörtgene çevrilir
(gövde maskesi ve karanlık halka geçerlilik maskesinden dışlanır), değilse yeniden boyutlandırılır."""
import json
import os
import sys
import time

import cv2
import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
from kamera import Kamera  # noqa: E402
from olc import sar  # noqa: E402
import yontem_komut  # noqa: E402

# Varsayılan yükseklik bandı (ufuktan yukarı derece). Gerekçe: pusula_degerlendir.py --tarama çıktısı (rapor).
VARSAYILAN_BANT = (5.0, 65.0)


def sar_aci(a):
    """Açıyı (-180, 180] aralığına sarar (sayı)."""
    return float(sar(a))


class GirisDonusturucu:
    """Kareyi (sim eşdikdörtgen ya da Theta çift balık gözü) G x G/2 gri eşdikdörtgene çevirir.
    Dönüş: (gri float32 0..255, gecerli float32 0/1 ya da None). Süre self.son_ms'de (yalnızca dönüşüm)."""

    def __init__(self, bilgi, kamera, genislik=512):
        self.G = int(genislik)
        self.Y = self.G // 2
        tur = ((bilgi or {}).get("kamera") or {}).get("tur", "esdikdortgen")
        self.balikgozu = tur == "cift_balikgozu"
        self.kamera = kamera
        self.gecerli = None
        self.son_ms = 0.0
        if self.balikgozu:
            yol = os.path.join(KLASOR, "kamera_maske.png")
            govde = cv2.imread(yol, cv2.IMREAD_GRAYSCALE)
            m = np.ones((kamera.Y, kamera.G), np.uint8) * 255
            if govde is not None:
                m = np.where(govde > 127, 0, 255).astype(np.uint8)       # gövde = geçersiz
            # kapsam (karanlık halka dışı) ve gövde: eşdikdörtgene çevir, bilinmeyen kenarı biraz ortala
            e, kapsam = kamera.balikgozu_to_esdikdortgen(m, self.G, self.Y, cv2.INTER_LINEAR)
            g = (e.astype(np.float32) > 250) & kapsam
            g = cv2.erode(g.astype(np.uint8), np.ones((5, 5), np.uint8)).astype(np.float32)
            self.gecerli = g

    def __call__(self, kare):
        a = time.perf_counter()
        if self.balikgozu:
            gri = cv2.cvtColor(kare, cv2.COLOR_BGR2GRAY)
            gri = cv2.GaussianBlur(gri, (0, 0), 1.0)       # küçültme öncesi hafif yumuşatma (örtüşme azaltma)
            e, _ = self.kamera.balikgozu_to_esdikdortgen(gri, self.G, self.Y, cv2.INTER_LINEAR)
            gri = e.astype(np.float32)
        else:
            gri = cv2.cvtColor(kare, cv2.COLOR_BGR2GRAY) if kare.ndim == 3 else kare
            if gri.shape[1] != self.G:
                gri = cv2.resize(gri, (self.G, self.Y), interpolation=cv2.INTER_AREA)
            gri = gri.astype(np.float32)
        self.son_ms = (time.perf_counter() - a) * 1000.0
        return gri, self.gecerli

    def satir_araligi(self, el_alt, el_ust):
        """Yükseklik açısı aralığı (derece, ufuktan yukarı pozitif) -> satır [v0, v1)."""
        v0 = int(np.clip(np.floor((90.0 - el_ust) / 180.0 * self.Y), 0, self.Y - 1))
        v1 = int(np.clip(np.ceil((90.0 - el_alt) / 180.0 * self.Y), v0 + 1, self.Y))
        return v0, v1


class PusulaTaban:
    """Anahtar kareli yaw kestirici iskeleti. Alt sınıflar:
        _cikar(gri, gecerli) -> özellik nesnesi            (kare için saklanacak şey)
        _esle(oz, anahtar_oz, merkez_der, pencere_der) -> (dyaw_derece, kalite 0..1, tamam, bilgi_dict)
            dyaw: anahtar kareye göre dönüş (saat yönü +), pencere: merkez_der ± pencere_der içinde ara.
        _durgun_kucuk(gri) -> küçük görüntü (durgunluk testi için; taban sınıf yapar)
    Ortak parametreler:
        bant=(alt, ust) yükseklik derece, genislik, komut_oncel, anahtar_mesafe_m (0,3), kalite_alt,
        pencere_komut_der (komut önceliyken ±), pencere_goruntu_der (yalnız görüntü ±),
        goreli_oran (kalite, anahtar karenin ilk iyi değerinin bu oranının altına inerse anahtar değiştir)."""

    def __init__(self, bilgi=None, kamera=None, genislik=512, bant=VARSAYILAN_BANT, komut_oncel=True,
                 anahtar_mesafe_m=0.3, kalite_alt=0.5, pencere_komut_der=25.0, pencere_goruntu_der=45.0,
                 durgun_oran=0.25, goreli_oran=0.6):
        self.bilgi = bilgi or {}
        self.kamera = kamera if kamera is not None else Kamera.bilgiden(self.bilgi)
        self.G = int(genislik)
        self.bant = (float(bant[0]), float(bant[1]))
        self.komut_oncel = bool(komut_oncel)
        self.anahtar_mesafe = float(anahtar_mesafe_m)
        self.kalite_alt = float(kalite_alt)
        self.pencere_komut = float(pencere_komut_der)
        self.pencere_goruntu = float(pencere_goruntu_der)
        self.durgun_oran = float(durgun_oran)
        self.goreli_oran = float(goreli_oran)
        self.don = GirisDonusturucu(self.bilgi, self.kamera, self.G)
        v, w = yontem_komut._nominal(self.bilgi)
        self.v_tam, self.w_tam = v, w
        self.kam_ofset = abs(float(((self.bilgi.get("kamera_ofseti_robot_m") or {}).get("ileri", 0.0)) or 0.0))
        self.sifirla()

    # ---- durum ---------------------------------------------------------------
    def sifirla(self):
        self.yon = 0.0                    # son kestirim (derece, anahtar kare açısı başlangıç 0)
        self.anahtar = None               # (özellik, yon)
        self.onceki = None                # (özellik, yon, kucuk_gri) bir önceki kare
        self.t_onceki = None
        self.k_onceki = (0.0, 0.0)
        self.mesafe = 0.0                 # komuta göre anahtar kareden beri kamera yolu (m)
        self.anahtar_degisim = 0
        self.kalite = 1.0
        self.kalite_ref = 0.0             # anahtar kare değişiminden beri görülen en iyi kalite
        self.durum = "baslangic"
        self.son_donusum_ms = 0.0
        self.son_kestirim_ms = 0.0
        self.son_bilgi = {}
        self.gunluk = []                  # (kare_sira, durum, kalite, yon, anahtar_degisim)
        self._n = 0

    # ---- komuttan ön bilgi ---------------------------------------------------
    def _komut_adimi(self, t_ms, komut):
        """(dyaw_komut_derece, kamera_yolu_m) önceki kareden bu kareye (yamuk kuralı)."""
        if self.t_onceki is None:
            return 0.0, 0.0
        dt = (t_ms - self.t_onceki) / 1000.0
        sol = (self.k_onceki[0] + komut[0]) / 2.0
        sag = (self.k_onceki[1] + komut[1]) / 2.0
        v = self.v_tam * (sol + sag) / 2.0 / 255.0
        w = self.w_tam * (sol - sag) / 510.0
        return w * dt, abs(v) * dt + self.kam_ofset * abs(np.radians(w * dt))

    @staticmethod
    def _kucuk(gri):
        return cv2.resize(gri, (128, 64), interpolation=cv2.INTER_AREA)

    def _durgun(self, kucuk):
        """Görüntü bir öncekinden ayırt edilemeyecek kadar az mı değişti? Ölçek kendiliğinden: önceki karenin 1 sütun
        (128 sütunda 2,8 derece) kaydırılmış haline göre değişim oranı."""
        onc = self.onceki[2]
        ref = float(np.mean(np.abs(onc - np.roll(onc, 1, axis=1)))) + 1e-3
        d = float(np.mean(np.abs(kucuk - onc)))
        return d < self.durgun_oran * ref, d / ref

    # ---- ana giriş -----------------------------------------------------------
    def guncelle(self, kare_bgr, t_ms, komut):
        gri, gecerli = self.don(kare_bgr)
        self.son_donusum_ms = self.don.son_ms
        a = time.perf_counter()
        komut = (float(komut[0]), float(komut[1]))
        kucuk = self._kucuk(gri)
        dyaw_k, yol = self._komut_adimi(t_ms, komut) if self.komut_oncel else (0.0, 0.0)
        self.durum = "ok"
        if self.anahtar is None:                           # ilk kare = anahtar
            oz = self._cikar(gri, gecerli)
            self.anahtar = (oz, 0.0)
            self.onceki = (oz, 0.0, kucuk)
            self.yon = 0.0
            self.durum = "baslangic"
            self.kalite = 1.0
        else:
            durgun, oran = self._durgun(kucuk)
            sifir = komut == (0.0, 0.0) and self.k_onceki == (0.0, 0.0)
            if durgun and (sifir or not self.komut_oncel):
                self.durum = "tut"                         # açıyı tut; özellik çıkarılmaz. Karşılaştırma görüntüsü
                                                           # son KESTİRİLEN kareden kalır: çok yavaş dönüş de birikip yakalanır
            else:
                oz = self._cikar(gri, gecerli)
                self._kestir(oz, kucuk, dyaw_k, yol)
        self.t_onceki = t_ms
        self.k_onceki = komut
        self.son_kestirim_ms = (time.perf_counter() - a) * 1000.0
        self.gunluk.append((self._n, self.durum, float(self.kalite), float(self.yon), self.anahtar_degisim))
        self._n += 1
        return None, None, float(self.yon % 360.0)

    def _arama(self, dyaw_k):
        """(merkez_der, pencere_der): merkez ANAHTAR KAREYE göre beklenen dönüş."""
        tahmin = self.yon + (dyaw_k if self.komut_oncel else 0.0)
        pen = self.pencere_komut if self.komut_oncel else self.pencere_goruntu
        return tahmin, pen

    def _kestir(self, oz, kucuk, dyaw_k, yol):
        anahtar_oz, yon_a = self.anahtar
        tahmin, pen = self._arama(dyaw_k)
        dy, kal, tamam, bilgi = self._esle(oz, anahtar_oz, sar_aci(tahmin - yon_a), pen)
        self.son_bilgi = bilgi
        if self.komut_oncel:
            self.mesafe += yol
        if tamam and kal >= self.kalite_alt:
            self.yon = yon_a + dy
            self.kalite = kal
            self.kalite_ref = max(self.kalite_ref, kal)
            if self.komut_oncel and self.mesafe > self.anahtar_mesafe:
                self._anahtar_degistir(oz, self.yon)
                self.durum = "anahtar_mesafe"
            elif kal < self.goreli_oran * self.kalite_ref:
                # eşleşme kalitesi anahtar karenin ilk iyi değerinin altına inmeye başladı: tamamen bozulmadan,
                # bu (hâlâ iyi eşleşen) kareyi yeni anahtar yap. Komutsuz kipte mesafe tetikleyicisinin yerini tutar.
                self._anahtar_degistir(oz, self.yon)
                self.durum = "anahtar_goreli"
        else:
            # kalite düştü: bir önceki kareyi anahtar yap (açısı iyi biliniyor), bu kareyi ona göre yeniden eşle
            onc_oz, onc_yon, _ = self.onceki
            dy2, kal2, tamam2, bilgi2 = self._esle(oz, onc_oz, sar_aci(tahmin - onc_yon), pen)
            self._anahtar_degistir(onc_oz, onc_yon)
            if tamam2 and kal2 >= self.kalite_alt * 0.6:
                self.yon = onc_yon + dy2
                self.kalite = kal2
                self.durum = "anahtar_kalite"
                self.son_bilgi = bilgi2
            else:                                             # eşleşme yok: komuta (varsa) ya da son açıya güven
                self.yon = onc_yon + (dyaw_k if self.komut_oncel else 0.0)
                self.kalite = 0.0
                self.durum = "yedek"
        self.onceki = (oz, self.yon, kucuk)

    def _anahtar_degistir(self, oz, yon):
        self.anahtar = (oz, float(yon))
        self.mesafe = 0.0
        self.kalite_ref = 0.0
        self.anahtar_degisim += 1

    # ---- alt sınıf -----------------------------------------------------------
    def _cikar(self, gri, gecerli):
        raise NotImplementedError

    def _esle(self, oz, anahtar_oz, merkez_der, pencere_der):
        raise NotImplementedError
