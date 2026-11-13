"""
Ham kayıt (Jetson): kameranın ORİJİNAL JPEG karelerini, her motor komutunu ve Arduino durumunu aynı saatle
(time.monotonic) diske yazar. Çevrimdışı konum bulma denemeleri için (H.264 akışı kayıplı olduğundan yetmez).

KAYIT BİÇİMİ v1 (değişken B):
  <kok>/<YYYY-AA-GG_SS-DD-SS>_<ad>/
    bilgi.json     kayıt başında yazılır, bitince tamamlanır
    kareler.mjpg   JPEG'ler değiştirilmeden art arda eklenir
    kareler.csv    kare,t_ms,ofset,bayt        (ofset/bayt: JPEG'in kareler.mjpg içindeki yeri)
    komutlar.csv   t_ms,sol,sag,kaynak         (Arduino'ya giden motor hedefi; PWM -255..255, ileri +)
    durum.csv      t_ms,sol,sag,sol_ma,sag_ma  (Arduino geri bildirimi, ~10 Hz)
  t_ms: kayıt başından beri tam sayı ms (köprünün saati, time.monotonic). Kare zamanı: JPEG'in GStreamer
  borusundan okunması bittiği an.

Köprüyü yavaşlatmamak için: kare/komut/durum çağrıları yalnızca bellekte tampona yazar (kare sınırlı kuyruğa
konur, kuyruk doluysa kare atılır ve sayılır); diske tek bir yazıcı iş parçacığı yazar. Bu sınıfın kare(), komut()
ve durum_satiri() yöntemleri hiçbir zaman hata fırlatmaz.

Python 3.6 uyumlu, yalnızca standart kütüphane.
"""

import json
import os
import queue
import re
import shutil
import threading
import time

VARSAYILAN_SURE = 180       # sn
EN_COK_SURE = 900           # sn (üst sınır)
EN_AZ_BOS_MB = 500          # MB: boş yer bunun altındaysa başlamaz / durur
KUYRUK_KARE = 60            # yazıcı yetişmezse bellekte en çok bu kadar kare bekler (~18 MB), sonrası atılır
KAMERA_SUSKUNLUK = 8        # sn: kayıt sürerken bu kadar kare gelmezse kamera durmuş sayılır
ILK_KARE_BEKLE = 15         # sn: kayıt başladıktan sonra ilk kareyi en çok bu kadar bekler (kamera açılıyor)
BOSALT_ARALIGI = 1.0        # sn: tamponlar diske bu aralıkla yazılır
KONTROL_ARALIGI = 0.5       # sn: süre / disk / kamera sınırları bu aralıkla denetlenir
BEKLENEN_MB_SN = (2, 6)     # JPEG kare 150-400 KB x 15 fps
EN_COK_TAMPON = 500000      # bellekteki komut / durum satırı sınırı (yazıcı takılırsa bellek şişmesin)

DOSYALAR = ("bilgi.json", "kareler.mjpg", "kareler.csv", "komutlar.csv", "durum.csv")
AD_DESENI = re.compile(r"[^A-Za-z0-9_-]")


class KayitHatasi(Exception):
    """Kayıt başlatılamadı (kullanıcıya gösterilecek Türkçe mesaj taşır)."""


def _iso_zaman():
    z = time.strftime("%z")
    ek = z[:3] + ":" + z[3:] if len(z) == 5 and z[0] in "+-" else ""
    return time.strftime("%Y-%m-%dT%H:%M:%S") + ek


