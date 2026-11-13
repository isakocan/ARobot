"""Sim kaydını (eşdikdörtgen) gerçek Theta S kamerasından gelmiş gibi gösteren yeni kayıt yazar.

Adımlar (her biri bayrakla kapatılabilir; seçilen güçler yeni bilgi.json'a yazılır):
  a) çift balık gözüne yeniden izdüşüm (gerçek mercek ölçüleri, daire dışı karanlık halka)   [her zaman]
  b) robot gövdesi maskesi (kamera_maske.json; YAKLAŞIK, bkz. maske_uret.py)             --govde / --govde_yok
  c) Gauss bulanıklığı (gerçek keskinliğe uysun)                                           --sigma (0 = kapalı)
  d) hareket bulanıklığı (ardışık gerçek konumlardan açısal hız)                          --hareket_bulaniklik
  e) sensör gürültüsü                                                                      --gurultu_yok
  f) JPEG yeniden kodlama (kare boyutu gerçek aralığa)                                     --jpeg_kalite / --jpeg_hedef_kb
komutlar.csv ve gercek_* dosyaları olduğu gibi kopyalanır. kamera.tur = cift_balikgozu, 1280x720.

Kullanım:
  python tools/konum_deney/theta_benzet.py <sim_kayit> <cikti_kayit> [--varyant A|B] [--max_kare N] ...
  python tools/konum_deney/theta_benzet.py --sigma_tahmin <gercek_kare.jpg> <sim_esdikdortgen.png>   (keskinlik karşılaştırması)"""
import argparse
import json
import os
import sys

import cv2
import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kamera import Kamera, KLASOR  # noqa: E402
from kayit import Kayit, KayitYazici  # noqa: E402

# Gerçek kare (kare.jpg) ile sim karesinin kenar genişliği karşılaştırmasından (BENIOKU.md, "keskinlik"):
VARSAYILAN_SIGMA = 0.5
VARSAYILAN_POZLAMA_MS = 16.0
GURULTU_A = 0.7        # okuma gürültüsü (gri seviye)
GURULTU_B = 0.014      # ışık payı: sd = sqrt(A^2 + B*I); 10 statik kareden ölçüldü (koyu 0.85, orta 1.7, parlak 1.9)
HEDEF_KB = 250


# ------------------------------------------------------------------ keskinlik ölçümü
def kenar_genisligi(gri, maske=None, en_az_kontrast=60.0):
    """Güçlü kenarlarda (10-90 yerine) 'kontrast / en büyük eğim' genişliği (piksel); ortanca ve kenar sayısı.
    İdeal bir basamak kenarı sigma ile bulanırsa bu genişlik sigma * sqrt(2*pi) olur."""
    g = gri.astype(np.float32)
    gx = cv2.Sobel(cv2.GaussianBlur(g, (0, 0), 0.7), cv2.CV_32F, 1, 0, ksize=3) / 8.0
    gy = cv2.Sobel(cv2.GaussianBlur(g, (0, 0), 0.7), cv2.CV_32F, 0, 1, ksize=3) / 8.0
    mag = np.hypot(gx, gy)
    yerel_max = cv2.dilate(mag, np.ones((5, 5), np.uint8))
    kontrast = cv2.dilate(g, np.ones((9, 9), np.uint8)) - cv2.erode(g, np.ones((9, 9), np.uint8))
    aday = (mag >= yerel_max) & (mag > 6.0) & (kontrast > en_az_kontrast)
    if maske is not None:
        aday &= maske
    w = kontrast[aday] / np.maximum(mag[aday], 1e-3)
    if len(w) == 0:
        return float("nan"), 0
    return float(np.median(w)), int(len(w))


