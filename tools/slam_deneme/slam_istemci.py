"""stella_vslam köprüsü için Python istemcisi (geliştirici ekranı arka ucu bunu kullanır).

BAŞLATMA (tek komut, Windows PowerShell, depo kökünden):
    powershell -File tools/slam_deneme/baslat_kopru.ps1            # WSL Ubuntu-22.04 içinde köprü, 127.0.0.1:9760 (Docker YOK)
  ya da Python'dan:  from slam_kopru_surec import KopruSurec;  with KopruSurec(port=9760) as s: ...   (süreç with bitince kapanır)
  Önkoşul bir kez: tools/slam_deneme/kurulum.sh ve kurulum_2.sh. Köprü açılınca stderr'e 'KOPRU_HAZIR port N' yazar.

KULLANIM:
    ist = SlamIstemci(port=9760)
    y = ist.feed(jpeg_bayt, t_ms, (sol_pwm, sag_pwm))      # jpeg_bayt: ham 1280x720 çift balık gözü JPEG
    y = ist.feed(jpeg_bayt, t_ms, (sol, sag), tam_durum=True)   # ayrıca tüm harita (nokta ve anahtar kare) ekler (pahalı: her ~1 sn'de bir iste)
  Dönüş y (sözlük):
    durum          'Initializing' | 'Tracking' | 'Lost'      izleniyor: bool (Tracking ve poz var)
    mod            'harita' | 'konum'
    poz_cw         16 sayı, satır sıralı 4x4, dünya->kamera.   kamera_konum [x,y,z] dünya (x sağ, y AŞAĞI, z ileri; dünya = ilk anahtar kare)
    ipuclari       [[u,v],...] haritadaki noktalara eşleşmiş 2B anahtar noktalar (eşdikdörtgen 1280x640 piksel)
    nokta_sayisi   karedeki tüm ORB noktaları;  eslesen_sayisi: haritaya eşleşen;  harita_nokta_sayisi, yerel_nokta_sayisi, anahtar_kare_sayisi
    sureler_ms     {donusum (istemci: balık gözü->eşdikdörtgen), kopru_alma, takip, cikarim, toplam_sunucu, uctan_uca}
    komut          gönderilen motor komutu geri yazılır (stella tek gözlü SLAM'dir, komutu kullanmaz)
    tam_durum=True ise ek: noktalar [[x,y,z,yerel_mi,id],...], anahtar_kareler [{id,t,poz_wc,gozlem:[nokta_id,...]}]
  ARTIMLI HARİTA:  yeni_nokta, yeni_kf = ist.artimli()   # önceki çağrıdan beri eklenen nokta/anahtar kareler (istemci tarafında id farkı)
  HARİTA:          ist.durum()  (tam harita);  ist.kaydet('/root/slam/cikti/ad.msg');  ist.yukle(yol, 'konum'|'harita');  ist.sifirla()
  KİP:             ist.mod('konum')  yalnız konumlama (harita değişmez);  ist.mod('harita')  haritalama açık
  2B HARİTA:       g = ist.izgara(olcek_m_birim)   # slam_izgara.izgara_hesapla: dolu/bos sayaç dizileri (güven = sayaç), slam_izgara.png_yaz ile PNG
  KAPAT:           ist.kapat(sunucuyu_durdur=True)
Ölçek: SLAM ölçeği keyfidir (tek göz). Metre için olcek_m_birim kullanıcıdan/ yer düzleminden gelmeli (bkz. slam_degerlendir.zemin_olcegi, bu veride güvenilmez).
Motor komutu şimdilik yalnız iletilir. Kare dönüşümü istemcidedir (ekdortgen_donustur.Donusturucu); köprü ham eşdikdörtgen BGR alır. Protokol: kopru.cc üstü.
Gecikme (olcum_ham, bu PC): bkz. kayitlar/slam/sonuc/kopru_test/test.json."""
import json
import os
import socket
import sys
import time

import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
from ekdortgen_donustur import Donusturucu  # noqa: E402


class SlamIstemci:
    def __init__(self, adres="127.0.0.1", port=9760, zaman_asimi=120.0, donusturucu=None):
        self.s = socket.create_connection((adres, port), timeout=zaman_asimi)
        self.s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self.f = self.s.makefile("rb")
        self.d = donusturucu or Donusturucu()

    # ---- düşük seviye ----
    def _istek(self, baslik, yuk=b""):
        baslik = dict(baslik)
        baslik["n"] = len(yuk)
        self.s.sendall((json.dumps(baslik) + "\n").encode() + yuk)
        satir = self.f.readline()
        if not satir:
            raise ConnectionError("köprü bağlantıyı kapattı")
        return json.loads(satir)

    # ---- sözleşme ----
    def feed(self, kare_jpeg, t_ms, motor_komutu=(0, 0), tam_durum=False):
        t0 = time.perf_counter()
        e = self.d.kare(kare_jpeg)
        t1 = time.perf_counter()
        return self.feed_esd(e, t_ms, motor_komutu, tam_durum, t0, t1)

    def feed_esd(self, esd_bgr, t_ms, motor_komutu=(0, 0), tam_durum=False, _t0=None, _t1=None):
        t0 = _t0 if _t0 is not None else time.perf_counter()
        t1 = _t1 if _t1 is not None else t0
        h, w = esd_bgr.shape[:2]
        yan = self._istek({"cmd": "feed", "t_ms": int(t_ms), "w": w, "h": h, "komut": list(motor_komutu), "tam": bool(tam_durum)},
                          np.ascontiguousarray(esd_bgr).tobytes())
        t2 = time.perf_counter()
        yan.setdefault("sureler_ms", {})
        yan["sureler_ms"]["donusum"] = (t1 - t0) * 1000
        yan["sureler_ms"]["uctan_uca"] = (t2 - t0) * 1000
        return yan

    def durum(self):
        """Tam harita: noktalar [[x,y,z]], anahtar kareler [{id, poz_cw}]."""
        return self._istek({"cmd": "state"})

    def mod(self, mod):
        """'harita' (haritalama açık) | 'konum' (yalnız konumlama, harita değişmez)."""
        return self._istek({"cmd": "mode", "mode": mod})

    def kaydet(self, yol):
        return self._istek({"cmd": "save", "path": yol})

    def yukle(self, yol, mod="konum"):
        return self._istek({"cmd": "load", "path": yol, "mode": mod})

    def artimli(self):
        """Önceki çağrıdan beri eklenen (yeni_nokta_listesi, yeni_anahtar_kare_listesi). İlk çağrı hepsini verir."""
        d = self.durum()
        if not hasattr(self, "_gor_nokta"):
            self._gor_nokta, self._gor_kf = set(), set()
        yn = [p for p in d["noktalar"] if int(p[4]) not in self._gor_nokta]
        yk = [k for k in d["anahtar_kareler"] if k["id"] not in self._gor_kf]
        self._gor_nokta.update(int(p[4]) for p in yn)
        self._gor_kf.update(k["id"] for k in yk)
        return yn, yk

    def izgara(self, olcek, **kw):
        from slam_izgara import izgara_hesapla
        return izgara_hesapla(self.durum(), olcek, **kw)

    def sifirla(self):
        return self._istek({"cmd": "reset"})

    def kapat(self, sunucuyu_durdur=False):
        try:
            if sunucuyu_durdur:
                self._istek({"cmd": "quit"})
        finally:
            self.s.close()
