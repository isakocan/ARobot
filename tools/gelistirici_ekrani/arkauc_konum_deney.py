"""Arka uç 1: tools/konum_deney hattı (kalibre gerçek kamera), kare kare (akış) çalışacak biçimde.

Çevrimdışı koşunun (gercek_e2_kos.py) aynı parçaları, aynı ayarlarla, ama önbellek yerine kare kare:
  1. Yön: M2D-LCN (panoramada ORB + düzlemsel hareket modeli), KOMUTSUZ girdi (komut_oncel=False), birlesik_yon.LcnM2D'nin aynısı
  2. Öteleme: LCN kuşbakışı zemin dokusu (birlesik_bev.LcnTablo + konum_bev.BevOtelemeci), yaw = 1'deki yön, nedensel tarama maskesiyle
  3. Kestirici: birlesik2_kestirici.Kestirici (E2): komut + yön + öteleme, hız/dönüş profilini kendi sürüşünden öğrenir
  4. Zemin sınırı + doluluk ızgarası: SegFormer-B0 (zemin_model), harita_kur.Izgara. PAHALI aşama, ayrı iş parçacığında ve en yeni karede
     çalışır (kendi hızında, çoğunlukla yön/öteleme hızından düşük); bu yüzden "tarama yaşı" durum şeridinde gösterilir.
Yöntem dosyalarına dokunulmaz; kamera ve maske yönlendirmesi gercek_e2_hazirla.kur() ile yapılır (çevrimdışı hatla aynı).
Fark (dürüstlük): çevrimdışı koşuda tarama her 5. karede ve hep karenin kendi pozundaydı; burada tarama en yeni kareden alınır, kendi karesinin
pozuyla ızgaraya eklenir, BEV maskesi için ondan sonraki karelerde (birkaç kare yaşlı) kullanılır."""
import base64
import os
import sys
import threading
import time

import cv2
import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
KOD = os.path.abspath(os.path.join(KLASOR, "..", "konum_deney"))
if KOD not in sys.path:
    sys.path.insert(0, KOD)
sys.path.insert(0, KLASOR)
from arkauc import ArkaUc  # noqa: E402

KAMERA_H = 0.17
BILGI = {"bicim": 1, "kaynak": "gercek", "ad": "canli",
         "kamera": {"tur": "cift_balikgozu", "genislik": 1280, "yukseklik": 720, "fps": 15},
         "kamera_yuksekligi_m": KAMERA_H,
         # yontem_komut._nominal bunu okur; E2 kullanmaz (profil kendinden öğrenir), yön (komutsuz) da kullanmaz. Çevrimdışı kayıtla aynı.
         "kayma_modeli": {"komut_donusumu": {"pwm_255_dogrusal_hiz_m_sn": 0.55, "pwm_255_donus_derece_sn": 197}}}
IZGARA = (-20.0, -20.0, 800, 800, 0.05)       # x0, y0, genişlik, yükseklik, hücre (gercek_e2_harita.py ile aynı)
GUVEN_MIN = 0.2
YORUNGE_ADIM_M = 0.01

_KURULDU = {}


def _kur():
    """Gerçek kamera + sabit gövde maskesi yönlendirmesi (bir kez). Dönüş: modüller."""
    if not _KURULDU:
        import gercek_e2_hazirla as hz
        hz.kur()
        _KURULDU["hz"] = hz
    return _KURULDU["hz"]


def _png_b64(a):
    ok, kod = cv2.imencode(".png", a, [cv2.IMWRITE_PNG_COMPRESSION, 3])
    return base64.b64encode(kod.tobytes()).decode("ascii")


def _yuvarla(a, n=4):
    return np.round(np.asarray(a, np.float64), n).ravel().tolist()


