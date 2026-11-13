#!/usr/bin/env python3
"""
Sürüş köprüsü (Jetson, bs kullanıcısıyla çalışır).

Tarayıcı / telefon  <-- HTTP + WebSocket :8080 -->  bu program  <-- seri 115200 -->  Arduino
                                                        ^
                                              Theta S (/dev/video0, MJPEG 1280x720 15 fps)

Görüntü: GStreamer kameranın JPEG karelerini ikiye ayırır (tee):
  - JPEG kolu: kareler çözülmeden aktarılır (/goruntu.mjpg, /kare.jpg). Yedek yol; ~20-50 Mbit/s.
  - H.264 kolu: Jetson'un donanım kodlayıcısı sıkıştırır (/goruntu-h264, WebSocket). Aynı görüntü üç bit
    hızında birden kodlanır (yüksek 4, orta 2,5, düşük 1,2 Mbit/s); her izleyiciye ağının taşıyabildiği gider
    (uyarlanabilir görüntü, ayrıntı: H264Abone). Geçiş tam karede olur, görüntü kesilmez.
  Kamera yalnızca izleyen biri varken açılır.

Sürüş (/kontrol, WebSocket): sayfa basılı tuş varken ~60 ms'de bir {"t":"surus"} gönderir.
  - Her komutta sayfanın saati (z) var. Ağ takılıp komutlar geç gelirse (sinyal zayıflayıp
    düzelince biriken komutlar) bayat komut uygulanmaz, robot durdurulur: TCP sırayı korur ama
    biz yalnızca en tazesini isteriz (UDP gibi davranış).
  - Arduino 300 ms komut alamazsa motorları kendisi durdurur.
  - Aynı anda iki kişi sürerse son hareket ettiren sürücü olur; öteki 0,6 sn yok sayılır.

Sürüş (UDP :8081, Unity uygulaması): uygulama 50 ms'de bir "istenen durum" paketi yollar (kayıp olursa
  sonraki aynı bilgiyi taşır, "aldım" beklemeye gerek kalmaz). Ayrıntı: UdpKanal.

Python 3.6 uyumlu, standart kütüphane + pyserial.
"""

import collections
import glob
import json
import os
import socket
import subprocess
import threading
import time
from http.server import BaseHTTPRequestHandler, HTTPServer
from socketserver import ThreadingMixIn
from urllib.parse import parse_qs, urlsplit

import serial

import websoket

SERI_PORT = "/dev/ttyACM0"
KAMERA_ADI = "THETA"         # /sys/class/video4linux/*/name içinde aranır
PORT = 8080
KLASOR = os.path.dirname(os.path.abspath(__file__))
SAYFA = os.path.join(KLASOR, "surus.html")
KAMERA_AYARI = os.path.expanduser("~/.config/robot/kamera_ayari.json")
KAMERA_BOSTA_KAPAT = 10     # sn: izleyen kalmayınca kamerayı kapat
VARSAYILAN_FPS = 15
AKIS_TAMPONU = 128 * 1024   # bayt; ağ yavaşsa kareler kuyrukta birikmesin (~1 kare)
# H.264 akışları (her biri ayrı donanım kodlayıcı): ad, bit/sn, kırpma (sol, üst, sağ, alt piksel) ya da None,
# çıkış boyutu ya da None (tam kare). İlk üçü tam kare kaliteleri (uyarlanabilir); "on" / "arka" tek lensin
# karesi (kamera iki balık gözünü yan yana verir: sol yarı lens 1, sağ yarı lens 2; daireler üstteki 640 satırda);
# "taban" tüm karenin küçüğü. "Görünen kısım" modunda uygulama tabanı ve baktığı lensi alır (H264Abone).
H264_AKISLAR = (
    ("yuksek", 4000000, None, None),
    ("orta", 2500000, None, None),
    ("dusuk", 1200000, None, None),
    ("on", 2200000, (0, 0, 640, 640), (640, 640)),
    ("arka", 2200000, (640, 0, 1280, 640), (640, 640)),
    ("taban", 400000, None, (640, 360)),
)
AKIS_ADLARI = tuple(a[0] for a in H264_AKISLAR)
TAM_KALITE = 3              # uyarlanabilir kalite yalnızca ilk üç (tam kare) akış arasında
YUKSELT_BEKLE = 8           # sn: bu kadar sıkışmasız geçerse bir üst kaliteye çıkılır (başarısız denemede ikiye katlanır)
YUKSELT_BEKLE_EN_COK = 30   # sn
H264_BEKCI = 5              # sn: JPEG gelirken H.264 bu kadar kare vermezse kamera hattı yeniden başlatılır
ISI_ALARMI = 80             # °C: Jetson bunu geçince bir kez alarm sesi (75'in altına inince yeniden kurulur)
ANAHTAR_ARALIGI = 15        # kare; 15 fps'te saniyede bir tam kare (yeni izleyen en geç 1 sn bekler)
ESKI_KOMUT_MS = 250         # bundan geç gelen hareket komutu uygulanmaz
TABAN_YENILEME = 1.5        # sn: bu kadar sürekli bayat sayılırsa gecikme kalıcı kabul edilir, taban yenilenir
UDP_RAPOR_ARALIGI = 10.0    # sn: kopru.log'a istemci başına durum satırı
SURUCU_KILIDI = 0.6        # sn
DURUM_ARALIGI = 0.1         # sn: WebSocket'ten durum gönderme
UDP_PORT = 8081
UDP_ISTEMCI_ZAMAN_ASIMI = 1.0   # sn: bu kadar paket gelmezse istemci gitmiş sayılır
KORNA_YENILEME = 0.12       # sn: korna basılıyken Arduino'ya bu aralıkla "K 250" (bırakınca en geç 250 ms'de susar)


LOG_DOSYASI = os.path.expanduser("~/robot/surus_koprusu/kopru.log")
LOG_SINIRI = 512 * 1024    # bayt; aşınca eski dosya kopru.log.1 olur (öncekinin üstüne yazılır)


