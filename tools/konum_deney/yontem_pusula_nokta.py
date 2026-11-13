"""M2: nokta özelliği ile görsel pusula. Anahtar kareye göre dönüş, ışın (bearing) çiftlerinden dayanıklı çözülür.

Adımlar (anahtar kare ve komut ön bilgisi için pusula_ortak.py):
  1. Kare G x G/2 gri eşdikdörtgene çevrilir (varsayılan G = 1024). Yükseklik bandı (varsayılan +5..+65 derece) içinde,
     geçerlilik maskesiyle, ORB noktaları bulunur. Dikiş (sütun 0 / G-1) için görüntü iki yana PAD sütun sarılır.
  2. Anahtar kareyle Hamming eşleştirme + Lowe oran testi (0,8).
  3. Noktalar kamera.py sözleşmesiyle birim ışına çevrilir: az = (u+0,5)/G*360-180, el = ufuktan yukarı; x sağ, y yukarı, z ileri.
  4. Dönme çözümü (kip; varsayılan "duzlem"):
       "yaw":  yalnızca dikey eksen. Her eşleşme bir aday verir (örnek boyutu 1); aday sayısı N küçük olduğundan tüm adaylar
               denenir (kapsamlı RANSAC), açısal artık eşiği ile içerik sayılır, içerikler üstünde kapalı biçimli en küçük kareler
               (atan2) ile inceltilir.
       "duzlem": ÖTELEMEYE DAYANIKLI. Düzlemsel hareket modeli: dönme yalnızca yaw (d), öteleme yönü yatay düzlemde (azimut phi,
               büyüklüğü bilinmez). Eşleşen ışın çifti için epipolar kısıt: t . (R a x b) = 0, t = (sin phi, 0, cos phi). (d, phi)
               ızgarada MSAC (kesilmiş kare) maliyetiyle aranır, sonra ince ızgarayla incelenir. Yaw'ı öteleme paralaksı
               saptırmaz (yalnız-yaw modeli saptırır); eşleşme sayısı kadar maliyet N x ızgara.
       "uc":   tam 3 serbestlik derecesi: rastgele 2 çiftten Kabsch/SVD, RANSAC (varsayılan 150 yineleme), içerikler üstünde
               SVD ile inceltme. Yaw R'nin ileri ekseninin azimutundan, eğim bileşenleri (pitch, roll) bilgi olarak döner.
  5. Kalite = min(1, içerik/100) * min(1, içerik/eşleşme/0,4) (0..1); içerik < 10 ise başarısız (kalite_alt 0,1).
Yön sözleşmesi: robot saat yönünde dönerse içerik ışınları azimutta azalır: dyaw = az_anahtar - az_simdi."""
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pusula_ortak import PusulaTaban, VARSAYILAN_BANT  # noqa: E402

PAD = 64


def _isin(az_der, el_der):
    az, el = np.radians(az_der), np.radians(el_der)
    return np.stack([np.cos(el) * np.sin(az), np.sin(el), np.cos(el) * np.cos(az)], -1)


def _yaw_cozum(a, b, w=None):
    """b = Ry(-d) a olacak d (derece): a, b (N,3). d = atan2(sum (a x b)_y ..., sum xz iç çarpım). Dönüş: d derece."""
    ax, az_ = a[:, 0], a[:, 2]
    bx, bz = b[:, 0], b[:, 2]
    if w is None:
        w = np.ones(len(a))
    dot = np.sum(w * (ax * bx + az_ * bz))
    cr = np.sum(w * (ax * bz - az_ * bx))          # sin(az_a - az_b); b = Ry(-d) a  =>  d = az_a - az_b
    return float(np.degrees(np.arctan2(cr, dot)))


def _ry_ters(d_der):
    """Ry(-d): ışınları saat yönünün tersine döndürür (içerik robot saat yönünde dönünce böyle kayar)."""
    c, s = np.cos(np.radians(d_der)), np.sin(np.radians(d_der))
    return np.array([[c, 0.0, -s], [0.0, 1.0, 0.0], [s, 0.0, c]])