def _sigma_esdegeri(genislik):
    """Ölçülen kenar genişliğini 'aynı genişliği veren Gauss sigması'na çevirir: kenar_genisligi'ni sentetik
    basamak kenarlarında (bilinen sigma) ölçüp tablo çıkarır."""
    im = np.zeros((300, 600), np.uint8)
    for i in range(8):
        cv2.rectangle(im, (40 + i * 65, 30 + i * 10), (80 + i * 65, 250), 255, -1)
    sigmalar = np.array([0.0, 0.3, 0.6, 0.9, 1.2, 1.5, 2.0, 2.5, 3.0])
    gen = np.array([kenar_genisligi(cv2.GaussianBlur(im, (0, 0), s) if s > 0 else im, None, 100.0)[0] for s in sigmalar])
    return float(np.interp(genislik, gen, sigmalar))


def sigma_tahmin(gercek_yol, sim_yol, kam=None, kontrast=100.0):
    """Gerçek çift balık gözü kare ile sim eşdikdörtgen karesinin (aynı yöntemle balık gözüne çevrilmiş) güçlü
    kenarlarını karşılaştırır. Dönüş: (w_gercek, n, w_sim, n, sigma_gercek, sigma_sim, eklenecek_sigma)."""
    kam = kam or Kamera()
    gercek = cv2.imread(gercek_yol, cv2.IMREAD_GRAYSCALE)
    sim = cv2.imread(sim_yol)
    sim_b, daire = kam.esdikdortgen_to_balikgozu(sim)
    sim_g = cv2.cvtColor(sim_b, cv2.COLOR_BGR2GRAY)
    iceri = cv2.erode(daire.astype(np.uint8), np.ones((25, 25), np.uint8)) > 0       # dairenin kenar halkasını at
    ms = govde_maskesi(kam) > 0
    iceri &= ~cv2.dilate(ms.astype(np.uint8), np.ones((15, 15), np.uint8)).astype(bool)   # gövde kenarı ayrı
    wg, ng = kenar_genisligi(gercek, iceri, kontrast)
    ws, ns = kenar_genisligi(sim_g, iceri, kontrast)
    sg, ss = _sigma_esdegeri(wg), _sigma_esdegeri(ws)
    return wg, ng, ws, ns, sg, ss, float(np.sqrt(max(sg * sg - ss * ss, 0.0)))


# ------------------------------------------------------------------ gövde maskesi
_MASKE_ONBELLEK = {}


def govde_maskesi(kam=None, yol=None):
    """kamera_maske.png (255 = gövde) ya da None."""
    yol = yol or os.path.join(KLASOR, "kamera_maske.png")
    if yol not in _MASKE_ONBELLEK:
        m = cv2.imread(yol, cv2.IMREAD_GRAYSCALE)
        _MASKE_ONBELLEK[yol] = m if m is not None else np.zeros((720, 1280), np.uint8)
    return _MASKE_ONBELLEK[yol]


def govde_uygula(balik, maske, renk, rng):
    """Maskeli alanı koyu gövde rengiyle doldurur (kenarda 2 piksellik yumuşak geçiş, hafif doku)."""
    yum = cv2.GaussianBlur(maske.astype(np.float32) / 255.0, (0, 0), 1.2)[..., None]
    doku = np.array(renk, np.float32) + rng.standard_normal(balik.shape[:2], dtype=np.float32)[..., None] * 1.5
    return (balik.astype(np.float32) * (1 - yum) + doku * yum).clip(0, 255).astype(np.uint8)


# ------------------------------------------------------------------ hareket bulanıklığı
def yaw_bulaniklik(esd, aci_derece, adim=None):
    """Eşdikdörtgeni dikey eksen etrafında aci_derece genişliğinde süpürerek ortalar (kare başına tam yaw bulanıklığı:
    dikey eksen dönüşü eşdikdörtgende yatay kaymadır). Dönüş: bulanık görüntü."""
    G = esd.shape[1]
    px = abs(aci_derece) / 360.0 * G
    if px < 0.3:
        return esd
    n = adim or int(min(25, max(3, np.ceil(px / 1.5) + 1)))
    f = esd.astype(np.float32)
    toplam = np.zeros_like(f)
    for k in np.linspace(-0.5, 0.5, n):
        kay = k * px
        a = int(np.floor(kay))
        r = kay - a
        toplam += (1 - r) * np.roll(f, a, axis=1) + r * np.roll(f, a + 1, axis=1)
    return (toplam / n).clip(0, 255).astype(np.uint8)