def log(*parca):
    # Kayıt yazılamasa bile (servisin çıktı borusu kapandı vb.) çağıran iş parçacığı ölmesin
    satir = time.strftime("%H:%M:%S") + " " + " ".join(str(p) for p in parca)
    try:
        print(satir, flush=True)
    except (OSError, ValueError):
        pass
    try:
        if os.path.exists(LOG_DOSYASI) and os.path.getsize(LOG_DOSYASI) > LOG_SINIRI:
            os.replace(LOG_DOSYASI, LOG_DOSYASI + ".1")     # eskiyi silme, bir önceki dosya kalsın
        with open(LOG_DOSYASI, "a") as f:
            f.write(satir + "\n")
    except OSError:
        pass


def ms():
    return time.monotonic() * 1000


class GecikmeSuzgeci:
    """Bayat hareket komutunu atar. Taban (fark_min) ağın en iyi hâlidir; gecikme kalıcı yükselirse
    (sıkışık Wi-Fi) her komut sonsuza dek atılmasın diye TABAN_YENILEME sn sürekli atılınca taban yenilenir."""

    def __init__(self, etiket=""):
        self.etiket = etiket
        self.fark_min = None
        self.fark = 0.0
        self.atilan = 0
        self.baslangic = None       # süren atma bölümünün başladığı an
        self.son_atma = -1e9
        self.bolum_atilan = 0
        self.baslangic_logu = -1e9
        self.bolum_loglandi = False

    def gecikme(self):
        """Tabanın üstündeki gecikme (ms)."""
        return 0 if self.fark_min is None else self.fark - self.fark_min

    def _bolum_bitir(self):
        if self.baslangic is not None and self.bolum_loglandi:
            log("bayat komutlar bitti, %d atıldı%s" % (self.bolum_atilan, " " + self.etiket if self.etiket else ""))
        self.baslangic = None
        self.bolum_atilan = 0
        self.bolum_loglandi = False

    def bayat_mi(self, fark, hareket, simdi=None):
        """True: bu komut atılmalı. fark = alınış saati - gönderen saati (ms)."""
        simdi = time.monotonic() if simdi is None else simdi   # saat ayarlansa da kaymaz
        self.fark = fark
        if self.fark_min is None or fark < self.fark_min:
            self.fark_min = fark
        if not hareket:
            return False
        ust = fark - self.fark_min
        if ust <= ESKI_KOMUT_MS:
            self._bolum_bitir()
            return False
        # Bölüm arada kesildiyse (uzun süredir atılan yok) yenisi sayılır
        if self.baslangic is not None and simdi - self.son_atma > 0.5:
            self._bolum_bitir()
        if self.baslangic is None:
            self.baslangic = simdi
            if simdi - self.baslangic_logu >= 2.0:
                self.baslangic_logu = simdi
                self.bolum_loglandi = True
                log("bayat komutlar atılıyor, gecikme +%d ms%s" % (ust, " " + self.etiket if self.etiket else ""))
        elif simdi - self.baslangic > TABAN_YENILEME:
            log("gecikme kalıcı yükseldi: komut tabanı yenilendi (+%d ms)%s" % (ust, " " + self.etiket if self.etiket else ""))
            self.fark_min = fark
            self.baslangic = None
            self.bolum_atilan = 0
            self.bolum_loglandi = False
            return False
        self.son_atma = simdi
        self.atilan += 1
        self.bolum_atilan += 1
        return True


# ---------------------------------------------------------------- Arduino

class Arduino:
    ALANLAR = ("sol", "sag", "sol_ma", "sag_ma", "ariza", "led", "melodi", "dortlu", "motor_sesi", "geri_bip", "sikisma")

    def __init__(self):
        self.kilit = threading.Lock()
        self.port = None
        self.bagli = False
        self.durum = {}            # son "D" satırı
        self.son_durum_zamani = 0
        threading.Thread(target=self._dongu, daemon=True).start()

    def gonder(self, komut):
        with self.kilit:
            if not self.port:
                return False
            try:
                self.port.write((komut + "\n").encode())
                return True
            except (serial.SerialException, OSError):
                return False

    def _dongu(self):
        while True:
            try:
                with serial.Serial(SERI_PORT, 115200, timeout=0.5) as p:
                    log("Arduino portu açıldı, HAZIR bekleniyor")
                    son = time.time() + 5
                    while time.time() < son:     # port açılınca Uno yeniden başlar
                        if p.readline().startswith(b"HAZIR"):
                            break
                    with self.kilit:
                        self.port = p
                    self.bagli = True
                    log("Arduino hazır")
                    while True:
                        satir = p.readline().decode("ascii", "replace").strip()
                        if satir.startswith("D "):
                            self._durum_isle(satir)
                        elif satir:
                            log("Arduino:", satir)
            except (serial.SerialException, OSError) as e:
                if self.bagli:
                    log("Arduino bağlantısı koptu:", e)
            with self.kilit:
                self.port = None
            self.bagli = False
            time.sleep(2)

    def _durum_isle(self, satir):
        try:
            deger = [int(x) for x in satir.split()[1:]]
        except ValueError:
            return
        if len(deger) < 6:
            return
        d = dict(zip(self.ALANLAR, deger))
        for ad in ("ariza", "led", "dortlu", "motor_sesi", "geri_bip"):
            if ad in d:
                d[ad] = bool(d[ad])
        self.durum = d
        self.son_durum_zamani = time.time()


# ---------------------------------------------------------------- kamera

def kamera_bul():
    """Kamera çıkarılıp takılınca numarası değişir (video0 → video1): adından bul, bulamazsan ilk aygıt."""
    aygitlar = sorted(glob.glob("/sys/class/video4linux/video*"))
    for yol in aygitlar:
        try:
            with open(yol + "/name") as f:
                if KAMERA_ADI in f.read():
                    return "/dev/" + os.path.basename(yol)
        except OSError:
            pass
    return "/dev/" + os.path.basename(aygitlar[0]) if aygitlar else None


