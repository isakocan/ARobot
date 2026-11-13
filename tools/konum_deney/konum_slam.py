"""Kamera tabanlı konum + harita döngüsü (tahmin -> haritaya eşle -> haritayı güncelle). Dört varyant:

  A  komut+M2D      : yaw = M2D (komutlu kip), ilerleme = komuttan nominal hız. Harita geri beslemesi YOK (taban çizgisi).
  B  A + harita     : A'nın tahmini, her taramada haritaya eşlenip düzeltilir (korelatif tarama eşleştirme), sonra harita güncellenir.
  C  yalnız kamera  : yaw = M2D (komutsuz kip), ilerleme = zemin BEV özelliklerinden metrik öteleme (konum_bev.py). Komut KULLANILMAZ.
  D  C + harita     : C'nin tahmini + tarama-harita düzeltmesi (yalnız kamera, tam döngü).
Hepsinde başlangıç pozu doğru verilir. Harita her varyantta o varyantın KENDİ pozlarıyla kurulur (A ve C'de düzeltme yok = açık döngü haritası).

Hızlar: M2D ve BEV her kare (15 Hz); zemin taraması ve harita düzeltmesi her 5. karede (3 Hz).
Zemin taramaları (SegFormer-B0) önbellekten okunur (kayitlar/sonuclar/harita/tarama/model_gorunum): canlı sistemde GPU'da ~300 ms/tarama öder;
bu çevrimdışı deneyde tarama gecikmesi (düzeltmenin 0,3 sn geç gelmesi) MODELLENMEDİ.
Poz = kameranın zemin izdüşümü (gercek_konum.csv ile aynı), yaw kuzeyden saat yönünde derece. Ayrıntı: konum_ortak.py."""
import os
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import konum_ortak as ko  # noqa: E402
import konum_yon_onbellek as yo  # noqa: E402
import konum_bev as kb  # noqa: E402
import konum_tarama as kt  # noqa: E402
import harita_kur as hk  # noqa: E402
from kayit import Kayit  # noqa: E402

VARYANTLAR = {
    "A": dict(odom="komut", yon_komutlu=True, duzelt=False),
    "B": dict(odom="komut", yon_komutlu=True, duzelt=True),
    "C": dict(odom="bev", yon_komutlu=False, duzelt=False),
    "D": dict(odom="bev", yon_komutlu=False, duzelt=True),
}
TARAMA_ADIMI = 5


def tarama_yukle(tur, ad):
    z = np.load(os.path.join(ko.zo.CIKTI, "tarama", "model_gorunum", tur, ad + ".npz"))
    return {k: z[k] for k in z.files}


def buyuk_izgara(klasor_sim, kenar_m=10.0):
    """Gerçek harita ızgarasıyla AYNI hücre örgüsünde, her yöne kenar_m kadar geniş izgara (kırpma kolay olsun).
    DÜRÜSTLÜK: gerçek haritanın kapsamı yalnızca bu izgaranın yeri/boyu için kullanılır, içeriği tahmine girmez."""
    import json
    with open(os.path.join(klasor_sim, "gercek_harita.json"), encoding="utf-8") as f:
        b = json.load(f)
    h = b.get("hucre_m", 0.05)
    k = int(round(kenar_m / h))
    iz = hk.Izgara(b["x0_m"] - k * h, b["y0_m"] - k * h, b["genislik"] + 2 * k, b["yukseklik"] + 2 * k, h)
    iz.kirp = (k, int(b["genislik"]), int(b["yukseklik"]))
    return iz


def izgara_kirp(iz, harita_uint8=None):
    """Büyük izgaranın gerçek haritayla aynı pencereye kırpılmış durum haritası (0/255/128)."""
    k, W, Hh = iz.kirp
    D = iz.durum() if harita_uint8 is None else harita_uint8
    r1 = iz.Hh - k
    return D[r1 - Hh:r1, k:k + W]