def _acisal_artik(a, b, d_der):
    """Yaw d (derece) için Ry(-d) a ile b arasındaki açı (derece); d dizi olabilir (K,) -> (K, N)."""
    d = np.atleast_1d(np.asarray(d_der, float))
    c, s = np.cos(np.radians(d))[:, None], np.sin(np.radians(d))[:, None]
    rx = c * a[None, :, 0] - s * a[None, :, 2]
    rz = s * a[None, :, 0] + c * a[None, :, 2]
    ry = np.broadcast_to(a[None, :, 1], rx.shape)
    cosang = rx * b[None, :, 0] + ry * b[None, :, 1] + rz * b[None, :, 2]
    return np.degrees(np.arccos(np.clip(cosang, -1.0, 1.0)))


def kabsch(P, Q):
    """R (3x3) öyle ki R P_i ~ Q_i (en küçük kareler)."""
    H = P.T @ Q
    U, _, Vt = np.linalg.svd(H)
    d = np.sign(np.linalg.det(Vt.T @ U.T))
    return Vt.T @ np.diag([1.0, 1.0, d]) @ U.T


def r_yaw_egim(R):
    """R (anahtar -> şimdi ışın dönüşü). Dönüş: yaw (derece, saat yönü +), pitch, roll (derece)."""
    yaw = -np.degrees(np.arctan2(R[0, 2], R[2, 2]))
    pitch = np.degrees(np.arcsin(np.clip(R[1, 2], -1, 1)))
    roll = np.degrees(np.arctan2(R[1, 0], R[1, 1]))
    return float(yaw), float(pitch), float(roll)


