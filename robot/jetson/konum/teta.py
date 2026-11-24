"""Theta S çift balık gözü modeli (Jetson ve PC; Python 3.6, numpy + OpenCV).

Kamera USB'den 1280x720 verir: sol çember ön lens, sağ çember arka lens; çemberler üstteki 640 satırda,
merkezler (320,320) ve (960,320), yarıçap 320 piksel, her lens 203 derece (eşit uzaklık: r = θ / (FOV/2) * 320).
Kamera robota yan yatık takılı. Model, tools/kayit_videoya.py'nin ffmpeg v360 çıktısıyla piksel piksel
karşılaştırılarak bulundu (14 Kasım kaydı, 3 kare, korelasyon 0,948).

Robot koordinatı: x sağ, y yukarı, z ileri (metre). Telefon (Unity) ile aynı el yönü.
"""
import json
import math
import os

import cv2
import numpy as np

GEN, YUK = 1280, 720
CEMBER_Y = 320.0
ON_X, ARKA_X = 320.0, 960.0
YARICAP = 320.0
FOV = math.radians(203.0)


# Ölçülmüş lens modeli (robot uygulamasının ARaki/DualFisheye gölgelendiricisi ve surus.html ile aynı; tools/lens_ayarla.py
# iki lensin örtüştüğü bantta görüntüler eşleşsin diye buldu): merkez x, y (px), ölçek (px/radyan); 2. lensin dönmesi.
# Kaba model (merkezler 320/960, 203 derece) ile 1-4 px fark var; bu fark iki lensin birleştiği çizgide (robotun tam sağı
# ve solu) 30 px'lik işareti büküyordu: 21 Kasım kaydında orada işaret yönü 25 derece yanılıyordu.
LENS1 = (322.42, 319.04, 180.87)
LENS2 = (956.67, 320.15, 180.30)


def _don(rx, ry, rz):
    cx, sx, cy, sy, cz, sz = [f(math.radians(a)) for a in (rx, ry, rz) for f in (math.cos, math.sin)]
    Rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    Ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    Rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    return Rz @ Ry @ Rx


LENS2_DON = _don(-0.141, 0.984, 0.683)

# Kameranın robota göre eğimi (24 Kasım): kamera tam düz takılı değil. İşaretin kamera karşısındaki yüksekliği bakış
# yönüne göre değişiyordu (sağda 4 cm yüksek); iki günün kaydından en küçük kareler: yana 2,46, öne 0,73 derece (artık
# 2,2 cm -> 0,5 cm). Düzeltilmezse AR nesneleri zemine oturmaz, kuşbakışı zemin ve işaret yüksekliği bozulur.
# ~/.config/robot/kamera_egimi.json {"ileri": derece, "yan": derece} (araç: konum/kalibre_egim.py) varsa ondan.
EGIM_DOSYASI = os.path.expanduser("~/.config/robot/kamera_egimi.json")


def egim_oku():
    try:
        with open(EGIM_DOSYASI) as f:
            e = json.load(f)
        return float(e.get("ileri", 0.0)), float(e.get("yan", 0.0))
    except (OSError, ValueError, TypeError):
        return 0.73, 2.46


EGIM_ILERI, EGIM_YAN = egim_oku()


def egim_matrisi(ileri, yan):
    """Gerçek (düz) robot yönü -> kameranın 'düz' sandığı yön: sağ (1,0,0) yan derece yukarı, ön (0,0,1) ileri derece
    yukarı görünür."""
    return _don(-ileri, 0.0, 0.0) if yan == 0 else _don(0.0, 0.0, yan) @ _don(-ileri, 0.0, 0.0)


EGIM = egim_matrisi(EGIM_ILERI, EGIM_YAN)


def yon_pikseller(d):
    """Robot koordinatındaki birim ışınlar (N,3) -> ham karedeki (u, v) piksel (N,2) float32.

    Kamera robota yan yatık (yatma 90): kamera = (-y, x, z). Ön yarı küre lens 1, arka lens 2."""
    d = np.asarray(d, dtype=np.float64) @ EGIM.T
    c = np.stack([-d[:, 1], d[:, 0], d[:, 2]], -1)
    on = c[:, 2] >= 0
    c2 = np.stack([-c[:, 0], c[:, 1], -c[:, 2]], -1) @ LENS2_DON.T
    k = np.where(on[:, None], c, c2)
    L = np.where(on[:, None], np.array(LENS1)[None], np.array(LENS2)[None])
    th = np.arccos(np.clip(k[:, 2], -1, 1))
    n = np.maximum(np.hypot(k[:, 0], k[:, 1]), 1e-12)
    u = L[:, 0] + k[:, 0] / n * th * L[:, 2]
    v = L[:, 1] - k[:, 1] / n * th * L[:, 2]
    return np.stack([u, v], -1).astype(np.float32)