class Kosucu:
    def __init__(self, ad, tur, varyant, par=None, guven_min=0.2, bev_olcek=1.0):
        self.bev_olcek = float(bev_olcek)      # DUYARLILIK DENEYİ: kamera yüksekliği yanlış ölçülürse BEV ölçeği bu kadar yanlış olur
        self.ad, self.tur = ad, tur
        self.v = VARYANTLAR[varyant]
        self.vad = varyant
        self.par = par or kt.EsleParam()
        self.guven_min = guven_min
        self.kay = Kayit(os.path.join(ko.KAYITLAR, tur, ad))
        self.bilgi = self.kay.bilgi
        self.gk = self.kay.gercek_konum()
        self.yz = yo.yukle(self.v["yon_komutlu"], tur, ad)
        self.bz = kb.bev_hesapla(tur, ad, self.v["yon_komutlu"]) if self.v["odom"] == "bev" else None
        self.tar = tarama_yukle(tur, ad)
        self.tar_sira = {int(k): i for i, k in enumerate(self.tar["kare"])}
        self.menzil = float(self.tar["max_menzil"])
        self.sim_klasor = os.path.join(ko.KAYITLAR, "sim", ad)
        self.n = len(self.kay)

    def kos(self, ilerleme=None):
        kay, gk, yz = self.kay, self.gk, self.yz
        n = self.n
        t_ms = kay.t_ms
        yaw0 = float(gk["yon_derece"][0]) - float(yz["yon"][0])
        poz = (float(gk["x_m"][0]), float(gk["y_m"][0]), float(gk["yon_derece"][0]))
        iz = buyuk_izgara(self.sim_klasor)
        iz_e = buyuk_izgara(self.sim_klasor) if self.v["duzelt"] else None      # eşleştirme izgarası: GECİKMELİ harita
        kuyruk = []
        P = np.zeros((n, 3))
        P[0] = poz
        yol_son_iyi = 0.0                  # son kabul edilen düzeltmeden bu yana gidilen yol (m), pencere büyütmek için
        log = {"esle_ms": [], "harita_ms": [], "kabul": 0, "deneme": 0, "kare": [], "durum": [], "d": [], "ort_f": [], "ayirt": [],
               "pencere": []}
        hiz_tam, _ = ko.nominal_hizlar(self.bilgi)
        for i in range(n):
            if i > 0:
                dt = (t_ms[i] - t_ms[i - 1]) / 1000.0
                dyaw = float(ko.sar(yz["yon"][i] - yz["yon"][i - 1]))
                if self.v["odom"] == "komut":
                    s0 = kay.komut(int(t_ms[i - 1]))
                    s1 = kay.komut(int(t_ms[i]))
                    sol, sag = (s0[0] + s1[0]) / 2.0, (s0[1] + s1[1]) / 2.0
                    v = hiz_tam * (sol + sag) / 2.0 / 255.0
                    poz = ko.merkez_ilerle(poz, v * dt, dyaw)
                    adim = abs(v) * dt
                else:
                    ds, di = self.bz["d"][i]
                    if not np.isfinite(ds):
                        ds = di = 0.0
                    ds, di = ds * self.bev_olcek, di * self.bev_olcek
                    poz = ko.poz_ekle(poz, float(ds), float(di), dyaw)
                    adim = float(np.hypot(ds, di))
                yol_son_iyi += adim
            # ---- tarama (3 Hz): düzelt, sonra haritayı güncelle
            j = self.tar_sira.get(int(kay.kare_numaralari[i]))
            if j is not None:
                mes, gv = self.tar["mesafe"][j], self.tar["guven"][j]
                if self.v["duzelt"] and i > 0:
                    a = time.perf_counter()
                    pen, pey = self.par.ay_xy, self.par.ay_yaw
                    s = kt.eslestir(iz_e, poz, mes, gv, self.menzil, self.par, ay_xy=pen, ay_yaw=pey)
                    log["esle_ms"].append((time.perf_counter() - a) * 1000.0)
                    log["deneme"] += 1
                    log["kare"].append(i)
                    log["pencere"].append(pen)
                    log["durum"].append(s["durum"])
                    if s["durum"] == "ok":
                        log["d"].append(s["d"])
                        log["ort_f"].append(s["ort_f"])
                        log["ayirt"].append(s["ayirt"])
                    else:
                        log["d"].append((np.nan,) * 3)
                        log["ort_f"].append(np.nan)
                        log["ayirt"].append(np.nan)
                    if kt.kabul(s, self.par):
                        poz = kt.karistir(poz, s, self.par)
                        yol_son_iyi = 0.0
                        log["kabul"] += 1
                a = time.perf_counter()
                iz.tara_ekle(poz[0], poz[1], poz[2], mes, gv, self.menzil, guven_min=self.guven_min)
                if iz_e is not None:
                    kuyruk.append((poz, mes, gv))
                    while len(kuyruk) > self.par.gecikme_tarama:
                        q = kuyruk.pop(0)
                        iz_e.tara_ekle(q[0][0], q[0][1], q[0][2], q[1], q[2], self.menzil, guven_min=self.guven_min)
                log["harita_ms"].append((time.perf_counter() - a) * 1000.0)
            P[i] = poz
            if ilerleme and i % 300 == 0:
                ilerleme(i, n)
        log["esle_ms"] = np.array(log["esle_ms"])
        log["harita_ms"] = np.array(log["harita_ms"])
        return P, iz, log


def tahmin_yaz(yol, kare, t_ms, P):
    os.makedirs(os.path.dirname(yol), exist_ok=True)
    with open(yol, "w", encoding="utf-8", newline="") as f:
        f.write("kare,t_ms,x_m,y_m,yon_derece\n")
        for k, t, p in zip(kare, t_ms, P):
            f.write("%d,%d,%.4f,%.4f,%.3f\n" % (k, t, p[0], p[1], p[2] % 360.0))


def tahmin_yol(varyant, tur, ad):
    return os.path.join(ko.TAHMIN, "%s_%s_%s.csv" % (varyant, tur, ad))
