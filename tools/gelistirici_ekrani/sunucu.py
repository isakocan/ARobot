"""Geliştirici ekranı sunucusu: tek süreç, tornado (HTTP + WebSocket), 0.0.0.0.

Akış:  Kaynak (tekrar | canlı) -> Posta (tek kare, en yeni üste yazar) -> İŞLEME iş parçacığı (arka uç.feed) -> yayın
       -> her istemci kendi hızında (önceki gönderim bitmeden yenisi hazırlanmaz) JPEG + durum alır.
Siparişler (istemciden WebSocket JSON): ayar(gorunum, kalite), duraklat, hiz, basa, sifirla, kaynak.
Kalıcı harita değişiklikleri (yörünge, doluluk ızgarası...) istemci karelerini atlasa bile kaybolmasın diye `Dunya`da birikir; her istemciye
imleçle yalnız yeni kısım gider.
HTTP: /  sayfa,  /api/durum  sunucu durumu,  /api/son_durum  son durum sözlüğü (JSON),  /kare.jpg?gorunum=pano|ham  son kare."""
import asyncio
import json
import os
import socket
import sys
import threading
import time
from collections import deque
from concurrent.futures import ThreadPoolExecutor

import cv2
import numpy as np
import tornado.ioloop
import tornado.web
import tornado.websocket

KLASOR = os.path.dirname(os.path.abspath(__file__))
SAYFA = os.path.join(KLASOR, "sayfa")
sys.path.insert(0, KLASOR)
from kaynaklar import CanliKaynak, Posta, TekrarKaynagi  # noqa: E402


class Dunya:
    """Arka ucun kalıcı harita/3B işlemlerinin birikimi ve istemci imleçleri (bkz. arkauc.py HARİTA İŞLEMLERİ).
    "ekle" işlemleri katman başına PARÇA listesi olarak tutulur; "guncelle" (id'li küme) işlemleri id -> değer sözlüğü olarak (sürüm damgalı), istemciye
    imleçten sonra değişen idler gider; "yerlestir" katman başına son işlem. Yeni bağlanana tek seferde özet."""

    def __init__(self):
        self.kilit = threading.Lock()
        self.epoch = 0
        self.sifirla()

    def sifirla(self):
        with self.kilit:
            self.epoch += 1
            self.ekle = {}           # katman -> [işlem, ...]
            self.yerles = {}         # katman -> (surum, işlem)
            self.kume = {}           # katman -> {"surum":n, "o":{id: [alanlar, surum]}, "sil":{id: surum}}
            self.katman_epoch = {}   # katman -> sayaç (temizle_katman)
            self.surum = 0

    def uygula(self, ops):
        with self.kilit:
            for op in ops:
                islem = op.get("islem")
                if islem == "temizle":
                    self.epoch += 1
                    self.ekle, self.yerles, self.kume, self.katman_epoch = {}, {}, {}, {}
                elif islem == "temizle_katman":
                    k = op["katman"]
                    self.ekle.pop(k, None)
                    self.yerles.pop(k, None)
                    self.kume.pop(k, None)
                    self.katman_epoch[k] = self.katman_epoch.get(k, 0) + 1
                elif islem == "ekle":
                    self.ekle.setdefault(op["katman"], []).append(op)
                elif islem == "yerlestir":
                    self.surum += 1
                    self.yerles[op["katman"]] = (self.surum, op)
                elif islem == "guncelle":
                    self._guncelle(op)

    def _guncelle(self, op):
        K = self.kume.setdefault(op["katman"], {"surum": 0, "o": {}, "sil": {}})
        K["surum"] += 1
        sv = K["surum"]
        ids = op.get("kimlik") or []
        xyz, guven, kam = op.get("xyz"), op.get("guven"), op.get("kameralar")
        for i, kid in enumerate(ids):
            alan = {}
            if xyz is not None:
                alan["xyz"] = xyz[i * 3:i * 3 + 3]
            if guven is not None:
                alan["guven"] = guven[i]
            if kam is not None:
                alan["kamera"] = kam[i]
            K["o"][kid] = [alan, sv]
            K["sil"].pop(kid, None)
        for kid in op.get("sil") or []:
            if K["o"].pop(kid, None) is not None or True:
                K["sil"][kid] = sv

    def imlec_yeni(self):
        return {"epoch": -1, "ekle": {}, "yerles": {}, "kume": {}, "ke": {}}

    def farki_al(self, imlec):
        """İstemcinin imlecine göre gönderilecek işlemler; imleç yerinde güncellenir."""
        ops = []
        with self.kilit:
            if imlec["epoch"] != self.epoch:
                ops.append({"islem": "temizle"})
                imlec.update(epoch=self.epoch, ekle={}, yerles={}, kume={}, ke={})
            for k, e in self.katman_epoch.items():
                if imlec["ke"].get(k, 0) != e:
                    ops.append({"katman": k, "islem": "temizle_katman"})
                    imlec["ke"][k] = e
                    imlec["ekle"].pop(k, None)
                    imlec["yerles"].pop(k, None)
                    imlec["kume"].pop(k, None)
            for k, liste in self.ekle.items():
                n0 = imlec["ekle"].get(k, 0)
                if len(liste) > n0:
                    ops.extend(liste[n0:])
                    imlec["ekle"][k] = len(liste)
            for k, (sv, op) in self.yerles.items():
                if imlec["yerles"].get(k, 0) < sv:
                    ops.append(op)
                    imlec["yerles"][k] = sv
            for k, K in self.kume.items():
                c = imlec["kume"].get(k, 0)
                if K["surum"] > c:
                    ids, xyz, guven, kam, sil = [], [], [], [], []
                    for kid, (alan, sv) in K["o"].items():
                        if sv > c:
                            ids.append(kid)
                            if "xyz" in alan:
                                xyz.extend(alan["xyz"])
                            if "guven" in alan:
                                guven.append(alan["guven"])
                            if "kamera" in alan:
                                kam.append(alan["kamera"])
                    sil = [kid for kid, sv in K["sil"].items() if sv > c]
                    op = {"katman": k, "islem": "guncelle", "kimlik": ids}
                    if xyz:
                        op["xyz"] = xyz
                    if guven:
                        op["guven"] = guven
                    if kam:
                        op["kameralar"] = kam
                    if sil:
                        op["sil"] = sil
                    if ids or sil:
                        ops.append(op)
                    imlec["kume"][k] = K["surum"]
        return ops