def anahtar_kare_mi(au):
    """H.264 erişim biriminde tam kare (IDR) ya da SPS var mı?"""
    i = au.find(b"\x00\x00\x01")
    while i != -1 and i + 3 < len(au):
        tur = au[i + 3] & 0x1F
        if tur in (5, 7):
            return True
        if tur == 1:
            return False
        i = au.find(b"\x00\x00\x01", i + 3)
    return False


class H264Abone:
    """Bir izleyicinin kare kuyruğu ve kalitesi.

    Ağ yetişmezse biriktirmez: kuyruğu boşaltıp sonraki tam kareden devam eder (ara kareler öncekilere
    dayandığı için tek tek atılamaz). Kalite (uyarlanabilir): kuyrukta kare birikmesi ağın o bit hızını
    taşıyamadığını gösterir. Taşma olursa, 2 sn içinde 3 kez 2 kare birikirse ya da son 30 karenin çoğu gelirken
    önceki kare hâlâ gönderilmeyi bekliyorsa (ağın sınırında, gecikme artıyor) bir alt kaliteye geçilir;
    YUKSELT_BEKLE sn sıkışmasız geçerse bir üstü denenir. Üste çıkınca 6 sn içinde yine sıkışırsa bekleme ikiye
    katlanır (sınırda gidip gelmesin). Geçiş, istenen kalitenin tam karesi gelince olur.
    """

    def __init__(self, seviye_sayisi, sabit=None):
        seviye_sayisi = min(seviye_sayisi, TAM_KALITE) if sabit is None else seviye_sayisi
        self.kosul = threading.Condition()
        self.kuyruk = collections.deque()
        self.anahtar_bekle = True
        self.seviye_sayisi = seviye_sayisi
        self.sabit = sabit                 # None: otomatik; 0/1/2: hep o kalite
        self.seviye = self.istenen = sabit if sabit is not None and sabit < seviye_sayisi else 0
        simdi = time.time()
        self.son_degisim = self.temiz_beri = simdi
        self.son_yukselis = -1e9
        self.yukselt_bekle = YUKSELT_BEKLE
        self.birikmeler = collections.deque()
        self.akis_sayisi = seviye_sayisi
        self.dolu = collections.deque(maxlen=30)   # son karelerde kuyruk boş değil miydi

    def iste(self, sira):
        """Görünen kısım: uygulama bakışına göre başka akış ister (sonraki tam karesinde geçilir)."""
        with self.kosul:
            if 0 <= sira < len(H264_AKISLAR):
                self.sabit = self.istenen = sira

    def _sikisti(self, simdi):
        self.temiz_beri = simdi
        if self.sabit is not None or self.istenen >= self.seviye_sayisi - 1 or simdi - self.son_degisim < 1.5:
            return
        if simdi - self.son_yukselis < 6:   # az önce çıkmıştık: o kalite taşınmıyor, sonraki denemeyi geciktir
            self.yukselt_bekle = min(YUKSELT_BEKLE_EN_COK, self.yukselt_bekle * 2)
        self.istenen += 1
        self.son_degisim = simdi

    def _rahat_mi(self, simdi):
        if self.sabit is not None or self.istenen == 0:
            return
        if simdi - self.temiz_beri > self.yukselt_bekle and simdi - self.son_degisim > self.yukselt_bekle:
            self.istenen -= 1
            self.son_degisim = self.son_yukselis = self.temiz_beri = simdi
        elif simdi - self.temiz_beri > 120:
            self.yukselt_bekle = YUKSELT_BEKLE   # uzun süre sorunsuz: yeniden çabuk dene

    def ekle(self, au, anahtar, seviye=0):
        with self.kosul:
            if seviye != self.seviye:
                # başka kalitenin karesi: yalnızca istenen kalitenin tam karesinde geçiş yap
                if seviye != self.istenen or not anahtar:
                    return
                self.seviye = seviye
                self.kuyruk.clear()
                self.dolu.clear()
                self.anahtar_bekle = False
            simdi = time.time()
            self.dolu.append(len(self.kuyruk) > 0)
            if len(self.dolu) == self.dolu.maxlen and sum(self.dolu) > 18:
                self.dolu.clear()
                self._sikisti(simdi)
            if len(self.kuyruk) >= 3:
                self.kuyruk.clear()
                self.anahtar_bekle = True
                self._sikisti(simdi)
            elif len(self.kuyruk) >= 2:
                self.birikmeler.append(simdi)
                while self.birikmeler and simdi - self.birikmeler[0] > 2:
                    self.birikmeler.popleft()
                if len(self.birikmeler) >= 3:
                    self.birikmeler.clear()
                    self._sikisti(simdi)
            else:
                self._rahat_mi(simdi)
            if self.anahtar_bekle and not anahtar:
                return
            self.anahtar_bekle = False
            self.kuyruk.append((au, anahtar))
            self.kosul.notify()

    def al(self, sure):
        with self.kosul:
            self.kosul.wait_for(lambda: self.kuyruk, timeout=sure)
            return self.kuyruk.popleft() if self.kuyruk else None


