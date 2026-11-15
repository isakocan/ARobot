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


class PostaVeDunyaTesti(unittest.TestCase):
    def test_posta_eskiyi_ezer_ve_kayip_sayar(self):
        from kaynaklar import Posta
        p = Posta()
        for i in range(5):
            p.koy({"i": i})
        self.assertEqual(p.al(0.01)["i"], 4)            # yalnız en yeni
        self.assertEqual(p.kayip, 4)
        self.assertIsNone(p.al(0.01))

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
        self.assertEqual(fark[0]["xy"], [1, 1, 2, 2])
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
                    self.assertIn(o["uzay"], ("pano", "ham", "harita"))
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
