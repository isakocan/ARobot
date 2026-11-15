"""Arka uç 2: stella_vslam (saf, tek kamera). tools/slam_deneme/ köprüsünü (WSL'de C++) ve istemcisini (SlamIstemci) kullanır.

HİÇBİR ZEMİN BÖLÜTLEME YOK, backend 1'den (konum_deney) hiçbir şey karışmaz: görüntü eşdikdörtgene çevrilir (slam_deneme/ekdortgen_donustur.py, kalibre model),
stella_vslam (ORB tabanlı, sinir ağı yok) izler ve haritalar. Motor komutu yalnız iletilir (stella kullanmaz).

ÖLÇEK BİLİNMİYOR: tek gözlü SLAM'in birimi keyfidir. Sayfa eksenleri ve ölçek çubuğunu "birim" olarak yazar (olcek_bilgisi), metre UYDURULMAZ.

KOORDİNATLAR: stella dünyası = ilk anahtar kare, x sağ, y AŞAĞI, z ileri. Sayfaya (x doğu=x, y kuzey=z, z yukarı=-y) giden sağ-elli dönüşüm uygulanır; yön = z ekseninden
(ilk kare yönü) saat yönünde derece.
KÖPRÜ NOTU (doğrulandı): köprünün "poz_cw" alanı aslında KAMERANIN DÜNYADAKİ pozudur (T_wc: öteleme = kamera merkezi, 3. sütun = ileri); "kamera_konum" alanı bu yüzden
yanlış (T_cw varsayımıyla -R^T t); burada kullanılmaz. Anahtar karelerin poz_wc'si ile tutarlılık ve nokta yeniden izdüşümü (%96 < 4 px) ile sınandı.

SÖZLEŞME (arkauc.py): katmanlar, id'li küme işlemleri (nokta bulutu ve anahtar kareler yerinde güncellenir, silinir), eylemler (sıfırla, kaydet, yükle, kip), olcek_bilgisi.
Çevrim kapama/optimizasyonda her şey sıçrayabilir: yaklaşık 1,5 sn'de bir TAM durum istenir, değişen idler güncellenir; anahtar kare konumları topluca kayarsa (tahmini çevrim kapama)
yörünge anahtar karelerden yeniden kurulur."""
import base64
import os
import shutil
import subprocess
import sys
import threading
import time

import cv2
import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
SLAM = os.path.abspath(os.path.join(KLASOR, "..", "slam_deneme"))
KOD = os.path.abspath(os.path.join(KLASOR, "..", "konum_deney"))
for p in (KLASOR, SLAM, KOD):
    if p not in sys.path:
        sys.path.insert(0, p)
from arkauc import ArkaUc  # noqa: E402

G, Y = 1280, 640
MESAFE_ARALIK = (0.0, 4.0)        # ipucu uzaklığı renk aralığı (harita birimi; stella ilk ölçeği orta derinliği ~1 civarına koyar)
YORUNGE_ADIM = 0.002
HARITA_KLASORU = "/root/slam/cikti"


def _png_b64(a):
    ok, kod = cv2.imencode(".png", a, [cv2.IMWRITE_PNG_COMPRESSION, 3])
    return base64.b64encode(kod.tobytes()).decode("ascii")


def _yuvarla(a, n=4):
    return np.round(np.asarray(a, np.float64), n).ravel().tolist()