class Kamera:
    """Son JPEG kareyi tutar, H.264 kareleri abonelere dağıtır; izleyen varken GStreamer'ı çalıştırır."""

    def __init__(self):
        self.kosul = threading.Condition()
        self.kare = None
        self.sayac = 0
        self.izleyen = 0
        self.son_izleyen = 0
        self.durum = "kapali"      # kapali | yok | baslatiliyor | aktif
        self.fps = 0.0
        self.h264_destek = True    # kodlayıcı açılamazsa yalnızca JPEG
        self.aygit = None
        self.son_h264 = 0.0        # kodlayıcıdan son kare (kolu durursa hat yeniden başlatılır)
        self.h264_takilma = 0      # art arda kaç kez durdu
        self.aboneler = set()
        self.gonderilen = 0        # izleyicilere giden bayt (veri hızı için)
        self.veri_kbps = 0
        self.seviye_sayisi = len(H264_AKISLAR)   # çoklu kodlayıcı açılamazsa 1
        self.donanim_cozucu = True # JPEG'i Jetson'un donanım çözücüsü (nvv4l2decoder mjpeg=1) çözer; olmazsa jpegdec
        self.h264_calisti = False  # çoklu kodlayıcı en az bir kez kare verdi mi
        self.izleyici_kbps = {}    # (IP, abone) → ona giden akışın bit hızı (durum paketinde IP başına toplanır)
        threading.Thread(target=self._dongu, daemon=True).start()
        threading.Thread(target=self._hiz_olc, daemon=True).start()

    def izle(self, artis):
        with self.kosul:
            self.izleyen += artis
            self.son_izleyen = time.time()
            self.kosul.notify_all()

    def _hiz_olc(self):
        while True:
            onceki = self.gonderilen
            time.sleep(1)
            self.veri_kbps = int((self.gonderilen - onceki) * 8 / 1000)

    def _dongu(self):
        while True:
            with self.kosul:
                while self.izleyen == 0:
                    self.durum = "kapali"
                    self.kosul.wait()
            self.aygit = kamera_bul()
            if not self.aygit:
                self.durum = "yok"
                time.sleep(2)
                continue
            self.durum = "baslatiliyor"
            kare_geldi = self._yayinla()
            if not kare_geldi and self.h264_destek:
                log("kamera hattı kare vermedi; H.264 kapatılıp yalnızca JPEG denenecek")
                self.h264_destek = False
            time.sleep(1)

    def _komut(self, h264_fdler):
        komut = ["gst-launch-1.0", "-q", "v4l2src", "device=" + self.aygit, "!",
                 "image/jpeg,width=1280,height=720", "!", "tee", "name=t",
                 "t.", "!", "queue", "leaky=downstream", "max-size-buffers=2", "!",
                 "multipartmux", "boundary=kare", "!", "fdsink", "fd=1"]
        if h264_fdler:
            # JPEG bir kez çözülür (Jetson'un donanım çözücüsüyle: CPU %38 → %4; olmazsa yazılımla), her kalite kendi
            # dönüştürücü + kodlayıcısıyla (donanım kodlayıcı birden çok 720p oturumu kaldırır; yük CPU'da değil)
            cozucu = ["nvv4l2decoder", "mjpeg=1"] if self.donanim_cozucu else ["jpegdec"]
            komut += ["t.", "!", "queue", "leaky=downstream", "max-size-buffers=2", "!"] + cozucu + ["!", "tee", "name=c"]
            for (_, bit, kirp, boyut), fd in zip(H264_AKISLAR, h264_fdler):
                donustur = ["nvvidconv"]
                if kirp:
                    donustur += ["left={}".format(kirp[0]), "top={}".format(kirp[1]),
                                 "right={}".format(kirp[2]), "bottom={}".format(kirp[3])]
                bicim = "video/x-raw(memory:NVMM),format=NV12" + (",width={},height={}".format(*boyut) if boyut else "")
                komut += ["c.", "!", "queue", "leaky=downstream", "max-size-buffers=2", "!"] + donustur + ["!", bicim, "!",
                          "nvv4l2h264enc", "bitrate={}".format(bit),
                          # idrinterval da şart: yalnız iframeinterval verilirse ara tam kareler IDR
                          # olmaz ve yeni bağlanan tarayıcı çözmeye başlayamaz
                          "iframeinterval={}".format(ANAHTAR_ARALIGI), "idrinterval={}".format(ANAHTAR_ARALIGI),
                          "insert-sps-pps=true",
                          "insert-aud=true", "maxperf-enable=true", "preset-level=1", "!",
                          "h264parse", "!", "video/x-h264,stream-format=byte-stream", "!",
                          "fdsink", "fd={}".format(fd)]
        return komut

    def _yayinla(self):
        borular = [os.pipe() for _ in range(self.seviye_sayisi)] if self.h264_destek else []
        h264_yaz = [y for _, y in borular]
        log("kamera açılıyor", "(JPEG + H.264 x%d, %s çözücü)" % (len(borular), "donanım" if self.donanim_cozucu else "yazılım")
            if borular else "(JPEG)")
        p = subprocess.Popen(self._komut(h264_yaz), stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                             bufsize=0, pass_fds=tuple(h264_yaz))
        for seviye, (oku, yaz) in enumerate(borular):
            os.close(yaz)
            threading.Thread(target=self._h264_oku, args=(oku, seviye), daemon=True).start()
        akis = p.stdout
        sayac_bas, zaman_bas = 0, time.time()
        kare_geldi = False
        baslangic = self.son_h264 = time.time()
        try:
            while True:
                # izleyen kalmadıysa bir süre sonra kapat
                if self.izleyen == 0 and time.time() - self.son_izleyen > KAMERA_BOSTA_KAPAT:
                    break
                # JPEG gelirken H.264 kolu durduysa (ör. kamera çıkarılıp takıldı, kodlayıcı takıldı) hattı yeniden
                # başlat; üç kez üst üste olursa yalnızca JPEG'e geç (uygulama da kendiliğinden JPEG'e geçer)
                if h264_yaz and kare_geldi and self.aboneler and time.time() - max(self.son_h264, baslangic) > H264_BEKCI:
                    if self.donanim_cozucu and not self.h264_calisti:
                        self.donanim_cozucu = False
                        log("donanım JPEG çözücü kare vermedi; yazılım çözücüyle devam")
                        break
                    if self.seviye_sayisi > 1 and not self.h264_calisti:
                        # çoklu kodlayıcı hiç kare vermediyse tek kaliteye dön (eski düzen)
                        self.seviye_sayisi = 1
                        log("çoklu H.264 kodlayıcı kare vermedi; tek kaliteyle devam")
                        break
                    self.h264_takilma += 1
                    log("H.264 kolu kare vermiyor, kamera hattı yeniden başlatılıyor (%d)" % self.h264_takilma)
                    if self.h264_takilma >= 3:
                        self.h264_destek = False
                    break
                uzunluk = None
                while True:                       # parça başlıkları
                    satir = akis.readline()
                    if not satir:
                        return kare_geldi
                    satir = satir.strip()
                    if satir.lower().startswith(b"content-length:"):
                        uzunluk = int(satir.split(b":")[1])
                    elif not satir and uzunluk is not None:
                        break
                veri = b""
                while len(veri) < uzunluk:
                    parca = akis.read(uzunluk - len(veri))
                    if not parca:
                        return kare_geldi
                    veri += parca
                kare_geldi = True
                with self.kosul:
                    self.kare = veri
                    self.sayac += 1
                    self.durum = "aktif"
                    self.kosul.notify_all()
                sayac_bas += 1
                if time.time() - zaman_bas >= 2:
                    self.fps = sayac_bas / (time.time() - zaman_bas)
                    sayac_bas, zaman_bas = 0, time.time()
        finally:
            p.kill()
            p.wait()
            self.fps = 0.0
            log("kamera kapandı")
        return kare_geldi

    def _h264_oku(self, fd, seviye):
        """Bir kalitenin kodlayıcı çıktısını erişim birimlerine (bir kare) böler: her kare AUD ile başlar."""
        AUD = b"\x00\x00\x00\x01\x09"
        tampon = b""
        with os.fdopen(fd, "rb", 0) as f:
            while True:
                parca = f.read(65536)
                if not parca:
                    return
                tampon += parca
                while True:
                    bas = tampon.find(AUD)
                    son = tampon.find(AUD, bas + 5) if bas != -1 else -1
                    if son == -1:
                        break
                    au = tampon[bas:son]
                    tampon = tampon[son:]
                    self.son_h264 = time.time()
                    self.h264_takilma = 0
                    self.h264_calisti = True
                    anahtar = anahtar_kare_mi(au)
                    for a in list(self.aboneler):
                        a.ekle(au, anahtar, seviye)

    def sonraki_kare(self, son_sayac, sure=2.0):
        with self.kosul:
            self.kosul.wait_for(lambda: self.kare is not None and self.sayac != son_sayac, timeout=sure)
            return self.sayac, self.kare