class Istatistik:
    def __init__(self, pencere=3.0):
        self.pencere = pencere
        self.t = deque()

    def ekle(self, t=None):
        t = time.time() if t is None else t
        self.t.append(t)
        self._kirp(t)

    def _kirp(self, t):
        while self.t and t - self.t[0] > self.pencere:
            self.t.popleft()

    def hz(self):
        t = time.time()
        self._kirp(t)
        if len(self.t) < 2:
            return 0.0
        return (len(self.t) - 1) / max(self.t[-1] - self.t[0], 1e-6)


class Boru:
    """Kaynak yönetimi + işleme iş parçacığı + yayın. Tek örnek."""

    def __init__(self, arkauc, kaynak_secenekleri, ioloop, jpeg_kalite=70, dongu=False, varsayilan_hiz=1.0, canli_sinir_sn=170.0, canli_yol="mjpg", fabrikalar=None, yontem_id=None):
        self.arkauc = arkauc
        self.secenekler = kaynak_secenekleri         # [{"id","tur","ad","yol"|"adres"}]
        self.ioloop = ioloop
        self.jpeg_kalite = jpeg_kalite
        self.dongu = dongu
        self.hiz = varsayilan_hiz
        self.canli_sinir_sn = canli_sinir_sn
        self.canli_yol = canli_yol
        self.posta = Posta()
        self.kaynak = None
        self.kaynak_id = None
        self.dunya = Dunya()
        self.istemciler = set()
        self.son_durum = None
        self.son_paket = None
        self.seq = 0
        self._sifirla_iste = threading.Event()
        self._kilit = threading.RLock()
        self._dur = False
        self.isleme_fps = Istatistik()
        self.kaynak_fps = Istatistik()
        self.kayip0 = 0
        self.gelen0 = 0
        self.son_sure = {}
        self.son_yas_ms = 0.0
        self.son_hata = ""
        self.islenen = 0
        self.isleme_ms = deque(maxlen=60)
        self.havuz = ThreadPoolExecutor(max_workers=3, thread_name_prefix="goruntu")
        self._goruntu_onbellek = {}
        self.fabrikalar = list(fabrikalar or [])        # [(id, ad, callable -> ArkaUc)]
        self.yontem_id = yontem_id
        self.yontem_durum = "hazir" if arkauc is not None else "yok"     # yok | baslatiliyor | hazir | hata
        self.yontem_hata = ""
        self._yontem_kilit = threading.RLock()           # feed / sifirla / değiştirme birbirini beklesin
        self._degisim_kilit = threading.Lock()
        self.istek_katmanlar = set()
        self.statik = arkauc.statik_katmanlar() if arkauc is not None else {}
        self.katmanlar = arkauc.katman_tanimlari() if arkauc is not None else []
        self.it = threading.Thread(target=self._isle, name="isleme", daemon=True)
        self.it.start()

    # ---------------------------------------------------------------- kaynak denetimi
    def kaynak_sec(self, kid):
        with self._kilit:
            sec = next((s for s in self.secenekler if s["id"] == kid), None)
            if sec is None:
                raise KeyError(kid)
            if self.kaynak is not None:
                self.kaynak.durdur()
                self.kaynak = None
            self.posta.bosalt()
            self.posta.kayip = 0
            self.posta.gelen = 0
            self.kayip0 = self.gelen0 = 0
            self.kaynak_fps = Istatistik()
            self.isleme_fps = Istatistik()
            self.islenen = 0
            self._sifirla_iste.set()
            if sec["tur"] == "tekrar":
                k = TekrarKaynagi(self.posta, sec["yol"], hiz=self.hiz, dongu=self.dongu)
            else:
                k = CanliKaynak(self.posta, sec["adres"], sinir_sn=self.canli_sinir_sn, yol=self.canli_yol)
            self.kaynak, self.kaynak_id = k, kid
            k.baslat()

    def basa(self):
        with self._kilit:
            if isinstance(self.kaynak, TekrarKaynagi):
                self.kaynak.basa_sar()
                self.posta.bosalt()
                self._sifirla_iste.set()
            elif self.kaynak is not None and self.kaynak_id:
                self.kaynak_sec(self.kaynak_id)        # canlı: yeniden bağlan (yeni 170 sn)

    def duraklat(self, v):
        if isinstance(self.kaynak, TekrarKaynagi):
            self.kaynak.ayarla(duraklat=v)

    def hiz_ayarla(self, h):
        self.hiz = float(h)
        if isinstance(self.kaynak, TekrarKaynagi):
            self.kaynak.ayarla(hiz=h)

    def sifirla(self):
        self._sifirla_iste.set()

    def kapat(self):
        self._dur = True
        if self.kaynak is not None:
            self.kaynak.durdur()
        with self._yontem_kilit:
            if self.arkauc is not None:
                try:
                    self.arkauc.kapat()
                except Exception:
                    pass

    # ---------------------------------------------------------------- yöntem seçimi (birden çok arka uç, tembel başlatma)
    def yontem_ad(self, yid):
        return next((ad for i, ad, _ in self.fabrikalar if i == yid), yid)

    def yontem_sec(self, yid):
        f = next((x for x in self.fabrikalar if x[0] == yid), None)
        if f is None:
            raise KeyError(yid)
        if self.yontem_durum == "baslatiliyor":
            return False
        if yid == self.yontem_id and self.yontem_durum == "hazir":
            return False
        self.yontem_durum = "baslatiliyor"          # eşzamanlı: iş parçacığı başlamadan sayfa "başlatılıyor" görsün
        self.yontem_id = yid
        self.yontem_hata = ""
        threading.Thread(target=self._yontem_degistir, args=(f,), name="yontem", daemon=True).start()
        return True

    def _merhaba_yayinla(self):
        for c in list(self.istemciler):
            c.merhaba_gonder()

    def _yontem_degistir(self, f):
        yid, ad, fabrika = f
        with self._degisim_kilit:
            self.yontem_durum, self.yontem_id, self.yontem_hata = "baslatiliyor", yid, ""
            self.ioloop.add_callback(self._merhaba_yayinla)
            with self._yontem_kilit:
                eski, self.arkauc = self.arkauc, None
            if eski is not None:
                try:
                    eski.kapat()                    # köprü süreci temiz kapanır
                except Exception as e:
                    print("eski yöntem kapanırken:", repr(e))
            self.statik, self.katmanlar = {}, []
            self.dunya.sifirla()
            self.son_paket = self.son_durum = None
            self._goruntu_onbellek.clear()
            try:
                yeni = fabrika()
            except Exception as e:
                self.yontem_hata = "%s: %s" % (type(e).__name__, e)
                self.yontem_durum = "hata"
                print("yöntem başlamadı (%s): %s" % (yid, self.yontem_hata), flush=True)
                self.ioloop.add_callback(self._merhaba_yayinla)
                return
            with self._yontem_kilit:
                self.arkauc = yeni
                self.statik = yeni.statik_katmanlar()
                self.katmanlar = yeni.katman_tanimlari()
            try:
                yeni.gorunumleri_ayarla({c.gorunum for c in self.istemciler} or {"ham"})
                yeni.katman_istegi(self._katman_birlesimi())
            except Exception:
                pass
            self.yontem_durum = "hazir"
            if self.kaynak_id:
                if isinstance(self.kaynak, TekrarKaynagi):
                    try:
                        self.kaynak_sec(self.kaynak_id)         # kayıt baştan
                    except KeyError:
                        pass
                else:
                    self._sifirla_iste.set()
            self.ioloop.add_callback(self._merhaba_yayinla)

    def _katman_birlesimi(self):
        u = set()
        for c in list(self.istemciler):
            u |= c.acik_katmanlar
        return u

    def katman_istegi_guncelle(self):
        a = self.arkauc
        if a is not None:
            try:
                a.katman_istegi(self._katman_birlesimi())
            except Exception:
                pass

    # ---------------------------------------------------------------- işleme iş parçacığı
    def _isle(self):
        while not self._dur:
            if self._sifirla_iste.is_set():
                self._sifirla_iste.clear()
                with self._yontem_kilit:
                    if self.arkauc is not None:
                        try:
                            self.arkauc.sifirla()
                        except Exception as e:
                            self.son_hata = "sıfırlama: " + repr(e)
                self.dunya.sifirla()
                self.posta.bosalt()
                self.son_durum = None
                continue
            p = self.posta.al(0.2)
            if p is None:
                continue
            if self.arkauc is None:                       # yöntem başlıyor/başlamadı: kare atılır, ekran donmaz (durum satırı "başlatılıyor" der)
                continue
            self.kaynak_fps.ekle(p["geldi"])
            a = time.perf_counter()
            try:
                with self._yontem_kilit:
                    if self.arkauc is None:
                        continue
                    durum = self.arkauc.feed(p["jpeg"], p["t_ms"], p["komut"])
            except Exception as e:
                import traceback
                traceback.print_exc()
                self.son_hata = "işleme: " + repr(e)
                continue
            b = time.perf_counter()
            self.isleme_ms.append((b - a) * 1000.0)
            self.islenen += 1
            self.isleme_fps.ekle()
            self.dunya.uygula(durum.get("harita_islem") or [])
            self.son_sure = durum.get("sure_ms", {})
            self.son_yas_ms = (time.time() - p["geldi"]) * 1000.0
            self.seq += 1
            paket = {"seq": self.seq, "durum": durum, "jpeg": p["jpeg"], "komut": p["komut"], "t_ms": p["t_ms"], "geldi": p["geldi"],
                     "yas_ms": self.son_yas_ms, "isleme_ms": (b - a) * 1000.0}
            self.son_durum = durum
            self.son_paket = paket
            self.ioloop.add_callback(self._yayinla)

    def _yayinla(self):
        for c in list(self.istemciler):
            c.sonrakini_gonder()

    # ---------------------------------------------------------------- sunucu istatistiği
    def sunucu_durumu(self, paket=None):
        k = self.kaynak
        gelen = self.posta.gelen
        kayip = self.posta.kayip
        ort = float(np.mean(self.isleme_ms)) if self.isleme_ms else 0.0
        d = {
            "isleme_fps": round(self.isleme_fps.hz(), 1), "kaynak_fps": round(self.posta.hz(), 1),
            "kayip": kayip, "gelen": gelen, "kayip_yuzde": round(100.0 * kayip / max(gelen, 1), 1),
            "isleme_ms": round(ort, 1), "yas_ms": round(self.son_yas_ms, 1),
            "kaynak": (k.bilgi() if k is not None else None), "kaynak_id": self.kaynak_id, "hata": self.son_hata,
            "komut": list(paket["komut"]) if paket else None, "arkauc": (self.arkauc.ad if self.arkauc is not None else None), "islenen": self.islenen,
            "yontem_id": self.yontem_id, "yontem_durum": self.yontem_durum, "yontem_hata": self.yontem_hata,
        }
        a = self.arkauc
        if a is not None:
            try:
                d["eylemler"] = a.eylemler()
                d["olcek"] = a.olcek_bilgisi()
            except Exception:
                pass
        return d

    # ---------------------------------------------------------------- görüntü üretimi (iş parçacığı)
    def goruntu_uret(self, paket, gorunum):
        anahtar = (paket["seq"], gorunum)
        o = self._goruntu_onbellek.get(anahtar)
        if o is not None:
            return o
        a = time.perf_counter()
        im = cv2.imdecode(np.frombuffer(paket["jpeg"], np.uint8), cv2.IMREAD_COLOR)
        if gorunum == "pano":
            ark = self.arkauc
            pn = ark.panorama_goruntu(im, 1024) if ark is not None else None
            if pn is None:
                pn = im
            im = pn
        else:
            im = cv2.resize(im, (960, 540), interpolation=cv2.INTER_AREA)
        ok, kod = cv2.imencode(".jpg", im, [cv2.IMWRITE_JPEG_QUALITY, int(self.jpeg_kalite)])
        out = (kod.tobytes(), im.shape[1], im.shape[0], (time.perf_counter() - a) * 1000.0)
        if len(self._goruntu_onbellek) > 6:
            self._goruntu_onbellek.clear()
        self._goruntu_onbellek[anahtar] = out
        return out


