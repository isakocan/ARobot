"""Bilinen konumlarla doluluk ızgarası (log-odds, 0,05 m hücre) kurma.

Girdi: kare başına 360 mesafe (azimut k = robot önünden saat yönünde k derece) + güven + güvenilir menzil, ve gercek_konum.csv
konumu (kameranın zemin izdüşümü = tarama başlangıcı; kamera, taramanın orijinidir, ek öteleme yok).
Her azimut kutusu 1 derece genişliğindedir; ışınlar 0,25 derece aralıkla atılır (4 alt ışın) ki 3-4 m'de bile hücre boşluğu kalmasın.
Kurallar:
  - güvenli ve güvenilir menzil içindeki sınır: sınıra kadar BOŞ kanıt, sınır hücresi DOLU kanıt;
  - sınır güvenilir menzilden uzaksa: yalnızca menzile kadar boş kanıt, dolu kanıt yok;
  - güven düşükse: yalnızca kısa bir yarıçapa (dusuk_guven_serbest_m) kadar boş kanıt (gövde maskesinin hemen ötesi), dolu yok;
  - bir taramada bir hücre en çok bir kez güncellenir (dolu, boşa baskın).
Harita sözleşmesi gercek_harita.png ile aynı: 0 = dolu, 255 = boş, 128 = bilinmiyor; satır 0 = kuzey."""
import json
import os

import numpy as np

HUCRE = 0.05
ALT_ISIN = np.array([-0.375, -0.125, 0.125, 0.375])      # derece, kutu içindeki ışın kayması


class Izgara:
    def __init__(self, x0, y0, genislik, yukseklik, hucre=HUCRE):
        self.x0, self.y0, self.W, self.Hh, self.h = float(x0), float(y0), int(genislik), int(yukseklik), float(hucre)
        self.lo = np.zeros((self.Hh, self.W), np.float32)
        self.gorulen = np.zeros((self.Hh, self.W), np.int32)

    @staticmethod
    def gercekten(klasor):
        """gercek_harita.json'dan aynı hücre ızgarası (karşılaştırma kolay olsun)."""
        with open(os.path.join(klasor, "gercek_harita.json"), encoding="utf-8") as f:
            b = json.load(f)
        return Izgara(b["x0_m"], b["y0_m"], b["genislik"], b["yukseklik"], b.get("hucre_m", HUCRE))

    def hucre_idx(self, x, y):
        c = np.floor((x - self.x0) / self.h).astype(np.int64)
        r = (self.Hh - 1 - np.floor((y - self.y0) / self.h)).astype(np.int64)
        return r, c

    def hucre_merkezi(self, r, c):
        return self.x0 + (c + 0.5) * self.h, self.y0 + (self.Hh - 1 - r + 0.5) * self.h

    def tara_ekle(self, x, y, yon_derece, mesafe, guven, max_menzil, guven_min=0.2, dusuk_guven_serbest_m=0.6,
                  l_bos=-0.4, l_dolu=0.85, adim=None, lo_sinir=(-4.0, 4.0)):
        """Tek taramayı işler. mesafe/guven (360,)."""
        adim = adim or self.h / 2.0
        mes0 = np.asarray(mesafe, float)
        gv0 = np.asarray(guven, float)
        # alt ışın mesafesi: komşu kutu ile doğrusal aradeğer (eğik yüzeylerde 1 derecelik kutu içinde mesafe hızla değişir);
        # komşu ile fark büyükse (kenar/süreksizlik) iki değerin küçüğü alınır (kısa kal = güvenli)
        k = np.arange(360)
        nk = (k[:, None] + np.where(ALT_ISIN[None, :] < 0, -1, 1)) % 360
        w = np.abs(ALT_ISIN)[None, :]
        d_k = np.repeat(mes0[:, None], len(ALT_ISIN), 1)
        d_n = mes0[nk]
        yakin = np.isfinite(d_k) & np.isfinite(d_n) & (np.abs(d_k - d_n) < 0.15 + 0.1 * np.minimum(d_k, d_n))
        mes_a = np.where(yakin, (1 - w) * d_k + w * d_n, np.fmin(d_k, d_n))
        gv_a = np.minimum(gv0[:, None], gv0[nk])
        mes = mes_a.ravel()
        gv = gv_a.ravel()
        sonlu = np.isfinite(mes) & (mes > 0)
        guvenli = sonlu & (gv >= guven_min)
        lim = np.where(guvenli, np.minimum(mes, max_menzil), np.where(sonlu, np.minimum(mes, dusuk_guven_serbest_m), 0.0))
        dv = guvenli & (mes <= max_menzil)
        dm = mes
        teta = np.radians(yon_derece + k[:, None] + ALT_ISIN[None, :])
        sx, sy = np.sin(teta).ravel(), np.cos(teta).ravel()
        tmax = float(np.max(lim)) if lim.size else 0.0
        if tmax <= 0:
            return
        t = np.arange(0.0, tmax, adim)[None, :]
        gecerli = t < lim[:, None]          # boş kanıt yüzeyden 2 cm önce biter
        px = x + sx[:, None] * t
        py = y + sy[:, None] * t
        r, c = self.hucre_idx(px, py)
        ic = (r >= 0) & (r < self.Hh) & (c >= 0) & (c < self.W) & gecerli
        bos = np.unique(r[ic] * self.W + c[ic])
        # dolu hücreler (sınır noktası)
        dolu = np.empty(0, np.int64)
        ic_ic = 0.0                                  # sınır noktasını yüzeyin 2 cm içine koy (hücre kenarına denk gelen yüzeyler için)
        if dv.any():
            ex, ey = x + sx[dv] * (dm[dv] + ic_ic), y + sy[dv] * (dm[dv] + ic_ic)
            er, ec = self.hucre_idx(ex, ey)
            oke = (er >= 0) & (er < self.Hh) & (ec >= 0) & (ec < self.W)
            dolu = np.unique(er[oke] * self.W + ec[oke])
        bos = np.setdiff1d(bos, dolu, assume_unique=True)
        lf = self.lo.reshape(-1)
        lf[bos] = np.clip(lf[bos] + l_bos, *lo_sinir)
        lf[dolu] = np.clip(lf[dolu] + l_dolu, *lo_sinir)
        gf = self.gorulen.reshape(-1)
        gf[bos] += 1
        gf[dolu] += 1

    def durum(self, esik_dolu=0.4, esik_bos=-0.4):
        """0 = dolu, 255 = boş, 128 = bilinmiyor."""
        d = np.full(self.lo.shape, 128, np.uint8)
        d[self.lo <= esik_bos] = 255
        d[self.lo >= esik_dolu] = 0
        return d


def kayittan_izgara(ad, taramalar, konum, kare, grid_klasor, max_menzil=None, **kw):
    """taramalar: dict(kare, mesafe, guven, max_menzil); konum: gercek_konum dict (kare indeksli)."""
    iz = Izgara.gercekten(grid_klasor)
    mm = max_menzil if max_menzil is not None else float(taramalar["max_menzil"])
    satir = {int(k): i for i, k in enumerate(konum["kare"])}
    for j, kr in enumerate(taramalar["kare"]):
        i = satir[int(kr)]
        iz.tara_ekle(konum["x_m"][i], konum["y_m"][i], konum["yon_derece"][i], taramalar["mesafe"][j], taramalar["guven"][j], mm, **kw)
    return iz