# ---------------------------------------------------------------- sürücü ve sinyal

class Surucu:
    """Kim sürüyor + bayat komutları ayıklama."""

    def __init__(self):
        self.kilit = threading.Lock()
        self.sahip = None
        self.son_hareket = 0

    def surus(self, baglanti, sol, sag):
        simdi = time.time()
        with self.kilit:
            if self.sahip is not None and self.sahip is not baglanti and simdi - self.son_hareket < SURUCU_KILIDI:
                return False
            if sol or sag:
                self.sahip, self.son_hareket = baglanti, simdi
        return ARDUINO.gonder("M {} {}".format(sol, sag))

    def ayrildi(self, baglanti):
        with self.kilit:
            if self.sahip is baglanti:
                self.sahip = None
                ARDUINO.gonder("M 0 0")


def wifi_sinyali():
    """wlan0 sinyal gücü (dBm) ya da None."""
    try:
        with open("/proc/net/wireless") as f:
            for satir in f:
                if satir.strip().startswith("wlan0:"):
                    return int(float(satir.split()[3].rstrip(".")))
    except (OSError, ValueError, IndexError):
        pass
    return None


def sicaklik():
    """Jetson'un en sıcak noktası (CPU/GPU, °C) ya da None. 80 üstünde Nano yavaşlamaya başlar."""
    en = None
    for ad in ("CPU-therm", "GPU-therm"):
        for yol in glob.glob("/sys/devices/virtual/thermal/thermal_zone*"):
            try:
                with open(yol + "/type") as f:
                    if f.read().strip() != ad:
                        continue
                with open(yol + "/temp") as f:
                    t = int(f.read()) / 1000.0
                en = t if en is None else max(en, t)
            except (OSError, ValueError):
                pass
    return None if en is None else round(en, 1)


ARDUINO = Arduino()
KAM = Kamera()
SURUCU = Surucu()


# Nabız: bir sürücü (sayfa ya da uygulama) son NABIZ_SURESI içinde bize bir şey yolladıysa Arduino'ya
# 250 ms'de bir "N" gider. Sürücü kopunca (sinyal gitti, uygulama kapandı, köprü çöktü) nabız durur ve
# Arduino 1,5 sn sonra sesleri kendisi susturur; motorlar zaten 300 ms'de durur.
NABIZ_ARALIGI = 0.25
NABIZ_SURESI = 1.2
SON_SURUCU_SESI = [0.0]


def surucu_duyuldu():
    SON_SURUCU_SESI[0] = time.time()


def nabiz_dongusu():
    alarm_verildi = False
    son_isi = 0.0
    while True:
        time.sleep(NABIZ_ARALIGI)
        if time.time() - son_isi > 5:
            son_isi = time.time()
            t = sicaklik()
            if t is not None and t >= ISI_ALARMI and not alarm_verildi:
                alarm_verildi = True
                log("Jetson ısındı:", t)
                ARDUINO.gonder("X 6")          # EF_ALARM
            elif t is not None and t < ISI_ALARMI - 5:
                alarm_verildi = False
        if time.time() - SON_SURUCU_SESI[0] < NABIZ_SURESI:
            ARDUINO.gonder("N")


_durum_onbellek = [0.0, None]


