"""Theta S kamera geometrisi: tek doğruluk kaynağı (parametreler kamera.json'da).

EKSEN SÖZLEŞMELERİ
  Robot ışını (birim vektör): x = sağ, y = yukarı, z = ileri. (Unity gölgelendiricisi ve surus.html ile aynı.)
  Yönelim (azimut): ileriden saat yönünde (üstten bakınca sağa doğru) derece. az = atan2(x, z).
  Yükseklik açısı: ufuktan yukarı pozitif. el = asin(y).
  Eşdikdörtgen görüntü: üst satır = yukarı, ORTA SÜTUN = robotun ilerisi, sütunlar sağa (saat yönü) artar.
      sütun u (piksel merkezi u+0.5): az = (u+0.5)/G*360 - 180;  satır v: el = 90 - (v+0.5)/Y*180.
  Çift balık gözü görüntü (1280x720, piksel x sağa, y aşağı): sol yarı lens 1, sağ yarı lens 2.
      Lens kamera ekseni: z = lens ekseni, x = görüntüde sağ, y = görüntüde yukarı.
      Model: eşit uzaklık, r = f * teta (kamera.json "model"). teta = lens eksenine açı.
      Piksel = (mx + (x/n) * f*teta,  my - (y/n) * f*teta),  n = hypot(x, y).
  Robot -> kamera: d_kam = Rz(yatma) Rx(egim) Ry(yon) d_robot  (yatma=90: kamera yan yatık; görüntü 90 derece döner).
      Varsayılan montaj yon=0, egim=0, yatma=90: lens 1 ekseni robotun ilerisine bakar; görüntünün SAĞI robotun AŞAĞISI,
      görüntünün ÜSTÜ robotun SAĞIDIR. Gerçek "Yön" ayarı robotta saklı, depoda yok: doğrulanamadı.
  Lens 2: c2 = Don2 * (-x, y, -z) ile aynı d_kam'dan hesaplanır (arkaya bakar, küçük dönüş Don2).
  Birleştirme: lens 1 ağırlığı w1 = 1 - smoothstep((teta1_derece - (90-bant)) / (2*bant)); w2 = 1 - w1;
      teta*f > rmax olan lens ağırlığı 0 (gölgelendirici ile aynı).
Kaynak satırları kamera.json içinde."""
import json
import os

import cv2
import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
VARSAYILAN_JSON = os.path.join(KLASOR, "kamera.json")


def _Rx(a):
    c, s = np.cos(np.radians(a)), np.sin(np.radians(a))
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def _Ry(a):
    c, s = np.cos(np.radians(a)), np.sin(np.radians(a))
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def _Rz(a):
    c, s = np.cos(np.radians(a)), np.sin(np.radians(a))
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def smoothstep(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3.0 - 2.0 * x)


def esd_isin_izgarasi(genislik, yukseklik):
    """Eşdikdörtgen görüntünün her pikseli için robot ışını (Y, G, 3), x sağ y yukarı z ileri."""
    az = np.radians((np.arange(genislik) + 0.5) / genislik * 360.0 - 180.0)
    el = np.radians(90.0 - (np.arange(yukseklik) + 0.5) / yukseklik * 180.0)
    AZ, EL = np.meshgrid(az, el)
    return np.stack([np.cos(EL) * np.sin(AZ), np.sin(EL), np.cos(EL) * np.cos(AZ)], -1)


def isin_esd_piksel(d, genislik, yukseklik):
    """Robot ışını (..., 3) -> eşdikdörtgen (u, v) piksel koordinatı (piksel merkezi tam sayıda)."""
    az = np.degrees(np.arctan2(d[..., 0], d[..., 2]))
    el = np.degrees(np.arcsin(np.clip(d[..., 1], -1, 1)))
    u = (az + 180.0) / 360.0 * genislik - 0.5
    v = (90.0 - el) / 180.0 * yukseklik - 0.5
    return u, v