def acisal_hizlar(gk, t_ms):
    """Kare başına yaw hızı (derece/sn): komşu kareler arası merkezi fark, 0/360 sarması doğru."""
    n = len(t_ms)
    w = np.zeros(n)
    if gk is None or n < 2:
        return w
    yon = gk["yon_derece"]
    t = np.asarray(t_ms, float) / 1000.0
    for i in range(n):
        a, b = max(i - 1, 0), min(i + 1, n - 1)
        d = (yon[b] - yon[a] + 180.0) % 360.0 - 180.0
        w[i] = d / max(t[b] - t[a], 1e-3)
    return w


# ------------------------------------------------------------------ gürültü ve JPEG
def gurultu_ekle(resim, rng, a=GURULTU_A, b=GURULTU_B):
    f = resim.astype(np.float32)
    sd = np.sqrt(a * a + b * f.mean(axis=2, keepdims=True))
    return (f + rng.standard_normal(f.shape, dtype=np.float32) * sd).clip(0, 255).astype(np.uint8)


def jpeg_kodla(resim, kalite):
    return cv2.imencode(".jpg", resim, [cv2.IMWRITE_JPEG_QUALITY, int(kalite)])[1].tobytes()


def jpeg_kalite_bul(resim, hedef_kb=HEDEF_KB):
    """Hedef boyuta en yakın JPEG kalitesi (ikili arama, 50-100)."""
    lo, hi = 50, 100
    while lo < hi:
        orta = (lo + hi + 1) // 2
        if len(jpeg_kodla(resim, orta)) / 1024.0 <= hedef_kb:
            lo = orta
        else:
            hi = orta - 1
    return lo


# ------------------------------------------------------------------ ana akış
def benzet(kaynak, cikti, varyant="A", max_kare=None, govde=True, sigma=VARSAYILAN_SIGMA, hareket=True,
           pozlama_ms=VARSAYILAN_POZLAMA_MS, gurultu=True, jpeg_kalite=0, hedef_kb=HEDEF_KB, tohum=0, sessiz=False):
    kay = Kayit(kaynak)
    if kay.bilgi.get("kamera", {}).get("tur") != "esdikdortgen":
        raise SystemExit("kaynak kayıt eşdikdörtgen olmalı (kamera.tur)")
    kam = Kamera()
    rng = np.random.default_rng(tohum)
    maske = govde_maskesi(kam) if govde else None
    renk = json.load(open(os.path.join(KLASOR, "kamera_maske.json"), encoding="utf-8"))["govde_rengi_bgr"] if govde else None
    gk = kay.gercek_konum()
    t_hepsi = kay.t_ms
    w_hepsi = acisal_hizlar(gk, t_hepsi) if (hareket and gk is not None) else np.zeros(len(kay))
    # gk kare numarasına göre; hizalama kare sırasıyla (kare numarası = sıra varsayımı doğrulanır)
    if hareket and gk is not None and len(gk["t_ms"]) != len(kay):
        w_hepsi = np.interp(t_hepsi, gk["t_ms"], acisal_hizlar(gk, gk["t_ms"]))
    bilgi = dict(kay.bilgi)
    bilgi["kaynak"] = kay.bilgi.get("kaynak", "sim")
    bilgi["ad"] = kay.bilgi.get("ad", "kayit") + "_theta"
    bilgi["kamera"] = {"tur": "cift_balikgozu", "genislik": kam.G, "yukseklik": kam.Y,
                       "fps": kay.bilgi.get("kamera", {}).get("fps", 15), "parametre_dosyasi": os.path.join(KLASOR, "kamera.json")}
    bilgi["theta_benzetim"] = {"govde_maskesi": bool(govde), "sigma_px": sigma, "hareket_bulaniklik": bool(hareket),
                               "pozlama_ms": pozlama_ms, "gurultu": bool(gurultu), "gurultu_a": GURULTU_A, "gurultu_b": GURULTU_B,
                               "jpeg_kalite": None, "jpeg_hedef_kb": hedef_kb, "tohum": tohum, "kaynak_kayit": os.path.abspath(kaynak)}
    kalite = jpeg_kalite or None
    boyutlar = []
    yz = KayitYazici(cikti, bilgi, varyant)
    for sira in range(len(kay) if not max_kare else min(max_kare, len(kay))):
        esd = kay.resim(sira)
        if hareket and w_hepsi[sira] != 0:
            esd = yaw_bulaniklik(esd, w_hepsi[sira] * pozlama_ms / 1000.0)
        balik, _ = kam.esdikdortgen_to_balikgozu(esd, cv2.INTER_CUBIC)
        if govde:
            balik = govde_uygula(balik, maske, renk, rng)
        if sigma > 0:
            balik = cv2.GaussianBlur(balik, (0, 0), sigma)
        if gurultu:
            balik = gurultu_ekle(balik, rng)
        if kalite is None:
            kalite = jpeg_kalite_bul(balik, hedef_kb)
            bilgi["theta_benzetim"]["jpeg_kalite"] = int(kalite)
            yz.bilgi["theta_benzetim"]["jpeg_kalite"] = int(kalite)
        b = jpeg_kodla(balik, kalite)
        boyutlar.append(len(b))
        yz.kare_ekle(kay._kareler[sira][1], b)
    yz.bilgi["theta_benzetim"]["jpeg_kalite"] = int(kalite) if kalite else None
    for ad in ("komutlar.csv", "gercek_konum.csv", "gercek_harita.png", "gercek_harita.json", "gercek_mesafe_zemin.csv",
               "gercek_mesafe_duvar.csv"):
        yz.dosya_kopyala(kaynak, ad)
    yz.kapat()
    if not sessiz:
        print("yazıldı: %s  kare=%d  JPEG kalite=%s  boyut KB: ort=%.0f min=%.0f maks=%.0f" % (
            cikti, len(boyutlar), kalite, np.mean(boyutlar) / 1024, min(boyutlar) / 1024, max(boyutlar) / 1024))
    return boyutlar