def hizli_bolutleyici_sinifi(zm):
    """zemin_model.Bolutleyici'nin AYNISI, tek fark `_tahmin`: softmax ve zemin sınıfı toplamı mantık çözünürlüğünde (128x128) yapılır,
    olasılık sonra 512x512'ye büyütülür (özgün: 150 kanallı mantık önce 512x512'ye büyütülür, sonra softmax). Görünüm başına yaklaşık 4 kat
    hızlı (RTX 3050 Ti: kare başına ~470 ms -> ~200 ms); sınır uzaklıkları ufak farklıdır (ölçüm: tests ve rapor)."""
    class Hizli(zm.Bolutleyici):
        def _tahmin(self, goruntuler, parca=8):
            torch = self.torch
            F = torch.nn.functional
            sonuc = []
            for i in range(0, len(goruntuler), parca):
                g = goruntuler[i:i + parca]
                x = g[..., ::-1].astype(np.float32) / 255.0
                x = (x - self.ort) / self.std
                t = torch.from_numpy(np.ascontiguousarray(x.transpose(0, 3, 1, 2))).to(self.cihaz)
                if self.yari:
                    t = t.half()
                with torch.no_grad():
                    lg = self.model(pixel_values=t).logits.float()
                    p = lg.softmax(1)[:, self.zemin_idx].sum(1, keepdim=True)
                    p = F.interpolate(p, size=g.shape[1:3], mode="bilinear", align_corners=False)[:, 0]
                    sonuc.append(p.cpu().numpy())
            return np.concatenate(sonuc)
    return Hizli


class Bolutleme:
    """Zemin sınırı hesaplayıcı (aynı süreçte): kare (BGR) -> (mesafe[360], guven[360], max_menzil)."""

    def __init__(self, hizli=True, cihaz=None):
        _kur()
        import zemin_model as zm
        import zemin_ortak as zo
        from kamera import Kamera
        self.zm, self.zo = zm, zo
        self.kam = Kamera.bilgiden(None)
        sinif = hizli_bolutleyici_sinifi(zm) if hizli else zm.Bolutleyici
        self.seg = sinif(cihaz)
        self.tara(self._isinma_karesi())

    @staticmethod
    def _isinma_karesi():
        im = np.zeros((720, 1280, 3), np.uint8)
        cv2.randu(im, 40, 200)
        return im

    def tara(self, im):
        zo, zm = self.zo, self.zm
        e, _ = self.kam.balikgozu_to_esdikdortgen(im, *zo.THETA_ESD)
        g = zo.Girdi(e, zo.theta_maskesi(self.kam), KAMERA_H, "theta")
        Pk, gec = self.seg.zemin_olasiligi(g, mod="gorunum")
        if self.seg.cihaz == "cuda":
            self.seg.torch.cuda.synchronize()
        r = zm.p_den_sinir(Pk, gec, g, "kesim")
        return np.asarray(r.mesafe, np.float32), np.asarray(r.guven, np.float32), float(r.max_menzil)


def _bolutleme_isci(bag, hizli, cihaz):
    """Ayrı süreç gövdesi: JPEG baytı al, (mesafe, guven, menzil) döndür. None = çık."""
    try:
        b = Bolutleme(hizli, cihaz)
        bag.send("hazir")
        while True:
            jpeg = bag.recv()
            if jpeg is None:
                break
            im = cv2.imdecode(np.frombuffer(jpeg, np.uint8), cv2.IMREAD_COLOR)
            bag.send(b.tara(im))
    except (EOFError, KeyboardInterrupt, BrokenPipeError):
        pass
    except Exception as e:
        try:
            bag.send(("hata", repr(e)))
        except Exception:
            pass


class BolutlemeSurec:
    """Bolutleme'yi AYRI SÜREÇTE çalıştırır: SegFormer önişleme/ardişleme Python işleri ana süreçteki yön/öteleme hattının GIL'ini
    yemesin (aynı süreçte ölçüldü: kare hattı 40 -> 55 ms). Aynı `tara` sözleşmesi; JPEG baytı gönderir, sonuç dizilerini alır."""

    def __init__(self, hizli=True, cihaz=None):
        import multiprocessing as mp
        ctx = mp.get_context("spawn")
        self.bag, cocuk = ctx.Pipe()
        self.p = ctx.Process(target=_bolutleme_isci, args=(cocuk, hizli, cihaz), daemon=True, name="bolutleme")
        self.p.start()
        ilk = self.bag.recv()
        if ilk != "hazir":
            raise RuntimeError("bölütleme süreci açılamadı: %r" % (ilk,))
        self.kilit = threading.Lock()

    def tara(self, im, jpeg):
        with self.kilit:
            self.bag.send(jpeg)
            r = self.bag.recv()
        if isinstance(r, tuple) and len(r) == 2 and r[0] == "hata":
            raise RuntimeError(r[1])
        return r

    def kapat(self):
        try:
            self.bag.send(None)
        except Exception:
            pass
        self.p.join(timeout=3.0)
        if self.p.is_alive():
            self.p.terminate()