class HamKayit:
    def __init__(self, kok, izle=None, bos_disk_mb=None, saat=None, bilgi_ek=None, log=None,
                 kuyruk_siniri=KUYRUK_KARE, en_az_bos_mb=EN_AZ_BOS_MB, kamera_suskunluk=KAMERA_SUSKUNLUK,
                 ilk_kare_bekle=ILK_KARE_BEKLE):
        self.kok = kok
        self._izle = izle                    # izle(+1) / izle(-1): kayıt sürerken kamera açık tutulur
        self._bos_disk_fn = bos_disk_mb or self._bos_disk_varsayilan
        self._saat = saat or time.monotonic
        self._bilgi_ek = bilgi_ek            # () -> sözlük (köprü sürümü, kamera ayarı...)
        self._log = log or (lambda *a: None)
        self._kuyruk_siniri = kuyruk_siniri
        self._en_az_bos_mb = en_az_bos_mb
        self._kamera_suskunluk = kamera_suskunluk
        self._ilk_kare_bekle = ilk_kare_bekle
        self.kilit = threading.Lock()
        self.aktif = False                   # kare/komut kabul ediyor mu
        self._is = None                      # yazıcı iş parçacığı
        self._baslat_kilidi = threading.Lock()
        self._son = None                     # son biten kaydın özeti
        self._sifirla()

    def _sifirla(self):
        self._klasor = None
        self._ad = None
        self._t0 = 0.0
        self._en_cok_sn = 0
        self._kuyruk = None
        self._kare_sayisi = 0
        self._yazilan = 0
        self._atilan = 0
        self._bayt = 0
        self._komutlar = []
        self._durumlar = []
        self._komut_sayisi = 0
        self._durum_sayisi = 0
        self._son_kare = None
        self._neden = None
        self._dosyalar = None
        self._bilgi = None
        self._izlendi = False

    # ------------------------------------------------------------ yardımcılar

    def _bos_disk_varsayilan(self):
        return shutil.disk_usage(self.kok).free / (1024.0 * 1024.0)

    def _bos_mb(self):
        try:
            return float(self._bos_disk_fn())
        except Exception:
            return None

    def _t(self):
        return int((self._saat() - self._t0) * 1000)

    def calisiyor(self):
        return self._is is not None and self._is.is_alive()

    # ------------------------------------------------------------ köprünün çağırdığı kancalar (hata fırlatmaz)

    def kare(self, veri):
        """Kameradan bir JPEG kare geldi (köprünün okuyucu iş parçacığı çağırır; asla beklemez)."""
        if not self.aktif:
            return
        try:
            simdi = self._saat()
            with self.kilit:
                if not self.aktif:
                    return
                self._son_kare = simdi
                try:
                    self._kuyruk.put_nowait((self._kare_sayisi, int((simdi - self._t0) * 1000), veri))
                except queue.Full:
                    self._atilan += 1
                    if self._atilan == 1:
                        self._log("ham kayıt: yazıcı yetişmiyor, kare atılıyor")
                    return
                self._kare_sayisi += 1
        except Exception:
            pass

    def komut(self, komut, kaynak):
        """Arduino'ya motor hedefi gitti ("M sol sag" ya da "S"). Diğer komutlar yok sayılır."""
        if not self.aktif:
            return
        try:
            if komut == "S":
                sol = sag = 0
            elif komut.startswith("M "):
                _, a, b = komut.split()
                sol, sag = int(a), int(b)
            else:
                return
            with self.kilit:
                if not self.aktif or len(self._komutlar) >= EN_COK_TAMPON:
                    return
                self._komutlar.append("%d,%d,%d,%s\n" % (self._t(), sol, sag, kaynak))
        except Exception:
            pass

    def durum_satiri(self, d):
        """Arduino'dan "D ..." durum satırı geldi (köprüdeki Arduino.durum sözlüğü)."""
        if not self.aktif:
            return
        try:
            with self.kilit:
                if not self.aktif or len(self._durumlar) >= EN_COK_TAMPON:
                    return
                self._durumlar.append("%d,%d,%d,%d,%d\n" % (self._t(), d["sol"], d["sag"], d["sol_ma"], d["sag_ma"]))
        except Exception:
            pass

    # ------------------------------------------------------------ başlat / durdur

    def baslat(self, ad=None, sure=None):
        """Kaydı başlatır; olmazsa KayitHatasi. Dönen: durum sözlüğü."""
        with self._baslat_kilidi:
            if self.aktif:
                raise KayitHatasi("Kayıt zaten sürüyor.")
            if self.calisiyor():
                raise KayitHatasi("Önceki kayıt kapanıyor, birkaç saniye sonra yeniden dene.")
            try:
                sure = VARSAYILAN_SURE if sure is None else float(sure)
            except (TypeError, ValueError):
                raise KayitHatasi("Süre sayı olmalı.")
            if not sure > 0:
                raise KayitHatasi("Süre sıfırdan büyük olmalı.")
            sure = min(sure, EN_COK_SURE)
            ad = AD_DESENI.sub("", (ad or "kayit"))[:40] or "kayit"
            try:
                os.makedirs(self.kok, exist_ok=True)
            except OSError as e:
                raise KayitHatasi("Kayıt klasörü açılamadı: {}".format(e))
            bos = self._bos_mb()
            if bos is None or bos < self._en_az_bos_mb:
                raise KayitHatasi("Diskte yeterli yer yok ({} MB boş, en az {} MB gerekir).".format(
                    "?" if bos is None else int(bos), self._en_az_bos_mb))
            gun = time.strftime("%Y-%m-%d_%H-%M-%S")
            klasor = os.path.join(self.kok, gun + "_" + ad)
            n = 1
            while os.path.exists(klasor):
                n += 1
                klasor = os.path.join(self.kok, "{}_{}_{}".format(gun, ad, n))
            try:
                os.makedirs(klasor)
                dosyalar = [open(os.path.join(klasor, "kareler.mjpg"), "wb"),
                            open(os.path.join(klasor, "kareler.csv"), "w"),
                            open(os.path.join(klasor, "komutlar.csv"), "w"),
                            open(os.path.join(klasor, "durum.csv"), "w")]
            except OSError as e:
                raise KayitHatasi("Kayıt dosyaları açılamadı: {}".format(e))
            dosyalar[1].write("kare,t_ms,ofset,bayt\n")
            dosyalar[2].write("t_ms,sol,sag,kaynak\n")
            dosyalar[3].write("t_ms,sol,sag,sol_ma,sag_ma\n")
            bilgi = {"bicim": 1, "kaynak": "robot", "ad": ad,
                     "kamera": {"tur": "cift_balikgozu", "genislik": 1280, "yukseklik": 720, "fps": 15},
                     "kamera_yuksekligi_m": None, "baslangic_duvar_saati": _iso_zaman()}
            if self._bilgi_ek:
                try:
                    bilgi.update(self._bilgi_ek())
                except Exception:
                    pass
            bilgi.update({"atilan_kare": 0, "bitis_nedeni": None})
            with self.kilit:
                self._sifirla()
                self._klasor, self._ad, self._en_cok_sn = klasor, ad, sure
                self._kuyruk = queue.Queue(maxsize=self._kuyruk_siniri)
                self._dosyalar = dosyalar
                self._bilgi = bilgi
                self._t0 = self._saat()
                self._yaz_bilgi()
                self.aktif = True
            if self._izle:
                self._izle(+1)               # kamera (JPEG kolu) izleyen olmasa da açık kalsın
                self._izlendi = True
            self._is = threading.Thread(target=self._calis, daemon=True)
            self._is.start()
            self._log("ham kayıt başladı:", klasor, "(en çok {:.0f} sn)".format(sure))
        return self.durum()

    def durdur(self, neden="elle"):
        """Kaydı durdurur, yazıcı bitirene dek bekler. Dönen: durum sözlüğü."""
        self._durdur_iste(neden)
        is_ = self._is
        if is_ is not None and is_ is not threading.current_thread():
            is_.join(30)
        return self.durum()

    def _durdur_iste(self, neden):
        with self.kilit:
            if self.aktif:
                self.aktif = False
                self._neden = neden

    # ------------------------------------------------------------ yazıcı iş parçacığı

    def _yaz_bilgi(self):
        yol = os.path.join(self._klasor, "bilgi.json")
        gecici = yol + ".tmp"
        with open(gecici, "w") as f:
            json.dump(self._bilgi, f, ensure_ascii=False, indent=1)
        os.replace(gecici, yol)

    def _tamponlari_yaz(self):
        with self.kilit:
            komutlar, self._komutlar = self._komutlar, []
            durumlar, self._durumlar = self._durumlar, []
        kom_csv, durum_csv = self._dosyalar[2], self._dosyalar[3]
        if komutlar:
            kom_csv.write("".join(komutlar))
            self._komut_sayisi += len(komutlar)
        if durumlar:
            durum_csv.write("".join(durumlar))
            self._durum_sayisi += len(durumlar)
        for f in self._dosyalar:
            f.flush()

    def _sinir_asildi(self, simdi):
        gecen = simdi - self._t0
        if gecen >= self._en_cok_sn:
            return "sure_doldu"
        bos = self._bos_mb()
        if bos is not None and bos < self._en_az_bos_mb:
            return "disk_dolu"
        son = self._son_kare
        if son is None:
            if gecen > self._ilk_kare_bekle:
                return "kamera_yok"
        elif simdi - son > self._kamera_suskunluk:
            return "kamera_durdu"
        return None

    def _calis(self):
        mjpg, kare_csv = self._dosyalar[0], self._dosyalar[1]
        ofset = 0
        son_bosalt = son_kontrol = self._saat()
        try:
            while True:
                try:
                    kare, t, veri = self._kuyruk.get(timeout=0.2)
                except queue.Empty:
                    if not self.aktif:
                        break
                else:
                    mjpg.write(veri)
                    kare_csv.write("%d,%d,%d,%d\n" % (kare, t, ofset, len(veri)))
                    ofset += len(veri)
                    self._yazilan += 1
                    self._bayt = ofset
                simdi = self._saat()
                if simdi - son_bosalt >= BOSALT_ARALIGI:
                    son_bosalt = simdi
                    self._tamponlari_yaz()
                if self.aktif and simdi - son_kontrol >= KONTROL_ARALIGI:
                    son_kontrol = simdi
                    neden = self._sinir_asildi(simdi)
                    if neden:
                        self._durdur_iste(neden)
        except Exception as e:
            self._durdur_iste("yazma_hatasi: {}".format(e))
        finally:
            self._bitir()

    def _bitir(self):
        try:
            try:
                self._tamponlari_yaz()
            except Exception:
                pass
            for f in self._dosyalar:
                try:
                    f.flush()
                    os.fsync(f.fileno())
                except Exception:
                    pass
                try:
                    f.close()
                except Exception:
                    pass
            gecen = max(0.0, self._saat() - self._t0)
            self._bilgi.update({"atilan_kare": self._atilan, "bitis_nedeni": self._neden or "bilinmiyor",
                                "kare_sayisi": self._yazilan, "bayt": self._bayt, "sure_sn": round(gecen, 2),
                                "olculen_fps": round(self._yazilan / gecen, 2) if gecen > 0 else None})
            try:
                self._yaz_bilgi()
            except Exception:
                pass
            self._son = {"klasor": self._klasor, "bitis_nedeni": self._neden, "kare": self._yazilan,
                         "atilan_kare": self._atilan, "mb": round(self._bayt / 1e6, 1), "sure_sn": round(gecen, 1)}
            self._log("ham kayıt bitti:", self._klasor, self._son)
        finally:
            with self.kilit:
                self.aktif = False
            if self._izle and self._izlendi:
                self._izlendi = False
                try:
                    self._izle(-1)
                except Exception:
                    pass

    # ------------------------------------------------------------ durum

    def durum(self):
        bos = self._bos_mb()
        with self.kilit:
            aktif = self.aktif
            kapaniyor = (not aktif) and self.calisiyor()
            sure = round(max(0.0, self._saat() - self._t0), 1) if (aktif and self._klasor) else 0
            return {"aktif": aktif, "kapaniyor": kapaniyor, "klasor": self._klasor if (aktif or kapaniyor) else None,
                    "kare": self._yazilan, "atilan_kare": self._atilan, "mb": round(self._bayt / 1e6, 1),
                    "sure_sn": sure, "en_cok_sn": self._en_cok_sn if aktif else 0,
                    "bos_disk_mb": None if bos is None else int(bos),
                    "beklenen_mb_sn": list(BEKLENEN_MB_SN),
                    "beklenen_boyut_mb": ([int(self._en_cok_sn * BEKLENEN_MB_SN[0]), int(self._en_cok_sn * BEKLENEN_MB_SN[1])]
                                          if aktif else None),
                    "komut_satiri": self._komut_sayisi, "durum_satiri": self._durum_sayisi,
                    "son": self._son}
