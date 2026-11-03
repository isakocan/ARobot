#!/usr/bin/env python3
"""
Sürüş köprüsü (Jetson, bs kullanıcısıyla çalışır).

Tarayıcı / telefon  <-- HTTP + WebSocket :8080 -->  bu program  <-- seri 115200 -->  Arduino
                                                        ^
                                              Theta S (/dev/video0, MJPEG 1280x720 15 fps)

Görüntü: GStreamer kameranın JPEG karelerini ikiye ayırır (tee):
  - JPEG kolu: kareler çözülmeden aktarılır (/goruntu.mjpg, /kare.jpg). Yedek yol; ~20-50 Mbit/s.
  - H.264 kolu: Jetson'un donanım kodlayıcısı sıkıştırır (/goruntu-h264, WebSocket). ~4 Mbit/s.
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
KAMERA = "/dev/video0"
PORT = 8080
KLASOR = os.path.dirname(os.path.abspath(__file__))
SAYFA = os.path.join(KLASOR, "surus.html")
KAMERA_AYARI = os.path.expanduser("~/.config/robot/kamera_ayari.json")
KAMERA_BOSTA_KAPAT = 10     # sn: izleyen kalmayınca kamerayı kapat
VARSAYILAN_FPS = 15
AKIS_TAMPONU = 128 * 1024   # bayt; ağ yavaşsa kareler kuyrukta birikmesin (~1 kare)
H264_BIT_HIZI = 4000000     # bit/sn
ANAHTAR_ARALIGI = 15        # kare; 15 fps'te saniyede bir tam kare (yeni izleyen en geç 1 sn bekler)
ESKI_KOMUT_MS = 250         # bundan geç gelen hareket komutu uygulanmaz
SURUCU_KILIDI = 0.6         # sn
DURUM_ARALIGI = 0.1         # sn: WebSocket'ten durum gönderme
UDP_PORT = 8081
UDP_ISTEMCI_ZAMAN_ASIMI = 1.0   # sn: bu kadar paket gelmezse istemci gitmiş sayılır
KORNA_YENILEME = 0.12       # sn: korna basılıyken Arduino'ya bu aralıkla "K 250" (bırakınca en geç 250 ms'de susar)


def log(*parca):
    print(time.strftime("%H:%M:%S"), *parca, flush=True)


def ms():
    return time.monotonic() * 1000


# ---------------------------------------------------------------- Arduino

class Arduino:
    ALANLAR = ("sol", "sag", "sol_ma", "sag_ma", "ariza", "led", "melodi", "dortlu", "motor_sesi", "geri_bip")

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
    """Bir izleyicinin kare kuyruğu. Ağ yetişmezse biriktirmez: kuyruğu boşaltıp sonraki
    tam kareden devam eder (ara kareler öncekilere dayandığı için tek tek atılamaz)."""

    def __init__(self):
        self.kosul = threading.Condition()
        self.kuyruk = collections.deque()
        self.anahtar_bekle = True

    def ekle(self, au, anahtar):
        with self.kosul:
            if len(self.kuyruk) >= 3:
                self.kuyruk.clear()
                self.anahtar_bekle = True
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
        self.aboneler = set()
        self.gonderilen = 0        # izleyicilere giden bayt (veri hızı için)
        self.veri_kbps = 0
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
            if not os.path.exists(KAMERA):
                self.durum = "yok"
                time.sleep(2)
                continue
            self.durum = "baslatiliyor"
            kare_geldi = self._yayinla()
            if not kare_geldi and self.h264_destek:
                log("kamera hattı kare vermedi; H.264 kapatılıp yalnızca JPEG denenecek")
                self.h264_destek = False
            time.sleep(1)

    def _komut(self, h264_fd):
        komut = ["gst-launch-1.0", "-q", "v4l2src", "device=" + KAMERA, "!",
                 "image/jpeg,width=1280,height=720", "!", "tee", "name=t",
                 "t.", "!", "queue", "leaky=downstream", "max-size-buffers=2", "!",
                 "multipartmux", "boundary=kare", "!", "fdsink", "fd=1"]
        if h264_fd is not None:
            komut += ["t.", "!", "queue", "leaky=downstream", "max-size-buffers=2", "!",
                      "jpegdec", "!", "nvvidconv", "!", "video/x-raw(memory:NVMM),format=NV12", "!",
                      "nvv4l2h264enc", "bitrate={}".format(H264_BIT_HIZI),
                      # idrinterval da şart: yalnız iframeinterval verilirse ara tam kareler IDR
                      # olmaz ve yeni bağlanan tarayıcı çözmeye başlayamaz
                      "iframeinterval={}".format(ANAHTAR_ARALIGI), "idrinterval={}".format(ANAHTAR_ARALIGI),
                      "insert-sps-pps=true",
                      "insert-aud=true", "maxperf-enable=true", "preset-level=1", "!",
                      "h264parse", "!", "video/x-h264,stream-format=byte-stream", "!",
                      "fdsink", "fd={}".format(h264_fd)]
        return komut

    def _yayinla(self):
        h264_oku = h264_yaz = None
        if self.h264_destek:
            h264_oku, h264_yaz = os.pipe()
        log("kamera açılıyor", "(JPEG + H.264)" if h264_yaz else "(JPEG)")
        p = subprocess.Popen(self._komut(h264_yaz), stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                             bufsize=0, pass_fds=(h264_yaz,) if h264_yaz else ())
        if h264_yaz:
            os.close(h264_yaz)
            threading.Thread(target=self._h264_oku, args=(h264_oku,), daemon=True).start()
        akis = p.stdout
        sayac_bas, zaman_bas = 0, time.time()
        kare_geldi = False
        try:
            while True:
                # izleyen kalmadıysa bir süre sonra kapat
                if self.izleyen == 0 and time.time() - self.son_izleyen > KAMERA_BOSTA_KAPAT:
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

    def _h264_oku(self, fd):
        """Kodlayıcı çıktısını erişim birimlerine (bir kare) böler: her kare AUD ile başlar."""
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
                    anahtar = anahtar_kare_mi(au)
                    for a in list(self.aboneler):
                        a.ekle(au, anahtar)

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
    while True:
        time.sleep(NABIZ_ARALIGI)
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
        fark_min = [None]          # sayfa saati ile bizim saat arasındaki en küçük fark (ms)
        atilan = [0]

        def durum_gonder():
            try:
                while ws.acik:
                    d = durum()
                    d.update(t="durum", atilan=atilan[0])
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
                    self._mesaj(ws, baglanti, m, fark_min, atilan)
                except (ValueError, TypeError, KeyError):
                    continue
        except websoket.Kapandi:
            pass
        finally:
            ws.acik = False
            SURUCU.ayrildi(baglanti)

    def _mesaj(self, ws, baglanti, m, fark_min, atilan):
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
                fark = ms() - float(m["z"])
                if fark_min[0] is None or fark < fark_min[0]:
                    fark_min[0] = fark
                if (sol or sag) and fark - fark_min[0] > ESKI_KOMUT_MS:
                    atilan[0] += 1
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
        abone = H264Abone()
        KAM.aboneler.add(abone)
        KAM.izle(+1)
        # Okuyan iş parçacığı: tarayıcı kapatınca ya da ping atınca haberimiz olsun
        threading.Thread(target=self._oku_bos, args=(ws,), daemon=True).start()
        try:
            while ws.acik:
                oge = abone.al(1.0)
                if not oge:
                    continue
                au, anahtar = oge
                ws.ikili((b"\x01" if anahtar else b"\x00") + au)
                KAM.gonderilen += len(au)
        except websoket.Kapandi:
            pass
        finally:
            ws.acik = False
            KAM.aboneler.discard(abone)
            KAM.izle(-1)

    @staticmethod
    def _oku_bos(ws):
        try:
            while True:
                ws.oku()
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
       "k":korna basılı 0/1, "ks":korna sesi 0-3, "fs":selektör sayacı, "mi":melodi sayacı, "mn":melodi no}
    - Paket kaybolursa sonraki paket aynı durumu taşıdığı için "aldım" (ack) gerekmez.
    - Tek seferlik işler (selektör, melodi başlatma) sayaçla gelir: sayaç değişince bir kez yapılır;
      o paket kaybolsa da sonraki pakette sayaç yine farklıdır.
    - LED gibi durumlar yalnızca uygulamanın istediği değer değişince Arduino'ya yazılır; böylece
      tarayıcıdan yapılan değişikliği yeni bağlanan uygulama ezmez.
    - Sırası eski (n küçük) ya da geç kalmış (z) paketin sürüş kısmı uygulanmaz.
    - Her pakete durum + aynı n ve z ile cevap verilir: uygulama gidip gelme süresini ve kaybı ölçer.
    {"t":"bul"} paketine (yayın) {"t":"robot"} cevabı verilir: uygulama robotun adresini kendisi bulur.
    """

    DURUMLAR = (("led", "L"), ("dortlu", "H"), ("motor_sesi", "E"), ("geri_bip", "V"))

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
            if t == "bul":
                self._cevap(adres, {"t": "robot", "ad": socket.gethostname(), "port": PORT})
            elif t == "d":
                try:
                    self._durum_paketi(adres, m)
                except (ValueError, TypeError):
                    continue

    def _durum_paketi(self, adres, m):
        surucu_duyuldu()
        simdi = time.time()
        ist = self.istemciler.get(adres)
        n = int(m.get("n", 0))
        # Uygulama yeniden açıldıysa sıra baştan başlar: uzun süre ses yoksa yeni istemci say
        if ist is None or simdi - ist["son"] > UDP_ISTEMCI_ZAMAN_ASIMI or n < ist["n"] - 1000:
            if ist:
                SURUCU.ayrildi(ist["baglanti"])
            ist = {"baglanti": object(), "n": n - 1, "fark_min": None, "onceki": None, "korna": 0.0, "atilan": 0}
            self.istemciler[adres] = ist
            log("UDP istemci:", adres[0])
        ist["son"] = simdi
        taze = n > ist["n"]
        if taze:
            ist["n"] = n

        sol = max(-255, min(255, int(m.get("sol", 0))))
        sag = max(-255, min(255, int(m.get("sag", 0))))
        if "z" in m:
            fark = ms() - float(m["z"])
            if ist["fark_min"] is None or fark < ist["fark_min"]:
                ist["fark_min"] = fark
            if (sol or sag) and fark - ist["fark_min"] > ESKI_KOMUT_MS:
                taze = False
                ist["atilan"] += 1
        if taze:
            SURUCU.surus(ist["baglanti"], sol, sag)
        elif sol or sag:
            SURUCU.surus(ist["baglanti"], 0, 0)

        onceki = ist["onceki"]
        if taze and onceki is not None:
            for alan, harf in self.DURUMLAR:
                if alan in m and m[alan] != onceki.get(alan):
                    ARDUINO.gonder("{} {}".format(harf, 1 if m[alan] else 0))
            if m.get("fs") != onceki.get("fs"):
                ARDUINO.gonder("F")
            if m.get("mi") != onceki.get("mi"):
                ARDUINO.gonder("C {}".format(int(m.get("mn", 0))))
            if not m.get("k") and onceki.get("k"):
                ARDUINO.gonder("K 1")                  # bırakıldı: hemen sus
        if taze and m.get("k") and simdi - ist["korna"] > KORNA_YENILEME:
            ist["korna"] = simdi
            ARDUINO.gonder("K 250 {}".format(max(0, min(3, int(m.get("ks", 0))))))
        if taze:
            ist["onceki"] = m

        d = durum()
        d.update(t="s", n=n, z=m.get("z"), atilan=ist["atilan"])
        self._cevap(adres, d)

    def _bekci(self):
        while True:
            time.sleep(0.2)
            simdi = time.time()
            for adres, ist in list(self.istemciler.items()):
                if simdi - ist["son"] > UDP_ISTEMCI_ZAMAN_ASIMI:
                    SURUCU.ayrildi(ist["baglanti"])
                    del self.istemciler[adres]
                    log("UDP istemci gitti:", adres[0])


def main():
    threading.Thread(target=nabiz_dongusu, daemon=True).start()
    UdpKanal()
    log("Sürüş köprüsü hazır, port", PORT, "UDP", UDP_PORT)
    Sunucu(("0.0.0.0", PORT), Istek).serve_forever()


if __name__ == "__main__":
    main()