def main():
    if "--sigma_tahmin" in sys.argv:
        i = sys.argv.index("--sigma_tahmin")
        wg, ng, ws, ns, sg, ss, s = sigma_tahmin(sys.argv[i + 1], sys.argv[i + 2])
        print("gerçek kenar genişliği: %.2f px (n=%d) -> eşdeğer Gauss sigma %.2f" % (wg, ng, sg))
        print("sim (ek bulanıklık yok) kenar genişliği: %.2f px (n=%d) -> eşdeğer sigma %.2f" % (ws, ns, ss))
        print("eklenecek sigma = sqrt(gercek^2 - sim^2) = %.2f px" % s)
        return
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("kaynak")
    p.add_argument("cikti")
    p.add_argument("--varyant", default="A", choices=["A", "B"])
    p.add_argument("--max_kare", type=int)
    p.add_argument("--govde_yok", action="store_true")
    p.add_argument("--sigma", type=float, default=VARSAYILAN_SIGMA, help="Gauss sigma (piksel); 0 kapalı")
    p.add_argument("--hareket_bulaniklik", action="store_true", help="yaw hızından hareket bulanıklığı")
    p.add_argument("--pozlama_ms", type=float, default=VARSAYILAN_POZLAMA_MS)
    p.add_argument("--gurultu_yok", action="store_true")
    p.add_argument("--jpeg_kalite", type=int, default=0, help="0 = hedef boyuta göre otomatik")
    p.add_argument("--jpeg_hedef_kb", type=float, default=HEDEF_KB)
    p.add_argument("--tohum", type=int, default=0)
    a = p.parse_args()
    benzet(a.kaynak, a.cikti, a.varyant, a.max_kare, not a.govde_yok, a.sigma, a.hareket_bulaniklik, a.pozlama_ms,
           not a.gurultu_yok, a.jpeg_kalite, a.jpeg_hedef_kb, a.tohum)


if __name__ == "__main__":
    main()
