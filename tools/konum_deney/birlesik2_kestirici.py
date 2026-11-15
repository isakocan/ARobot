"""E2: robot profilini KENDİ SÜRÜŞÜNDEN öğrenen birleşik konum kestiricisi (E'nin devamı; E dosyalarına dokunulmaz).

E'den farkı: PWM -> hız eşlemesi (sim sabiti 0,55 m/sn, 197 derece/sn, ikisi de PWM ile doğrusal) KODDA YOK. İki küçük parçalı tablo çevrimiçi öğrenilir:
  v(|p|)   p = (sol+sag)/2 (PWM ortalaması)   -> ileri hız (m/sn)           kaynak: B (kuşbakışı zemin dokusu) artımı / dt
  w(|q|)   q = (sol-sag)/2 (PWM farkı yarısı) -> dönüş hızı (derece/sn)     kaynak: M2D (panoramada nokta eşleme) yaw artımı / dt
Tablo: |x| için 8 sabit kutu (0,25,50,75,100,125,160,200,256); her kutuda son N gözlemin MEDYANI (sayı + medyan kutu sınırı değil, kutudaki ortanca x);
tahmin = (0,0) ve kutu ortancalarından geçen parçalı doğrusal eğri (ölü bölge ve doğrusal olmama kendiliğinden oluşur); kutu dolduktan sonra kapılar
ÖĞRENİLEN profile göre çalışır; kutular sabit uzunluklu halka tamponu (yeni zemin eskiyi yavaşça iter).
Gözlem kabulü (profil öğrenilmeden önce yalnız kamera ölçümlerinin kendi iç tutarlılığı):
  yaw: M2D iç eşleşme >= 10, komut o anda (son 5 karede) sabit, |w_gozlem| <= 360 derece/sn (fiziksel üst sınır, evrensel), sonra kutu oturduktan
       sonra E'nin kapısı (4 + 0,5*|komut dönüşü|) ÖĞRENİLEN profile göre (kutu kısmen öğrenilmişse 4 + 1,0*|dyaw_c|, hiç yoksa 4 + 1,5*|dyaw_c|).
  hız: B tamam, komut düz (|q| <= 20) ve sabit, B artımının yönü komutun yönüyle tutarlı (|yan| <= 0,35*|ileri| ve işaret komutla aynı), 0,02 < dt < 0,25.
Profil oturmadan (|p| kutusu dolmadan) B kare başına yalnız yön tutarlılığıyla kabul edilir ve K (komut) kullanılmaz; oturduktan sonra E'nin
pencere (0,70-1,45) ve kare (|B-K| > 0,5|K| + 1 cm) denetimleri aynen, K = öğrenilen profil.
Başlangıç öncülü (YALNIZ hiç kutu öğrenilmemişken): v = 0,40 m/sn * |p|/255, w = 100 derece/sn * |q|/255 (geniş; yalnız ilk saniyelerde K ve kapı için).
kw (dönüş ölçeği izleme) kaldırıldı: dönüş profili w(q) aynı işi doğrudan öğrenir; k (kayma çarpanı) AYNEN duruyor.
bev_olcek varsayılan 1,0 (E: 0,98, sim'e göre seçilmişti)."""
import os
import sys
from collections import deque

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import konum_ortak as ko  # noqa: E402

KUTULAR = np.array([0.0, 25.0, 50.0, 75.0, 100.0, 125.0, 160.0, 200.0, 256.0])


