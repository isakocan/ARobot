"""M1: sütun profili ile görsel pusula (ucuz). Anahtar kareye göre yaw, dairesel FFT çapraz ilintisi.

Adımlar (bkz. pusula_ortak.py için anahtar kare ve komut ön bilgisi):
  1. Kare G x G/2 gri eşdikdörtgene çevrilir (varsayılan G = 512; 1 sütun = 0,70 derece).
  2. Seçilen yükseklik bandı (varsayılan ufuktan +5..+65 derece) n_alt eşit alt banda bölünür; her alt bandın sütun
     ortalaması bir dairesel profil verir (geçerlilik maskesi: gövde ve karanlık halka dışlanır).
  3. Her profil frekans uzayında bant geçiren süzülür (alçak: ~1,4 derece, yüksek geçiren: ~12 derece: yavaş ışık
     değişimi atılır) ve birim norma getirilir -> ışığa dayanıklı, çıplak parlaklığa bakmaz.
  4. Anahtar kare profilleriyle dairesel çapraz ilinti (rfft). Alt bandların ilintileri enerjilerine göre ağırlıklı
     toplanır; tepe, komut/süreklilik penceresi içinde aranır; alt sütun için parabol enterpolasyonu.
  5. Kalite = birleşik tepe değeri (ağırlıklı ortalama NCC, -1..1). Alt bandların tepe uyumu bilgi olarak döner.
Dönüş: guncelle() -> (None, None, yon_derece)."""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pusula_ortak import PusulaTaban, VARSAYILAN_BANT  # noqa: E402


class Yontem(PusulaTaban):
    def __init__(self, bilgi=None, kamera=None, genislik=512, bant=VARSAYILAN_BANT, n_alt=4, komut_oncel=True,
                 anahtar_mesafe_m=0.3, kalite_alt=0.5, alcak_der=1.4, yuksek_der=12.0, **kw):
        self.n_alt = int(n_alt)
        self.alcak_der = float(alcak_der)
        self.yuksek_der = float(yuksek_der)
        super().__init__(bilgi, kamera, genislik, bant, komut_oncel, anahtar_mesafe_m, kalite_alt, **kw)
        G = self.G
        f = np.fft.rfftfreq(G)                                   # döngü/sütun
        sa = self.alcak_der / 360.0 * G                           # sütun
        sy = self.yuksek_der / 360.0 * G
        alcak = np.exp(-2.0 * (np.pi * f * sa) ** 2)
        yuksek = 1.0 - np.exp(-2.0 * (np.pi * f * sy) ** 2)
        self._suzgec = alcak * yuksek
        self._parseval = np.full(f.shape, 2.0)                  # rfft yarı spektrum: DC ve Nyquist bir kez sayılır
        self._parseval[0] = 1.0
        if G % 2 == 0:
            self._parseval[-1] = 1.0
        edges = np.linspace(self.bant[0], self.bant[1], self.n_alt + 1)
        self._satirlar = [self.don.satir_araligi(edges[i], edges[i + 1]) for i in range(self.n_alt)]

    def _cikar(self, gri, gecerli):
        """Özellik: (F (n_alt, G/2+1) karmaşık, ağırlık (n_alt,)). Satırlar ufuk yönünde ağırlıklandırılmaz (alt bant içinde
        eşit), geçerlilik maskesi satır başına uygulanır."""
        P = np.empty((self.n_alt, self.G), np.float32)
        for i, (v0, v1) in enumerate(self._satirlar):
            blok = gri[v0:v1]
            if gecerli is None:
                P[i] = blok.mean(axis=0)
            else:
                w = gecerli[v0:v1]
                ws = w.sum(axis=0)
                ort = (blok * w).sum(axis=0) / np.maximum(ws, 1e-3)
                kotu = ws < 0.5 * (v1 - v0)
                if kotu.any():
                    ort[kotu] = ort[~kotu].mean() if (~kotu).any() else 0.0
                P[i] = ort
        F = np.fft.rfft(P, axis=1) * self._suzgec
        norm = np.sqrt((np.abs(F) ** 2 * self._parseval).sum(axis=1) / self.G) + 1e-9   # sum x^2 ... bkz. Parseval
        e = norm / np.sqrt(self.G)                              # süzülmüş profilin std'si (gri düzeyi)
        Fn = F / norm[:, None]                                  # sum x^2 = 1
        ag = e / (e + 1.0)                                       # çok düz bandı (gürültü) küçük ağırlıkla say
        return Fn, ag

    def _ilinti(self, oz, anahtar_oz):
        """Birleşik dairesel çapraz ilinti c(s), s = 0..G-1: içerik s sütun sola kaymışsa tepe s'te. + alt bant ilintileri."""
        Fc, ac = oz
        Fk, ak = anahtar_oz
        w = ac * ak
        C = np.fft.irfft(np.conj(Fc) * Fk, n=self.G, axis=1)        # (n_alt, G), birim normdan: NCC
        toplam = (C * w[:, None]).sum(axis=0) / max(w.sum(), 1e-9)
        return toplam, C, w

    def _esle(self, oz, anahtar_oz, merkez_der, pencere_der):
        G = self.G
        c, C, w = self._ilinti(oz, anahtar_oz)
        sutun_der = 360.0 / G
        orta = int(round(merkez_der / sutun_der)) % G
        yar = int(np.ceil(pencere_der / sutun_der))
        idx = (orta + np.arange(-yar, yar + 1)) % G
        k = int(idx[np.argmax(c[idx])])
        # alt sütun: parabol
        ym, y0, yp = c[(k - 1) % G], c[k], c[(k + 1) % G]
        den = ym - 2.0 * y0 + yp
        d = 0.5 * (ym - yp) / den if den < -1e-9 else 0.0
        s = k + float(np.clip(d, -1.0, 1.0))
        if s > G / 2.0:
            s -= G
        kal = float(c[k])
        # ikinci tepe: ana tepeden >5 derece uzakta en büyük değer
        uz = np.abs(((np.arange(G) - k + G // 2) % G) - G // 2) * sutun_der
        ikinci = float(c[uz > 5.0].max()) if (uz > 5.0).any() else 0.0
        # alt bant uyumu
        uyum = 0
        for b in range(C.shape[0]):
            kb = int(idx[np.argmax(C[b][idx])])
            dk = abs(((kb - k + G // 2) % G) - G // 2)
            uyum += int(dk <= 2)
        bilgi = {"ikinci": ikinci, "alt_uyum": uyum, "kayma_sutun": s}
        return s * sutun_der, kal, True, bilgi
