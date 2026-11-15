"""stella_vslam arka ucu ve çoklu yöntem altyapısı testleri (unittest). Çalıştır:  python tools/gelistirici_ekrani/test_stella.py
Stella testleri WSL Ubuntu-22.04 ve derlenmiş köprü ister (tools/slam_deneme/kurulum.sh); yoksa atlanır. Kayıt: test_gelistirici_ekrani.KAYIT ile aynı kural."""
import base64
import json
import os
import shutil
import sys
import threading
import time
import unittest

import cv2
import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
from test_gelistirici_ekrani import KAYIT  # noqa: E402
from sunucu import Boru, Dunya  # noqa: E402
from arkauc import ArkaUc  # noqa: E402


def wsl_var():
    if shutil.which("wsl") is None:
        return False
    import subprocess
    try:
        r = subprocess.run(["wsl", "-d", "Ubuntu-22.04", "-u", "root", "--", "bash", "-c", "test -x /root/slam/kopru/build/kopru && echo var"], capture_output=True, text=True,
                           timeout=60, env=dict(os.environ, MSYS_NO_PATHCONV="1"))
        return "var" in r.stdout
    except Exception:
        return False


class DunyaKumeTesti(unittest.TestCase):
    def test_guncelle_sil_temizle_katman(self):
        d = Dunya()
        im = d.imlec_yeni()
        d.uygula([{"katman": "landmark", "islem": "guncelle", "kimlik": [1, 2, 3], "xyz": [0, 0, 0, 1, 1, 1, 2, 2, 2], "guven": [0.2, 0.4, 1.0]}])
        a = d.farki_al(im)
        self.assertEqual(a[0]["islem"], "temizle")
        self.assertEqual(a[1]["kimlik"], [1, 2, 3])
        self.assertEqual(d.farki_al(im), [])
        # id 2 yerinde hareket etti, id 3 silindi, id 4 eklendi
        d.uygula([{"katman": "landmark", "islem": "guncelle", "kimlik": [2, 4], "xyz": [5, 5, 5, 6, 6, 6], "guven": [0.6, 0.2], "sil": [3]}])
        f = d.farki_al(im)
        self.assertEqual(f[0]["kimlik"], [2, 4])
        self.assertEqual(f[0]["xyz"], [5, 5, 5, 6, 6, 6])
        self.assertEqual(f[0]["sil"], [3])
        # yeni istemci özeti: güncel hâl (1, 2, 4), silinen yok sayılmaz ama 3 "sil" listesinde
        im2 = d.imlec_yeni()
        o = d.farki_al(im2)
        self.assertEqual(sorted(o[1]["kimlik"]), [1, 2, 4])
        self.assertEqual(o[1]["xyz"][o[1]["kimlik"].index(2) * 3:o[1]["kimlik"].index(2) * 3 + 3], [5, 5, 5])
        # katman temizleme
        d.uygula([{"katman": "landmark", "islem": "temizle_katman"}])
        f = d.farki_al(im)
        self.assertEqual(f[0], {"katman": "landmark", "islem": "temizle_katman"})
        self.assertEqual(d.farki_al(im), [])


class _Dongu:
    def add_callback(self, f, *a):
        f(*a)


class _Sahte(ArkaUc):
    ad = "sahte"

    def __init__(self, ad, bekle=0.0, hata=None):
        self.yontem_adi = ad
        self.kapatildi = False
        if bekle:
            time.sleep(bekle)
        if hata:
            raise RuntimeError(hata)

    def katman_tanimlari(self):
        return [{"id": "x", "ad": "x", "uzay": ["harita"], "renk": "#fff", "varsayilan": True, "grup": "harita"}]

    def feed(self, jpeg, t_ms, komut):
        return {"kare": 1, "t_ms": t_ms, "poz": {"x": 0, "y": 0, "yon": 0}, "katmanlar": {}, "harita_islem": [], "sure_ms": {}, "bilgi": {}, "uyari": []}

    def sifirla(self):
        pass

    def kapat(self):
        self.kapatildi = True