class Yontem(PusulaTaban):
    def __init__(self, bilgi=None, kamera=None, genislik=1024, bant=VARSAYILAN_BANT, kip="duzlem", komut_oncel=True,
                 anahtar_mesafe_m=0.3, kalite_alt=0.1, n_nokta=800, oran=0.8, esik_der=2.0, **kw):
        self.kip = kip
        self.n_nokta = int(n_nokta)
        self.oran = float(oran)
        self.esik = float(esik_der)
        super().__init__(bilgi, kamera, genislik, bant, komut_oncel, anahtar_mesafe_m, kalite_alt, **kw)
        self._orb = cv2.ORB_create(nfeatures=self.n_nokta, scaleFactor=1.2, nlevels=4, edgeThreshold=15,
                                   fastThreshold=8, patchSize=31)
        self._bf = cv2.BFMatcher(cv2.NORM_HAMMING)
        self._v0, self._v1 = self.don.satir_araligi(self.bant[0], self.bant[1])
        self._rng = np.random.RandomState(7)
        self.egim = []                                           # kare başına (pitch, roll), yalnızca "uc" kipi
        self.son_nokta = 0
        self.son_eslesme = 0

    def _cikar(self, gri, gecerli):
        g8 = np.clip(gri + 0.5, 0, 255).astype(np.uint8)
        m = np.zeros_like(g8)
        m[self._v0:self._v1] = 255
        if gecerli is not None:
            m[gecerli < 0.5] = 0
        # sütun dikişi: iki yana sar
        gp = np.concatenate([g8[:, -PAD:], g8, g8[:, :PAD]], axis=1)
        mp = np.concatenate([m[:, -PAD:], m, m[:, :PAD]], axis=1)
        kp, des = self._orb.detectAndCompute(gp, mp)
        if des is None or not kp:
            return np.zeros((0, 3)), np.zeros((0, 32), np.uint8)
        pt = np.array([k.pt for k in kp], np.float32)
        ic = (pt[:, 0] >= PAD) & (pt[:, 0] < PAD + self.G)
        pt, des = pt[ic], des[ic]
        u = pt[:, 0] - PAD
        az = (u + 0.5) / self.G * 360.0 - 180.0
        el = 90.0 - (pt[:, 1] + 0.5) / self.don.Y * 180.0
        self.son_nokta = len(pt)
        return _isin(az, el), des

    def _esle(self, oz, anahtar_oz, merkez_der, pencere_der):
        pc, dc = oz
        pk, dk = anahtar_oz
        if len(dc) < 8 or len(dk) < 8:
            return 0.0, 0.0, False, {"eslesme": 0}
        ml = self._bf.knnMatch(dc, dk, k=2)
        iyi = [m[0] for m in ml if len(m) == 2 and m[0].distance < self.oran * m[1].distance]
        self.son_eslesme = len(iyi)
        if len(iyi) < 8:
            return 0.0, 0.0, False, {"eslesme": len(iyi)}
        b = pc[[m.queryIdx for m in iyi]]            # şimdiki
        a = pk[[m.trainIdx for m in iyi]]            # anahtar
        N = len(a)
        # tüm yaw adayları: azimut farkı
        az_a = np.degrees(np.arctan2(a[:, 0], a[:, 2]))
        az_b = np.degrees(np.arctan2(b[:, 0], b[:, 2]))
        cand = ((az_a - az_b + 180.0) % 360.0) - 180.0
        # pencere: merkez ± pencere
        fark = ((cand - merkez_der + 180.0) % 360.0) - 180.0
        izinli = np.abs(fark) <= pencere_der
        if not izinli.any():
            return 0.0, 0.0, False, {"eslesme": N, "icerik": 0}
        if self.kip == "duzlem":
            return self._duzlem(a, b, merkez_der, pencere_der, N)
        ci = np.where(izinli)[0]
        art = _acisal_artik(a, b, cand[ci])                     # (K, N)
        sayi = (art < self.esik).sum(axis=1)
        j = int(np.argmax(sayi))
        d = float(cand[ci[j]])
        # kapalı biçimli inceltme (2 tur)
        for _ in range(2):
            ic = _acisal_artik(a, b, d)[0] < self.esik
            if ic.sum() < 3:
                break
            w = np.cos(np.arcsin(np.clip(a[ic, 1], -1, 1)))      # kutuplara yakın noktaların azimutu belirsiz
            d = _yaw_cozum(a[ic], b[ic], w + 1e-3)
        ic = _acisal_artik(a, b, d)[0] < self.esik
        n_ic = int(ic.sum())
        bilgi = {"eslesme": N, "icerik": n_ic, "nokta": len(dc)}
        if self.kip == "uc" and n_ic >= 6:
            R = self._ransac3(a, b, d)
            if R is not None:
                yaw, pit, rol = r_yaw_egim(R)
                bilgi.update({"pitch": pit, "roll": rol})
                if abs(((yaw - d + 180.0) % 360.0) - 180.0) < 10.0:
                    d = yaw
                n_ic = int(self._ic3(a, b, R).sum())
                bilgi["icerik"] = n_ic
        kal = min(1.0, n_ic / 100.0) * min(1.0, n_ic / max(N, 1) / 0.4)
        return d, float(kal), n_ic >= 10, bilgi

    @staticmethod
    def _epi_hata(a, b, d_der, phi_der):
        """Düzlemsel öteleme modelinde epipolar açısal hata (radyan, sin ~ açı): d (K,), phi (L,) -> (K, L, N)."""
        d = np.radians(np.atleast_1d(d_der))[:, None]
        c, sn = np.cos(d), np.sin(d)
        rx = c * a[None, :, 0] - sn * a[None, :, 2]               # (K, N)
        rz = sn * a[None, :, 0] + c * a[None, :, 2]
        ry = np.broadcast_to(a[None, :, 1], rx.shape)
        cx = ry * b[None, :, 2] - rz * b[None, :, 1]               # (R a x b)_x
        cz = rx * b[None, :, 1] - ry * b[None, :, 0]               # (R a x b)_z
        ph = np.radians(np.atleast_1d(phi_der))[None, :, None]
        sp, cp = np.sin(ph), np.cos(ph)
        pay = np.abs(sp * cx[:, None, :] + cp * cz[:, None, :])
        td = sp * rx[:, None, :] + cp * rz[:, None, :]             # t . R a
        pay_da = np.sqrt(np.maximum(1.0 - td * td, 0.02))          # epipole yakını: payda tabanı (belirsiz noktaları sönümle)
        return pay / pay_da

    def _duzlem(self, a, b, merkez_der, pencere_der, N):
        """Düzlemsel hareket modeli ile (yaw, öteleme azimutu) en iyi MSAC çözümü; iki seviye ızgara."""
        esik = np.sin(np.radians(self.esik))
        yar = min(float(pencere_der), 90.0)
        d1 = merkez_der + np.arange(-yar, yar + 1e-9, 1.0)
        f1 = np.arange(0.0, 180.0, 6.0)
        maliyet = np.empty((len(d1), len(f1)))
        for i0 in range(0, len(d1), 16):                          # bellek için parça parça
            h = self._epi_hata(a, b, d1[i0:i0 + 16], f1)
            maliyet[i0:i0 + 16] = np.minimum(h * h, esik * esik).sum(axis=2)
        i, j = np.unravel_index(np.argmin(maliyet), maliyet.shape)
        d2 = d1[i] + np.arange(-1.5, 1.5 + 1e-9, 0.1)
        f2 = f1[j] + np.arange(-8.0, 8.0 + 1e-9, 1.0)
        h = self._epi_hata(a, b, d2, f2)
        m2 = np.minimum(h * h, esik * esik).sum(axis=2)
        i2, j2 = np.unravel_index(np.argmin(m2), m2.shape)
        d, phi = float(d2[i2]), float(f2[j2] % 180.0)
        # içerik sayısı ve alt adım inceltme (parabol, d ekseninde)
        ic = self._epi_hata(a, b, d, phi)[0, 0] < esik
        if 0 < i2 < len(d2) - 1:
            ym, y0, yp = m2[i2 - 1, j2], m2[i2, j2], m2[i2 + 1, j2]
            den = ym - 2 * y0 + yp
            if den > 1e-12:
                d += float(np.clip(0.5 * (ym - yp) / den, -1, 1)) * 0.1
        n_ic = int(ic.sum())
        kal = min(1.0, n_ic / 100.0) * min(1.0, n_ic / max(N, 1) / 0.4)
        return d, float(kal), n_ic >= 10, {"eslesme": N, "icerik": n_ic, "phi": phi}

    def _ic3(self, a, b, R):
        ra = a @ R.T
        ang = np.degrees(np.arccos(np.clip(np.sum(ra * b, axis=1), -1, 1)))
        return ang < self.esik

    def _ransac3(self, a, b, d0, it=150):
        """3 serbestlik dereceli RANSAC (2 çiftten Kabsch). Başlangıç tahmini d0'a (yaw) yakın çözümler tercih edilir."""
        N = len(a)
        en_iyi, enR = -1, None
        for _ in range(it):
            i = self._rng.choice(N, 2, replace=False)
            P, Q = a[i], b[i]
            # 2 noktadan: üçüncü eksen çarpımla
            P3 = np.vstack([P, np.cross(P[0], P[1])])
            Q3 = np.vstack([Q, np.cross(Q[0], Q[1])])
            R = kabsch(P3, Q3)
            y, _, _ = r_yaw_egim(R)
            if abs(((y - d0 + 180.0) % 360.0) - 180.0) > 20.0:
                continue
            s = int(self._ic3(a, b, R).sum())
            if s > en_iyi:
                en_iyi, enR = s, R
        if enR is None:
            return None
        for _ in range(2):
            ic = self._ic3(a, b, enR)
            if ic.sum() < 4:
                break
            enR = kabsch(a[ic], b[ic])
        return enR

    def guncelle(self, kare_bgr, t_ms, komut):
        r = super().guncelle(kare_bgr, t_ms, komut)
        if self.kip == "uc":
            self.egim.append((self.son_bilgi.get("pitch", np.nan), self.son_bilgi.get("roll", np.nan)))
        return r
