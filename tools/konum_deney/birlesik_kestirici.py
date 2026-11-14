"""Birleşik konum kestiricisi E: kaynakları tutarlılık denetimiyle kaynaştırır; kare başına poz + konum güveni verir.

KAYNAKLAR (hepsi gerçek robotta da bulunur: kamera kareleri, motor PWM'i, bilinen başlangıç pozu):
  yaw   : M2D'nin aydınlanmaya dayanıklı sürümü (birlesik_yon.py: ORB'den önce yerel kontrast normalizasyonu, komut öncelli).
          Komutun dönüş hızı bir KAPI: kare başına M2D dönüşü, komutun söylediğinden (4 + 0,5*|komut dönüşü|) dereceden fazla
          ayrışırsa o kare M2D reddedilir, komut dönüşü kullanılır; M2D iç eşleşmesi 10'dan azsa da.
  öteleme (kamera çerçevesinde (sag, ileri) artımı, kare başına):
     B  zemin BEV'i, yerel kontrast normalizasyonlu (LCN) sürüm (birlesik_bev.py). Doğruluk en yüksek kaynak (adım hatası ~%8, ölçek ~1,01).
     K  komut hızı * k (k = kayma çarpanı, BEV sağlıklıyken izlenir), motor gecikmesi (tau 0,1 sn) süzgeciyle. Her zaman var; sapma +-%15, 2 derece/m.
  B kare kare iki denetimden geçer: (1) pencere denetimi: son W=22 karede (B tamam olan kareler) B yolu / komut yolu [0,70, 1,45] dışındaysa
     (komutun söylediği yolun 0,7 katından azı ya da 1,45 katından fazlası) B bu pencerede GÜVENİLMEZ: farın "yalnız %30 hareket" hatası
     B'nin kendi iç sayısına bakmadan böyle yakalanır; (2) kare denetimi: |B - K| > 0,5*|K| + 1 cm ise o kare K.
  Komut sıfırsa (süzülmüş hız ~0 ve iki tekerlek durgun) öteleme sıfır (yalnız merkez kaçıklığından doğan yay): robot kıpırdamazken
  zemin dokusundan gelen sahte hareket (gürültü) konuma işlemez.
KAYNAŞTIRMA: B güvenilirse ters-varyans ortalaması (B ~%4, K ~%13 göreli sapma varsayımı), değilse K. K'nın ölçeği k: B-K yol oranının
  yavaş (üstel unutmalı) ortalaması, [0,85, 1,15]'e kırpılır; B kaybolunca k son sağlıklı değerde donar.
KONUM GÜVENİ: sigma_poz (m) = kaynağa göre göreli hata * adım yolu birikimi. Oranlar GELİŞTİRME kayıtlarında konum hatasına NNLS ile uyduruldu:
  B ile gidilen yolda 0,004 m/m, komutla gidilen yolda 0,049 m/m (yaw terimi katsayısı 0 çıktı, atıldı). guven = exp(-sigma_poz / s0), s0 = 0,15 m.
TARAMA (zemin sınırı): (a) kare-kare/tarama-tarama konum artımı kaynağı olarak DENENDİ ve BIRAKILDI (birlesik_tarama.py: SegFormer menzili
  ~%70-90'a sıkıştırıyor, 5 karelik adımda ölçüm hatası 2-8 cm, komutun 1 cm'inden kötü, harekete ters yönde yanlı); (b) D'deki tarama-harita
  düzeltmesi (4 kare gecikmeli) DENENDİ ve BIRAKILDI: 9 geliştirme kaydının 4'ünde RMSE'yi kötüleştirdi (birlesik_degerlendir.kos_E duzelt=True)."""
import os
import sys
from collections import deque

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import konum_ortak as ko  # noqa: E402