class YontemSecimiTesti(unittest.TestCase):
    def test_baslatiliyor_hazir_ve_hata(self):
        olusan = []

        def fab(ad, bekle=0.0, hata=None):
            def f():
                o = _Sahte(ad, bekle, hata)
                olusan.append(o)
                return o
            return f
        b = Boru(None, [], _Dongu(), fabrikalar=[("a", "A yöntemi", fab("A", 0.3)), ("b", "B yöntemi", fab("B")), ("c", "C bozuk", fab("C", 0, "WSL yok"))], yontem_id="a")
        try:
            self.assertTrue(b.yontem_sec("a"))
            self.assertEqual(b.yontem_durum, "baslatiliyor")                 # hemen: sayfa "başlatılıyor" görür, ekran donmaz
            self.assertFalse(b.yontem_sec("b"))                              # başlarken ikinci seçim yok sayılır
            t0 = time.time()
            while b.yontem_durum != "hazir" and time.time() - t0 < 5:
                time.sleep(0.05)
            self.assertEqual(b.yontem_durum, "hazir")
            self.assertEqual(b.arkauc.yontem_adi, "A")
            self.assertEqual(b.katmanlar[0]["id"], "x")
            b.dunya.uygula([{"katman": "yorunge", "islem": "ekle", "xy": [1, 1]}])
            # a -> b: eski kapatılır, dünya sıfırlanır
            eski = b.arkauc
            self.assertTrue(b.yontem_sec("b"))
            t0 = time.time()
            while (b.yontem_durum != "hazir" or b.arkauc is eski) and time.time() - t0 < 5:
                time.sleep(0.05)
            self.assertTrue(eski.kapatildi)
            self.assertEqual(b.arkauc.yontem_adi, "B")
            self.assertEqual(b.dunya.ekle, {})
            # hatalı yöntem: net hata, ekran donmaz, yeniden denenebilir
            self.assertTrue(b.yontem_sec("c"))
            t0 = time.time()
            while b.yontem_durum == "baslatiliyor" and time.time() - t0 < 5:
                time.sleep(0.05)
            self.assertEqual(b.yontem_durum, "hata")
            self.assertIn("WSL yok", b.yontem_hata)
            self.assertIsNone(b.arkauc)
            self.assertTrue(b.sunucu_durumu()["yontem_durum"] == "hata")
            self.assertTrue(b.yontem_sec("c"))                               # aynı yöntem yeniden denenebilir (hata durumunda)
        finally:
            time.sleep(0.3)
            b.kapat()


class StellaHataTesti(unittest.TestCase):
    def test_wsl_yoksa_net_hata(self):
        import arkauc_stella
        eski = arkauc_stella.shutil.which
        arkauc_stella.shutil.which = lambda *a, **k: None
        try:
            with self.assertRaises(RuntimeError) as c:
                arkauc_stella.Stella(port=9779)
            self.assertIn("WSL", str(c.exception))
        finally:
            arkauc_stella.shutil.which = eski


