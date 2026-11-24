"""Parçacık süzgeci ile konum (Monte Carlo yerelleştirme; Jetson ve PC, Python 3.6 + numpy).

Robotun nerede olabileceği yüzlerce aday (x, z, yön) ile tutulur. Her karede:
  1. hareket: pusula / zemin izi / motor komutuyla her aday ilerler, gürültüyle yayılır (hızlı dönüşte yön çok yayılır)
  2. harita: telefon haritasında açık olmayan yerdeki aday zayıflar (robot eşyanın içinde olamaz)
  3. zemin sınırı (sinir.py): görüntü, adayın yerinden beklenen zemin bitişiyle ne kadar uyuşuyorsa o kadar güçlenir
  4. ArUco: görülürse görülme yönü / uzaklığı / yüzey yönü ile
Zayıflayan adaylar atılır, güçlüler çoğaltılır. Sonuç: ağırlıklı ortalama ve yayılım (güven).
Kalman süzgecinden farkı: birden çok olası yeri aynı anda tutabilir; kaybolunca haritanın her yerine dağılıp
yeniden toplanabilir.
"""
import math

import numpy as np

N = 400
S_ILERI = (0.01, 0.15)          # ileri hareket gürültüsü: a + b * |adım|
S_YON = (math.radians(0.4), 0.06)   # dönüş gürültüsü: a + b * |dönüş|; güvensiz dönüşte YON_GUVENSIZ
YON_GUVENSIZ = 0.5              # güvensiz dönüşte dönüşün bu oranı kadar sapma (hızlı dönüşte bulanık kare)
HARITA_DISI = 0.05              # açık olmayan hücredeki adayın ağırlık çarpanı
BILINMEYEN = 0.3                # haritada bilinmeyen hücre
SINIR_BETA = 8.0                # zemin sınırı puanının üssü: exp(beta * (puan - en iyi))
ISARET_S_YON = math.radians(2.0)
ISARET_S_UZ = (0.03, 0.03)
ISARET_S_NORMAL = (math.radians(6.0), math.radians(8.0))


def _aci(a):
    return (a + np.pi) % (2 * np.pi) - np.pi


class Parcacik:
    def __init__(self, harita, n=N, tohum=0):
        self.H = harita
        self.n = n
        self.r = np.random.RandomState(tohum)
        self.x = np.zeros(n); self.z = np.zeros(n); self.yaw = np.zeros(n)
        self.w = np.full(n, 1.0 / n)
        self.var = False

    # ------------------------------------------------------------ başlatma
    def baslat(self, x, z, yaw, s_poz=0.1, s_yaw=math.radians(10)):
        n = self.n
        self.x = x + self.r.randn(n) * s_poz; self.z = z + self.r.randn(n) * s_poz
        self.yaw = yaw + self.r.randn(n) * s_yaw
        self.w[:] = 1.0 / n; self.var = True

    def baslat_genel(self):
        """Nerede olduğu hiç bilinmiyor: haritanın açık zemininin her yerine, her yöne."""
        m = self.H.acik_merkezler()
        k = self.r.randint(0, len(m), self.n)
        self.x = m[k, 0] + self.r.uniform(-0.025, 0.025, self.n); self.z = m[k, 1] + self.r.uniform(-0.025, 0.025, self.n)
        self.yaw = self.r.uniform(-np.pi, np.pi, self.n)
        self.w[:] = 1.0 / self.n; self.var = True

    # ------------------------------------------------------------ adımlar
    def hareket(self, ileri, yan, dyaw, yon_guvenli=True, duruyor=False):
        """Robot çerçevesinde ileri / sağa (m) ve dönüş (radyan, sağa +)."""
        n = self.n
        if duruyor and abs(dyaw) < math.radians(0.5) and math.hypot(ileri, yan) < 0.005:
            return
        sy = S_YON[0] + S_YON[1] * abs(dyaw)
        if not yon_guvenli:
            sy += YON_GUVENSIZ * abs(dyaw)
        yeni = self.yaw + dyaw + self.r.randn(n) * sy
        orta = (self.yaw + yeni) / 2
        d = math.hypot(ileri, yan)
        si = S_ILERI[0] + S_ILERI[1] * d
        f = ileri + self.r.randn(n) * si; s = yan + self.r.randn(n) * si * 0.5
        # yerel (sağ, ileri) -> dünya: ileri (sin, cos), sağ (cos, -sin)
        self.x = self.x + f * np.sin(orta) + s * np.cos(orta)
        self.z = self.z + f * np.cos(orta) - s * np.sin(orta)
        self.yaw = _aci(yeni)

    def harita(self):
        h = self.H.hucre(self.x, self.z)
        self.w *= np.where(h == 1, 1.0, np.where(h == 0, BILINMEYEN, HARITA_DISI))
        self._normal()

    def sinir(self, S, beta=None):
        """S: hazırlanmış sinir.Sinir. -> en iyi puan (teşhis)."""
        beta = SINIR_BETA if beta is None else beta
        p = S.puan(self.H, self.x, self.z, self.yaw)
        self.w *= np.exp(beta * (p - p.max()))
        self._normal()
        return float(p.max())

    def isaret(self, E, gx, gz, gaci, r):
        """E: işaretin ev pozu (x, z, aci). Gözlem robot çerçevesinde (gx sağ, gz ileri), gaci: yüzey yönü robota göre."""
        dx, dz = E[0] - self.x, E[1] - self.z
        c, s = np.cos(-self.yaw), np.sin(-self.yaw)
        lx = dx * c + dz * s; lz = -dx * s + dz * c
        e_yon = _aci(np.arctan2(lx, lz) - math.atan2(gx, gz)) / ISARET_S_YON
        e_uz = (np.hypot(lx, lz) - r) / (ISARET_S_UZ[0] + ISARET_S_UZ[1] * r)
        e_n = _aci(E[2] - self.yaw - gaci) / (ISARET_S_NORMAL[0] + ISARET_S_NORMAL[1] * max(0.0, r - 0.8))
        # aykırıya dayanıklı (Cauchy): tek yanlış ölçüm bütün adayları silmesin
        self.w *= 1.0 / (1 + e_yon ** 2) / (1 + e_uz ** 2) / (1 + 0.5 * e_n ** 2)
        self._normal()

    def _normal(self):
        t = self.w.sum()
        if not np.isfinite(t) or t <= 0:
            self.w[:] = 1.0 / self.n
        else:
            self.w /= t

    def neff(self):
        return 1.0 / float((self.w ** 2).sum())

    def yeniden(self, oran=0.5):
        """Etkin aday sayısı düşünce sistematik yeniden örnekleme."""
        if self.neff() > oran * self.n:
            return False
        c = np.cumsum(self.w); c[-1] = 1.0
        u = (self.r.uniform() + np.arange(self.n)) / self.n
        k = np.searchsorted(c, u)
        self.x, self.z, self.yaw = self.x[k], self.z[k], self.yaw[k]
        self.w[:] = 1.0 / self.n
        return True

    def ozet(self):
        """-> (x, z, yaw, konum yayılımı m, yön yayılımı radyan)."""
        w = self.w
        x = float((w * self.x).sum()); z = float((w * self.z).sum())
        cy = float((w * np.cos(self.yaw)).sum()); sy = float((w * np.sin(self.yaw)).sum())
        yaw = math.atan2(sy, cy)
        sp = math.sqrt(float((w * ((self.x - x) ** 2 + (self.z - z) ** 2)).sum()))
        R = min(1.0, math.hypot(cy, sy))
        syaw = math.sqrt(max(0.0, -2 * math.log(max(R, 1e-9))))
        return x, z, yaw, sp, syaw