def kaba_yon_pikseller(d):
    """Eski kaba model (merkezler 320/960, yarıçap 320 = 101,5 derece). Yalnız pusula kullanır: pusula_maske.png bu
    modelin şeridinden çıkarıldı ve yerinde dönüş testinde ölçülmüş modelden daha iyi sonuç verdi (720 derecede 3,5'e 17)."""
    d = np.asarray(d, dtype=np.float64)
    cx, cy, cz = d[:, 1], -d[:, 0], d[:, 2]
    on = cz >= 0
    th = np.where(on, np.arccos(np.clip(cz, -1, 1)), np.arccos(np.clip(-cz, -1, 1)))
    ph = np.arctan2(cy, cx)
    r = th / math.radians(101.5) * 320.0
    u = np.where(on, 320.0 - r * np.cos(ph), 960.0 + r * np.cos(ph))
    v = 320.0 + r * np.sin(ph)
    return np.stack([u, v], -1).astype(np.float32)


def es_dikdortgen_haritasi(gen, yuk, enlem_alt=-90.0, enlem_ust=90.0, model=None):
    """Eş dikdörtgen (boylam -180..180, robotun önü ortada; enlem aralığı) için cv2.remap haritaları."""
    v, u = np.mgrid[0:yuk, 0:gen]
    lon = ((u + 0.5) / gen - 0.5) * 2 * math.pi
    lat = math.radians(enlem_ust) - (v + 0.5) / yuk * math.radians(enlem_ust - enlem_alt)
    d = np.stack([np.cos(lat) * np.sin(lon), np.sin(lat), np.cos(lat) * np.cos(lon)], -1).reshape(-1, 3)
    p = (model or yon_pikseller)(d)
    return p[:, 0].reshape(yuk, gen).copy(), p[:, 1].reshape(yuk, gen).copy()


class SanalKamera:
    """Robotun etrafına bakan sanal iğne deliği kamera (ArUco aramak için): yaw (derece, sağa +), yatay görüş, boyut.

    CV koordinatı: x sağ, y aşağı, z ileri. ray_robot = a*sag + b*asagi + ileri (a, b normalleştirilmiş)."""

    def __init__(self, yaw_derece, hfov_derece=80.0, gen=400, yuk=300, pitch_derece=0.0):
        self.yaw = math.radians(yaw_derece)
        self.gen, self.yuk = gen, yuk
        self.f = (gen / 2.0) / math.tan(math.radians(hfov_derece) / 2)
        self.cx, self.cy = gen / 2.0, yuk / 2.0
        p = math.radians(pitch_derece)
        ileri = np.array([math.sin(self.yaw) * math.cos(p), math.sin(p), math.cos(self.yaw) * math.cos(p)])
        sag = np.array([math.cos(self.yaw), 0.0, -math.sin(self.yaw)])
        asagi = np.cross(ileri, sag)     # sağ el çarpımı: ileri x sağ = aşağı (y aşağı CV)
        if asagi[1] > 0:
            asagi = -asagi
        self.R = np.stack([sag, asagi, ileri], 1)          # sütunlar: CV x, y, z -> robot
        v, u = np.mgrid[0:yuk, 0:gen]
        a = (u + 0.5 - self.cx) / self.f
        b = (v + 0.5 - self.cy) / self.f
        isin = np.stack([a, b, np.ones_like(a)], -1).reshape(-1, 3) @ self.R.T
        isin /= np.linalg.norm(isin, axis=1, keepdims=True)
        p2 = yon_pikseller(isin)
        self.mx = p2[:, 0].reshape(yuk, gen).copy()
        self.my = p2[:, 1].reshape(yuk, gen).copy()

    def goruntu(self, ham_gri):
        return cv2.remap(ham_gri, self.mx, self.my, cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0)

    def robota(self, a, b):
        """Normalleştirilmiş görüntü (a, b) -> robot koordinatında yön (birim değil)."""
        return self.R @ np.array([a, b, 1.0])