def filtrele_katmanlar(katmanlar, gorunum):
    out = {}
    for kid, ogeler in (katmanlar or {}).items():
        liste = [o for o in ogeler if o.get("uzay") in (gorunum, "harita", "3d")]
        if liste:
            out[kid] = liste
    return out


class IstemciWS(tornado.websocket.WebSocketHandler):
    def initialize(self, boru):
        self.boru = boru
        self.gorunum = "ham"
        self.imlec = boru.dunya.imlec_yeni()
        self.son_seq = 0
        self.yolda = False
        self.kapali = False
        self.kimlik = "i%x" % id(self)
        self.acik_katmanlar = set()

    def check_origin(self, origin):
        return True

    def merhaba_mesaji(self):
        b = self.boru
        a = b.arkauc
        return json.dumps({
            "t": "merhaba", "arkauc": a.ad if a else None, "katmanlar": b.katmanlar, "statik": b.statik,
            "kaynaklar": [{"id": s["id"], "ad": s["ad"], "tur": s["tur"]} for s in b.secenekler], "kaynak_id": b.kaynak_id,
            "hiz": b.hiz, "kalite": b.jpeg_kalite,
            "yontem": (a.yontem_adi if a else b.yontem_ad(b.yontem_id)), "yontem_aciklama": getattr(a, "yontem_aciklama", "") if a else "",
            "yontem_id": b.yontem_id, "yontemler": [{"id": i, "ad": ad} for i, ad, _ in b.fabrikalar],
            "yontem_durum": b.yontem_durum, "yontem_hata": b.yontem_hata,
            "varsayilan_gorunum": getattr(a, "varsayilan_gorunum", "ham") if a else "ham",
            "olcek": a.olcek_bilgisi() if a else {"birim": "m", "metrik": True},
            "paneller": a.kamera_panelleri() if a else None, "guven": a.guven_bilgisi() if a else None,
        })

    def merhaba_gonder(self):
        if self.kapali:
            return
        self.imlec = self.boru.dunya.imlec_yeni()
        self.son_seq = 0
        try:
            self.write_message(self.merhaba_mesaji())
        except tornado.websocket.WebSocketClosedError:
            self.kapali = True
            return
        self.sonrakini_gonder()

    def open(self):
        b = self.boru
        b.istemciler.add(self)
        self._gorunumleri_bildir()
        self.merhaba_gonder()

    def on_close(self):
        self.kapali = True
        self.boru.istemciler.discard(self)
        self._gorunumleri_bildir()
        self.boru.katman_istegi_guncelle()

    def _gorunumleri_bildir(self):
        g = {c.gorunum for c in self.boru.istemciler} or {"ham"}
        if self.boru.arkauc is not None:
            self.boru.arkauc.gorunumleri_ayarla(g)

    def on_message(self, mesaj):
        try:
            m = json.loads(mesaj)
        except ValueError:
            return
        b, t = self.boru, m.get("t")
        if t == "ayar":
            if m.get("gorunum") in ("pano", "ham"):
                self.gorunum = m["gorunum"]
                self._gorunumleri_bildir()
            if "kalite" in m:
                b.jpeg_kalite = int(max(30, min(95, m["kalite"])))
            self.son_seq = 0
            self.sonrakini_gonder()
        elif t == "duraklat":
            b.duraklat(bool(m.get("deger")))
        elif t == "hiz":
            b.hiz_ayarla(float(m.get("deger", 1)))
        elif t == "basa":
            b.basa()
        elif t == "sifirla":
            b.sifirla()
        elif t == "yontem":
            try:
                b.yontem_sec(m.get("id"))
            except KeyError:
                pass
        elif t == "katmanlar":
            self.acik_katmanlar = set(str(x) for x in (m.get("acik") or []))
            b.katman_istegi_guncelle()
        elif t == "eylem":
            asyncio.ensure_future(self._eylem(m.get("id"), m.get("arg")))
        elif t == "uzat":
            if isinstance(b.kaynak, CanliKaynak):
                b.kaynak.uzat(120)
        elif t == "kaynak":
            try:
                b.kaynak_sec(m.get("id"))
                self.write_message(json.dumps({"t": "olay", "metin": "kaynak değişti", "kaynak_id": b.kaynak_id}))
            except KeyError:
                pass

    async def _eylem(self, kimlik, arg):
        a = self.boru.arkauc
        if a is None:
            return
        try:
            loop = asyncio.get_running_loop()
            r = await loop.run_in_executor(self.boru.havuz, a.eylem, kimlik, arg)
        except Exception as e:
            r = {"ok": False, "mesaj": "%s: %s" % (type(e).__name__, e)}
        if r.get("sifirla"):
            self.boru.sifirla()
        if r.get("dunya_sifirla"):
            self.boru.dunya.sifirla()
        try:
            self.write_message(json.dumps({"t": "olay", "metin": r.get("mesaj", ""), "eylem_ok": bool(r.get("ok")), "eylem": kimlik}))
        except tornado.websocket.WebSocketClosedError:
            self.kapali = True

    def sonrakini_gonder(self):
        if self.yolda or self.kapali:
            return
        p = self.boru.son_paket
        if p is None or p["seq"] == self.son_seq:
            # kare yok ya da yeni kare yok: yine de durum (kaynak/duraklatma) için ara sıra sunucu durumu
            return
        self.yolda = True
        self.son_seq = p["seq"]
        asyncio.ensure_future(self._gonder(p))

    async def _gonder(self, p):
        b = self.boru
        try:
            loop = asyncio.get_running_loop()
            jpeg, w, h, goruntu_ms = await loop.run_in_executor(b.havuz, b.goruntu_uret, p, self.gorunum)
            d = dict(p["durum"])
            ops = b.dunya.farki_al(self.imlec)
            d.pop("harita_islem", None)
            d["katmanlar"] = filtrele_katmanlar(d.get("katmanlar"), self.gorunum)
            sd = b.sunucu_durumu(p)
            sd["goruntu_ms"] = round(goruntu_ms, 1)
            sd["gonderim_yas_ms"] = round((time.time() - p["geldi"]) * 1000.0, 1)
            sd["isleme_ms_kare"] = round(p["isleme_ms"], 1)
            baslik = json.dumps({"t": "kare", "seq": p["seq"], "gorunum": self.gorunum, "w": w, "h": h, "durum": d, "harita_islem": ops, "sunucu": sd},
                                separators=(",", ":")).encode("utf-8")
            msg = len(baslik).to_bytes(4, "big") + baslik + jpeg
            await self.write_message(msg, binary=True)
        except tornado.websocket.WebSocketClosedError:
            self.kapali = True
        except Exception as e:
            print("gönderim hatası:", repr(e))
        finally:
            self.yolda = False
            if not self.kapali:
                self.sonrakini_gonder()


