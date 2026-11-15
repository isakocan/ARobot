"""Kare kaynakları: kayıttan gerçek zamanlı tekrar ve robottan canlı akış.

İkisi de en yeni kareyi bir `Posta`ya koyar (tek yer; işleyici yetişemezse eski kare ÜSTÜNE yazılır, kuyruk yok, kayıp sayılır).
Paket: {"jpeg": bytes, "t_ms": int, "komut": (sol, sag), "geldi": time.time(), "no": int}

TEKRAR  kayıt klasörü (kayit.py biçimi: bilgi.json, kareler.mjpg | kareler/*.jpg, kareler.csv, komutlar.csv, durum.csv). Kare, kaydın kendi
        saatinde (t_ms) gerçek zamanda (x1, x2, x4) yayımlanır. Komut: durum.csv varsa o (Arduino'nun gerçekten uyguladığı PWM, çevrimdışı
        koşulardaki girdi), yoksa komutlar.csv. Kayıt klasörüne YAZILMAZ.
CANLI   robotun köprüsünden YALNIZ OKUR: GET /goruntu.mjpg (ham JPEG akışı) ve GET /api/durum (motor durumu, ~8 Hz). Robota hiçbir komut,
        ayar ya da kayıt isteği gönderilmez (yalnız GET; POST yok). Kamera aşırı ısınmasın diye oturum en çok `sinir_sn` (varsayılan 170 sn) sürer.
        Kare zamanı = karenin bilgisayara ulaştığı an (köprü kare damgası yollamaz): ağ titreşimi dt'ye karışır, kayıtlardaki ham kayıt saatinden
        biraz gürültülüdür."""
import bisect
import csv
import http.client
import json
import os
import re
import sys
import threading
import time

import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
KOD = os.path.abspath(os.path.join(KLASOR, "..", "konum_deney"))
if KOD not in sys.path:
    sys.path.insert(0, KOD)


class Posta:
    """Tek gözlü kutu: en yeni paket. `koy` bekleyen (alınmamış) paketi ezerse kayıp sayılır."""

    def __init__(self):
        self._k = threading.Condition()
        self._paket = None
        self.kayip = 0
        self.gelen = 0
        self._ver = 0
        self._zam = []

    def koy(self, paket):
        with self._k:
            if self._paket is not None:
                self.kayip += 1
            self._paket = paket
            self.gelen += 1
            t = time.time()
            self._zam.append(t)
            while self._zam and t - self._zam[0] > 3.0:
                self._zam.pop(0)
            self._k.notify()

    def al(self, bekle=0.2):
        with self._k:
            if self._paket is None:
                self._k.wait(bekle)
            p, self._paket = self._paket, None
            return p

    def hz(self):
        with self._k:
            z = [t for t in self._zam if time.time() - t <= 3.0]
        return (len(z) - 1) / max(z[-1] - z[0], 1e-6) if len(z) > 1 else 0.0

    def bosalt(self):
        with self._k:
            self._paket = None


def _csv_komut(yol):
    with open(yol, newline="", encoding="utf-8") as f:
        s = list(csv.DictReader(f))
    if not s:
        return None
    t = np.array([int(float(r["t_ms"])) for r in s], dtype=np.int64)
    return t, np.array([float(r["sol"]) for r in s]), np.array([float(r["sag"]) for r in s])


class Kaynak:
    """Ortak üst sınıf: iş parçacığı, duraklat, durum sözlüğü."""
    tur = "kaynak"

    def __init__(self, posta, ad):
        self.posta = posta
        self.ad = ad
        self.durum = "hazir"          # hazir | oynuyor | duraklatildi | bitti | kesildi | hata
        self.mesaj = ""
        self._dur = threading.Event()
        self._it = None
        self.kare_no = 0

    def baslat(self):
        self._it = threading.Thread(target=self._calis, name=self.tur, daemon=True)
        self._it.start()

    def durdur(self):
        self._dur.set()
        if self._it is not None and self._it is not threading.current_thread():
            self._it.join(timeout=3.0)

    def bilgi(self):
        return {"ad": self.ad, "tur": self.tur, "durum": self.durum, "mesaj": self.mesaj}