def durum():
    # UDP her pakete (saniyede 20) cevap verir; /proc okumasını ve sözlüğü 100 ms'de bir yenile
    simdi = time.time()
    if _durum_onbellek[1] is not None and simdi - _durum_onbellek[0] < 0.1:
        return dict(_durum_onbellek[1])
    taze = simdi - ARDUINO.son_durum_zamani < 1
    d = {
        "arduino": ARDUINO.bagli and taze,
        "motor": ARDUINO.durum if taze else None,
        "kamera": KAM.durum,
        "fps": round(KAM.fps, 1),
        "h264": KAM.h264_destek,
        "sinyal": wifi_sinyali(),
        "sicaklik": sicaklik(),
        "veri_kbps": KAM.veri_kbps,
    }
    _durum_onbellek[0], _durum_onbellek[1] = simdi, d
    return dict(d)


# ---------------------------------------------------------------- web sunucusu

class Sunucu(ThreadingMixIn, HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


class Istek(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def setup(self):
        super().setup()
        # Küçük paketler (sürüş komutu, durum) bekletilmeden gitsin
        self.connection.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)

    def _yanit(self, govde, tur, kod=200):
        self.send_response(kod)
        self.send_header("Content-Type", tur)
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(govde)))
        self.end_headers()
        self.wfile.write(govde)

    def _json(self, veri, kod=200):
        self._yanit(json.dumps(veri, ensure_ascii=False).encode("utf-8"), "application/json; charset=utf-8", kod)

    def _govde(self):
        uzunluk = int(self.headers.get("Content-Length") or 0)
        try:
            return json.loads(self.rfile.read(uzunluk).decode("utf-8") or "{}")
        except ValueError:
            return {}

    def do_GET(self):
        adres = urlsplit(self.path)
        if adres.path in ("/", "/index.html"):
            with open(SAYFA, "rb") as f:
                self._yanit(f.read(), "text/html; charset=utf-8")
        elif adres.path == "/kontrol":
            self._kontrol()
        elif adres.path == "/goruntu-h264":
            self._h264_akisi()
        elif adres.path == "/goruntu.mjpg":
            fps = float(parse_qs(adres.query).get("fps", [VARSAYILAN_FPS])[0])
            self._goruntu_akisi(max(1.0, min(fps, 30.0)))
        elif adres.path == "/kare.jpg":
            KAM.izle(+1)
            try:
                _, kare = KAM.sonraki_kare(-1, sure=8)
            finally:
                KAM.izle(-1)
            if kare:
                self._yanit(kare, "image/jpeg")
            else:
                self._json({"hata": "Kamera görüntü vermiyor."}, 503)
        elif adres.path == "/api/durum":
            self._json(durum())
        elif adres.path == "/api/kamera-ayari":
            try:
                with open(KAMERA_AYARI) as f:
                    self._json(json.load(f))
            except (OSError, ValueError):
                self._json({})
        else:
            self.send_error(404)

    # -- sürüş ve durum (WebSocket)
    def _kontrol(self):
        ws = websoket.el_sikis(self)
        if not ws:
            return
        baglanti = object()
        suzgec = GecikmeSuzgeci("(sayfa)")   # sayfa saati ile bizim saat arasındaki en küçük fark (ms) burada tutulur

        def durum_gonder():
            try:
                while ws.acik:
                    d = durum()
                    d.update(t="durum", atilan=suzgec.atilan)
                    ws.metin(json.dumps(d))
                    time.sleep(DURUM_ARALIGI)
            except websoket.Kapandi:
                pass

        threading.Thread(target=durum_gonder, daemon=True).start()
        try:
            while True:
                tur, metin = ws.oku()
                if tur != websoket.METIN:
                    continue
                try:
                    m = json.loads(metin)
                    self._mesaj(ws, baglanti, m, suzgec)
                except (ValueError, TypeError, KeyError):
                    continue
        except websoket.Kapandi:
            pass
        finally:
            ws.acik = False
            SURUCU.ayrildi(baglanti)

    def _mesaj(self, ws, baglanti, m, suzgec):
        surucu_duyuldu()
        t = m.get("t")
        if t == "ping":
            ws.metin(json.dumps({"t": "pong", "z": m.get("z")}))
        elif t == "surus":
            sol = max(-255, min(255, int(m.get("sol", 0))))
            sag = max(-255, min(255, int(m.get("sag", 0))))
            # Bayat komut: sayfanın gönderdiği an ile bize geldiği an arasındaki fark,
            # şimdiye kadarki en küçük farktan (ağın en iyi hâli) ESKI_KOMUT_MS fazlaysa
            if "z" in m:
                if suzgec.bayat_mi(ms() - float(m["z"]), bool(sol or sag)):
                    SURUCU.surus(baglanti, 0, 0)
                    return
            SURUCU.surus(baglanti, sol, sag)
        elif t == "led":
            ARDUINO.gonder("L {}".format(1 if m.get("acik") else 0))
        elif t == "selektor":
            ARDUINO.gonder("F")
        elif t == "dortlu":
            ARDUINO.gonder("H {}".format(1 if m.get("acik") else 0))
        elif t == "korna":
            ARDUINO.gonder("K {}".format(max(50, min(1000, int(m.get("ms", 250))))))
        elif t == "melodi":
            ARDUINO.gonder("C {}".format(int(m.get("no", 0))))
        elif t == "motor_sesi":
            ARDUINO.gonder("E {}".format(1 if m.get("acik") else 0))
        elif t == "geri_bip":
            ARDUINO.gonder("V {}".format(1 if m.get("acik") else 0))

    # -- görüntü
    def _h264_akisi(self):
        ws = websoket.el_sikis(self)
        if not ws:
            return
        self.connection.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, AKIS_TAMPONU)
        sorgu = parse_qs(urlsplit(self.path).query)
        # ?akis=on|arka|taban|yuksek|... : o akış (görünen kısım); ?kalite= eski ad; ikisi de yoksa uyarlanabilir tam kare
        ad = sorgu.get("akis", sorgu.get("kalite", [""]))[0]
        sira = AKIS_ADLARI.index(ad) if ad in AKIS_ADLARI else None
        if sira is not None and sira >= KAM.seviye_sayisi:
            sira = None            # kodlayıcı açılamadıysa yalnızca tam kare var
        abone = H264Abone(KAM.seviye_sayisi, sira)
        ip = self.client_address[0]
        KAM.aboneler.add(abone)
        KAM.izle(+1)
        # Okuyan iş parçacığı: tarayıcı kapatınca ya da ping atınca haberimiz olsun
        threading.Thread(target=self._oku_istek, args=(ws, abone), daemon=True).start()
        try:
            while ws.acik:
                oge = abone.al(1.0)
                if not oge:
                    continue
                au, anahtar = oge
                KAM.izleyici_kbps[(ip, id(abone))] = H264_AKISLAR[abone.seviye][1] // 1000
                # ilk bayt: bit 0 tam kare, üstü akışın sırası (uygulama karenin hangi lens olduğunu bilsin)
                ws.ikili(bytes([(1 if anahtar else 0) | (abone.seviye << 1)]) + au)
                KAM.gonderilen += len(au)
        except websoket.Kapandi:
            pass
        finally:
            ws.acik = False
            KAM.aboneler.discard(abone)
            KAM.izleyici_kbps.pop((ip, id(abone)), None)
            KAM.izle(-1)

    @staticmethod
    def _oku_istek(ws, abone):
        """Görüntü soketinden gelen istekler: {"akis": "on"} → o akışa geç. Kapanınca akış durur."""
        try:
            while True:
                tur, veri = ws.oku()
                if tur == websoket.METIN:
                    try:
                        ad = json.loads(veri).get("akis")
                    except (ValueError, AttributeError):
                        continue
                    if ad in AKIS_ADLARI and AKIS_ADLARI.index(ad) < KAM.seviye_sayisi:
                        abone.iste(AKIS_ADLARI.index(ad))
        except websoket.Kapandi:
            ws.acik = False

    def _goruntu_akisi(self, fps):
        # Gönderme tamponu küçük: ağ yavaşsa kareler kuyrukta birikip gecikme büyümesin,
        # yazma bekler ve sonra en yeni kare gönderilir (aradakiler atlanır).
        self.connection.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, AKIS_TAMPONU)
        self.close_connection = True
        self.send_response(200)
        self.send_header("Content-Type", "multipart/x-mixed-replace; boundary=kare")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        KAM.izle(+1)
        sayac, aralik, son = -1, 1.0 / fps, 0.0
        try:
            while True:
                bekle = aralik - (time.time() - son)
                if bekle > 0:
                    time.sleep(bekle)
                sayac, kare = KAM.sonraki_kare(sayac)
                if not kare:
                    continue
                son = time.time()
                self.wfile.write(b"--kare\r\nContent-Type: image/jpeg\r\nContent-Length: " +
                                 str(len(kare)).encode() + b"\r\n\r\n" + kare + b"\r\n")
                KAM.gonderilen += len(kare)
        except (BrokenPipeError, ConnectionResetError, OSError):
            pass
        finally:
            KAM.izle(-1)

    def do_POST(self):
        yol = urlsplit(self.path).path
        veri = self._govde()
        if yol == "/api/dur":
            self._json({"tamam": ARDUINO.gonder("S")})
        elif yol == "/api/efekt":       # {"no": n}: kısa ses efekti (panel kapanırken EF_KAPANIS çalar)
            self._json({"tamam": ARDUINO.gonder("X {}".format(int(veri.get("no", 0))))})
        elif yol == "/api/kamera-ayari":
            os.makedirs(os.path.dirname(KAMERA_AYARI), exist_ok=True)
            with open(KAMERA_AYARI, "w") as f:
                json.dump(veri, f)
            self._json({"tamam": True})
        else:
            self.send_error(404)