class Param:
    def __init__(self, **kw):
        # yaw kapısı (E ile aynı taban/oran)
        self.yaw_kapi_taban = 4.0
        self.yaw_kapi_oran = 0.5
        self.yaw_kapi_oran_kismen = 1.0
        self.yaw_kapi_oran_bos = 1.5
        self.bosluk_max = 45                 # kare (3 sn): köprü en çok bu kadar kare bekler
        self.w_fiziksel_ust = 360.0          # derece/sn: gözlem üst sınırı (evrensel, robota özel değil)
        self.m2d_min_ic = 10
        # B penceresi (E ile aynı)
        self.pencere = 22
        self.min_yol = 0.08
        self.oran_alt = 0.70
        self.oran_ust = 1.45
        self.kare_kapi_oran = 0.5
        self.kare_kapi_taban = 0.01
        self.bev_olcek = 1.0
        # kaynaştırma (E ile aynı)
        self.sig_b = 0.04
        self.sig_k = 0.13
        self.sig_taban = 0.002
        # komut modeli
        self.tau_motor = 0.10
        self.k_unut = 0.995
        self.k_min, self.k_max = 0.85, 1.15
        # güven (E ile aynı)
        self.rel_b = 0.004
        self.rel_k = 0.049
        self.s0 = 0.15
        # profil öğrenme
        self.halka_v = 60                    # kutu başına en çok gözlem (hız)
        self.halka_w = 80                    # (dönüş)
        self.n_otur = 8                      # kutu "oturdu" için en az gözlem
        self.sabit_kare = 5                  # komutun sabit sayılması için geriye bakış
        self.sabit_tol = 20.0                # PWM
        self.duz_q = 20.0                    # hız öğrenirken |q| en çok
        self.yon_yan_oran = 0.35
        self.yon_yan_oran_yay = 0.8
        self.dt_min, self.dt_max = 0.02, 0.25
        self.oncul_v = 0.40                  # m/sn tam PWM, yalnız hiçbir kutu öğrenilmemişken
        self.oncul_w = 100.0                 # derece/sn tam PWM farkı
        self.__dict__.update(kw)


SATIR_Q = np.array([0.0, 20.0, 70.0, 140.0, 256.0])      # hız tablosunun rejimi: |q| sınıfı (düz / hafif yay / yay / yerinde dönüşe yakın)
SATIR_P = np.array([0.0, 50.0, 110.0, 180.0, 256.0])     # dönüş tablosunun rejimi: |p| sınıfı (yerinde / yavaş / orta / hızlı)


def satir_q(q):
    return int(min(max(np.searchsorted(SATIR_Q, abs(q), side="right") - 1, 0), len(SATIR_Q) - 2))


def satir_p(p):
    return int(min(max(np.searchsorted(SATIR_P, abs(p), side="right") - 1, 0), len(SATIR_P) - 2))


class Tablo:
    """|x| -> değer; REJİM satırı başına 8 kutu, kutu başına medyan halka tamponu, satır içinde (0,0)'dan geçen parçalı doğrusal tahmin.
    Neden rejim: gerçek robotta aynı |q|'ya (PWM farkı) yerinde dönerken 74 derece/sn, tam gazla yay çizerken 18 derece/sn düşüyor (kızaklı sürüş);
    tek boyutlu tablo ikisini karıştırır. Satır boşsa öncül (başka rejimin değeri ödünç alınmaz: ilk deneme 73. sn'de yay değerini yerinde dönüşe uygulayıp yönü 60 derece kaçırdı)."""

    def __init__(self, halka, n_otur, oncul_egim, n_satir=4, ad=""):
        self.ad = ad
        self.n_satir = n_satir
        self.xs = [[deque(maxlen=halka) for _ in range(len(KUTULAR) - 1)] for _ in range(n_satir)]
        self.ys = [[deque(maxlen=halka) for _ in range(len(KUTULAR) - 1)] for _ in range(n_satir)]
        self.n_otur = n_otur
        self.oncul = oncul_egim              # değer / x (öncül)

    def kutu(self, x):
        return int(min(max(np.searchsorted(KUTULAR, x, side="right") - 1, 0), len(KUTULAR) - 2))

    def ekle(self, x, y, satir=0):
        b = self.kutu(abs(x))
        self.xs[satir][b].append(abs(x))
        self.ys[satir][b].append(y)

    def _satir_noktalar(self, r):
        return [(float(np.median(self.xs[r][b])), float(np.median(self.ys[r][b]))) for b in range(len(KUTULAR) - 1) if len(self.ys[r][b]) >= self.n_otur]

    def durum(self, x, satir=0):
        """2 = bu satırdaki kutu oturdu, 1 = satırın başka kutuları oturdu (ara değer), 0 = satır boş (öncül)."""
        if len(self.ys[satir][self.kutu(abs(x))]) >= self.n_otur:
            return 2
        return 1 if self._satir_noktalar(satir) else 0

    def noktalar(self, satir=0):
        return self._satir_noktalar(satir)

    def tum_noktalar(self):
        """[(satir, x, y)] tüm oturmuş kutular."""
        return [(r, x, y) for r in range(self.n_satir) for x, y in self._satir_noktalar(r)]

    def tahmin(self, x, satir=0):
        """Yalnız KENDİ rejim satırı (başka rejimin değeri kullanılmaz: yerinde dönüş ile yay farklı dinamik); satır boşsa öncül."""
        x = abs(float(x))
        pts = self._satir_noktalar(satir)
        if not pts:
            return self.oncul * x
        px = np.array([0.0] + [p[0] for p in pts])
        py = np.array([0.0] + [p[1] for p in pts])
        if x <= px[-1]:
            return float(np.interp(x, px, py))
        return float(py[-1] * x / max(px[-1], 1.0))