class Kamera:
    def __init__(self, ayar=None):
        if ayar is None:
            ayar = VARSAYILAN_JSON
        if isinstance(ayar, (str, os.PathLike)):
            with open(ayar, encoding="utf-8") as f:
                ayar = json.load(f)
        self.ayar = ayar
        self.G = int(ayar["kare"]["genislik"])
        self.Y = int(ayar["kare"]["yukseklik"])
        self.lens = [ayar["lens1"], ayar["lens2"]]
        self.rmax = float(ayar["rmax_px"])
        self.daire_r = float(ayar.get("daire_yaricap_px", self.rmax))
        self.bant = float(ayar["karisim_bandi_derece"])
        m = ayar["montaj"]
        self.montaj = (float(m["yon"]), float(m["egim"]), float(m["yatma"]))
        self.M = _Rz(self.montaj[2]) @ _Rx(self.montaj[1]) @ _Ry(self.montaj[0])
        dx, dy, dz = ayar["don2_derece"]
        self.R2 = _Rz(dz) @ _Ry(dy) @ _Rx(dx)
        self._onbellek = {}

    @staticmethod
    def bilgiden(bilgi=None):
        """bilgi.json içindeki kamera.parametre_dosyasi varsa onu, yoksa varsayılan kamera.json'u yükler."""
        yol = None
        if bilgi:
            yol = (bilgi.get("kamera") or {}).get("parametre_dosyasi")
        return Kamera(yol if yol and os.path.exists(yol) else VARSAYILAN_JSON)

    # ---- tek ışın dönüşümleri -------------------------------------------------
    def robot_to_lens(self, d, lens):
        """Robot ışını (...,3) -> lens (0|1) piksel (u, v) ve teta (radyan)."""
        d = np.asarray(d, dtype=np.float64)
        dk = d @ self.M.T
        if lens == 0:
            c = dk
        else:
            c = (dk * np.array([-1.0, 1.0, -1.0])) @ self.R2.T
        teta = np.arccos(np.clip(c[..., 2], -1, 1))
        n = np.maximum(np.hypot(c[..., 0], c[..., 1]), 1e-9)
        L = self.lens[lens]
        r = teta * L["f"]
        return L["merkez_x"] + c[..., 0] / n * r, L["merkez_y"] - c[..., 1] / n * r, teta

    def piksel_to_isin(self, u, v, lens):
        """Çift balık gözü piksel (u, v) (lens 0|1) -> robot ışını (...,3) ve geçerli (r <= daire yarıçapı) maskesi."""
        L = self.lens[lens]
        u = np.asarray(u, dtype=np.float64)
        v = np.asarray(v, dtype=np.float64)
        dx = u - L["merkez_x"]
        dy = -(v - L["merkez_y"])
        r = np.hypot(dx, dy)
        teta = r / L["f"]
        n = np.maximum(r, 1e-9)
        st = np.sin(teta)
        c = np.stack([st * dx / n, st * dy / n, np.cos(teta)], -1)
        if lens == 1:
            dk = (c @ self.R2) * np.array([-1.0, 1.0, -1.0])      # c = R2 * flip(d)  ->  d = flip(R2^T c)
        else:
            dk = c
        d = dk @ self.M                                            # d_robot = M^T d_kam
        return d, r <= self.daire_r

    def merkez_isini(self, lens=0):
        L = self.lens[lens]
        return self.piksel_to_isin(L["merkez_x"], L["merkez_y"], lens)[0]

    def agirliklar(self, d):
        """Birleştirme ağırlıkları (w1, w2) robot ışını için (gölgelendirici ile aynı)."""
        _, _, t1 = self.robot_to_lens(d, 0)
        _, _, t2 = self.robot_to_lens(d, 1)
        x = (np.degrees(t1) - (90.0 - self.bant)) / (2.0 * self.bant)
        w1 = np.where(t1 * self.lens[0]["f"] > self.rmax, 0.0, 1.0 - smoothstep(x))
        w2 = np.where(t2 * self.lens[1]["f"] > self.rmax, 0.0, 1.0 - w1)
        return w1, w2

    # ---- görüntü dönüşümleri --------------------------------------------------
    def _esd_tablo(self, g, y):
        anahtar = ("esd", g, y)
        if anahtar not in self._onbellek:
            d = esd_isin_izgarasi(g, y).reshape(-1, 3)
            tablolar = []
            for k in (0, 1):
                u, v, _ = self.robot_to_lens(d, k)
                tablolar.append((u.reshape(y, g).astype(np.float32), v.reshape(y, g).astype(np.float32)))
            w1, w2 = self.agirliklar(d)
            self._onbellek[anahtar] = (tablolar, w1.reshape(y, g).astype(np.float32), w2.reshape(y, g).astype(np.float32))
        return self._onbellek[anahtar]

    def balikgozu_to_esdikdortgen(self, resim, genislik=1280, yukseklik=640, enterpolasyon=cv2.INTER_LINEAR):
        """Çift balık gözü kare (BGR, 1280x720) -> eşdikdörtgen (BGR). Çakışmada gölgelendirici gibi karıştırır.
        Dönüş: (resim, kapsam); kapsam: w1+w2 > 0 olan piksel maskesi (bool)."""
        tablolar, w1, w2 = self._esd_tablo(genislik, yukseklik)
        a = cv2.remap(resim, tablolar[0][0], tablolar[0][1], enterpolasyon, borderMode=cv2.BORDER_CONSTANT)
        b = cv2.remap(resim, tablolar[1][0], tablolar[1][1], enterpolasyon, borderMode=cv2.BORDER_CONSTANT)
        toplam = np.maximum(w1 + w2, 1e-4)
        k1, k2 = w1 / toplam, w2 / toplam
        if a.ndim == 3:
            k1, k2 = k1[..., None], k2[..., None]
        cikti = a.astype(np.float32) * k1 + b.astype(np.float32) * k2
        return np.clip(cikti + 0.5, 0, 255).astype(np.uint8), (w1 + w2) > 0

    def _balikgozu_tablo(self, g, y):
        anahtar = ("bal", g, y)
        if anahtar not in self._onbellek:
            mapx = np.zeros((self.Y, self.G), np.float32)
            mapy = np.zeros_like(mapx)
            gecerli = np.zeros((self.Y, self.G), bool)
            for k in (0, 1):
                L = self.lens[k]
                x0 = max(int(L["merkez_x"] - self.daire_r) - 1, 0)
                x1 = min(int(L["merkez_x"] + self.daire_r) + 2, self.G)
                y0 = max(int(L["merkez_y"] - self.daire_r) - 1, 0)
                y1 = min(int(L["merkez_y"] + self.daire_r) + 2, self.Y)
                U, V = np.meshgrid(np.arange(x0, x1, dtype=np.float64), np.arange(y0, y1, dtype=np.float64))
                d, ok = self.piksel_to_isin(U, V, k)
                u, v = isin_esd_piksel(d, g, y)
                mapx[y0:y1, x0:x1][ok] = u[ok]
                mapy[y0:y1, x0:x1][ok] = v[ok]
                gecerli[y0:y1, x0:x1] |= ok
            self._onbellek[anahtar] = (mapx, mapy, gecerli)
        return self._onbellek[anahtar]

    def esdikdortgen_to_balikgozu(self, resim, enterpolasyon=cv2.INTER_LINEAR):
        """Eşdikdörtgen kare -> çift balık gözü (1280x720). Daire dışı siyah. Dönüş: (resim, daire_maskesi)."""
        y, g = resim.shape[:2]
        mapx, mapy, gecerli = self._balikgozu_tablo(g, y)
        genis = cv2.copyMakeBorder(resim, 0, 0, 0, 2, cv2.BORDER_WRAP)     # u ekseninde sarma
        mx = np.where(mapx < 0, mapx + g, mapx)
        cikti = cv2.remap(genis, mx, mapy, enterpolasyon, borderMode=cv2.BORDER_REPLICATE)
        cikti[~gecerli] = 0
        return cikti, gecerli