class UdpKanal:
    """Unity uygulamasının sürüş kanalı (UDP).

    Uygulama 50 ms'de bir, ayrıca bir şey değişince hemen, tüm "istenen durumu" yollar:
      {"t":"d", "n":sıra, "z":uygulamanın saati (ms), "sol":-255..255, "sag":..,
       "led":0/1, "dortlu":0/1, "motor_sesi":0/1, "geri_bip":0/1,
       "k":korna basılı 0/1, "ks":korna sesi (0-4 basılı, 5+ ezgili), "fs":selektör sayacı, "mi":melodi sayacı, "mn":melodi no,
       "ef":efekt sayacı, "en":efekt no, "efekt":kendiliğinden efektler 0/1}
    - Paket kaybolursa sonraki paket aynı durumu taşıdığı için "aldım" (ack) gerekmez.
    - Tek seferlik işler (selektör, melodi başlatma) sayaçla gelir: sayaç değişince bir kez yapılır;
      o paket kaybolsa da sonraki pakette sayaç yine farklıdır.
    - LED gibi durumlar yalnızca uygulamanın istediği değer değişince Arduino'ya yazılır; böylece
      tarayıcıdan yapılan değişikliği yeni bağlanan uygulama ezmez.
    - Sırası eski (n küçük) ya da geç kalmış (z) paketin sürüş kısmı uygulanmaz.
    - Her pakete durum + aynı n ve z ile cevap verilir: uygulama gidip gelme süresini ve kaybı ölçer.
    {"t":"bul"} paketine (yayın) {"t":"robot"} cevabı verilir: uygulama robotun adresini kendisi bulur.
    """

    DURUMLAR = (("led", "L"), ("dortlu", "H"), ("motor_sesi", "E"), ("geri_bip", "V"), ("efekt", "Z"))

    def __init__(self):
        self.soket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.soket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.soket.bind(("0.0.0.0", UDP_PORT))
        # Wi-Fi önceliği: DSCP EF → WMM "ses" kuyruğu. Sinyal zayıfken bile sürüş paketleri görüntünün önüne geçer.
        try:
            self.soket.setsockopt(socket.IPPROTO_IP, socket.IP_TOS, 0xB8)
        except OSError:
            pass
        self.istemciler = {}
        threading.Thread(target=self._dongu, daemon=True).start()
        threading.Thread(target=self._bekci, daemon=True).start()

    def _cevap(self, adres, veri):
        try:
            self.soket.sendto(json.dumps(veri).encode(), adres)
        except OSError:
            pass

    def _dongu(self):
        while True:
            try:
                paket, adres = self.soket.recvfrom(2048)
                m = json.loads(paket.decode())
            except (OSError, ValueError, UnicodeDecodeError):
                continue
            t = m.get("t") if isinstance(m, dict) else None
            try:
                if t == "bul":
                    self._cevap(adres, {"t": "robot", "ad": socket.gethostname(), "port": PORT})
                elif t == "d":
                    self._durum_paketi(adres, m)
            except (ValueError, TypeError):
                continue
            except Exception as e:      # beklenmeyen hata sürüş kanalını öldürmesin
                log("UDP paket hatası:", repr(e))

    def _durum_paketi(self, adres, m):
        surucu_duyuldu()
        simdi = time.time()
        ist = self.istemciler.get(adres)
        n = int(m.get("n", 0))
        # Uygulama yeniden açıldıysa sıra baştan başlar: uzun süre ses yoksa yeni istemci say
        if ist is None or simdi - ist["son"] > UDP_ISTEMCI_ZAMAN_ASIMI or n < ist["n"] - 1000:
            if ist:
                SURUCU.ayrildi(ist["baglanti"])
            ist = {"baglanti": object(), "n": n - 1, "onceki": None, "korna": 0.0,
                   "suzgec": GecikmeSuzgeci(adres[0]), "paket": 0, "istenen": (0, 0),
                   "rapor_zamani": simdi, "rapor_paket": 0, "rapor_atilan": 0}
            self.istemciler[adres] = ist
            log("UDP istemci:", adres[0])
        ist["son"] = simdi
        taze = n > ist["n"]
        if taze:
            ist["n"] = n

        sol = max(-255, min(255, int(m.get("sol", 0))))
        sag = max(-255, min(255, int(m.get("sag", 0))))
        ist["paket"] += 1
        if "z" in m and ist["suzgec"].bayat_mi(ms() - float(m["z"]), bool(sol or sag), simdi):
            taze = False
        elif taze:
            ist["istenen"] = (sol, sag)
        if taze:
            SURUCU.surus(ist["baglanti"], sol, sag)
        elif sol or sag:
            SURUCU.surus(ist["baglanti"], 0, 0)

        onceki = ist["onceki"]
        if taze and onceki is None and "efekt" in m:
            ARDUINO.gonder("Z {}".format(1 if m["efekt"] else 0))   # yeni istemci: efekt tercihi baştan
        if taze and onceki is not None:
            for alan, harf in self.DURUMLAR:
                if alan in m and m[alan] != onceki.get(alan):
                    ARDUINO.gonder("{} {}".format(harf, 1 if m[alan] else 0))
            if m.get("fs") != onceki.get("fs"):
                ARDUINO.gonder("F")
            if m.get("mi") != onceki.get("mi"):
                ARDUINO.gonder("C {}".format(int(m.get("mn", 0))))
            if m.get("ef") != onceki.get("ef"):
                ARDUINO.gonder("X {}".format(int(m.get("en", 0))))
            if not m.get("k") and onceki.get("k"):
                ARDUINO.gonder("K 1")                  # bırakıldı: hemen sus
        if taze and m.get("k") and simdi - ist["korna"] > KORNA_YENILEME:
            ist["korna"] = simdi
            ARDUINO.gonder("K 250 {}".format(max(0, min(15, int(m.get("ks", 0))))))   # Arduino kendi sınırına kırpar
        if taze:
            ist["onceki"] = m

        d = durum()
        d.update(t="s", n=n, z=m.get("z"), atilan=ist["suzgec"].atilan, hedef_kbps=sum(v for (i, _), v in list(KAM.izleyici_kbps.items()) if i == adres[0]))
        self._cevap(adres, d)

    def _bekci(self):
        while True:
            time.sleep(0.2)
            simdi = time.time()
            for adres, ist in list(self.istemciler.items()):
                if simdi - ist["son"] > UDP_ISTEMCI_ZAMAN_ASIMI:
                    SURUCU.ayrildi(ist["baglanti"])
                    del self.istemciler[adres]
                    log("UDP istemci gitti: %s (son paket %.1f sn önce)" % (adres[0], simdi - ist["son"]))
                elif simdi - ist["rapor_zamani"] >= UDP_RAPOR_ARALIGI:
                    self._rapor(adres, ist, simdi)

    def _rapor(self, adres, ist, simdi):
        """İstemci başına 10 sn'de bir durum satırı (kopru.log): kopma sonrası neyin olduğu buradan okunur."""
        try:
            sz = ist["suzgec"]
            motor = ARDUINO.durum if simdi - ARDUINO.son_durum_zamani < 1 else {}
            sinyal = wifi_sinyali()
            isi = sicaklik()
            log("UDP %s: %d paket, %d atılan, gecikme +%d ms, istenen %d/%d, motor %s/%s, görüntü %d kbit/s, sinyal %s, %s" % (
                adres[0], ist["paket"] - ist["rapor_paket"], sz.atilan - ist["rapor_atilan"], sz.gecikme(),
                ist["istenen"][0], ist["istenen"][1], motor.get("sol", "?"), motor.get("sag", "?"), KAM.veri_kbps,
                "?" if sinyal is None else "%d dBm" % sinyal, "?" if isi is None else "%d°C" % isi))
        except Exception as e:
            log("UDP rapor hatası:", repr(e))
        ist["rapor_zamani"] = simdi
        ist["rapor_paket"] = ist["paket"]
        ist["rapor_atilan"] = ist["suzgec"].atilan


def main():
    threading.Thread(target=nabiz_dongusu, daemon=True).start()
    UdpKanal()
    log("Sürüş köprüsü hazır, port", PORT, "UDP", UDP_PORT)
    Sunucu(("0.0.0.0", PORT), Istek).serve_forever()


if __name__ == "__main__":
    main()
