"""Geliştirici ekranı testleri (unittest). Çalıştır:  python tools/gelistirici_ekrani/test_gelistirici_ekrani.py
Kayıt: GELISTIRICI_KAYIT ortam değişkeni, yoksa kayitlar/gercek/olcum_ham, yoksa ham ölçüm klasörü; hiçbiri yoksa kayıt gerektiren testler atlanır.
Arka uç testi bölütlemeyi aynı süreçte çalıştırır (GPU yoksa CPU: yavaş ama geçer)."""
import base64
import json
import os
import sys
import threading
import time
import unittest

import cv2
import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
KOK = os.path.abspath(os.path.join(KLASOR, "..", ".."))
ADAYLAR = [os.environ.get("GELISTIRICI_KAYIT"), os.path.join(KOK, "kayitlar", "gercek", "olcum_ham"),
           r"C:\Users\PC\Desktop\ARobot\kayitlar\2026-11-14_20-12-25_olcum_ham"]
KAYIT = next((a for a in ADAYLAR if a and os.path.exists(os.path.join(a, "bilgi.json"))), None)


class CanliKaynakTesti(unittest.TestCase):
    """Sahte robot köprüsü (yalnız GET kabul eder): okuyucu çok parçalı JPEG'i ayrıştırır, yalnız GET yollar, süre sınırında durur."""

    def test_mjpg_okuma_yalniz_get_ve_sure_siniri(self):
        import http.server
        from kaynaklar import CanliKaynak, Posta
        yontemler = []
        jpeg = cv2.imencode(".jpg", np.full((72, 128, 3), 120, np.uint8))[1].tobytes()

        class H(http.server.BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, *a):
                pass

            def do_GET(self):
                yontemler.append(("GET", self.path))
                if self.path.startswith("/api/durum"):
                    b = json.dumps({"arduino": True, "motor": {"sol": 10, "sag": -20, "sol_ma": 1, "sag_ma": 2}, "kamera": "aktif", "fps": 15.0}).encode()
                    self.send_response(200); self.send_header("Content-Length", str(len(b))); self.end_headers(); self.wfile.write(b)
                    return
                self.send_response(200)
                self.send_header("Content-Type", "multipart/x-mixed-replace; boundary=kare")
                self.end_headers()
                try:
                    for _ in range(400):
                        self.wfile.write(b"--kare\r\nContent-Type: image/jpeg\r\nContent-Length: " + str(len(jpeg)).encode() + b"\r\n\r\n" + jpeg + b"\r\n")
                        time.sleep(0.02)
                except OSError:
                    pass

            def do_POST(self):
                yontemler.append(("POST", self.path))
                self.send_error(405)

        sunucu = http.server.ThreadingHTTPServer(("127.0.0.1", 0), H)
        threading.Thread(target=sunucu.serve_forever, daemon=True).start()
        p = Posta()
        k = CanliKaynak(p, "127.0.0.1:%d" % sunucu.server_address[1], sinir_sn=1.5, yol="mjpg")
        k.baslat()
        t0 = time.time()
        while time.time() - t0 < 4 and k.durum != "bitti":
            time.sleep(0.1)
        b = k.bilgi()
        k.durdur()
        sunucu.shutdown()
        self.assertEqual(k.durum, "bitti")
        self.assertTrue(b["sinir_asildi"])
        self.assertGreater(p.gelen, 20)
        self.assertGreater(b["alinan_fps"], 10)
        self.assertTrue(all(m == "GET" for m, _ in yontemler), yontemler)
        self.assertEqual(b["robot"]["fps"], 15.0)


