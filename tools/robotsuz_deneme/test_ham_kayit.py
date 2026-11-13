"""Ham kayıt denemeleri (robot gerekmez):

    python tools/robotsuz_deneme/test_ham_kayit.py

1) HamKayit birim denemeleri: sahte JPEG karelerle dosya biçimi, kuyruk dolunca atma, disk koruması,
   süre sınırı, kamera durunca durma.
2) Uçtan uca: gerçek surus_koprusu.py bu bilgisayarda içe aktarılır; yalnızca pyserial (sahte Arduino) ve
   gst-launch (sahte JPEG akışı) taklit edilir. HTTP API ile başlat/komut/durdur/liste/indir denenir
   (tools/ham_kayit.py indirme koduyla).
"""
import http.client as hc
import importlib.util
import json
import os
import random
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import types
import unittest

BURASI = os.path.dirname(os.path.abspath(__file__))
KOK = os.path.normpath(os.path.join(BURASI, "..", ".."))
KOPRU = os.path.join(KOK, "robot", "jetson", "surus_koprusu")
sys.path.insert(0, KOPRU)
import ham_kayit  # noqa: E402


def sahte_jpeg(i, rng):
    return b"\xff\xd8" + i.to_bytes(4, "big") + bytes(rng.getrandbits(8) for _ in range(rng.randint(50, 400))) + b"\xff\xd9"


def csv_oku(yol):
    with open(yol) as f:
        satirlar = f.read().strip().split("\n")
    return satirlar[0], [[int(x) if x.lstrip("-").isdigit() else x for x in s.split(",")] for s in satirlar[1:]]


def bekle(kosul, sure=5.0):
    son = time.time() + sure
    while time.time() < son:
        if kosul():
            return True
        time.sleep(0.02)
    return False