class TekrarKaynagi(Kaynak):
    tur = "tekrar"

    def __init__(self, posta, klasor, hiz=1.0, dongu=False):
        from kayit import Kayit
        super().__init__(posta, os.path.basename(os.path.normpath(klasor)))
        self.klasor = klasor
        self.kay = Kayit(klasor)
        self.t = self.kay.t_ms
        self.n = len(self.kay)
        yol = os.path.join(klasor, "durum.csv")
        self._komut = _csv_komut(yol) if os.path.exists(yol) else None
        self.komut_kaynagi = "durum.csv" if self._komut is not None else "komutlar.csv"
        self.hiz = float(hiz)
        self.dongu = dongu
        self.duraklat = False
        self._basa = threading.Event()
        self._sanal = float(self.t[0]) if self.n else 0.0      # kaydın saati (ms)
        self.sira = 0
        self.kare_sayisi = self.n
        self.sure_sn = float(self.t[-1] - self.t[0]) / 1000.0 if self.n else 0.0
        self.baslangic_t = float(self.t[0]) if self.n else 0.0
        self.durum = "duraklatildi" if self.duraklat else "oynuyor"

    def komut(self, t_ms):
        if self._komut is None:
            return self.kay.komut(t_ms)
        kt, ks, kg = self._komut
        i = bisect.bisect_right(kt, t_ms) - 1
        return (float(ks[i]), float(kg[i])) if i >= 0 else (0.0, 0.0)

    def kare_uret(self, bas=0, adet=None, adim=1):
        """Gerçek zamanlı bekleme OLMADAN sıralı paketler (testler, ölçüm): aynı paket biçimi, atlama yok."""
        son = self.n if adet is None else min(self.n, bas + adet * adim)
        for i in range(bas, son, adim):
            t = int(self.t[i])
            yield {"jpeg": self.kay.jpeg_bayt(i), "t_ms": t, "komut": self.komut(t), "geldi": time.time(), "no": i}

    def basa_sar(self):
        self._basa.set()

    def ayarla(self, duraklat=None, hiz=None):
        if duraklat is not None:
            self.duraklat = bool(duraklat)
        if hiz is not None:
            self.hiz = max(0.25, min(float(hiz), 8.0))

    def bilgi(self):
        d = super().bilgi()
        d.update(konum_sn=round((self._sanal - self.baslangic_t) / 1000.0, 2), toplam_sn=round(self.sure_sn, 2), hiz=self.hiz,
                 duraklat=self.duraklat, kare=self.sira, kare_sayisi=self.n, komut_kaynagi=self.komut_kaynagi)
        return d

    def _calis(self):
        son = time.perf_counter()
        while not self._dur.is_set():
            simdi = time.perf_counter()
            dt = simdi - son
            son = simdi
            if self._basa.is_set():
                self._basa.clear()
                self.sira = 0
                self._sanal = float(self.t[0])
                self.durum = "duraklatildi" if self.duraklat else "oynuyor"
            if self.sira >= self.n:
                if self.dongu:
                    self.sira = 0
                    self._sanal = float(self.t[0])
                else:
                    self.durum = "bitti"
                    time.sleep(0.05)
                    continue
            if self.duraklat:
                self.durum = "duraklatildi"
                time.sleep(0.02)
                continue
            self.durum = "oynuyor"
            self._sanal += dt * 1000.0 * self.hiz
            # kaydın saati ilerledi: vadesi gelen kareler arasından EN YENİSİ yayımlanır (kamera gibi; gerideysek ara kareler atlanır)
            if self.t[self.sira] <= self._sanal:
                hedef = int(np.searchsorted(self.t, self._sanal, side="right")) - 1
                hedef = max(self.sira, min(hedef, self.n - 1))
                t = int(self.t[hedef])
                try:
                    jpeg = self.kay.jpeg_bayt(hedef)
                except Exception as e:
                    self.durum, self.mesaj = "hata", repr(e)
                    return
                self.posta.koy({"jpeg": jpeg, "t_ms": t, "komut": self.komut(t), "geldi": time.time(), "no": hedef,
                                "atlanan_kaynak": hedef - self.sira})
                self.sira = hedef + 1
            else:
                time.sleep(min(0.004, max(0.0005, (self.t[self.sira] - self._sanal) / 1000.0 / max(self.hiz, 0.1))))