@unittest.skipIf(KAYIT is None or not wsl_var(), "kayıt ya da WSL köprüsü yok")
class StellaUctanUcaTesti(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from arkauc_stella import Stella
        from kaynaklar import Posta, TekrarKaynagi
        cls.a = Stella(port=9774)
        cls.k = TekrarKaynagi(Posta(), KAYIT)
        cls.durumlar = []
        t0 = time.time()
        for p in cls.k.kare_uret(bas=0, adet=300):
            cls.durumlar.append(cls.a.feed(p["jpeg"], p["t_ms"], p["komut"]))
        cls.sure = time.time() - t0

    @classmethod
    def tearDownClass(cls):
        cls.a.kapat()

    def test_baslatma_sonrasi_izliyor_ve_poz_degisir(self):
        etiketler = [d["izleme"]["etiket"] for d in self.durumlar]
        self.assertEqual(etiketler[0], "başlatılıyor")                       # başlatma durumu açıkça görünür
        self.assertIn("izliyor", etiketler)
        ilk = etiketler.index("izliyor")
        izlenen = sum(1 for e in etiketler[ilk:] if e == "izliyor") / len(etiketler[ilk:])
        self.assertGreater(izlenen, 0.9)
        p0 = next(d["poz"] for d in self.durumlar if d["izleme"]["etiket"] == "izliyor")
        p1 = self.durumlar[-1]["poz"]
        self.assertGreater(np.hypot(p1["x"] - p0["x"], p1["y"] - p0["y"]), 0.05)   # birim keyfi: yalnız değişim aranır
        self.assertEqual(self.a.olcek_bilgisi()["metrik"], False)

    def test_nokta_anahtar_kare_ve_katmanlar(self):
        ops = [o for d in self.durumlar for o in d["harita_islem"]]
        lm = [o for o in ops if o.get("katman") == "landmark"]
        self.assertTrue(lm and all(o["islem"] == "guncelle" for o in lm))
        for o in lm:
            self.assertEqual(len(o["xyz"]), 3 * len(o["kimlik"])) if o.get("xyz") else None
            if o.get("guven"):
                self.assertTrue(all(0.0 <= g <= 1.0 for g in o["guven"]))
                self.assertEqual(len(o["guven"]), len(o["kimlik"]))
        self.assertTrue(any(o.get("katman") == "anahtar3d" and o.get("kameralar") for o in ops))
        self.assertTrue(any(o.get("katman") == "yorunge" for o in ops))
        izle = [d for d in self.durumlar if d["izleme"]["etiket"] == "izliyor"]
        self.assertTrue(sum(1 for d in izle if d["katmanlar"]["ipuclari"]) > 0.8 * len(izle))
        self.assertTrue(sum(1 for d in izle if d["katmanlar"]["ipuclari_mesafe"]) > 0.5 * len(izle))
        son = izle[-1]
        for it in son["katmanlar"]["ipuclari_mesafe"]:
            self.assertEqual(len(it["deger"]) * 2, len(it["xy"]))
            self.assertTrue(all(v > 0 for v in it["deger"]))
        ozet = {o["ad"]: o["deger"] for o in son["ozet"]}
        self.assertGreater(ozet["Anahtar kare"], 3)
        self.assertGreater(ozet["Nokta"], 50)
        tanimli = {t["id"] for t in self.a.katman_tanimlari()}
        for d in self.durumlar:
            self.assertTrue(set(d["katmanlar"]) <= tanimli)
        st = self.a.statik_katmanlar()["maske"]
        self.assertEqual({o["uzay"] for o in st}, {"pano", "ham"})
        for o in st:
            im = cv2.imdecode(np.frombuffer(base64.b64decode(o["png"]), np.uint8), cv2.IMREAD_UNCHANGED)
            self.assertEqual(im.shape[2], 4)
            self.assertGreater(int((im[..., 3] > 0).sum()), 100)
        json.dumps(self.durumlar[-1])

    def test_gorunum_donusumu_ve_deger_araligi(self):
        izle = [d for d in self.durumlar if d["izleme"]["etiket"] == "izliyor"][-1]
        for it in izle["katmanlar"]["ipuclari"]:
            self.assertTrue(-0.05 <= min(it["xy"]) and max(it["xy"]) <= 1.05)
        d = self.a.katman_tanimlari()
        mes = next(t for t in d if t["id"] == "ipuclari_mesafe")
        self.assertEqual(mes["lejant"]["birim"], "birim")                    # metre uydurulmaz

    def test_sifirla_ve_eylemler(self):
        e = self.a.eylemler()
        self.assertEqual([d["id"] for d in e["dugmeler"]], ["sifirla", "kaydet", "yukle"])
        self.assertEqual(e["secimler"][0]["id"], "kip")
        self.assertEqual(self.a.eylem("kip", "konum")["ok"], True)
        self.assertEqual(self.a.eylem("kip", "harita")["ok"], True)
        self.assertFalse(self.a.eylem("kip", "saçma")["ok"])
        r = self.a.eylem("kaydet")
        self.assertTrue(r["ok"], r)
        self.assertIn("harita_", r["mesaj"])
        self.assertTrue(self.a.eylemler()["dugmeler"][2]["ad"].startswith("Haritayı yükle (son kayıt"))
        self.assertTrue(self.a.eylem("sifirla")["sifirla"])                  # sunucu işleme iş parçacığında sifirla() çağırır
        self.a.sifirla()
        p = next(self.k.kare_uret(bas=0, adet=1))
        d = self.a.feed(p["jpeg"], p["t_ms"], p["komut"])
        self.assertEqual(d["izleme"]["etiket"], "başlatılıyor")
        self.assertTrue(any(o.get("islem") == "temizle" for o in d["harita_islem"]))
        self.assertEqual(d["ozet"][0]["deger"], 0)                           # anahtar kare sayısı sıfırlandı


if __name__ == "__main__":
    unittest.main(verbosity=2)