class Param:
    def __init__(self, **kw):
        # yaw kapısı
        self.yaw_kapi_taban = 4.0            # derece / kare
        self.yaw_kapi_oran = 0.5
        self.m2d_min_ic = 10
        # B penceresi
        self.pencere = 22                    # kare (1,5 sn)
        self.min_yol = 0.08                  # m: pencerede doğrulama için gereken komut yolu
        self.oran_alt = 0.70
        self.oran_ust = 1.45
        self.kare_kapi_oran = 0.5
        self.kare_kapi_taban = 0.01
        self.bev_olcek = 0.98                # geliştirme kayıtlarında seçildi (BEV'in +%1-3 ölçek eğilimi)
        # kaynaştırma
        self.sig_b = 0.04
        self.sig_k = 0.13
        self.sig_taban = 0.002
        # komut modeli
        self.tau_motor = 0.10                # sn
        self.k_unut = 0.995                  # kare başına unutma
        self.k_min, self.k_max = 0.85, 1.15
        self.kw_min, self.kw_max = 0.85, 1.15
        # güven
        self.rel_b = 0.004
        self.rel_k = 0.049
        self.s0 = 0.15
        self.__dict__.update(kw)


class Kestirici:
    def __init__(self, poz0, v_tam, w_tam, par=None):
        self.p = par or Param()
        self.poz = (float(poz0[0]), float(poz0[1]), float(poz0[2]))
        self.v_tam, self.w_tam = float(v_tam), float(w_tam)
        self.t_onc = None
        self.k_onc = (0.0, 0.0)
        self.yaw_m_onc = None
        self.k = 1.0
        self.kw = 1.0
        self._sb = 0.0                      # k izleme toplamları (B yolu, komut yolu), üstel unutma
        self._sc = 0.0
        self._wb = 0.0                      # kw: M2D dönüşü / komut dönüşü (yalnız dönerken)
        self._wc = 0.0
        self.pen_c = deque(maxlen=self.p.pencere)
        self.pen_b = deque(maxlen=self.p.pencere)
        self.guven_b = False
        self.oran = np.nan
        self.sigma = 0.0
        self.v_lag = 0.0                    # motor gecikmesi (birinci derece) uygulanmış komut hızı (m/sn) ve dönüş (derece/sn)
        self.w_lag = 0.0

    def adim(self, t_ms, yaw_m2d, m2d_ic, bev, komut):
        """t_ms, M2D yaw (derece, ham), M2D iç eşleşme sayısı, bev=(ds, di, ok, ic), komut=(sol, sag) -> sözlük (poz + tanılar)."""
        p = self.p
        info = dict(bev_kullan=0, yaw_komut=0, komut_sifir=0, kare_red=0)
        if self.t_onc is None:
            self.t_onc, self.k_onc, self.yaw_m_onc = t_ms, (float(komut[0]), float(komut[1])), float(yaw_m2d)
            info.update(x=self.poz[0], y=self.poz[1], yaw=self.poz[2], sigma=0.0, guven=1.0, k=self.k, oran=np.nan, guven_b=False)
            return info
        dt = (t_ms - self.t_onc) / 1000.0
        sol = (self.k_onc[0] + komut[0]) / 2.0
        sag = (self.k_onc[1] + komut[1]) / 2.0
        v_komut = self.v_tam * (sol + sag) / 2.0 / 255.0
        w_komut = self.w_tam * (sol - sag) / 510.0
        a = 1.0 - np.exp(-dt / p.tau_motor)
        self.v_lag += (v_komut - self.v_lag) * a
        self.w_lag += (w_komut - self.w_lag) * a
        v, w = self.v_lag, self.w_lag
        durgun = abs(v) < 0.004 and (abs(sol) + abs(sag)) < 1e-9
        # ---- yaw
        dyaw_m = float(ko.sar(yaw_m2d - self.yaw_m_onc))
        dyaw_c = self.kw * w * dt
        kapi = p.yaw_kapi_taban + p.yaw_kapi_oran * abs(dyaw_c)
        if abs(dyaw_m - dyaw_c) > kapi or m2d_ic < p.m2d_min_ic:
            dyaw = dyaw_c
            info["yaw_komut"] = 1
        else:
            dyaw = dyaw_m
            if abs(dyaw_c) > 0.3:                          # dönüş ölçeği kw izlenir (yalnız M2D kabul edilen dönüşlerde)
                self._wb = self._wb * p.k_unut + abs(dyaw_m)
                self._wc = self._wc * p.k_unut + abs(w * dt)
                if self._wc > 3.0:
                    self.kw = float(np.clip(self._wb / self._wc, p.kw_min, p.kw_max))
        # ---- öteleme
        v_k = self.k * v
        u_k = self._geo(v_k * dt, dyaw)                    # komut tahmini, kamera çerçevesi (sag, ileri)
        mag_k = abs(v_k * dt)
        ds, di, ok, ic = bev
        ok = bool(ok) and np.isfinite(ds) and np.isfinite(di)
        yol_c = abs(v) * dt                                # ölçeksiz komut yolu
        if ok and not durgun:
            u_b = np.array([ds, di]) * p.bev_olcek
            pb = float(np.hypot(*u_b))
        else:
            u_b, pb = None, 0.0
        self.pen_c.append(yol_c if (ok and not durgun) else 0.0)
        self.pen_b.append(pb)
        sc = float(sum(self.pen_c))
        if sc >= p.min_yol:
            self.oran = float(sum(self.pen_b)) / sc
            self.guven_b = p.oran_alt <= self.oran <= p.oran_ust
        else:
            self.oran = np.nan
            self.guven_b = False
        kullan = False
        if durgun or abs(v) * dt < 1e-5:
            u = u_k
            info["komut_sifir"] = 1
            rel = 0.0
        elif u_b is not None and self.guven_b:
            if np.hypot(*(u_b - u_k)) > p.kare_kapi_oran * max(mag_k, 1e-6) + p.kare_kapi_taban:     # kare denetimi
                u = u_k
                info["kare_red"] = 1
                rel = p.rel_k
            else:
                s2b = (p.sig_b * max(mag_k, 1e-6)) ** 2 + p.sig_taban ** 2
                s2k = (p.sig_k * max(mag_k, 1e-6)) ** 2 + p.sig_taban ** 2
                u = (u_b / s2b + u_k / s2k) / (1.0 / s2b + 1.0 / s2k)
                kullan = True
                rel = p.rel_b
                self._sb = self._sb * p.k_unut + pb / p.bev_olcek          # k izleme (yalnız sağlıklı B ile)
                self._sc = self._sc * p.k_unut + yol_c
                if self._sc > 0.15:
                    self.k = float(np.clip(self._sb / self._sc, p.k_min, p.k_max))
        else:
            u = u_k
            rel = p.rel_k
        info["bev_kullan"] = int(kullan)
        # ---- poz ve belirsizlik
        self.poz = ko.poz_ekle(self.poz, float(u[0]), float(u[1]), dyaw)
        self.sigma += rel * float(np.hypot(*u))
        self.t_onc, self.k_onc, self.yaw_m_onc = t_ms, (float(komut[0]), float(komut[1])), float(yaw_m2d)
        info.update(x=self.poz[0], y=self.poz[1], yaw=self.poz[2], sigma=self.sigma, guven=float(np.exp(-self.sigma / p.s0)),
                    k=self.k, oran=self.oran, guven_b=self.guven_b)
        return info

    @staticmethod
    def _geo(v_dt, dyaw):
        """Dönme merkezi v_dt ilerlerken ve yaw dyaw dönerken kamera artımı (önceki kamera çerçevesinde (sag, ileri))."""
        z = ko.merkez_ilerle((0.0, 0.0, 0.0), v_dt, dyaw)
        return np.array([z[0], z[1]])                      # yaw0 = 0: x doğu = sag, y kuzey = ileri