class CanliKaynak(Kaynak):
    tur = "canli"

    def __init__(self, posta, adres, sinir_sn=170.0, durum_hz=8.0, yol="mjpg"):
        adres = adres.replace("http://", "").rstrip("/")
        self.host, _, port = adres.partition(":")
        self.port = int(port) if port else 8080
        super().__init__(posta, "robot " + self.host)
        self.sinir_sn = float(sinir_sn)
        self.durum_hz = float(durum_hz)
        self.sol_sag = (0.0, 0.0)
        self.robot = {}
        self.t0 = time.monotonic()
        self.baslangic = None
        self.ilk_kare = None
        self.byte = 0
        self.fps_olc = 0.0
        self.yol = yol
        self.sinir_asildi = False
        self._varis = []

    def bilgi(self):
        d = super().bilgi()
        gecen = 0.0 if self.baslangic is None else time.monotonic() - self.baslangic
        v = list(self._varis)
        alinan = (len(v) - 1) / (v[-1] - v[0]) if len(v) > 1 and v[-1] > v[0] else 0.0
        if self.durum == "oynuyor" and v and time.monotonic() - v[-1] > 1.5:
            alinan = 0.0
        d.update(alinan_fps=round(alinan, 1), yol=self.yol, sinir_asildi=self.sinir_asildi, mbit=round(self.byte * 8 / 1e6 / max(gecen, 1e-3), 1),
                 konum_sn=round(gecen, 1), toplam_sn=self.sinir_sn, robot=self.robot, host=self.host, kalan_sn=round(max(0.0, self.sinir_sn - gecen), 1))
        return d

    # YALNIZ GET: robota istek değiştirici hiçbir şey gitmez
    def _get(self, baglanti, yol):
        baglanti.request("GET", yol)
        return baglanti.getresponse()

    def _durum_dongu(self):
        bag = None
        while not self._dur.is_set():
            try:
                if bag is None:
                    bag = http.client.HTTPConnection(self.host, self.port, timeout=3)
                r = self._get(bag, "/api/durum")
                j = json.loads(r.read().decode("utf-8"))
                m = j.get("motor") or {}
                if m:
                    self.sol_sag = (float(m.get("sol", 0)), float(m.get("sag", 0)))
                self.robot = {k: j.get(k) for k in ("arduino", "kamera", "fps", "sinyal", "sicaklik", "veri_kbps")}
                self.robot["motor_ma"] = [m.get("sol_ma"), m.get("sag_ma")] if m else None
                self.robot["ariza"] = m.get("ariza") if m else None
            except Exception as e:
                if bag is not None:
                    try:
                        bag.close()
                    except Exception:
                        pass
                bag = None
                self.mesaj = "durum okunamadı: " + repr(e)
            self._dur.wait(1.0 / self.durum_hz)

    def _kare_dongusu_mjpg(self):
        """GET /goruntu.mjpg: çok parçalı JPEG. Tamponlu okuyucudan satır + tam uzunluk okuma (bayt birleştirme yok)."""
        bag = http.client.HTTPConnection(self.host, self.port, timeout=6)
        try:
            r = self._get(bag, "/goruntu.mjpg?fps=15")
            if r.status != 200:
                raise RuntimeError("HTTP %d" % r.status)
            self.durum = "oynuyor"
            fp = r.fp
            n = None
            while not self._dur.is_set():
                if self._sinir_doldu():
                    return
                satir = fp.readline(4096)
                if not satir:
                    raise RuntimeError("akış kapandı")
                if satir[:15].lower() == b"content-length:":
                    n = int(satir[15:])
                elif satir in (b"\r\n", b"\n") and n:
                    jpeg = fp.read(n)
                    if len(jpeg) < n:
                        raise RuntimeError("akış kapandı")
                    self._kare_geldi(jpeg)
                    n = None
        finally:
            try:
                bag.close()          # akışı kapat: köprü izleyen kalmayınca kamerayı 10 sn sonra kapatır
            except Exception:
                pass

    def _kare_dongusu_h264(self):
        """ws://robot:8080/goruntu-h264 (Unity uygulamasının kullandığı yol): robotun donanım H.264 akışı, YALNIZ OKUNUR (istemci hiçbir mesaj yollamaz;
        akış adı sorguda: ?akis=yuksek). Çözme: imageio_ffmpeg'in ffmpeg'i (boru), çıktı JPEG. Kayıplı (4 Mbit/s) olduğundan ham JPEG'den biraz bulanıktır."""
        import subprocess
        import imageio_ffmpeg
        from websockets.sync.client import connect
        ff = subprocess.Popen([imageio_ffmpeg.get_ffmpeg_exe(), "-loglevel", "error", "-fflags", "nobuffer", "-flags", "low_delay", "-probesize", "32768", "-use_wallclock_as_timestamps", "1",
                               "-f", "h264", "-i", "pipe:0", "-an", "-fps_mode", "passthrough", "-pix_fmt", "yuvj420p", "-c:v", "mjpeg", "-q:v", "3", "-f", "image2pipe", "pipe:1"],
                              stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, bufsize=0)

        def jpeg_oku():
            tampon = bytearray()
            while not self._dur.is_set():
                p = ff.stdout.read(65536)
                if not p:
                    break
                tampon += p
                while True:
                    a = tampon.find(b"\xff\xd8")
                    b = tampon.find(b"\xff\xd9", a + 2) if a >= 0 else -1
                    # JPEG içinde 0xFFD9 yalnız bitiş imidir (veri baytları 0xFF sonrası 0x00 ile kaçırılır)
                    if a < 0 or b < 0:
                        break
                    jpeg = bytes(tampon[a:b + 2])
                    del tampon[:b + 2]
                    self._kare_geldi(jpeg)
        th = threading.Thread(target=jpeg_oku, name="h264_jpeg", daemon=True)
        th.start()
        try:
            with connect("ws://%s:%d/goruntu-h264?akis=yuksek" % (self.host, self.port), max_size=8 * 1024 * 1024, open_timeout=6) as ws:
                self.durum = "oynuyor"
                while not self._dur.is_set():
                    if self._sinir_doldu():
                        return
                    m = ws.recv(timeout=6)
                    if isinstance(m, (bytes, bytearray)) and len(m) > 1:
                        ff.stdin.write(bytes(m[1:]))
        finally:
            try:
                ff.stdin.close()
            except Exception:
                pass
            ff.terminate()

    def uzat(self, sn):
        """Süre sınırını uzat (kamera ısınması riskiyle: her tıklama +sn, sürücü bilinçli uzatır)."""
        self.sinir_sn += float(sn)
        if self.durum == "bitti" and self.sinir_asildi:
            return False
        return True

    def _sinir_doldu(self):
        if time.monotonic() - self.baslangic > self.sinir_sn:
            self.durum = "bitti"
            self.sinir_asildi = True
            self.mesaj = "süre sınırı (%d sn) doldu; kamera ısınmasın diye bağlantı kapatıldı" % self.sinir_sn
            return True
        return False

    def _kare_geldi(self, jpeg):
        self.byte += len(jpeg)
        t = int(round((time.monotonic() - self.t0) * 1000.0))
        self.kare_no += 1
        self._varis.append(time.monotonic())
        while self._varis and self._varis[0] < self._varis[-1] - 3.0:
            self._varis.pop(0)
        self.posta.koy({"jpeg": jpeg, "t_ms": t, "komut": self.sol_sag, "geldi": time.time(), "no": self.kare_no})

    def _calis(self):
        threading.Thread(target=self._durum_dongu, name="canli_durum", daemon=True).start()
        self.baslangic = time.monotonic()
        self.t0 = self.baslangic
        try:
            if self.yol == "h264":
                self._kare_dongusu_h264()
            else:
                self._kare_dongusu_mjpg()
        except Exception as e:
            self.durum, self.mesaj = "kesildi", "bağlantı: " + repr(e)
        finally:
            self._dur.set()