class DinamikMaske:
    """Robotun kendi gövdesini (direk, anten, kablo, üstüne konan telefon) görüntüden kendiliğinden ayırır (25 Kasım).

    Robot dönerken / giderken oda görüntüde kayar, robotun parçaları yerinde kalır. Hareketli karelerde ardışık iki
    görüntünün piksel farkı üstel ortalamayla tutulur: farkı sürekli küçük olan piksel gövdedir. Antenler oynasa ya da
    bir şey takılsa birkaç hareketten sonra yeni yerini öğrenir. Yeterince hareket görülene kadar dosyadaki sabit
    maske (varsa) kullanılır. Dokusuz yüzey (düz duvar) de "değişmiyor" görünebilir; zaten eşleşmeye katkısı yoktur.

    kullan(): True = kullan (gövde değil), dizi biçimi görüntüyle aynı."""

    def __init__(self, sekil, sabit=None, oran=0.08, esik=0.3, en_az=8):
        self.fark = np.zeros(sekil, np.float32)
        self.sabit = sabit
        self.oran, self.esik, self.en_az = oran, esik, en_az
        self.sayi = 0
        self.onceki = None
        self._m = None

    def guncelle(self, g, hareketli):
        """g: bu karenin görüntüsü (float32, sabit boyut). hareketli: robot bu adımda belirgin döndü / ilerledi mi."""
        if self.onceki is not None and hareketli:
            d = cv2.GaussianBlur(np.abs(g - self.onceki), (0, 0), 1.0)
            self.fark += self.oran * (d - self.fark) if self.sayi else (d - self.fark)
            self.sayi += 1
            self._m = None
        self.onceki = g

    def kullan(self):
        if self.sayi < self.en_az:
            return self.sabit if self.sabit is not None else np.ones(self.fark.shape, bool)
        if self._m is None:
            # piksel, sahnenin tipik değişiminin esik katından az değişiyorsa gövde
            m = self.fark > self.esik * float(np.median(self.fark))
            self._m = cv2.erode(m.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
        return self._m


class Pusula:
    """Görsel pusula: ufuk bandının (enlem -5..+30 derece, robot gövdesinin üstü) 360 derece eş dikdörtgen şeridi
    kareden kareye faz korelasyonuyla kaydırılır; kayma = dönüş. Ölçek: 2 piksel / derece."""

    PIKSEL_DERECE = 2.0

    def __init__(self):
        self.gen = int(360 * self.PIKSEL_DERECE)
        self.yuk = 72
        self.mx, self.my = es_dikdortgen_haritasi(self.gen, self.yuk, -5.0, 31.0, kaba_yon_pikseller)
        self.onceki = None
        self.pencere = cv2.createHanningWindow((self.gen, self.yuk), cv2.CV_32F)
        # boylamda pencere istemiyoruz (şerit dairesel): yalnız dikey kenarları yumuşat
        dikey = np.hanning(self.yuk).astype(np.float32)[:, None]
        self.pencere = np.repeat(dikey, self.gen, axis=1)
        # robotun kendi sabit parcalari (anten): hic kaymadiklari icin sifirda sahte tepe yapiyorlardi (21 Kasim kaydi);
        # maske kayittaki zamanla degismeyen seritten pikselleri (pusula_maske.png, beyaz = kullan)
        import os
        mp = os.path.join(os.path.dirname(os.path.abspath(__file__)), "pusula_maske.png")
        mk = cv2.imread(mp, cv2.IMREAD_GRAYSCALE) if os.path.exists(mp) else None
        sabit = (mk > 127) if mk is not None and mk.shape == (self.yuk, self.gen) else None
        # gövde maskesi kendiliğinden güncellenir (DinamikMaske); dosya yalnız başlangıçta
        self.maske = DinamikMaske((self.yuk, self.gen), sabit) if Pusula.DINAMIK else None
        if sabit is not None and not Pusula.DINAMIK:
            self.pencere = self.pencere * sabit.astype(np.float32)
        self.son_ham = None

    DINAMIK = True

    def serit(self, ham_gri):
        """Ufuk şeridinin yatay kenar görüntüsü (parlaklıktan bağımsız; desensiz duvarda da çalışır)."""
        s = cv2.remap(ham_gri, self.mx, self.my, cv2.INTER_LINEAR).astype(np.float32)
        s = cv2.GaussianBlur(s, (0, 0), 1.5)
        self.son_ham = s
        k = cv2.Sobel(s, cv2.CV_32F, 1, 0) * self.pencere
        if self.maske is not None:
            k = k * self.maske.kullan()
        return k

    def _kayma(self, a, b, tahmin):
        """a'dan b'ye dönüş (derece) ve güven. tahmin verilirse ve birden çok güçlü tepe varsa tahmine en yakını."""
        Fa = np.fft.rfft(a, axis=1); Fb = np.fft.rfft(b, axis=1)
        c = np.fft.irfft((Fa * np.conj(Fb)).sum(0), n=self.gen)
        k = int(np.argmax(c))
        if tahmin is not None:
            komsu = np.maximum(np.roll(c, 1), np.roll(c, -1))
            tepeler = np.where((c >= komsu) & (c >= 0.6 * c[k]))[0]
            if len(tepeler) > 1:
                def aci(i):
                    d0 = i if i <= self.gen / 2 else i - self.gen
                    return d0 / self.PIKSEL_DERECE
                k = int(min(tepeler, key=lambda i: abs(_fark(aci(i), tahmin))))
        y0, y1, y2 = c[k - 1], c[k], c[(k + 1) % self.gen]
        ince = 0.5 * (y0 - y2) / (y0 - 2 * y1 + y2) if (y0 - 2 * y1 + y2) != 0 else 0.0
        d = k + ince
        if d > self.gen / 2:
            d -= self.gen
        guven = float(y1 / (np.sqrt((a * a).sum() * (b * b).sum()) + 1e-9))
        # şimdiki şerit d piksel kaydırılınca öncekine oturur: sağa dönüşte sahne sola kayar -> d > 0
        return d / self.PIKSEL_DERECE, guven

    REF_YENILE = 25.0      # derece: referans kareden bu kadar uzaklaşınca yenilenir
    REF_GUVEN = 0.25       # referansa göre güven bunun altına düşerse (ilerleyince sahne değişir) yenilenir

    def guncelle(self, ham_gri, tahmin=None):
        """Dönüş (derece, sağa dönüş +) ve güven (0..1, normalleştirilmiş korelasyon tepesi). İlk karede (0, 0).

        Her kare bir REFERANS kareyle karşılaştırılır (bir öncekiyle değil): kareden kareye ölçümün küçük hatası
        (alt piksel tepesinin tam piksele çekilmesi) her adımda birikir; 21 Kasım ikinci kaydında 830 derece dönüşte
        -25 dereceye ulaşmıştı. Referans, ondan REF_YENILE derece uzaklaşınca ya da benzerlik düşünce yenilenir.
        Referansla güvenilir eşleşme yoksa bir önceki kareyle ölçülür.

        tahmin: motor komutundan beklenen dönüş (derece). Birden çok tepe varsa (en iyisinin %60'ı kadar güçlü),
        tahmine en yakını seçilir: hızlı dönüşte bulanık karede sıfırdaki sahte tepe kazanabiliyordu.

        Şeritler boylamda döngüseldir: satır satır FFT ile döngüsel çapraz korelasyon, tepe = kayma. OpenCV
        phaseCorrelate gerçek kayıtta (21 Kasım, beyaz toplantı odası, hızlı dönüş) sıfıra yapışıyordu."""
        g = self.serit(ham_gri)
        if self.onceki is None:
            self.onceki = g; self.ref = g; self.ref_aci = 0.0; self.toplam = 0.0
            if self.maske is not None:
                self.maske.guncelle(self.son_ham, False)
            return 0.0, 0.0
        t = 0.0 if tahmin is None else tahmin
        dr, gr = self._kayma(self.ref, g, _fark(self.toplam + t, self.ref_aci))
        if gr >= self.REF_GUVEN and abs(dr) < 60.0:
            yeni = self.ref_aci + dr
            d, guven = _fark(yeni, self.toplam), gr
        else:
            d, guven = self._kayma(self.onceki, g, tahmin)
            yeni = self.toplam + d
        self.onceki = g
        self.toplam = yeni
        if not (gr >= self.REF_GUVEN and abs(dr) < self.REF_YENILE):
            self.ref = g; self.ref_aci = yeni
        if self.maske is not None:
            # hareketli: ölçülen ya da komuttan beklenen dönüş belirgin (sıfıra yapışmış ölçümde de komut söyler)
            self.maske.guncelle(self.son_ham, abs(d) > 3.0 or abs(t) > 3.0)
        return d, guven


def _fark(a, b):
    """Derece farkı a - b, -180..180."""
    return (a - b + 180.0) % 360.0 - 180.0