class PostaVeDunyaTesti(unittest.TestCase):
    def test_posta_eskiyi_ezer_ve_kayip_sayar(self):
        from kaynaklar import Posta
        p = Posta()
        for i in range(5):
            p.koy({"i": i})
        self.assertEqual(p.al(0.01)["i"], 4)            # yalnız en yeni
        self.assertEqual(p.kayip, 4)
        self.assertIsNone(p.al(0.01))

    def test_dunya_3b_parcalari_ve_yerlestirme(self):
        from sunucu import Dunya
        d = Dunya()
        im = d.imlec_yeni()
        d.uygula([{"katman": "duvar3d", "islem": "ekle", "xyz": [0, 0, 0, 1, 1, 0], "deger": [0.5, 1.0]},
                  {"katman": "anahtar3d", "islem": "ekle", "kameralar": [{"x": 0, "y": 0, "z": 0.17, "yon": 90}]},
                  {"katman": "doluluk", "islem": "yerlestir", "x0": 0, "y0": 0, "hucre": 0.05, "w": 1, "h": 1, "png": ""}])
        a = d.farki_al(im)
        self.assertEqual([o.get("katman") for o in a], [None, "duvar3d", "anahtar3d", "doluluk"])
        self.assertEqual(d.farki_al(im), [])                                        # yeni bir şey yok
        d.uygula([{"katman": "duvar3d", "islem": "ekle", "xyz": [2, 2, 0], "deger": [2.0]}])
        self.assertEqual(d.farki_al(im)[0]["deger"], [2.0])

    def test_dunya_imlec_atlanan_kareleri_kaybetmez(self):
        from sunucu import Dunya
        d = Dunya()
        im = d.imlec_yeni()
        d.uygula([{"katman": "yorunge", "islem": "ekle", "xy": [0, 0]}])
        ilk = d.farki_al(im)
        self.assertEqual([o["islem"] for o in ilk], ["temizle", "ekle"])
        d.uygula([{"katman": "yorunge", "islem": "ekle", "xy": [1, 1]}])           # istemci bu kareyi atladı...
        d.uygula([{"katman": "yorunge", "islem": "ekle", "xy": [2, 2]}])
        fark = d.farki_al(im)                                                      # ...yine de ikisi de gelir
        self.assertEqual([o["xy"] for o in fark], [[1, 1], [2, 2]])             # parça listesi: hiçbiri kaybolmadı
        d.uygula([{"islem": "temizle"}])
        self.assertEqual(d.farki_al(im)[0]["islem"], "temizle")


@unittest.skipIf(KAYIT is None, "kayıt klasörü yok")
class TekrarKaynagiTesti(unittest.TestCase):
    def test_gercek_zamanli_yayin_ve_dusme(self):
        from kaynaklar import Posta, TekrarKaynagi
        p = Posta()
        k = TekrarKaynagi(p, KAYIT, hiz=4.0)
        k.basa_sar()
        k.baslat()
        t0 = time.time()
        alinan = []
        while time.time() - t0 < 2.0:
            x = p.al(0.5)
            if x:
                alinan.append(x)
                time.sleep(0.15)           # yavaş tüketici: kareler ezilmeli
        k.durdur()
        self.assertGreater(len(alinan), 3)
        self.assertGreater(p.kayip, 0)
        t = [x["t_ms"] for x in alinan]
        self.assertEqual(t, sorted(t))
        self.assertTrue(all(isinstance(x["jpeg"], bytes) and x["jpeg"][:2] == b"\xff\xd8" for x in alinan))


@unittest.skipIf(KAYIT is None, "kayıt klasörü yok")
class ArkaUcSegsizTesti(unittest.TestCase):
    """Varsayılan arka uç: bölütleme KAPALI. Harita ızgarası ve zemin katmanları hiç bildirilmez, çıkmaz; yöntem adı belli."""

    def test_varsayilan_bolutlemesiz(self):
        from arkauc_konum_deney import KonumDeneyi
        from kaynaklar import Posta, TekrarKaynagi
        a = KonumDeneyi()
        try:
            self.assertFalse(a.bolutleme_acik)
            self.assertIn("kendi yöntemimiz", a.yontem_adi)
            kimlik = {x["id"] for x in a.katman_tanimlari()}
            for yok in ("doluluk", "zemin", "zemin_mesafe", "sinir", "sinir_mesafe", "sinir_nokta", "duvar3d", "doluluk3d"):
                self.assertNotIn(yok, kimlik)
            for var in ("orb_ic", "govde", "olcum_disi", "yorunge", "yorunge3d", "kamera3d", "anahtar3d"):
                self.assertIn(var, kimlik)
            k = TekrarKaynagi(Posta(), KAYIT)
            durumlar = [a.feed(p["jpeg"], p["t_ms"], p["komut"]) for p in k.kare_uret(bas=330, adet=60)]
            ops = [o for d in durumlar for o in d["harita_islem"]]
            self.assertFalse(any(o.get("katman") in ("doluluk", "duvar3d") for o in ops))
            self.assertTrue(any(o.get("katman") == "yorunge3d" for o in ops))
            for d in durumlar:
                self.assertTrue(set(d["katmanlar"]) <= kimlik)
                self.assertEqual(d["sure_ms"]["bolutleme"], 0.0)
            p0, p1 = durumlar[0]["poz"], durumlar[-1]["poz"]
            self.assertGreater(np.hypot(p1["x"] - p0["x"], p1["y"] - p0["y"]), 0.2)
        finally:
            a.kapat()