class KonumDeneyi(ArkaUc):
    ad = "konum_deney"

    def __init__(self, bolutleme=True, cihaz=None, hizli_bolutleme=True, bolutleme_surec=True):
        _kur()
        import yontem_pusula_nokta as ypn
        import birlesik_bev as bb
        import birlesik_yon as by
        import birlesik2_kestirici as b2
        import konum_bev as kb
        import harita_kur as hk
        import zemin_ortak as zo
        import zemin_model as zm
        from kamera import Kamera
        self.ypn, self.bb, self.by, self.b2, self.kb, self.hk, self.zo, self.zm = ypn, bb, by, b2, kb, hk, zo, zm
        self.kam = Kamera.bilgiden(None)
        self.gorunumler = {"pano", "ham"}
        self.bolutleme_acik = bool(bolutleme)
        self._kilit = threading.Lock()           # tarama sonucu + ızgara
        self.hizli_bolutleme = bool(hizli_bolutleme)
        self.bolutleme_surec = bool(bolutleme_surec) and self.bolutleme_acik
        self.seg = None
        if self.bolutleme_acik:
            self.seg = BolutlemeSurec(hizli_bolutleme, cihaz) if self.bolutleme_surec else Bolutleme(hizli_bolutleme, cihaz)
        self._hazirla_geometri()
        self._m2d_sinifi()
        self._tablo = bb.LcnTablo("theta", self.kam, lcn_par=bb.LcnParam, h=KAMERA_H)
        self._govde_png = None
        # tarama iş parçacığı
        self._tarama_gonder = threading.Condition()
        self._tarama_is = None
        self._tarama_calis = True
        self._tarama_it = threading.Thread(target=self._tarama_dongu, name="tarama", daemon=True)
        self._tarama_it.start()
        self.sifirla()
        self._isinma()

    # ------------------------------------------------------------------ kurulum
    def _hazirla_geometri(self):
        """Ham ve panorama görünümü için ışın tabloları (sabit)."""
        kam = self.kam
        # ham görünüm: 4 pikselde bir örnek; her piksel için (azimut dizini, yükseklik açısı, geçerli)
        U, V = np.meshgrid(np.arange(2, 1280, 4), np.arange(2, 720, 4))
        U = U.astype(np.float64)
        V = V.astype(np.float64)
        d = np.zeros(U.shape + (3,))
        ok = np.zeros(U.shape, bool)
        for lens in (0, 1):
            sol = U < 640 if lens == 0 else U >= 640
            dd, oo = kam.piksel_to_isin(U, V, lens)
            d[sol] = dd[sol]
            ok[sol] = oo[sol]
        self._ham_el = np.degrees(np.arcsin(np.clip(d[..., 1], -1, 1)))
        self._ham_az = np.rint(np.degrees(np.arctan2(d[..., 0], d[..., 2])) % 360.0).astype(np.int64) % 360
        self._ham_ok = ok
        # panorama görünümü 512x256
        G, Y = 512, 256
        az = ((np.arange(G) + 0.5) / G * 360.0 - 180.0)
        el = 90.0 - (np.arange(Y) + 0.5) / Y * 180.0
        self._pan_az = np.broadcast_to((np.rint(az) % 360).astype(np.int64) % 360, (Y, G))
        self._pan_el = np.broadcast_to(el[:, None], (Y, G))

    def _m2d_sinifi(self):
        ypn, bb, by = self.ypn, self.bb, self.by
        arka = self

        class LcnM2D(ypn.Yontem):
            """birlesik_yon.hesapla içindeki LcnM2D'nin aynısı + çizim için nokta/içerik kaydı."""

            def _cikar(self, gri, gecerli):
                g = bb.lcn(np.clip(gri, 0, 255), by.LCN_SIGMA, by.LCN_EPS, by.LCN_KAZANC).astype(np.float32)
                r = super()._cikar(g, gecerli)
                self.cizim_tum = r[0]
                return r

            def _duzlem(self, a, b, merkez_der, pencere_der, N):
                r = super()._duzlem(a, b, merkez_der, pencere_der, N)
                self.son_phi = r[3]["phi"]
                self.son_ic = r[3]["icerik"]
                try:
                    esik = np.sin(np.radians(self.esik))
                    ic = self._epi_hata(a, b, r[0], r[3]["phi"])[0, 0] < esik
                    self.cizim_a, self.cizim_b, self.cizim_ic = a, b, ic
                except Exception:
                    pass
                return r

        self._LcnM2D = LcnM2D

    def _isinma(self):
        """Remap tablolarının ilk çağrı gecikmesini kullanıcı görmeden öde (model ısınması Bolutleme kurulurken yapıldı)."""
        im = np.zeros((720, 1280, 3), np.uint8)
        cv2.randu(im, 40, 200)
        self.kam.balikgozu_to_esdikdortgen(im, 1024, 512)
        self.sifirla()

    # ------------------------------------------------------------------ sözleşme
    def katman_tanimlari(self):
        return [
            {"id": "orb", "ad": "ORB noktaları (yön)", "uzay": ["pano", "ham"], "renk": "#7a8aa0", "varsayilan": True, "grup": "kamera"},
            {"id": "orb_ic", "ad": "Eşleşen / uyumlu noktalar", "uzay": ["pano", "ham"], "renk": "#46e08a", "varsayilan": True, "grup": "kamera"},
            {"id": "orb_vektor", "ad": "Hareket vektörleri", "uzay": ["pano", "ham"], "renk": "#ffd166", "varsayilan": False, "grup": "kamera"},
            {"id": "zemin", "ad": "Zemin maskesi", "uzay": ["pano", "ham"], "renk": "#2fa8ff", "varsayilan": False, "grup": "kamera"},
            {"id": "sinir", "ad": "Zemin sınırı", "uzay": ["pano", "ham"], "renk": "#ff7a45", "varsayilan": True, "grup": "kamera"},
            {"id": "govde", "ad": "Robot gövde maskesi", "uzay": ["pano", "ham"], "renk": "#ff4d6d", "varsayilan": False, "grup": "kamera"},
            {"id": "ufuk", "ad": "Ufuk çizgisi", "uzay": ["pano", "ham"], "renk": "#c8b6ff", "varsayilan": False, "grup": "kamera"},
            {"id": "doluluk", "ad": "Doluluk ızgarası", "uzay": ["harita"], "renk": "#a9b7c6", "varsayilan": True, "grup": "harita"},
            {"id": "yorunge", "ad": "Sürülen yol", "uzay": ["harita"], "renk": "#ffd166", "varsayilan": True, "grup": "harita"},
            {"id": "sinir_nokta", "ad": "Güncel sınır noktaları", "uzay": ["harita"], "renk": "#ff7a45", "varsayilan": True, "grup": "harita"},
            {"id": "anahtar", "ad": "Anahtar kareler (öteleme)", "uzay": ["harita"], "renk": "#46e08a", "varsayilan": False, "grup": "harita"},
        ]

    def statik_katmanlar(self):
        kam = self.kam
        out = {"govde": [], "ufuk": []}
        m = cv2.imread(os.path.join(KOD, "kamera_maske_gercek_sabit.png"), cv2.IMREAD_GRAYSCALE)
        if m is None:
            m = np.zeros((720, 1280), np.uint8)
        # ham: 320x180 RGBA (gövde = 255)
        k = cv2.resize(m, (320, 180), interpolation=cv2.INTER_AREA) > 127
        rgba = np.zeros((180, 320, 4), np.uint8)
        rgba[k] = (109, 77, 255, 150)            # BGRA: kırmızımsı (renk sayfada #ff4d6d)
        out["govde"].append({"tur": "resim", "uzay": "ham", "png": _png_b64(rgba)})
        e, kapsam = kam.balikgozu_to_esdikdortgen(m, 512, 256, cv2.INTER_LINEAR)
        kp = e > 127
        rgba = np.zeros((256, 512, 4), np.uint8)
        rgba[kp] = (109, 77, 255, 150)
        out["govde"].append({"tur": "resim", "uzay": "pano", "png": _png_b64(rgba)})
        # ufuk
        out["ufuk"].append({"tur": "kirik", "uzay": "pano", "xy": [0.0, 0.5, 1.0, 0.5], "kapali": False})
        az = np.arange(0, 361, 2.0)
        d = np.stack([np.sin(np.radians(az)), np.zeros_like(az), np.cos(np.radians(az))], -1)
        for xy in self._ham_cizgi_parcalari(d):
            out["ufuk"].append({"tur": "kirik", "uzay": "ham", "xy": _yuvarla(xy), "kapali": False})
        return out

    def gorunumleri_ayarla(self, gorunumler):
        self.gorunumler = set(gorunumler) or {"pano", "ham"}

    def panorama_goruntu(self, kare_bgr, genislik=1024):
        e, _ = self.kam.balikgozu_to_esdikdortgen(kare_bgr, genislik, genislik // 2)
        return e

    def kapat(self):
        self._tarama_calis = False
        with self._tarama_gonder:
            self._tarama_gonder.notify_all()
        if self.bolutleme_surec and self.seg is not None:
            self.seg.kapat()

    # ------------------------------------------------------------------ durum
    def sifirla(self):
        with self._kilit:
            kb, hk = self.kb, self.hk
            self.m2d = self._LcnM2D(dict(BILGI), self.kam, kip="duzlem", komut_oncel=False)
            self.oz = kb.BevOtelemeci(self._tablo)
            self.oz.orb = cv2.ORB_create(nfeatures=500, scaleFactor=1.2, nlevels=3, edgeThreshold=13, fastThreshold=self.bb.LcnParam.fast_esik, patchSize=21)
            self.est = self.b2.Kestirici((0.0, 0.0, 0.0))
            self.izgara = hk.Izgara(*IZGARA)
            self._izgara_kutu = None
            self._izgara_degisti = False
            self._izgara_gonder_t = 0.0
            self._tarama = None                  # dict: mesafe, guven, menzil, kare, poz, t (zaman), sure_ms
            self._ek_maske = None
            self._tarama_n = 0
            self._tarama_ms = 0.0
            self._harita_ms = 0.0
            self.kare_sayaci = 0
            self._son_yorunge = None
            self._son_anahtar = 0
            self._ilk_kare = True
            self._maske_onbellek = {}
            self._temizle_gonder = True
            self._son_cizim = None
        with self._tarama_gonder:
            self._tarama_is = None

    # ------------------------------------------------------------------ tarama (SegFormer + ızgara), ayrı iş parçacığı
    def _tara(self, im, jpeg, poz, kare_no):
        a = time.perf_counter()
        if self.bolutleme_surec:
            mesafe, guven, menzil = self.seg.tara(im, jpeg)
        else:
            mesafe, guven, menzil = self.seg.tara(im)
        b = time.perf_counter()
        em = self.kb.tarama_maskesi(self._tablo, mesafe, guven, 0.25)
        with self._kilit:
            self.izgara.tara_ekle(poz[0], poz[1], poz[2], mesafe, guven, menzil, guven_min=GUVEN_MIN)
            self._izgara_degisti = True
            self._tarama = dict(mesafe=mesafe, guven=guven, menzil=menzil, kare=kare_no, poz=tuple(poz), t=time.time())
            self._ek_maske = em
            self._tarama_n += 1
        c = time.perf_counter()
        self._tarama_ms = (b - a) * 1000.0
        self._harita_ms = (c - b) * 1000.0

    def _tarama_dongu(self):
        while self._tarama_calis:
            with self._tarama_gonder:
                while self._tarama_is is None and self._tarama_calis:
                    self._tarama_gonder.wait(0.2)
                is_ = self._tarama_is
                self._tarama_is = None
            if is_ is None:
                continue
            try:
                self._tara(*is_)
            except Exception as e:      # tarama hatası akışı durdurmasın
                self.son_hata = repr(e)

    def _tarama_iste(self, im, jpeg, poz, kare_no):
        with self._tarama_gonder:
            self._tarama_is = (im, jpeg, poz, kare_no)
            self._tarama_gonder.notify()

    # ------------------------------------------------------------------ yardımcılar: ışın -> görünüm
    def _isin_panoya(self, d):
        az = np.degrees(np.arctan2(d[:, 0], d[:, 2]))
        el = np.degrees(np.arcsin(np.clip(d[:, 1], -1, 1)))
        return np.stack([(az + 180.0) / 360.0, (90.0 - el) / 180.0], 1)

    def _isin_hama(self, d):
        """(N,3) robot ışını -> normalize ham (u, v) ve hangi lens (daha az eğik olan)."""
        kam = self.kam
        u0, v0, t0 = kam.robot_to_lens(d, 0)
        u1, v1, t1 = kam.robot_to_lens(d, 1)
        l1 = t1 < t0
        u = np.where(l1, u1, u0)
        v = np.where(l1, v1, v0)
        ok = np.where(l1, t1, t0) * np.where(l1, kam.lens[1]["f"], kam.lens[0]["f"]) <= kam.daire_r
        return np.stack([u / 1280.0, v / 720.0], 1), l1.astype(np.int8), ok

    def _ham_cizgi_parcalari(self, d):
        uv, lens, ok = self._isin_hama(d)
        parcalar = []
        bas = 0
        for i in range(1, len(d) + 1):
            if i == len(d) or lens[i] != lens[bas] or ok[i] != ok[bas]:
                if ok[bas] and i - bas >= 2:
                    parcalar.append(uv[bas:i])
                bas = i
        return parcalar

    # ------------------------------------------------------------------ ana giriş
    def feed(self, kare_jpeg, t_ms, komut):
        T = {}
        t0 = time.perf_counter()
        im = cv2.imdecode(np.frombuffer(kare_jpeg, np.uint8), cv2.IMREAD_COLOR)
        if im is None:
            raise ValueError("kare çözülemedi")
        t1 = time.perf_counter()
        T["cozme"] = (t1 - t0) * 1000.0
        komut = (float(komut[0]), float(komut[1]))
        ilk = self._ilk_kare
        if ilk:
            self._ilk_kare = False
            if self.seg is not None:      # çevrimdışı koşudaki gibi ilk karenin taraması hemen hazır (BEV maskesi karelere erişsin)
                self._tara(im, kare_jpeg, (0.0, 0.0, 0.0), 0)
        m2d, oz, est = self.m2d, self.oz, self.est
        # ---- yön
        m2d.son_phi = np.nan
        m2d.son_ic = 0
        m2d.cizim_tum = np.zeros((0, 3))
        m2d.cizim_a = m2d.cizim_b = np.zeros((0, 3))
        m2d.cizim_ic = np.zeros(0, bool)
        r = m2d.guncelle(im, int(t_ms), komut)
        yon = float(r[2])
        ic = int(m2d.son_ic)
        t2 = time.perf_counter()
        T["yon"] = (t2 - t1) * 1000.0
        T["yon_donusum"] = float(m2d.son_donusum_ms)
        # ---- öteleme (kuşbakışı zemin dokusu, taramadan nedensel maske)
        with self._kilit:
            em = self._ek_maske
            tar = self._tarama
        ds, di, ok, icb = oz.guncelle(im, yon, em)
        t3 = time.perf_counter()
        T["oteleme"] = (t3 - t2) * 1000.0
        # ---- kestirici
        bilgi = est.adim(int(t_ms), yon, ic, (ds, di, ok, icb), komut)
        poz = est.poz
        t4 = time.perf_counter()
        T["kestirici"] = (t4 - t3) * 1000.0
        self.kare_sayaci += 1
        # ---- tarama iste (yalnız boşta ise; kendi hızında akar)
        if self.seg is not None and not ilk:
            with self._tarama_gonder:
                bos = self._tarama_is is None
            if bos:
                self._tarama_iste(im, kare_jpeg, poz, self.kare_sayaci)
        # ---- çizim katmanları
        kat = self._katmanlar(m2d, poz, tar, bilgi, im.shape)
        ops = self._harita_islemleri(poz, oz)
        t5 = time.perf_counter()
        T["katman"] = (t5 - t4) * 1000.0
        T["bolutleme"] = float(self._tarama_ms)
        T["harita"] = float(self._harita_ms)
        tarama_yasi = None if tar is None else max(0.0, (time.time() - tar["t"]) * 1000.0)
        profil_adlari = [("v(255) düz", "m/sn"), ("v(150) düz", "m/sn"), ("w(255) yerinde", "°/sn"),
                         ("w(110) yerinde", "°/sn"), ("v(150) yay", "m/sn"), ("w(110) hızlı yay", "°/sn")]
        pr = est.profil_ozeti()
        durum = {
            "kare": self.kare_sayaci, "t_ms": int(t_ms),
            "poz": {"x": float(poz[0]), "y": float(poz[1]), "yon": float(poz[2]),
                    "sigma": float(bilgi.get("sigma", 0.0)), "guven": float(bilgi.get("guven", 0.0))},
            "katmanlar": kat, "harita_islem": ops, "sure_ms": {k: round(v, 2) for k, v in T.items()},
            "bilgi": {
                "profil": [{"ad": n, "deger": round(float(v), 3), "birim": b} for (n, b), v in zip(profil_adlari, pr)],
                "ek": [
                    {"ad": "yön içerik", "deger": ic}, {"ad": "öteleme içerik", "deger": int(icb)},
                    {"ad": "öteleme ok", "deger": bool(ok)}, {"ad": "tarama yaşı ms", "deger": None if tarama_yasi is None else round(tarama_yasi)},
                    {"ad": "tarama sayısı", "deger": int(self._tarama_n)},
                    {"ad": "kayma k", "deger": round(float(bilgi.get("k", 1.0)), 3)},
                    {"ad": "yön durumu", "deger": str(m2d.durum)},
                    {"ad": "öteleme oranı B/K", "deger": None if not np.isfinite(bilgi.get("oran", np.nan)) else round(float(bilgi["oran"]), 2)},
                ],
            },
            "uyari": [],
        }
        if m2d.durum == "tut":
            durum["uyari"].append("yön: görüntü durgun, açı tutuluyor")
        if getattr(self, "son_hata", None):
            durum["uyari"].append("tarama hatası: " + self.son_hata)
            self.son_hata = None
        return durum

    # ------------------------------------------------------------------ katman üretimi
    def _katmanlar(self, m2d, poz, tar, bilgi, sekil):
        kat = {}
        pano = "pano" in self.gorunumler
        ham = "ham" in self.gorunumler
        tum = np.asarray(m2d.cizim_tum, np.float64).reshape(-1, 3)
        a, b, icm = np.asarray(m2d.cizim_a, np.float64).reshape(-1, 3), np.asarray(m2d.cizim_b, np.float64).reshape(-1, 3), np.asarray(m2d.cizim_ic, bool)
        if len(tum):
            self._son_cizim = (tum, a, b, icm)
        elif m2d.durum == "tut" and self._son_cizim is not None:
            # yön hattı durgun kareyi atladı (özellik çıkarmadı): son işlenen karenin noktaları gösterilir (titremesin); "yön durumu: tut" durum şeridinde
            tum, a, b, icm = self._son_cizim
        ai, bi = a[icm], b[icm]
        kaynaklar = {"orb": [tum], "orb_ic": [bi], "orb_vektor": [ai, bi]}
        for kid, (liste) in kaynaklar.items():
            ogeler = []
            if kid == "orb_vektor":
                if len(liste[1]):
                    if pano:
                        pa, pb = self._isin_panoya(liste[0]), self._isin_panoya(liste[1])
                        yakin = np.abs(pa[:, 0] - pb[:, 0]) < 0.5            # dikiş üstünden geçen parça çizilmez
                        seg = np.concatenate([pa[yakin], pb[yakin]], 1)
                        ogeler.append({"tur": "cizgiler", "uzay": "pano", "xy": _yuvarla(seg, 4)})
                    if ham:
                        ua, la, oa = self._isin_hama(liste[0])
                        ub, lb, ob = self._isin_hama(liste[1])
                        s = (la == lb) & oa & ob
                        ogeler.append({"tur": "cizgiler", "uzay": "ham", "xy": _yuvarla(np.concatenate([ua[s], ub[s]], 1), 4)})
            else:
                d = liste[0]
                if len(d):
                    if pano:
                        ogeler.append({"tur": "noktalar", "uzay": "pano", "xy": _yuvarla(self._isin_panoya(d), 4)})
                    if ham:
                        uv, _, oo = self._isin_hama(d)
                        ogeler.append({"tur": "noktalar", "uzay": "ham", "xy": _yuvarla(uv[oo], 4)})
            kat[kid] = ogeler
        # ---- zemin sınırı ve maskesi (tarama, yön farkı kadar döndürülmüş)
        sinir, zemin = [], []
        if tar is not None:
            s = int(np.rint(((poz[2] - tar["poz"][2] + 180.0) % 360.0) - 180.0))
            mes = np.roll(tar["mesafe"], -s)
            gv = np.roll(tar["guven"], -s)
            elb = -np.degrees(np.arctan2(KAMERA_H, np.where(np.isfinite(mes) & (mes > 0), mes, np.nan)))     # NaN: bilinmiyor
            k = np.arange(360.0)
            azr = np.radians(k)
            elr = np.radians(np.where(np.isfinite(elb), elb, 0.0))
            d = np.stack([np.cos(elr) * np.sin(azr), np.sin(elr), np.cos(elr) * np.cos(azr)], -1)
            gecerli = np.isfinite(elb)
            if pano:
                uv = self._isin_panoya(d)
                # azimut 180 sınırından (dikiş) bölünmüş çizgi: u sıçraması
                u = uv[:, 0]
                bas = 0
                for i in range(1, 361):
                    if i == 360 or abs(u[i] - u[i - 1]) > 0.5 or gecerli[i] != gecerli[bas]:
                        if gecerli[bas] and i - bas >= 2:
                            sinir.append({"tur": "kirik", "uzay": "pano", "xy": _yuvarla(uv[bas:i], 4), "kapali": False})
                        bas = i
            if ham:
                dd = d[gecerli]
                if len(dd):
                    # ardışık azimutlar komşu; lens değişiminde bölünür
                    uvh, lens, ok = self._isin_hama(d)
                    bas = 0
                    for i in range(1, 361):
                        if i == 360 or lens[i] != lens[bas] or ok[i] != ok[bas] or gecerli[i] != gecerli[bas]:
                            if gecerli[bas] and ok[bas] and i - bas >= 2:
                                sinir.append({"tur": "kirik", "uzay": "ham", "xy": _yuvarla(uvh[bas:i], 4), "kapali": False})
                            bas = i
            # zemin maskesi (RGBA), s ve tarama sayısına bağlı önbellek
            anahtar = (s, tar["kare"], tar["t"])
            maske = self._maske_onbellek.get(anahtar)
            if maske is None:
                self._maske_onbellek = {}
                eb = np.where(gecerli, elb, -91.0)          # bilinmeyen: hiçbir piksel "altında" değil
                maske = []
                rgba_renk = (255, 168, 47, 110)             # BGRA (sayfada #2fa8ff)
                if pano:
                    z = (self._pan_el < eb[self._pan_az]) & (self._pan_el < -1.0)
                    rgba = np.zeros(z.shape + (4,), np.uint8)
                    rgba[z] = rgba_renk
                    maske.append({"tur": "resim", "uzay": "pano", "png": _png_b64(rgba)})
                if ham:
                    z = (self._ham_el < eb[self._ham_az]) & self._ham_ok & (self._ham_el < -1.0)
                    rgba = np.zeros(z.shape + (4,), np.uint8)
                    rgba[z] = rgba_renk
                    maske.append({"tur": "resim", "uzay": "ham", "png": _png_b64(rgba)})
                self._maske_onbellek[anahtar] = maske
            zemin = maske
        kat["sinir"] = sinir
        kat["zemin"] = zemin
        # ---- harita: güncel sınır noktaları (taramanın kendi pozunda)
        if tar is not None:
            m = np.isfinite(tar["mesafe"]) & (tar["mesafe"] > 0.35) & (tar["mesafe"] <= tar["menzil"] - 0.12) & (tar["guven"] >= GUVEN_MIN)
            k = np.arange(360.0)[m]
            teta = np.radians(tar["poz"][2] + k)
            xy = np.stack([tar["poz"][0] + np.sin(teta) * tar["mesafe"][m], tar["poz"][1] + np.cos(teta) * tar["mesafe"][m]], 1)
            kat["sinir_nokta"] = [{"tur": "noktalar", "uzay": "harita", "xy": _yuvarla(xy, 3)}] if len(xy) else []
        else:
            kat["sinir_nokta"] = []
        return kat

    def _harita_islemleri(self, poz, oz):
        ops = []
        if self._temizle_gonder:
            self._temizle_gonder = False
            ops.append({"islem": "temizle"})
        p = np.array([poz[0], poz[1]])
        if self._son_yorunge is None or np.hypot(*(p - self._son_yorunge)) >= YORUNGE_ADIM_M:
            self._son_yorunge = p
            ops.append({"katman": "yorunge", "islem": "ekle", "xy": [round(float(poz[0]), 4), round(float(poz[1]), 4)]})
        if oz.anahtar_degisim != self._son_anahtar:
            self._son_anahtar = oz.anahtar_degisim
            ops.append({"katman": "anahtar", "islem": "ekle", "xy": [round(float(poz[0]), 4), round(float(poz[1]), 4)]})
        simdi = time.time()
        with self._kilit:
            degisti = self._izgara_degisti and simdi - self._izgara_gonder_t >= 0.25
            if degisti:
                self._izgara_degisti = False
                self._izgara_gonder_t = simdi
                D = self.izgara.durum()
        if degisti:
            iz = self.izgara
            bil = iz.gorulen > 0
            satir = np.where(bil.any(1))[0]
            sut = np.where(bil.any(0))[0]
            if len(satir) and len(sut):
                r0, r1, c0, c1 = max(satir[0] - 1, 0), min(satir[-1] + 2, iz.Hh), max(sut[0] - 1, 0), min(sut[-1] + 2, iz.W)
                crop = D[r0:r1, c0:c1]
                ops.append({"katman": "doluluk", "islem": "yerlestir", "x0": round(iz.x0 + c0 * iz.h, 4), "y0": round(iz.y0 + (iz.Hh - r1) * iz.h, 4),
                            "hucre": iz.h, "w": int(c1 - c0), "h": int(r1 - r0), "png": _png_b64(crop)})
        return ops