class Stella(ArkaUc):
    ad = "stella"
    yontem_adi = "stella_vslam (saf, tek kamera)"
    yontem_aciklama = "Klasik ORB tabanlı tek kamera SLAM (stella_vslam), eşdikdörtgen görüntüde. Sinir ağı yok, zemin bölütleme yok. Ölçek bilinmiyor: birimler keyfi."
    varsayilan_gorunum = "ham"
    TAM_ARALIK = 1.5

    def __init__(self, port=9760, bekleme=60):
        from slam_istemci import SlamIstemci
        from slam_kopru_surec import KopruSurec
        self.port = port
        self._kilit = threading.RLock()
        self.surec = None
        self.ist = None
        self._kapali = False
        if shutil.which("wsl") is None:
            raise RuntimeError("WSL bulunamadı (wsl.exe yok). stella köprüsü WSL Ubuntu-22.04 ister: tools/slam_deneme/kurulum.sh")
        try:
            self.surec = KopruSurec(port=port, bekleme=bekleme)
        except Exception as e:
            raise RuntimeError("stella köprüsü başlamadı (WSL Ubuntu-22.04, port %d). Port dolu olabilir ya da kurulum eksik (tools/slam_deneme/kurulum.sh). Ayrıntı: %s" % (port, e))
        try:
            self.ist = SlamIstemci(port=port)
        except Exception as e:
            self._surec_durdur()
            raise RuntimeError("stella köprüsüne bağlanılamadı: %s" % e)
        self.kam = self.ist.d.kam
        self.gorunumler = {"pano", "ham"}
        self.acik = set()
        self.kip = "harita"
        self._son_harita = self._son_harita_bul()
        self._hazirla_geometri()
        self._durum_sifirla()
        self._izgara_it = threading.Thread(target=self._izgara_dongu, name="stella_izgara", daemon=True)
        self._izgara_it.start()

    # ------------------------------------------------------------------ kurulum
    def _hazirla_geometri(self):
        kam = self.kam
        maske = self.ist.d.maske                      # 255 = stella'ya verilen (kullanılan) piksel, 0 = maskeli
        self._maske = maske
        # ham görünümde maskeli bölge: ham 320x180 örnek ızgarası -> ışın -> eşdikdörtgen piksel -> maske
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
        az = np.degrees(np.arctan2(d[..., 0], d[..., 2]))
        el = np.degrees(np.arcsin(np.clip(d[..., 1], -1, 1)))
        u = np.clip(((az + 180.0) / 360.0 * G).astype(int), 0, G - 1)
        v = np.clip(((90.0 - el) / 180.0 * Y).astype(int), 0, Y - 1)
        self._maske_ham = (maske[v, u] == 0) & ok
        self._maske_pano = cv2.resize((maske == 0).astype(np.uint8) * 255, (512, 256), interpolation=cv2.INTER_AREA) > 127

    def _durum_sifirla(self):
        self.kare_sayaci = 0
        self.poz = (0.0, 0.0, 0.0, 0.0)
        self.Twc = np.eye(4)
        self.tam = None
        self.tam_t = 0.0
        self.tam_iste = True
        self._tam_ms = 0.0
        self.lm = {}              # id -> (x, y, z, guven)
        self.kf = {}              # id -> (x, y, z, yon)
        self.lm_xyz = np.zeros((0, 3))            # stella dünya koordinatı (yeniden izdüşüm için)
        self.cevrim = 0
        self._yorunge_son = None
        self._temizle_gonder = True
        self._izgara_op = None
        self._izgara_t = 0.0

    def _surec_durdur(self):
        if self.surec is not None:
            try:
                self.surec.durdur()
            except Exception:
                pass
            self.surec = None

    def _son_harita_bul(self):
        try:
            r = subprocess.run(["wsl", "-d", "Ubuntu-22.04", "-u", "root", "--", "bash", "-c", "ls -t %s/harita_*.msg 2>/dev/null | head -1" % HARITA_KLASORU],
                               capture_output=True, text=True, timeout=20, env=dict(os.environ, MSYS_NO_PATHCONV="1"))
            s = r.stdout.strip().splitlines()
            return s[0].strip() if s else None
        except Exception:
            return None

    # ------------------------------------------------------------------ sözleşme
    def kamera_panelleri(self):
        return [{"id": "takip", "ad": "Takip edilen noktalar", "katmanlar": ["ipuclari", "maske"]},
                {"id": "mesafe", "ad": "Noktalar uzaklığa göre", "katmanlar": ["ipuclari_mesafe"]}]

    def guven_bilgisi(self):
        return {"ad": "Güven eşiği", "birim": "gözlem", "carpan": 5, "min": 1, "max": 5, "varsayilan": 2}

    def olcek_bilgisi(self):
        return {"birim": "birim", "metrik": False, "not": "ölçek bilinmiyor"}

    def katman_tanimlari(self):
        mes = {"ad": "İpucu uzaklığı", "birim": "birim", "aralik": list(MESAFE_ARALIK), "harita": "turbo"}
        return [
            {"id": "ipuclari", "ad": "İpuçları (haritaya eşleşen)", "uzay": ["pano", "ham"], "renk": "#46e08a", "varsayilan": True, "grup": "kamera", "ana": True},
            {"id": "ipuclari_mesafe", "ad": "İpuçları, uzaklığa göre renkli", "uzay": ["pano", "ham"], "renk": "#e0b84a", "varsayilan": True, "grup": "kamera", "ana": True, "lejant": mes},
            {"id": "maske", "ad": "Stella'ya verilen maske", "uzay": ["pano", "ham"], "renk": "#ff4d6d", "varsayilan": False, "grup": "kamera", "ana": True},
            {"id": "yorunge", "ad": "Yol", "uzay": ["harita"], "renk": "#ffd166", "varsayilan": True, "grup": "harita", "ana": True, "tur2d": "cizgi"},
            {"id": "landmark", "ad": "Nokta bulutu", "uzay": ["harita"], "renk": "#7fb2ff", "varsayilan": True, "grup": "harita", "ana": True, "tur2d": "kume"},
            {"id": "anahtar", "ad": "Anahtar kareler", "uzay": ["harita"], "renk": "#46e08a", "varsayilan": True, "grup": "harita", "ana": True, "tur2d": "elmas"},
            {"id": "doluluk", "ad": "Noktalardan ızgara (deneme)", "uzay": ["harita"], "renk": "#a9b7c6", "varsayilan": False, "grup": "harita", "ana": False, "tur2d": "izgara"},
            {"id": "yorunge3d", "ad": "Yol", "uzay": ["3d"], "tur3d": "cizgi", "renk": "#ffd166", "varsayilan": True, "grup": "3d", "ana": True},
            {"id": "kamera3d", "ad": "Güncel kamera", "uzay": ["3d"], "tur3d": "kameralar", "renk": "#ffb347", "varsayilan": True, "grup": "3d", "ana": True},
            {"id": "anahtar3d", "ad": "Anahtar kareler", "uzay": ["3d"], "tur3d": "kameralar", "renk": "#46e08a", "varsayilan": True, "grup": "3d", "ana": True},
            {"id": "landmark3d", "ad": "Nokta bulutu", "uzay": ["3d"], "tur3d": "kume", "kume": "landmark", "renk": "#7fb2ff", "varsayilan": True, "grup": "3d", "ana": True},
            {"id": "doluluk3d", "ad": "Izgara zemini", "uzay": ["3d"], "tur3d": "zemin", "renk": "#a9b7c6", "varsayilan": False, "grup": "3d", "ana": False},
        ]

    def statik_katmanlar(self):
        out = {"maske": []}
        rgba = np.zeros((256, 512, 4), np.uint8)
        rgba[self._maske_pano] = (109, 77, 255, 150)           # BGRA: #ff4d6d
        out["maske"].append({"tur": "resim", "uzay": "pano", "png": _png_b64(rgba)})
        rgba = np.zeros((180, 320, 4), np.uint8)
        rgba[self._maske_ham] = (109, 77, 255, 150)
        out["maske"].append({"tur": "resim", "uzay": "ham", "png": _png_b64(rgba)})
        return out

    def gorunumleri_ayarla(self, gorunumler):
        self.gorunumler = set(gorunumler) or {"pano", "ham"}

    def katman_istegi(self, acik):
        self.acik = set(acik)

    def panorama_goruntu(self, kare_bgr, genislik=1024):
        e = self.ist.d.kare(kare_bgr)                  # stella'nın gördüğü görüntünün aynısı
        if genislik != G:
            e = cv2.resize(e, (genislik, genislik // 2), interpolation=cv2.INTER_AREA)
        return e

    def eylemler(self):
        yuk = "Haritayı yükle" + (" (son kayıt: %s)" % os.path.basename(self._son_harita) if self._son_harita else " (kayıt yok)")
        return {"dugmeler": [{"id": "sifirla", "ad": "SLAM'i sıfırla"}, {"id": "kaydet", "ad": "Haritayı kaydet"}, {"id": "yukle", "ad": yuk}],
                "secimler": [{"id": "kip", "ad": "Kip", "deger": self.kip,
                              "secenekler": [{"id": "harita", "ad": "Haritalama"}, {"id": "konum", "ad": "Yalnız konumlama"}]}]}

    def eylem(self, kimlik, arg=None):
        if kimlik == "sifirla":
            return {"ok": True, "mesaj": "SLAM sıfırlanıyor", "sifirla": True}          # sunucu işleme iş parçacığında sifirla() çağırır
        with self._kilit:
            if kimlik == "kaydet":
                yol = "%s/harita_%s.msg" % (HARITA_KLASORU, time.strftime("%Y%m%d_%H%M%S"))
                r = self.ist.kaydet(yol)
                if r.get("ok"):
                    self._son_harita = yol
                    return {"ok": True, "mesaj": "Harita kaydedildi: " + yol}
                return {"ok": False, "mesaj": "Kaydedilemedi: %s" % (r.get("hata") or "bilinmeyen hata")}
            if kimlik == "yukle":
                yol = self._son_harita or self._son_harita_bul()
                if not yol:
                    return {"ok": False, "mesaj": "Yüklenecek kayıtlı harita yok (önce \"Haritayı kaydet\")"}
                r = self.ist.yukle(yol, self.kip)
                if not r.get("ok", True):
                    return {"ok": False, "mesaj": "Yüklenemedi: %s" % r.get("hata")}
                self._durum_sifirla()
                self.tam_iste = True
                return {"ok": True, "mesaj": "Harita yüklendi (%s kipi): %s" % (self.kip, os.path.basename(yol)), "dunya_sifirla": True}
            if kimlik == "kip":
                if arg not in ("harita", "konum"):
                    return {"ok": False, "mesaj": "bilinmeyen kip"}
                r = self.ist.mod(arg)
                self.kip = arg
                return {"ok": True, "mesaj": "Kip: " + ("haritalama (harita büyür)" if arg == "harita" else "yalnız konumlama (harita değişmez)")}
        return {"ok": False, "mesaj": "bilinmeyen eylem: %s" % kimlik}

    def sifirla(self):
        with self._kilit:
            self.ist.sifirla()
            self.kip = "harita"
            self._durum_sifirla()

    def kapat(self):
        if self._kapali:
            return
        self._kapali = True
        try:
            if self.ist is not None:
                self.ist.kapat(sunucuyu_durdur=True)
        except Exception:
            pass
        self._surec_durdur()

    # ------------------------------------------------------------------ ışın <-> görünüm
    def _px_ray(self, uv):
        """Eşdikdörtgen piksel (N,2) -> robot ışını (kamera.py: x sağ, y yukarı, z ileri)."""
        az = np.radians((uv[:, 0] + 0.5) / G * 360.0 - 180.0)
        el = np.radians(90.0 - (uv[:, 1] + 0.5) / Y * 180.0)
        return np.stack([np.cos(el) * np.sin(az), np.sin(el), np.cos(el) * np.cos(az)], 1)

    def _ray_ham(self, d):
        kam = self.kam
        u0, v0, t0 = kam.robot_to_lens(d, 0)
        u1, v1, t1 = kam.robot_to_lens(d, 1)
        l1 = t1 < t0
        u = np.where(l1, u1, u0)
        v = np.where(l1, v1, v0)
        ok = np.where(l1, t1, t0) * np.where(l1, kam.lens[1]["f"], kam.lens[0]["f"]) <= kam.daire_r
        return np.stack([u / 1280.0, v / 720.0], 1), ok

    @staticmethod
    def _cam_px(Xc):
        """Kamera çerçevesi (x sağ, y aşağı, z ileri) -> eşdikdörtgen piksel (u, v)."""
        az = np.arctan2(Xc[:, 0], Xc[:, 2])
        el = np.arctan2(-Xc[:, 1], np.hypot(Xc[:, 0], Xc[:, 2]))
        return np.stack([(az + np.pi) / (2 * np.pi) * G, (np.pi / 2 - el) / np.pi * Y], 1)

    # ------------------------------------------------------------------ ana giriş
    def feed(self, kare_jpeg, t_ms, komut):
        T = {}
        t0 = time.perf_counter()
        simdi = time.time()
        tam = self.tam_iste or (simdi - self.tam_t >= self.TAM_ARALIK)
        with self._kilit:
            y = self.ist.feed(kare_jpeg, t_ms, (float(komut[0]), float(komut[1])), tam_durum=tam)
        t1 = time.perf_counter()
        self.kare_sayaci += 1
        sy = y.get("sureler_ms", {})
        durum_ad = y.get("durum", "Initializing")
        ops = []
        if self._temizle_gonder:
            self._temizle_gonder = False
            ops.append({"islem": "temizle"})
        izleniyor = durum_ad == "Tracking" and y.get("poz_cw")
        if izleniyor:
            Tm = np.array(y["poz_cw"], np.float64).reshape(4, 4)          # köprü: kamera pozu (T_wc); bkz. modül notu
            self.Twc = Tm
            c, fwd = Tm[:3, 3], Tm[:3, 2]
            self.poz = (float(c[0]), float(c[2]), float(-c[1]), float(np.degrees(np.arctan2(fwd[0], fwd[2])) % 360.0))
        if tam and "noktalar" in y:
            self.tam_t = simdi
            self.tam_iste = False
            self._tam_ms = (time.perf_counter() - t0) * 1000.0
            ops += self._tam_islemleri(y)
        # yörünge
        if izleniyor:
            p = np.array(self.poz[:3])
            if self._yorunge_son is None or np.linalg.norm(p - self._yorunge_son) >= YORUNGE_ADIM:
                self._yorunge_son = p
                ops.append({"katman": "yorunge", "islem": "ekle", "xy": [round(self.poz[0], 5), round(self.poz[1], 5)]})
                ops.append({"katman": "yorunge3d", "islem": "ekle", "xyz": [round(self.poz[0], 5), round(self.poz[1], 5), round(self.poz[2], 5)]})
        if self._izgara_op is not None:
            ops.append(self._izgara_op)
            self._izgara_op = None
        t2 = time.perf_counter()
        kat = self._katmanlar(y, izleniyor)
        t3 = time.perf_counter()
        T["donusum"] = sy.get("donusum", 0.0)
        T["takip"] = sy.get("takip", 0.0)
        T["kopru"] = max(0.0, sy.get("uctan_uca", 0.0) - sy.get("donusum", 0.0) - sy.get("toplam_sunucu", 0.0))
        T["katman"] = (t3 - t2) * 1000.0
        T["cikarim"] = sy.get("cikarim", 0.0)
        T["uctan_uca"] = sy.get("uctan_uca", 0.0)
        etiket, iz = {"Initializing": ("başlatılıyor", "zayif"), "Tracking": ("izliyor", "iyi"), "Lost": ("kayıp", "kayip")}.get(durum_ad, (durum_ad, "zayif"))
        uyari = []
        if durum_ad == "Initializing":
            uyari.append("Başlatılıyor: robot hareket etmeli (başlatma için paralaks gerekir). Takılırsa \"Sıfırla\".")
        elif durum_ad == "Lost":
            uyari.append("İzleme kayıp: stella yeniden konumlanmaya çalışıyor. Olmazsa \"Sıfırla\".")
        n_lm = int(y.get("harita_nokta_sayisi", 0))
        n_kf = int(y.get("anahtar_kare_sayisi", 0))
        durum = {
            "kare": self.kare_sayaci, "t_ms": int(t_ms),
            "poz": {"x": self.poz[0], "y": self.poz[1], "z": self.poz[2], "yon": self.poz[3]},
            "katmanlar": kat, "harita_islem": ops,
            "sure_ms": {k: round(v, 2) for k, v in T.items()},
            "izleme": {"durum": iz, "etiket": etiket, "metin": "stella durumu: %s" % durum_ad},
            "ozet": [{"ad": "Anahtar kare", "deger": n_kf}, {"ad": "Nokta", "deger": n_lm}, {"ad": "Eşleşen", "deger": int(y.get("eslesen_sayisi", 0))},
                     {"ad": "Çevrim kapama*", "deger": self.cevrim}, {"ad": "Köprü gecikmesi", "deger": "%d ms" % round(sy.get("uctan_uca", 0.0))}],
            "bilgi": {
                "asama_adlari": {"donusum": "Eşdikdörtgen dönüşümü", "takip": "stella takip", "kopru": "Köprü iletimi", "katman": "Katman"},
                "esz_asamalar": [], "alt_asamalar": {"cikarim": "takip içinde özellik çıkarma", "uctan_uca": "istemciden köprüye ve geri toplam"},
                "profil": [],
                "ek": [{"ad": "stella durumu", "deger": durum_ad}, {"ad": "kip", "deger": self.kip}, {"ad": "karedeki ORB noktası", "deger": int(y.get("nokta_sayisi", 0))},
                       {"ad": "haritaya eşleşen", "deger": int(y.get("eslesen_sayisi", 0))}, {"ad": "yerel harita noktası", "deger": int(y.get("yerel_nokta_sayisi", 0))},
                       {"ad": "tam durum alma ms", "deger": round(self._tam_ms)}, {"ad": "çevrim kapama (tahmini)", "deger": self.cevrim}],
            },
            "uyari": uyari,
        }
        return durum

    # ------------------------------------------------------------------ tam durum: nokta bulutu, anahtar kareler, çevrim kapama
    def _tam_islemleri(self, y):
        ops = []
        P = np.array(y["noktalar"], np.float64).reshape(-1, 5)
        KF = y.get("anahtar_kareler", [])
        # güven: noktayı gözlemleyen anahtar kare sayısı (5 gözlem = 1,0)
        sayim = {}
        gozlem = [np.asarray(k.get("gozlem", []), np.int64) for k in KF]
        if gozlem:
            tum = np.concatenate(gozlem) if gozlem else np.zeros(0, np.int64)
            if len(tum):
                u, c = np.unique(tum, return_counts=True)
                sayim = dict(zip(u.tolist(), c.tolist()))
        ids = P[:, 4].astype(np.int64)
        self.lm_xyz = P[:, :3].copy()
        yeni = {}
        for i, kid in enumerate(ids.tolist()):
            yeni[kid] = (round(P[i, 0], 5), round(P[i, 2], 5), round(-P[i, 1], 5), round(min(1.0, sayim.get(kid, 1) / 5.0), 2))
        degisen = [kid for kid, v in yeni.items() if self.lm.get(kid) != v]
        silinen = [kid for kid in self.lm if kid not in yeni]
        if degisen or silinen:
            op = {"katman": "landmark", "islem": "guncelle", "kimlik": degisen}
            if degisen:
                op["xyz"] = [c for kid in degisen for c in yeni[kid][:3]]
                op["guven"] = [yeni[kid][3] for kid in degisen]
            if silinen:
                op["sil"] = silinen
            ops.append(op)
        self.lm = yeni
        # anahtar kareler
        kfy = {}
        for k in KF:
            W = np.array(k["poz_wc"], np.float64).reshape(4, 4)
            fwd = W[:3, 2]
            kfy[int(k["id"])] = (round(W[0, 3], 5), round(W[2, 3], 5), round(-W[1, 3], 5), round(float(np.degrees(np.arctan2(fwd[0], fwd[2])) % 360.0), 2))
        # tahmini çevrim kapama: ortak anahtar karelerin topluca kayması
        ortak = [i for i in kfy if i in self.kf]
        cevrim = False
        if len(ortak) >= 8:
            a = np.array([kfy[i][:3] for i in ortak])
            b = np.array([self.kf[i][:3] for i in ortak])
            ext = float(np.max(np.linalg.norm(a - a[0], axis=1))) + 1e-6
            if float(np.max(np.linalg.norm(a - b, axis=1))) > 0.08 * ext:
                cevrim = True
                self.cevrim += 1
        kdeg = [i for i, v in kfy.items() if self.kf.get(i) != v]
        ksil = [i for i in self.kf if i not in kfy]
        if kdeg or ksil:
            o2 = {"katman": "anahtar", "islem": "guncelle", "kimlik": kdeg, "xyz": [c for i in kdeg for c in kfy[i][:3]]}
            o3 = {"katman": "anahtar3d", "islem": "guncelle", "kimlik": kdeg, "kameralar": [{"x": kfy[i][0], "y": kfy[i][1], "z": kfy[i][2], "yon": kfy[i][3]} for i in kdeg]}
            if ksil:
                o2["sil"] = ksil
                o3["sil"] = ksil
            ops += [o2, o3]
        self.kf = kfy
        if cevrim and kfy:                       # yörüngeyi anahtar karelerden yeniden kur (geçmiş kaymış olabilir)
            ops += [{"katman": "yorunge", "islem": "temizle_katman"}, {"katman": "yorunge3d", "islem": "temizle_katman"}]
            sira = sorted(kfy)
            ops.append({"katman": "yorunge", "islem": "ekle", "xy": [c for i in sira for c in kfy[i][:2]]})
            ops.append({"katman": "yorunge3d", "islem": "ekle", "xyz": [c for i in sira for c in kfy[i][:3]]})
            self._yorunge_son = None
        self.tam = y
        return ops

    # ------------------------------------------------------------------ kare üstü katmanlar
    def _katmanlar(self, y, izleniyor):
        kat = {}
        pano = "pano" in self.gorunumler
        ham = "ham" in self.gorunumler
        ip = np.array(y.get("ipuclari") or [], np.float64).reshape(-1, 2)
        pi, hi = [], []
        if len(ip):
            if pano:
                pi.append({"tur": "noktalar", "uzay": "pano", "xy": _yuvarla(np.c_[ip[:, 0] / G, ip[:, 1] / Y], 4)})
            if ham:
                uv, ok = self._ray_ham(self._px_ray(ip))
                pi.append({"tur": "noktalar", "uzay": "ham", "xy": _yuvarla(uv[ok], 4)})
        kat["ipuclari"] = pi
        # ipucu uzaklığı: haritadaki noktaları güncel pozla eşdikdörtgene izdüşür, en yakın nokta (<=3 px) o ipucunun noktası sayılır
        md = []
        if len(ip) and izleniyor and len(self.lm_xyz):
            R, c = self.Twc[:3, :3], self.Twc[:3, 3]
            Xc = (self.lm_xyz - c) @ R               # R^T (X - c)
            uv = self._cam_px(Xc)
            from scipy.spatial import cKDTree
            dist, j = cKDTree(uv).query(ip, distance_upper_bound=3.0)
            iyi = np.isfinite(dist)
            if iyi.any():
                derin = np.linalg.norm(Xc[j[iyi]], axis=1)
                ipi = ip[iyi]
                if pano:
                    md.append({"tur": "noktalar", "uzay": "pano", "xy": _yuvarla(np.c_[ipi[:, 0] / G, ipi[:, 1] / Y], 4), "deger": _yuvarla(derin, 3), "boyut": 5})
                if ham:
                    uvh, ok = self._ray_ham(self._px_ray(ipi))
                    md.append({"tur": "noktalar", "uzay": "ham", "xy": _yuvarla(uvh[ok], 4), "deger": _yuvarla(derin[ok], 3), "boyut": 5})
        kat["ipuclari_mesafe"] = md
        kat["kamera3d"] = [{"tur": "kamera", "uzay": "3d", "x": round(self.poz[0], 5), "y": round(self.poz[1], 5), "z": round(self.poz[2], 5), "yon": round(self.poz[3], 2), "guncel": True}]
        return kat

    # ------------------------------------------------------------------ noktalardan 2B ızgara (isteğe bağlı, deneme)
    def _izgara_dongu(self):
        while not self._kapali:
            time.sleep(0.5)
            if "doluluk" not in self.acik or self.tam is None or time.time() - self._izgara_t < 4.0 or self._izgara_op is not None:
                continue
            try:
                from slam_izgara import izgara_hesapla, durum_izgarasi
                P = np.array(self.tam["noktalar"], np.float64).reshape(-1, 5)
                if len(P) < 20:
                    continue
                span = float(np.ptp(P[:, [0, 2]], axis=0).max())
                # birim keyfi: olcek 1,0 ("1 birim = 1 m" varsayımı YOK, yalnız sayısal kolaylık); hücre ~ kapsamın 1/80'i
                g = izgara_hesapla(self.tam, 1.0, hucre_m=max(span / 80.0, 0.01), yukseklik_bandi=(0.02 * span, 0.25 * span), kamera_yuksekligi_m=0.05 * span,
                                   max_menzil_m=span * 2, kenar_m=0.05 * span)
                self._izgara_t = time.time()
                if g is None:
                    continue
                st = durum_izgarasi(g)
                gri = np.where(st == 1, 255, np.where(st == 2, 0, 128)).astype(np.uint8)[::-1]
                conf = np.clip(np.maximum(g["dolu"], g["bos"]) * 51, 0, 255).astype(np.uint8)[::-1]
                H, W = st.shape
                self._izgara_op = {"katman": "doluluk", "islem": "yerlestir", "x0": round(g["origin_x_m"], 4), "y0": round(g["origin_z_m"], 4), "hucre": g["hucre_m"],
                                   "w": int(W), "h": int(H), "png": _png_b64(gri), "guven_png": _png_b64(conf)}
            except Exception as e:      # ızgara denemesi akışı bozmasın
                self._izgara_t = time.time()
                print("stella ızgara hatası:", repr(e), flush=True)