class DurumHandler(tornado.web.RequestHandler):
    def initialize(self, boru):
        self.boru = boru

    def get(self):
        self.set_header("Content-Type", "application/json; charset=utf-8")
        self.write(json.dumps(self.boru.sunucu_durumu(self.boru.son_paket), ensure_ascii=False))


class SonDurumHandler(tornado.web.RequestHandler):
    def initialize(self, boru):
        self.boru = boru

    def get(self):
        p = self.boru.son_paket
        self.set_header("Content-Type", "application/json; charset=utf-8")
        if p is None:
            self.set_status(204)
            return
        d = dict(p["durum"])
        d["seq"] = p["seq"]
        self.write(json.dumps(d))


class KareHandler(tornado.web.RequestHandler):
    def initialize(self, boru):
        self.boru = boru

    async def get(self):
        p = self.boru.son_paket
        if p is None:
            self.set_status(204)
            return
        g = self.get_argument("gorunum", "ham")
        loop = asyncio.get_running_loop()
        jpeg, w, h, _ = await loop.run_in_executor(self.boru.havuz, self.boru.goruntu_uret, p, "pano" if g == "pano" else "ham")
        self.set_header("Content-Type", "image/jpeg")
        self.set_header("Cache-Control", "no-store")
        self.write(jpeg)


class SayfaHandler(tornado.web.RequestHandler):
    def get(self):
        with open(os.path.join(SAYFA, "index.html"), "rb") as f:
            self.set_header("Content-Type", "text/html; charset=utf-8")
            self.set_header("Cache-Control", "no-store")
            self.write(f.read())


def uygulama_kur(boru):
    return tornado.web.Application([
        (r"/", SayfaHandler),
        (r"/ws", IstemciWS, {"boru": boru}),
        (r"/api/durum", DurumHandler, {"boru": boru}),
        (r"/api/son_durum", SonDurumHandler, {"boru": boru}),
        (r"/kare.jpg", KareHandler, {"boru": boru}),
        (r"/sayfa/(.*)", tornado.web.StaticFileHandler, {"path": SAYFA}),
    ], websocket_ping_interval=20)


def yerel_adresler():
    adresler = []
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("10.255.255.255", 1))          # yönlendirme tablosu sorgusu; paket gitmez
        adresler.append(s.getsockname()[0])
        s.close()
    except Exception:
        pass
    try:
        for a in socket.gethostbyname_ex(socket.gethostname())[2]:
            if a not in adresler and not a.startswith("127."):
                adresler.append(a)
    except Exception:
        pass
    return adresler