class BirimDeneme(unittest.TestCase):
    def setUp(self):
        self.kok = tempfile.mkdtemp(prefix="hamkayit_")
        self.izle = []
        self.bos = [10000.0]

    def tearDown(self):
        shutil.rmtree(self.kok, ignore_errors=True)

    def yap(self, **kw):
        return ham_kayit.HamKayit(self.kok, izle=self.izle.append, bos_disk_mb=lambda: self.bos[0], **kw)

    def test_dosyalar_ve_kareler(self):
        h = self.yap()
        d = h.baslat("salon ../x", 60)
        self.assertTrue(d["aktif"])
        klasor = d["klasor"]
        self.assertTrue(klasor.endswith("_salonx"), klasor)        # ad temizlendi
        rng = random.Random(1)
        girdi = []
        for i in range(25):
            j = sahte_jpeg(i, rng)
            girdi.append(j)
            h.kare(j)
            if i == 5:
                h.komut("M 100 -100", "udp")
                h.komut("S", "dur")
                h.durum_satiri({"sol": 10, "sag": 20, "sol_ma": 300, "sag_ma": 400})
                h.komut("N", "udp")                                   # motor komutu değil: yok sayılır
            time.sleep(0.01)
        d = h.durdur()
        self.assertFalse(d["aktif"])
        self.assertEqual(self.izle, [1, -1])
        self.assertEqual(sorted(os.listdir(klasor)), sorted(ham_kayit.DOSYALAR))
        ust, satirlar = csv_oku(os.path.join(klasor, "kareler.csv"))
        self.assertEqual(ust, "kare,t_ms,ofset,bayt")
        self.assertEqual(len(satirlar), 25)
        with open(os.path.join(klasor, "kareler.mjpg"), "rb") as f:
            veri = f.read()
        onceki_t = -1
        for i, (kare, t, ofset, bayt) in enumerate(satirlar):
            self.assertEqual(kare, i)
            parca = veri[ofset:ofset + bayt]
            self.assertEqual(parca[:2], b"\xff\xd8")
            self.assertEqual(parca[-2:], b"\xff\xd9")
            self.assertEqual(parca, girdi[i])
            self.assertGreaterEqual(t, onceki_t)
            onceki_t = t
        self.assertEqual(satirlar[-1][2] + satirlar[-1][3], len(veri))
        ust, kom = csv_oku(os.path.join(klasor, "komutlar.csv"))
        self.assertEqual(ust, "t_ms,sol,sag,kaynak")
        self.assertEqual([k[1:] for k in kom], [[100, -100, "udp"], [0, 0, "dur"]])
        ust, dur = csv_oku(os.path.join(klasor, "durum.csv"))
        self.assertEqual(ust, "t_ms,sol,sag,sol_ma,sag_ma")
        self.assertEqual([x[1:] for x in dur], [[10, 20, 300, 400]])
        with open(os.path.join(klasor, "bilgi.json")) as f:
            b = json.load(f)
        self.assertEqual((b["bicim"], b["kaynak"], b["atilan_kare"], b["bitis_nedeni"]), (1, "robot", 0, "elle"))
        self.assertEqual(b["kamera"], {"tur": "cift_balikgozu", "genislik": 1280, "yukseklik": 720, "fps": 15})
        self.assertIn("kamera_yuksekligi_m", b)
        self.assertIn("baslangic_duvar_saati", b)

    def test_kuyruk_dolunca_atar(self):
        yazan = []
        gercek_open = open

        class Yavas(object):
            def __init__(self, f):
                self.f = f

            def write(self, x):
                time.sleep(0.05)
                return self.f.write(x)

            def __getattr__(self, ad):
                return getattr(self.f, ad)

        def yavas_open(yol, kip="r", *a, **k):
            f = gercek_open(yol, kip, *a, **k)
            return Yavas(f) if yol.endswith("kareler.mjpg") else f

        ham_kayit.open = yavas_open
        try:
            h = self.yap(kuyruk_siniri=3)
            d = h.baslat("yavas", 30)
            rng = random.Random(2)
            for i in range(40):
                h.kare(sahte_jpeg(i, rng))
            d = h.durdur()
        finally:
            del ham_kayit.open
        self.assertGreater(d["atilan_kare"], 0)
        self.assertEqual(d["kare"] + d["atilan_kare"], 40)
        _, satirlar = csv_oku(os.path.join(self.durum_klasor(), "kareler.csv"))
        self.assertEqual([s[0] for s in satirlar], list(range(len(satirlar))))   # yazılanların numarası ardışık
        with open(os.path.join(self.durum_klasor(), "bilgi.json")) as f:
            self.assertEqual(json.load(f)["atilan_kare"], d["atilan_kare"])

    def durum_klasor(self):
        return os.path.join(self.kok, sorted(os.listdir(self.kok))[-1])

    def test_disk_koruma(self):
        h = self.yap()
        self.bos[0] = 100
        with self.assertRaises(ham_kayit.KayitHatasi):
            h.baslat("a", 10)
        self.assertEqual(os.listdir(self.kok), [])
        self.bos[0] = 10000
        h.baslat("b", 60)
        h.kare(sahte_jpeg(0, random.Random(3)))
        self.bos[0] = 100                                     # kayıt sürerken disk doluyor
        self.assertTrue(bekle(lambda: not h.aktif, 3))
        bekle(lambda: not h.calisiyor(), 3)
        self.assertEqual(h.durum()["son"]["bitis_nedeni"], "disk_dolu")
        self.assertEqual(self.izle, [1, -1])

    def test_sure_siniri_ve_ust_sinir(self):
        h = self.yap()
        d = h.baslat("c", 5000)
        self.assertEqual(d["en_cok_sn"], ham_kayit.EN_COK_SURE)
        self.assertEqual(d["beklenen_boyut_mb"], [900 * 2, 900 * 6])
        h.durdur()
        d = h.baslat("d", 0.7)
        rng = random.Random(4)
        son = time.time() + 3
        while h.aktif and time.time() < son:
            h.kare(sahte_jpeg(0, rng))
            time.sleep(0.05)
        bekle(lambda: not h.calisiyor(), 3)
        self.assertEqual(h.durum()["son"]["bitis_nedeni"], "sure_doldu")
        with self.assertRaises(ham_kayit.KayitHatasi):
            h.baslat("e", 0)

    def test_kamera_durursa_durur(self):
        h = self.yap(ilk_kare_bekle=0.6, kamera_suskunluk=0.6)
        h.baslat("f", 60)                                     # hiç kare gelmiyor
        self.assertTrue(bekle(lambda: not h.calisiyor(), 4))
        self.assertEqual(h.durum()["son"]["bitis_nedeni"], "kamera_yok")
        h.baslat("g", 60)
        h.kare(sahte_jpeg(0, random.Random(5)))                # bir kare geldi, sonra kesildi
        self.assertTrue(bekle(lambda: not h.calisiyor(), 4))
        self.assertEqual(h.durum()["son"]["bitis_nedeni"], "kamera_durdu")
        self.assertEqual(self.izle, [1, -1, 1, -1])

    def test_ikinci_baslatma_reddedilir(self):
        h = self.yap()
        h.baslat("h", 60)
        with self.assertRaises(ham_kayit.KayitHatasi):
            h.baslat("i", 60)
        h.durdur()

    def test_kayit_yokken_kancalar_etkisiz(self):
        h = self.yap()
        h.kare(b"x")
        h.komut("M 1 1", "udp")
        h.durum_satiri({})
        self.assertFalse(h.aktif)
        self.assertEqual(os.listdir(self.kok), [])


# ---------------------------------------------------------------- uçtan uca

