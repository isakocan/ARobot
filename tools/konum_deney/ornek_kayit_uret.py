"""Unity'siz küçük sentetik kayıt üretir (test düzeneği): dikdörtgen bir oda, ışın atarak çizilmiş eşdikdörtgen kareler.

Oda: x 0..G m (doğu), y 0..D m (kuzey), z 0..Y m (yukarı); zemin, tavan, 4 duvar farklı desenli dokularla kaplı.
Kamera robotun üstünde (zemin üstü kamera_yuksekligi_m). Hareket, motor komutlarından basit sürüş modeliyle üretilir:
  ileri hız v = V * (sol+sag)/2/255, dönüş hızı (saat yönü) w = W * (sol-sag)/510; nominal V = 0.40 m/s, W = 60 derece/sn.
  Gerçek hareket nominalin %92'si (ileri) ve %95'i (dönüş): komut kestirimi bu yüzden hata yapar.

Kullanım:  python tools/konum_deney/ornek_kayit_uret.py <klasor> [kare_sayisi] [A|B]"""
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kamera import esd_isin_izgarasi  # noqa: E402
from kayit import KayitYazici  # noqa: E402

V_NOMINAL = 0.40
W_NOMINAL = 60.0
FPS = 15
ODA = (4.0, 3.0, 2.6)          # doğu-batı, kuzey-güney, yükseklik (m)

# (süre sn, sol, sag) parçaları
VARSAYILAN_PLAN = [(1.5, 255, 255), (1.5, 255, -255), (2.0, 255, 255), (1.0, -255, 255), (1.0, 255, 255), (0.5, 255, -255),
                   (0.5, 0, 0)]