class Kestirici:
    def __init__(self, poz0, par=None):
        self.p = par or Param()
        p = self.p
        self.poz = (float(poz0[0]), float(poz0[1]), float(poz0[2]))
        self.tv = Tablo(p.halka_v, p.n_otur, p.oncul_v / 255.0, 4, "v")
        self.tw = Tablo(p.halka_w, p.n_otur, p.oncul_w / 255.0, 4, "w")
        self.t_onc = None
        self.k_onc = (0.0, 0.0)
        self.yaw_m_onc = None
        self.yaw_m_ref = 0.0
        self.t_ref = 0
        self.acc = 0.0
        self.acc_h = 0.0
        self.bosluk = 0
        self.onc_iyi = False
        self.k = 1.0
        self._sb = 0.0
        self._sc = 0.0
        self.pen_c = deque(maxlen=p.pencere)
        self.pen_b = deque(maxlen=p.pencere)
        self.gecmis = deque(maxlen=p.sabit_kare)       # (p, q) komut geçmişi
        self.guven_b = False
        self.oran = np.nan
        self.sigma = 0.0
        self.v_lag = 0.0
        self.w_lag = 0.0

    # ---- profil tahminleri (işaretli, PWM'den)
    def v_komut(self, sol, sag):
        pp, qq = (sol + sag) / 2.0, (sol - sag) / 2.0
        if abs(pp) < 8.0:
            return 0.0
        return float(np.sign(pp)) * self.tv.tahmin(pp, satir_q(qq))

    def w_komut(self, sol, sag):
        pp, qq = (sol + sag) / 2.0, (sol - sag) / 2.0
        return float(np.sign(qq)) * self.tw.tahmin(qq, satir_p(pp))

    def profil_ozeti(self):
        """Zaman çizimi için: v(255, düz), v(150, düz), w(255, yerinde), w(110, yerinde), v(150, yay q=100), w(110, hızlı yay p=200)."""
        return (self.tv.tahmin(255.0, 0), self.tv.tahmin(150.0, 0), self.tw.tahmin(255.0, 0), self.tw.tahmin(110.0, 0),
                self.tv.tahmin(150.0, satir_q(100.0)), self.tw.tahmin(110.0, satir_p(200.0)))

    def _sabit(self, pq):
        if len(self.gecmis) < self.p.sabit_kare:
            return False
        g = np.array(self.gecmis)
        return bool(np.all(g.max(0) - g.min(0) <= self.p.sabit_tol))

    def adim(self, t_ms, yaw_m2d, m2d_ic, bev, komut):
        p = self.p
        info = dict(bev_kullan=0, yaw_komut=0, komut_sifir=0, kare_red=0, yaw_ogren=0, hiz_ogren=0)
        sol0, sag0 = float(komut[0]), float(komut[1])
        if self.t_onc is None:
            self.t_onc, self.k_onc, self.yaw_m_onc = t_ms, (sol0, sag0), float(yaw_m2d)
            self.yaw_m_ref, self.t_ref = float(yaw_m2d), t_ms
            self.gecmis.append(((sol0 + sag0) / 2.0, (sol0 - sag0) / 2.0))
            info.update(x=self.poz[0], y=self.poz[1], yaw=self.poz[2], sigma=0.0, guven=1.0, k=self.k, oran=np.nan, guven_b=False)
            return info
        dt = (t_ms - self.t_onc) / 1000.0
        sol = (self.k_onc[0] + sol0) / 2.0
        sag = (self.k_onc[1] + sag0) / 2.0
        pm, qm = (sol + sag) / 2.0, (sol - sag) / 2.0
        self.gecmis.append(((sol0 + sag0) / 2.0, (sol0 - sag0) / 2.0))
        sabit = self._sabit(self.gecmis[-1]) and (1e-3 < dt < 1.0)
        v_hedef = self.v_komut(sol, sag)
        w_hedef = self.w_komut(sol, sag)
        a = 1.0 - np.exp(-dt / p.tau_motor)
        self.v_lag += (v_hedef - self.v_lag) * a
        self.w_lag += (w_hedef - self.w_lag) * a
        v, w = self.v_lag, self.w_lag
        durgun = abs(v) < 0.004 and (abs(sol) + abs(sag)) < 1e-9
        # ---- yaw (E'den fark: son KABUL EDİLEN M2D karesine göre boşluk köprüsü; M2D anahtar kareye göre mutlak olduğundan,
        # iç eşleşmesi düşük kareler atlanınca sonraki iyi kare aradaki dönüşün tamamını (komut toplamıyla tutarlıysa) geri kazandırır)
        dyaw_c = w * dt
        dyaw_h = w_hedef * dt                      # gecikmesiz (kararlı hal) tahmin: gerçek motor gecikmesi ölçülmediği için kapı ikisine de bakar
        acc_tot = self.acc + dyaw_c
        acc_h = self.acc_h + dyaw_h
        t_ref_dt = (t_ms - self.t_ref) / 1000.0
        dyaw_m_tot = float(ko.sar(yaw_m2d - self.yaw_m_ref))
        w_obs = dyaw_m_tot / t_ref_dt if t_ref_dt > 1e-3 else 0.0
        durum = self.tw.durum(qm, satir_p(pm)) if abs(qm) > 1e-9 else 2
        oran = {2: p.yaw_kapi_oran, 1: p.yaw_kapi_oran_kismen, 0: p.yaw_kapi_oran_bos}[durum]
        kapi = p.yaw_kapi_taban + oran * max(abs(acc_tot), abs(acc_h))
        iyi_ic = m2d_ic >= p.m2d_min_ic
        kabul_toplam = iyi_ic and min(abs(dyaw_m_tot - acc_tot), abs(dyaw_m_tot - acc_h)) <= kapi and abs(w_obs) <= p.w_fiziksel_ust
        # kare kare kapı (E'nin kapısı): önceki karenin M2D'si güvenilirse yalnız bu karenin artışı komutla tutarlı mı
        dyaw_m1 = float(ko.sar(yaw_m2d - self.yaw_m_onc))
        kabul_kare = iyi_ic and self.onc_iyi and min(abs(dyaw_m1 - dyaw_c), abs(dyaw_m1 - dyaw_h)) <= p.yaw_kapi_taban + oran * max(abs(dyaw_c), abs(dyaw_h)) and abs(dyaw_m1 / max(dt, 1e-3)) <= p.w_fiziksel_ust
        kabul = kabul_toplam or kabul_kare
        ardisik = self.bosluk == 0
        if kabul_toplam:
            dyaw = dyaw_m_tot - self.acc
            self.yaw_m_ref, self.t_ref, self.acc, self.acc_h, self.bosluk = float(yaw_m2d), t_ms, 0.0, 0.0, 0
        elif kabul_kare:
            dyaw = dyaw_m1
            self.yaw_m_ref, self.t_ref, self.acc, self.acc_h, self.bosluk = float(yaw_m2d), t_ms, 0.0, 0.0, 0
        else:
            dyaw = dyaw_c
            info["yaw_komut"] = 1
            self.acc = acc_tot
            self.acc_h = acc_h
            self.bosluk += 1
            if self.bosluk > p.bosluk_max and iyi_ic:             # çok uzun uyuşmazlık: kendini yeniden eşle
                self.yaw_m_ref, self.t_ref, self.acc, self.acc_h, self.bosluk = float(yaw_m2d), t_ms, 0.0, 0.0, 0
        self.onc_iyi = bool(iyi_ic)
        # yaw öğrenme: ardışık kabul, komut sabit, dönüş komutu var
        if kabul and ardisik and sabit and abs(qm) >= 8.0 and p.dt_min < dt < p.dt_max:
            self.tw.ekle(qm, w_obs * float(np.sign(qm)), satir_p(pm))
            info["yaw_ogren"] = 1
        # ---- öteleme
        v_k = self.k * v
        u_k = self._geo(v_k * dt, dyaw)
        mag_k = abs(v_k * dt)
        ds, di, ok, ic = bev
        ok = bool(ok) and np.isfinite(ds) and np.isfinite(di)
        yol_c = abs(v) * dt
        if ok and not durgun:
            u_b = np.array([ds, di]) * p.bev_olcek
            pb = float(np.hypot(*u_b))
        else:
            u_b, pb = None, 0.0
        vdurum = self.tv.durum(pm, satir_q(qm)) if abs(pm) > 8.0 else 2
        # hız öğrenme (B yön tutarlılığıyla; yaylarda ileri bileşen pozitif ve yan bileşen sınırlı)
        yon_tutarli = False
        if u_b is not None and abs(pm) > 8.0:
            ileri = u_b[1] * float(np.sign(pm))
            yan = p.yon_yan_oran if abs(qm) <= p.duz_q else p.yon_yan_oran_yay
            yon_tutarli = ileri > 0 and abs(u_b[0]) <= yan * ileri
            if yon_tutarli and sabit and p.dt_min < dt < p.dt_max:
                self.tv.ekle(pm, pb / dt, satir_q(qm))
                info["hiz_ogren"] = 1
        self.pen_c.append(yol_c if (ok and not durgun) else 0.0)
        self.pen_b.append(pb)
        sc = float(sum(self.pen_c))
        if vdurum == 2 and sc >= p.min_yol:
            self.oran = float(sum(self.pen_b)) / sc
            self.guven_b = p.oran_alt <= self.oran <= p.oran_ust
        else:
            self.oran = np.nan
            self.guven_b = False
        kullan = False
        if durgun or abs(v) * dt < 1e-5 or abs(pm) <= 8.0:
            u = u_k                                              # yerinde dönüş / komut yok: yalnız merkez kaçıklığı yayı (E ile aynı)
            info["komut_sifir"] = 1
            rel = 0.0
        elif u_b is not None and vdurum != 2:
            # bu rejimde profil oturmadı: K güvenilmez, B yalnız iç tutarlılıkla (ileri yönlü, yan bileşen sınırlı) kabul edilir
            if yon_tutarli:
                u = u_b
                kullan = True
                rel = p.rel_b
            else:
                u = u_k
                rel = p.rel_k
        elif u_b is not None and self.guven_b:
            if np.hypot(*(u_b - u_k)) > p.kare_kapi_oran * max(mag_k, 1e-6) + p.kare_kapi_taban:
                u = u_k
                info["kare_red"] = 1
                rel = p.rel_k
            else:
                s2b = (p.sig_b * max(mag_k, 1e-6)) ** 2 + p.sig_taban ** 2
                s2k = (p.sig_k * max(mag_k, 1e-6)) ** 2 + p.sig_taban ** 2
                u = (u_b / s2b + u_k / s2k) / (1.0 / s2b + 1.0 / s2k)
                kullan = True
                rel = p.rel_b
                self._sb = self._sb * p.k_unut + pb
                self._sc = self._sc * p.k_unut + yol_c
                if self._sc > 0.15:
                    self.k = float(np.clip(self._sb / self._sc, p.k_min, p.k_max))
        else:
            u = u_k
            rel = p.rel_k
        info["bev_kullan"] = int(kullan)
        self.poz = ko.poz_ekle(self.poz, float(u[0]), float(u[1]), dyaw)
        self.sigma += rel * float(np.hypot(*u))
        self.t_onc, self.k_onc, self.yaw_m_onc = t_ms, (sol0, sag0), float(yaw_m2d)
        info.update(x=self.poz[0], y=self.poz[1], yaw=self.poz[2], sigma=self.sigma, guven=float(np.exp(-self.sigma / p.s0)),
                    k=self.k, oran=self.oran, guven_b=self.guven_b)
        return info

    @staticmethod
    def _geo(v_dt, dyaw):
        z = ko.merkez_ilerle((0.0, 0.0, 0.0), v_dt, dyaw)
        return np.array([z[0], z[1]])