class SahteSeri(object):
    """pyserial yerine: HAZIR der, sonra ~10 Hz "D" durum satırı verir; yazılanları toplar."""
    yazilan = []

    def __init__(self, *a, **k):
        self.ilk = True

    def __enter__(self):
        return self

    def __exit__(self, *a):
        return False

    def readline(self):
        if self.ilk:
            self.ilk = False
            return b"HAZIR\n"
        time.sleep(0.1)
        return b"D 10 20 300 400 0 0 0 0 0 0 0\n"

    def write(self, b):
        SahteSeri.yazilan.append(b)


class SahtePopen(object):
    """gst-launch yerine: stdout'a 15 fps multipart JPEG yazar. Gönderilen kareler SahtePopen.gonderilen'de."""
    gonderilen = []
    acik = 0

    def __init__(self, komut, stdout=None, stderr=None, bufsize=0, pass_fds=()):
        r, w = os.pipe()
        self.stdout = os.fdopen(r, "rb", 0)
        self._w = w
        self._dur = False
        SahtePopen.acik += 1
        self.rng = random.Random(7)
        threading.Thread(target=self._besle, daemon=True).start()

    def _besle(self):
        i = len(SahtePopen.gonderilen)
        try:
            while not self._dur:
                j = sahte_jpeg(i, self.rng)
                SahtePopen.gonderilen.append(j)
                i += 1
                os.write(self._w, b"--kare\r\nContent-Type: image/jpeg\r\nContent-Length: %d\r\n\r\n" % len(j) + j + b"\r\n")
                time.sleep(1 / 15.0)
        except OSError:
            pass

    def kill(self):
        self._dur = True
        time.sleep(0.1)
        try:
            os.close(self._w)
        except OSError:
            pass
        SahtePopen.acik -= 1

    def wait(self):
        pass


def istek(port, yontem, yol):
    c = hc.HTTPConnection("127.0.0.1", port, timeout=20)
    c.request(yontem, yol)
    r = c.getresponse()
    govde = r.read()
    c.close()
    return r.status, govde