def _doku(boyut, tohum, ton):
    """Dikkat çekici, tekrarsız bir doku: çok ölçekli gürültü + çizgiler + dikdörtgenler (BGR uint8)."""
    r = np.random.RandomState(tohum)
    t = np.zeros((boyut, boyut), np.float32)
    for k, a in ((4, 1.0), (8, 0.6), (16, 0.4), (32, 0.25), (64, 0.15)):
        n = cv2.resize(r.rand(k, k).astype(np.float32), (boyut, boyut), interpolation=cv2.INTER_CUBIC)
        t += a * n
    t = (t - t.min()) / (t.max() - t.min())
    im = np.zeros((boyut, boyut, 3), np.float32)
    for c in range(3):
        im[..., c] = 0.25 + 0.55 * t * (0.6 + 0.4 * ton[c])
    im = (im * 255).astype(np.uint8)
    for _ in range(14):
        x0, y0 = r.randint(0, boyut - 40, 2)
        w, h = r.randint(15, 90, 2)
        renk = tuple(int(v) for v in r.randint(20, 235, 3))
        cv2.rectangle(im, (x0, y0), (min(x0 + w, boyut - 1), min(y0 + h, boyut - 1)), renk, -1 if r.rand() < 0.6 else 2)
    for i in range(0, boyut, boyut // 8):
        cv2.line(im, (i, 0), (i, boyut), (30, 30, 30), 1)
        cv2.line(im, (0, i), (boyut, i), (30, 30, 30), 1)
    return cv2.GaussianBlur(im, (0, 0), 0.8)


class OdaCizici:
    def __init__(self, genislik=640, yukseklik=320, oda=ODA, doku_boyutu=512):
        self.G, self.Y = genislik, yukseklik
        self.oda = oda
        self.isin = esd_isin_izgarasi(genislik, yukseklik)          # robot ışınları (x sağ, y yukarı, z ileri)
        tonlar = [(0.3, 0.6, 1.0), (1.0, 0.5, 0.3), (0.4, 1.0, 0.4), (0.9, 0.9, 0.2), (0.6, 0.4, 0.9), (0.7, 0.7, 0.7)]
        self.dokular = [_doku(doku_boyutu, 10 + i, tonlar[i]) for i in range(6)]
        self.dB = doku_boyutu

    def ciz(self, x, y, yon_derece, kamera_yuksekligi=0.25):
        gx, gy, gz = self.oda
        psi = np.radians(yon_derece)
        ileri = np.array([np.sin(psi), np.cos(psi), 0.0])
        sag = np.array([np.cos(psi), -np.sin(psi), 0.0])
        yukari = np.array([0.0, 0.0, 1.0])
        d = (self.isin[..., 0:1] * sag + self.isin[..., 1:2] * yukari + self.isin[..., 2:3] * ileri)   # (Y,G,3) dünya
        o = np.array([x, y, kamera_yuksekligi])
        # çıkış parametresi t: her eksen için
        sinir = np.array([gx, gy, gz])
        with np.errstate(divide="ignore", invalid="ignore"):
            t_ust = np.where(d > 1e-9, (sinir - o) / d, np.inf)
            t_alt = np.where(d < -1e-9, (0.0 - o) / d, np.inf)
        tt = np.minimum(t_ust, t_alt)                                  # (Y,G,3)
        eksen = np.argmin(tt, axis=-1)
        t = np.take_along_axis(tt, eksen[..., None], -1)[..., 0]
        nokta = o + d * t[..., None]
        yuz = np.where(eksen == 2, np.where(d[..., 2] > 0, 1, 0),        # 1 tavan, 0 zemin
                       np.where(eksen == 0, np.where(d[..., 0] > 0, 2, 3), np.where(d[..., 1] > 0, 4, 5)))
        cikti = np.zeros((self.Y, self.G, 3), np.uint8)
        olcek = self.dB / 2.0                                          # 1 m = 256 doku pikseli
        for f in range(6):
            m = yuz == f
            if not m.any():
                continue
            P = nokta[m]
            if f in (0, 1):
                a, b = P[:, 0], P[:, 1]
            elif f in (2, 3):
                a, b = P[:, 1], P[:, 2]
            else:
                a, b = P[:, 0], P[:, 2]
            u = (a * olcek) % self.dB
            v = (b * olcek) % self.dB
            u0 = np.floor(u).astype(int)
            v0 = np.floor(v).astype(int)
            fu = (u - u0)[:, None]
            fv = (v - v0)[:, None]
            u0 %= self.dB
            v0 %= self.dB
            u1 = (u0 + 1) % self.dB
            v1 = (v0 + 1) % self.dB
            T = self.dokular[f].astype(np.float32)
            ornek = (T[v0, u0] * (1 - fu) * (1 - fv) + T[v0, u1] * fu * (1 - fv)
                     + T[v1, u0] * (1 - fu) * fv + T[v1, u1] * fu * fv)
            cikti[m] = np.clip(ornek, 0, 255).astype(np.uint8)
        # uzaklığa göre hafif koyulaşma
        cikti = (cikti.astype(np.float32) * np.clip(1.15 - 0.12 * t, 0.5, 1.0)[..., None]).astype(np.uint8)
        return cikti


V_CARPAN = 0.92       # gerçek hareket nominalden yavaş (kayma): komut kestirimi bu yüzden hata yapar
W_CARPAN = 0.95


def surus_simule(plan, fps=FPS, kare_sayisi=None, baslangic=(1.0, 1.0, 0.0), V=V_NOMINAL * V_CARPAN, W=W_NOMINAL * W_CARPAN, dt=0.005):
    """plan: [(sure_sn, sol, sag)]. Döndürür: komut tabloları ve kare başına gerçek konum."""
    x, y, psi = baslangic
    t = 0.0
    komut_t, sol, sag = [], [], []
    gecen = 0.0
    for sure, s, g in plan:
        komut_t.append(int(round(gecen * 1000)))
        sol.append(s)
        sag.append(g)
        gecen += sure
    toplam = gecen
    n = kare_sayisi if kare_sayisi else int(toplam * fps)
    kt = np.array(komut_t)
    ksol = np.array(sol, float)
    ksag = np.array(sag, float)
    kareler = []
    t_ms_kare = [int(round(i * 1000.0 / fps)) for i in range(n)]
    sira = 0
    t_s = 0.0
    for i, tk in enumerate(t_ms_kare):
        hedef = tk / 1000.0
        while t_s < hedef - 1e-9:
            h = min(dt, hedef - t_s)
            j = max(0, np.searchsorted(kt, int(round(t_s * 1000)), side="right") - 1)
            v = V * (ksol[j] + ksag[j]) / 2.0 / 255.0
            w = W * (ksol[j] - ksag[j]) / 510.0
            x += v * h * np.sin(np.radians(psi))
            y += v * h * np.cos(np.radians(psi))
            psi = (psi + w * h) % 360.0
            t_s += h
        kareler.append((i, tk, x, y, psi))
    return kt, ksol, ksag, kareler


def uret(klasor, kare_sayisi=100, varyant="A", genislik=640, yukseklik=320, plan=None, kalite=92):
    plan = plan or VARSAYILAN_PLAN
    kt, ksol, ksag, gk = surus_simule(plan, kare_sayisi=kare_sayisi)
    cizici = OdaCizici(genislik, yukseklik)
    bilgi = {"bicim": 1, "kaynak": "sim", "ad": "ornek_oda",
             "kamera": {"tur": "esdikdortgen", "genislik": genislik, "yukseklik": yukseklik, "fps": FPS},
             "kamera_yuksekligi_m": 0.25,
             "kayma_modeli": {"V_ileri_m_s": V_NOMINAL, "W_don_derece_s": W_NOMINAL, "gercek_V_carpani": V_CARPAN,
                              "gercek_W_carpani": W_CARPAN},
             "not": "ornek_kayit_uret.py: sentetik dikdortgen oda, test duzenegi"}
    with KayitYazici(klasor, bilgi, varyant) as yz:
        for (i, t, x, y, psi) in gk:
            yz.kare_ekle(t, cizici.ciz(x, y, psi), kalite)
        yz.komutlar_yaz(kt, ksol, ksag)
        yz.gercek_konum_yaz([g[0] for g in gk], [g[1] for g in gk], [g[2] for g in gk], [g[3] for g in gk],
                            [g[4] for g in gk])
    return klasor


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    uret(sys.argv[1], int(sys.argv[2]) if len(sys.argv) > 2 else 100, sys.argv[3] if len(sys.argv) > 3 else "A")
    print("yazıldı:", sys.argv[1])