@unittest.skipIf(KAYIT is None, "kayıt klasörü yok")
class ArkaUcTesti(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from arkauc_konum_deney import KonumDeneyi
        from kaynaklar import Posta, TekrarKaynagi
        cls.arka = KonumDeneyi(bolutleme=True, bolutleme_surec=False)
        cls.kaynak = TekrarKaynagi(Posta(), KAYIT)
        cls.durumlar = []
        n = 100
        # robot ~22.8. sn'de düz gitmeye başlar (kare ~340): 300'den 100 kare = 20 sn - 27 sn
        for p in cls.kaynak.kare_uret(bas=300, adet=n):
            cls.durumlar.append(cls.arka.feed(p["jpeg"], p["t_ms"], p["komut"]))
        time.sleep(1.0)

    @classmethod
    def tearDownClass(cls):
        cls.arka.kapat()

    def test_durum_sozlesmesi(self):
        self.assertEqual(len(self.durumlar), 100)
        for d in self.durumlar:
            for k in ("kare", "t_ms", "poz", "katmanlar", "harita_islem", "sure_ms", "bilgi", "uyari"):
                self.assertIn(k, d)
            self.assertTrue(all(np.isfinite(d["poz"][k]) for k in ("x", "y", "yon")))
            for ad in ("cozme", "yon", "oteleme", "kestirici", "bolutleme", "harita"):
                self.assertIn(ad, d["sure_ms"])
            json.dumps(d)                                                      # JSON'a çevrilebilir
            for kid, ogeler in d["katmanlar"].items():
                for o in ogeler:
                    self.assertIn(o["uzay"], ("pano", "ham", "harita", "3d"))
                    if o["tur"] in ("noktalar", "cizgiler", "kirik"):
                        self.assertEqual(len(o["xy"]) % 2, 0)
                        if o["uzay"] != "harita":
                            self.assertTrue(-0.05 <= min(o["xy"]) and max(o["xy"]) <= 1.05, kid)
                    elif o["tur"] == "resim":
                        im = cv2.imdecode(np.frombuffer(base64.b64decode(o["png"]), np.uint8), cv2.IMREAD_UNCHANGED)
                        self.assertIsNotNone(im)
                        self.assertEqual(im.shape[2], 4)
        tanimlar = {t["id"] for t in self.arka.katman_tanimlari()}
        for d in self.durumlar:
            self.assertTrue(set(d["katmanlar"]) <= tanimlar)

    def test_poz_robot_gidince_degisir(self):
        p0 = self.durumlar[0]["poz"]
        p1 = self.durumlar[-1]["poz"]
        yol = float(np.hypot(p1["x"] - p0["x"], p1["y"] - p0["y"]))
        self.assertGreater(yol, 0.5, "robot ~27. sn'ye kadar ~1 m ilerledi; yol=%.2f" % yol)
        self.assertLess(yol, 3.0)
        # durgun başlangıç: ilk 30 karede (komut 0) poz neredeyse sabit
        pb = self.durumlar[30]["poz"]
        self.assertLess(float(np.hypot(pb["x"] - p0["x"], pb["y"] - p0["y"])), 0.05)
        # robot kuzeye (yon ~0) gider: y artar, x ~ sabit
        self.assertGreater(p1["y"] - p0["y"], 0.4)

    def test_maske_katmanlari_ayri_ve_durustur(self):
        st = self.arka.statik_katmanlar()
        self.assertIn("govde", st)
        self.assertIn("olcum_disi", st)
        tan = {t["id"]: t for t in self.arka.katman_tanimlari()}
        self.assertEqual(tan["govde"]["ad"], "Robot gövdesi")
        self.assertEqual(tan["olcum_disi"]["ad"], "Ölçüm dışı bölgeler")
        self.assertNotEqual(tan["govde"]["renk"], tan["olcum_disi"]["renk"])
        a = self.arka._maskeler
        govde, fark = a["govde"] > 127, a["olcum_disi"] > 127
        self.assertFalse((govde & fark).any())                                        # kesişmezler
        self.assertGreater(fark.mean(), 0.02)
        self.assertLess(govde.mean(), 0.20)                                           # gerçek gövde ~%16,6; sabit maske (%20,3) değil
        for uzay in ("ham", "pano"):
            r = [o for k in ("govde", "olcum_disi") for o in st[k] if o["uzay"] == uzay]
            self.assertEqual(len(r), 2)
            self.assertNotEqual(r[0]["png"], r[1]["png"])

    def test_yeni_katman_turleri_ve_3b(self):
        L = [d["katmanlar"] for d in self.durumlar]
        zm = [k["zemin_mesafe"] for k in L if k.get("zemin_mesafe")]
        self.assertGreater(len(zm), 10)
        for o in zm[-1]:
            self.assertEqual(o["tur"], "skalar_resim")
            g = cv2.imdecode(np.frombuffer(base64.b64decode(o["png"]), np.uint8), cv2.IMREAD_UNCHANGED)
            self.assertEqual(g.ndim, 2)
            self.assertGreater(int((g > 0).sum()), 500)                                # zemin pikselleri dolu
            self.assertLessEqual(int(g.max()), 254)
            self.assertEqual(o["aralik"], [0.3, 3.0])
        sm = [k["sinir_mesafe"] for k in L if k.get("sinir_mesafe")]
        self.assertGreater(len(sm), 5)
        for o in sm[-1]:
            self.assertEqual(len(o["deger"]) * 2, len(o["xy"]))
            self.assertTrue(all(0.3 <= v < 30 for v in o["deger"]))
        ops = [o for d in self.durumlar for o in d["harita_islem"]]
        duvar = [o for o in ops if o.get("katman") == "duvar3d"]
        self.assertTrue(duvar)
        for o in duvar:
            self.assertEqual(len(o["xyz"]) % 3, 0)
            self.assertEqual(len(o["deger"]) * 3, len(o["xyz"]))
            self.assertTrue(all(o["xyz"][i] == 0.0 for i in range(2, len(o["xyz"]), 3)))   # zemin sınırı z=0: uydurma yükseklik yok
        n = sum(len(o["xyz"]) // 3 for o in duvar)
        xs = [(round(o["xyz"][i], 2), round(o["xyz"][i + 1], 2)) for o in duvar for i in range(0, len(o["xyz"]), 3)]
        self.assertGreater(n, 50)
        self.assertGreater(len(set(xs)), 0.8 * n)                                       # 5 cm hücre tekrarı yok
        self.assertTrue(any(o.get("katman") == "yorunge3d" for o in ops))
        self.assertTrue(all(isinstance(d["katmanlar"]["kamera3d"][0]["z"], float) and d["katmanlar"]["kamera3d"][0]["z"] == 0.17 for d in self.durumlar))
        tan = {t["id"]: t for t in self.arka.katman_tanimlari()}
        self.assertEqual(tan["duvar3d"]["tur3d"], "noktalar")
        self.assertEqual(tan["zemin_mesafe"]["lejant"]["birim"], "m")

    def test_guven_ve_izleme_alanlari(self):
        ops = [o for d in self.durumlar for o in d["harita_islem"]]
        izg = [o for o in ops if o.get("katman") == "doluluk"]
        self.assertTrue(izg)
        g = cv2.imdecode(np.frombuffer(base64.b64decode(izg[-1]["guven_png"]), np.uint8), cv2.IMREAD_UNCHANGED)
        self.assertEqual(g.shape, (izg[-1]["h"], izg[-1]["w"]))
        self.assertGreater(int(g.max()), 51)                                           # birden çok kez görülen hücre var
        for o in [o for o in ops if o.get("katman") == "duvar3d"]:
            self.assertEqual(len(o["guven"]), len(o["deger"]))
            self.assertTrue(all(0.0 <= v <= 1.0 for v in o["guven"]))
        sn = [k["sinir_nokta"] for k in (d["katmanlar"] for d in self.durumlar) if k.get("sinir_nokta")]
        self.assertTrue(sn and all(len(it["guven"]) * 2 == len(it["xy"]) for it in sn[-1]))
        self.assertTrue(all(d["izleme"]["durum"] in ("iyi", "zayif", "kayip", "tut") for d in self.durumlar))
        self.assertTrue(any(d["izleme"]["durum"] == "iyi" for d in self.durumlar))
        self.assertIn("asama_adlari", self.durumlar[-1]["bilgi"])

    def test_harita_islemleri_ve_orb(self):
        ops = [o for d in self.durumlar for o in d["harita_islem"]]
        self.assertTrue(any(o.get("katman") == "yorunge" for o in ops))
        self.assertTrue(any(o.get("katman") == "doluluk" for o in ops), "bölütleme + ızgara en az bir kez gelmeli")
        orb = [d for d in self.durumlar if d["katmanlar"].get("orb")]
        self.assertGreater(len(orb), 5, "hareket sırasında ORB noktaları çizilmeli")
        ic = [d for d in self.durumlar if d["katmanlar"].get("orb_ic")]
        self.assertGreater(len(ic), 3)


if __name__ == "__main__":
    unittest.main(verbosity=2)