class UcAUcaDeneme(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.mkdtemp(prefix="kopru_")
        seri = types.ModuleType("serial")
        seri.Serial = SahteSeri
        seri.SerialException = type("SerialException", (Exception,), {})
        sys.modules["serial"] = seri
        spec = importlib.util.spec_from_file_location("surus_koprusu_test", os.path.join(KOPRU, "surus_koprusu.py"))
        k = cls.k = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(k)
        k.LOG_DOSYASI = os.path.join(cls.tmp, "kopru.log")
        k.KAYIT_KOKU = os.path.join(cls.tmp, "kayitlar")
        k.HAM_KAYIT.kok = k.KAYIT_KOKU
        k.kamera_bul = lambda: "/dev/fake"
        k.KAM.h264_destek = False
        k.subprocess = types.SimpleNamespace(Popen=SahtePopen, PIPE=subprocess.PIPE, DEVNULL=subprocess.DEVNULL,
                                             check_output=subprocess.check_output)
        cls.sunucu = k.Sunucu(("127.0.0.1", 0), k.Istek)
        cls.port = cls.sunucu.server_address[1]
        threading.Thread(target=cls.sunucu.serve_forever, daemon=True).start()
        assert bekle(lambda: k.ARDUINO.port is not None, 5), "sahte Arduino bağlanmadı"

    @classmethod
    def tearDownClass(cls):
        cls.sunucu.shutdown()
        shutil.rmtree(cls.tmp, ignore_errors=True)

    def api(self, yol, yontem="GET"):
        kod, govde = istek(self.port, yontem, yol)
        return kod, json.loads(govde.decode("utf-8"))

    def test_uctan_uca(self):
        k = self.k
        kod, d = self.api("/api/kayit")
        self.assertEqual((kod, d["aktif"]), (200, False))
        self.assertEqual(k.KAM.izleyen, 0)                  # izleyen yok: kamera kapalı
        n0 = len(SahtePopen.gonderilen)
        kod, d = self.api("/api/kayit?ac=1&ad=salon&sure=30", "POST")
        self.assertEqual((kod, d["aktif"]), (200, True), d)
        self.assertEqual(k.KAM.izleyen, 1)                  # kayıt kamerayı açık tutuyor
        kod, d2 = self.api("/api/kayit?ac=1")
        self.assertEqual(kod, 409)
        # kayıt sürerken indirme reddedilir
        self.assertEqual(istek(self.port, "GET", "/kayitlar/" + os.path.basename(d["klasor"]) + "/bilgi.json")[0], 409)
        self.assertTrue(bekle(lambda: self.api("/api/kayit")[1]["kare"] >= 10, 8))
        # sürüş komutları: UDP/WebSocket yolunun kullandığı Surucu üzerinden
        baglanti = object()
        k.SURUCU.surus(baglanti, 120, 120, "udp")
        time.sleep(0.15)
        k.SURUCU.surus(baglanti, 80, -80, "ws")
        time.sleep(0.15)
        k.SURUCU.surus(baglanti, 0, 0, "ws_eski")
        self.api("/api/dur", "POST")
        k.SURUCU.ayrildi(baglanti)                          # sürücü sahibi gidince "M 0 0"
        time.sleep(1.2)
        kod, d = self.api("/api/kayit?ac=0")
        self.assertEqual((kod, d["aktif"]), (200, False))
        self.assertEqual(k.KAM.izleyen, 0)
        klasor = os.path.join(k.KAYIT_KOKU, os.path.basename(d["son"]["klasor"]))
        _, satirlar = csv_oku(os.path.join(klasor, "kareler.csv"))
        with open(os.path.join(klasor, "kareler.mjpg"), "rb") as f:
            veri = f.read()
        gonderilen = SahtePopen.gonderilen[n0:]
        yazilan = [veri[o:o + b] for (_, _, o, b) in satirlar]
        self.assertGreaterEqual(len(yazilan), 15)
        i0 = gonderilen.index(yazilan[0])
        self.assertEqual(gonderilen[i0:i0 + len(yazilan)], yazilan)       # her kare, sırayla, tekrarsız, atlamasız
        _, kom = csv_oku(os.path.join(klasor, "komutlar.csv"))
        self.assertEqual([x[1:] for x in kom], [[120, 120, "udp"], [80, -80, "ws"], [0, 0, "ws_eski"], [0, 0, "dur"], [0, 0, "ayrildi"]])
        ts = [x[0] for x in kom]
        self.assertEqual(ts, sorted(ts))
        _, dur = csv_oku(os.path.join(klasor, "durum.csv"))
        self.assertGreaterEqual(len(dur), 5)
        self.assertEqual(dur[0][1:], [10, 20, 300, 400])
        with open(os.path.join(klasor, "bilgi.json")) as f:
            b = json.load(f)
        self.assertEqual((b["ad"], b["bitis_nedeni"]), ("salon", "elle"))
        self.assertIn("kopru_surumu", b)
        # Arduino'ya giden komutlar kayıttakilerle aynı
        m = [x.decode().strip() for x in SahteSeri.yazilan if x.startswith(b"M ") or x == b"S\n"]
        self.assertEqual(m[-5:], ["M 120 120", "M 80 -80", "M 0 0", "S", "M 0 0"])

        # liste + indirme (PC aracıyla), yol aşımı
        kod, liste = self.api("/api/kayitlar")
        self.assertIn(os.path.basename(klasor), [x["ad"] for x in liste["kayitlar"]])
        spec = importlib.util.spec_from_file_location("pc_ham_kayit", os.path.join(KOK, "tools", "ham_kayit.py"))
        pc = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(pc)
        pc.KOK = os.path.join(self.tmp, "pc")
        sys.argv = ["ham_kayit.py", "127.0.0.1:%d" % self.port, "indir", os.path.basename(klasor)]
        pc.main()
        indirilen = os.path.join(pc.KOK, "kayitlar", os.path.basename(klasor))
        for dosya in ham_kayit.DOSYALAR:
            with open(os.path.join(klasor, dosya), "rb") as a, open(os.path.join(indirilen, dosya), "rb") as c:
                self.assertEqual(a.read(), c.read(), dosya)
        # yarım kalan indirme sürer
        yol = os.path.join(indirilen, "kareler.mjpg")
        with open(yol, "rb+") as f:
            f.truncate(1000)
        pc.main()
        with open(yol, "rb") as a, open(os.path.join(klasor, "kareler.mjpg"), "rb") as c:
            self.assertEqual(a.read(), c.read())
        ad = os.path.basename(klasor)
        for kotu in ("/kayitlar/../surus_koprusu.py", "/kayitlar/%2e%2e/%2e%2e/etc/passwd", "/kayitlar/" + ad + "/../../x",
                     "/kayitlar/" + ad + "/baska.txt", "/kayitlar/.." + "/bilgi.json", "/kayitlar/" + ad):
            self.assertEqual(istek(self.port, "GET", kotu)[0], 404, kotu)

    def test_kamera_ve_sinirlar_sonra(self):
        """Kayıt bitince kamera 10 sn boşta bekleyip kapanır (izleyen 0); durum uçları çalışır."""
        k = self.k
        self.assertEqual(k.KAM.izleyen, 0)
        kod, d = self.api("/api/kayit?ac=1&ad=kisa&sure=1")
        self.assertEqual(kod, 200, d)
        self.assertTrue(bekle(lambda: not self.api("/api/kayit")[1]["aktif"], 10))
        bekle(lambda: not self.api("/api/kayit")[1]["kapaniyor"], 5)
        self.assertEqual(self.api("/api/kayit")[1]["son"]["bitis_nedeni"], "sure_doldu")
        self.assertEqual(k.KAM.izleyen, 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
