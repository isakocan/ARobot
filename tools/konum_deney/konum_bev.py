"""Zemin düzleminden METRİK öteleme: kuş bakışı (BEV) görüntüde zemin noktalarının hareketi.

Fikir: kamera yerden h = 0,255 m yukarıda ve zemin düz. Ufkun a kadar altında, azimut z yönünde görülen zemin noktası kameradan
r = h / tan(a) uzaklıktadır, yani (sag, ileri) = (r sin z, r cos z) metre. Eşdikdörtgen (ya da çift balık gözü) görüntü bu bağıntıyla
METRİK bir kuş bakışı görüntüye (2 cm/piksel, kameraya göre sabit eksenler) çevrilir. Zemin üstündeki doku BEV'de bir katı
cisim gibi hareket eder: kamera dünyada t kadar ilerleyip yön Δ değiştirirse zemin noktaları BEV'de
        p_anahtar = R(Δ) p_simdi + t_anahtar        (anahtar çerçevede, R = [[cosΔ, sinΔ], [-sinΔ, cosΔ]], Δ = yaw_simdi - yaw_anahtar)
ile ilişkilidir. Δ'yı görsel pusula (M2D) verir; geriye yalnız t kalır ve her ORB eşleşmesi t için bir OY verir
(t_j = p_anahtar_j - R p_simdi_j). En çok oy alan küme (RANSAC, örnek boyutu 1) ve ortalaması = öteleme. Zemin DIŞI noktalar
(duvar, mobilya) BEV'de yanlış yere düşer, ama kendi aralarında tutarsız oldukları için çoğunlukla oy çoğunluğunu bozmaz.
Anahtar kare: hata yalnız anahtar değişirken birikir (t anahtara göre ölçülür, kare farkı değil).
Dönüş (kare başına): artım (d_sag, d_ileri) ÖNCEKİ karenin kamera çerçevesinde, metre; ok bayrağı; iç eşleşme sayısı.
Girdi: theta için çift balık gözü doğrudan BEV'e (tek remap tablosu, canlı sistemin bedeli); sim için eşdikdörtgen.
Robot gövdesi maskesi, r < R_MIN ve (varsa) tarama sınırından ötesi dışlanır."""
import os
import sys
import time

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import konum_ortak as ko  # noqa: E402
from kamera import isin_esd_piksel  # noqa: E402


class BevTablo:
    """BEV piksel -> kaynak görüntü remap tabloları ve geçerlilik maskesi (bir kez kurulur)."""

    def __init__(self, tur, kamera, h=ko.KAM_H, res=0.02, r_min=0.32, r_max=1.45, esd=(1280, 640), sim_boyut=(1536, 768)):
        self.res, self.h, self.tur = res, h, tur
        self.R = r_max
        self.n = int(2 * r_max / res)
        n = self.n
        ii, jj = np.meshgrid(np.arange(n), np.arange(n))
        self.x = ((ii + 0.5 - n / 2) * res).astype(np.float32)          # sag
        self.y = ((n / 2 - jj - 0.5) * res).astype(np.float32)          # ileri
        r = np.hypot(self.x, self.y)
        self.r = r
        az = np.arctan2(self.x, self.y)
        a = np.arctan2(h, r)
        d = np.stack([np.cos(a) * np.sin(az), -np.sin(a), np.cos(a) * np.cos(az)], -1)
        halka = (r >= r_min) & (r <= r_max)
        self.az_der = (np.degrees(az) % 360.0).astype(np.float32)
        self.az_idx = (np.rint(self.az_der).astype(np.int64)) % 360
        if tur == "theta":
            self.kam = kamera
            self.tab = []
            for k in (0, 1):
                u, v, _ = kamera.robot_to_lens(d.reshape(-1, 3), k)
                self.tab.append((u.reshape(n, n).astype(np.float32), v.reshape(n, n).astype(np.float32)))
            w1, w2 = kamera.agirliklar(d.reshape(-1, 3))
            top = np.maximum(w1 + w2, 1e-4)
            self.k1 = (w1 / top).reshape(n, n).astype(np.float32)
            self.k2 = (w2 / top).reshape(n, n).astype(np.float32)
            kapsam = (w1 + w2).reshape(n, n) > 0
            govde = cv2.imread(os.path.join(ko.KLASOR, "kamera_maske.png"), cv2.IMREAD_GRAYSCALE)
            m1 = cv2.remap(govde, *self.tab[0], cv2.INTER_NEAREST).astype(np.float32) / 255.0
            m2 = cv2.remap(govde, *self.tab[1], cv2.INTER_NEAREST).astype(np.float32) / 255.0
            gv = (self.k1 * m1 + self.k2 * m2) > 0.2
            gv = cv2.dilate(gv.astype(np.uint8), np.ones((9, 9), np.uint8)) > 0
            self.gecerli = halka & kapsam & ~gv
        else:
            u, v = isin_esd_piksel(d, sim_boyut[0], sim_boyut[1])
            self.u, self.v = u.astype(np.float32), v.astype(np.float32)
            self.gecerli = halka

    def uret(self, kare_bgr):
        """Kare (theta: çift balık gözü BGR; sim: eşdikdörtgen BGR) -> BEV gri uint8."""
        if self.tur == "theta":
            g = cv2.cvtColor(kare_bgr, cv2.COLOR_BGR2GRAY)
            a = cv2.remap(g, self.tab[0][0], self.tab[0][1], cv2.INTER_LINEAR)
            b = cv2.remap(g, self.tab[1][0], self.tab[1][1], cv2.INTER_LINEAR)
            return np.clip(a * self.k1 + b * self.k2 + 0.5, 0, 255).astype(np.uint8)
        g = cv2.cvtColor(kare_bgr, cv2.COLOR_BGR2GRAY)
        return cv2.remap(g, self.u, self.v, cv2.INTER_LINEAR, borderMode=cv2.BORDER_WRAP)


def _rot(delta_der):
    c, s = np.cos(np.radians(delta_der)), np.sin(np.radians(delta_der))
    return np.array([[c, s], [-s, c]])


def tarama_maskesi(tablo, mesafe, guven, pay_m=0.15, belirsiz_m=0.6, guven_min=0.2):
    """Zemin taramasından BEV izin maskesi (uint8 0/255): azimutta zeminin bittiği yerden pay_m öncesi izinli; güvensiz azimutta yalnız belirsiz_m.
    Amaç: duvar/mobilya özelliklerinin (zemin düzleminde olmadığı için BEV'de YANLIŞ uzaklığa düşer, öteleme ölçeğini şişirir) dışlanması."""
    d = np.where((np.asarray(guven) >= guven_min) & np.isfinite(mesafe), mesafe, belirsiz_m)
    izin = tablo.r < (d[tablo.az_idx] - pay_m)
    return izin.astype(np.uint8) * 255


class BevOtelemeci:
    """Kare kare metrik öteleme artımı. guncelle(kare, yon_derece) -> (d_sag, d_ileri, ok, ic_sayisi)."""

    def __init__(self, tablo, n_nokta=500, oran=0.8, tol_m=0.035, min_ic=8, anahtar_t_m=0.30, anahtar_yaw_der=20.0,
                 ic_oran_min=0.25, kapi_m=0.10):
        self.T = tablo
        self.tol, self.min_ic, self.oran = tol_m, min_ic, oran
        self.anahtar_t, self.anahtar_yaw, self.ic_oran_min = anahtar_t_m, anahtar_yaw_der, ic_oran_min
        self.kapi_m = kapi_m
        self.orb = cv2.ORB_create(nfeatures=n_nokta, scaleFactor=1.2, nlevels=3, edgeThreshold=13, fastThreshold=6, patchSize=21)
        self.bf = cv2.BFMatcher(cv2.NORM_HAMMING)
        self.maske = (tablo.gecerli.astype(np.uint8) * 255)
        self.sifirla()
        self.son_ms = 0.0
        self.bev_ms = 0.0

    def sifirla(self):
        self.anahtar = None          # (pt (N,2) sag/ileri m, des, yaw)
        self.t_onceki = np.zeros(2)  # önceki karenin anahtara göre t'si
        self.yon_onceki = 0.0
        self.anahtar_degisim = 0
        self.son_t = np.zeros(2)
        self.son_ok = False
        self.v_son = np.zeros(2)
        self.basarisiz = 0

    def _ozellik(self, bev, ek_maske=None):
        m = self.maske if ek_maske is None else (self.maske & ek_maske)
        g = cv2.GaussianBlur(bev, (0, 0), 0.8)
        kp, des = self.orb.detectAndCompute(g, m)
        if des is None or len(kp) == 0:
            return np.zeros((0, 2), np.float32), np.zeros((0, 32), np.uint8)
        pt = np.array([k.pt for k in kp], np.float32)
        n = self.T.n
        sag = (pt[:, 0] + 0.0 - n / 2) * self.T.res
        ileri = (n / 2 - pt[:, 1]) * self.T.res
        return np.stack([sag, ileri], 1), des

    def guncelle(self, kare, yon_derece, ek_maske=None):
        a = time.perf_counter()
        bev = self.T.uret(kare)
        self.bev_ms = (time.perf_counter() - a) * 1000.0
        pt, des = self._ozellik(bev, ek_maske)
        d_sag = d_ileri = 0.0
        ok = False
        ic = 0
        if self.anahtar is None:
            self.anahtar = (pt, des, float(yon_derece))
            self.t_onceki = np.zeros(2)
            self.yon_onceki = float(yon_derece)
            self.son_ms = (time.perf_counter() - a) * 1000.0
            return 0.0, 0.0, True, 0
        pk, dk, yk = self.anahtar
        delta = ((yon_derece - yk + 180.0) % 360.0) - 180.0
        t = None
        N = 0
        if len(des) >= 8 and len(dk) >= 8:
            ml = self.bf.knnMatch(des, dk, k=2)
            iyi = [m[0] for m in ml if len(m) == 2 and m[0].distance < self.oran * m[1].distance]
            N = len(iyi)
            if N >= self.min_ic:
                pc = pt[[m.queryIdx for m in iyi]].astype(np.float64)
                pa = pk[[m.trainIdx for m in iyi]].astype(np.float64)
                R = _rot(delta)
                oy = pa - pc @ R.T                           # (N,2): her eşleşmenin t oyu
                # fiziksel kapı: robot bir karede en çok ~0,6 m/sn * dt gider; öncekine göre kapı dışındaki oylar (tekrarlı doku
                # kaymaları: parke tahtası periyodu ~0,3 m) sayılmaz
                oy = oy[np.linalg.norm(oy - self.t_onceki, axis=1) < self.kapi_m] if self.kapi_m else oy
                if len(oy) < self.min_ic:
                    oy = np.zeros((0, 2))
                fark = np.linalg.norm(oy[:, None, :] - oy[None, :, :], axis=2) < self.tol
                sayi = fark.sum(1)
                j = int(np.argmax(sayi)) if len(oy) else 0
                ic_mask = fark[j] if len(oy) else np.zeros(0, bool)
                ic = int(ic_mask.sum())
                if len(oy) and ic >= self.min_ic and ic >= self.ic_oran_min * N:
                    t = oy[ic_mask].mean(0)
                    t = oy[np.linalg.norm(oy - t, axis=1) < self.tol].mean(0)
        Rp = _rot(((self.yon_onceki - yk + 180.0) % 360.0) - 180.0)
        if t is not None:
            ok = True
            dk_ = t - self.t_onceki
            d = Rp.T @ dk_                                   # anahtar çerçevesinden önceki karenin çerçevesine
            d_sag, d_ileri = float(d[0]), float(d[1])
            self.v_son = dk_
            self.basarisiz = 0
            self.t_onceki = t
            self.son_t = t
            if np.linalg.norm(t) > self.anahtar_t or abs(delta) > self.anahtar_yaw or ic < 2 * self.min_ic:
                self.anahtar = (pt, des, float(yon_derece))
                self.t_onceki = np.zeros(2)
                self.anahtar_degisim += 1
        else:
            # eşleşme yok: kısa süre (en çok 5 kare) son artım sürdürülür (sabit hız tahmini; ok=False işaretlenir), sonra sıfır.
            self.basarisiz += 1
            if self.basarisiz <= 5:
                dk_ = self.v_son
                d = Rp.T @ dk_
                d_sag, d_ileri = float(d[0]), float(d[1])
                self.t_onceki = self.t_onceki + dk_
            else:
                self.v_son = np.zeros(2)
                if len(des) >= 20:                           # uzun kopukluk: bu kareden yeni anahtar (öteleme ölçülemedi)
                    self.anahtar = (pt, des, float(yon_derece))
                    self.t_onceki = np.zeros(2)
                    self.anahtar_degisim += 1
        self.yon_onceki = float(yon_derece)
        self.son_ok = ok
        self.son_ms = (time.perf_counter() - a) * 1000.0
        return d_sag, d_ileri, ok, ic


def bev_onbellek_yolu(tur, ad, yon_komutlu, tarama=True):
    return os.path.join(ko.ONBELLEK, "bev%s_%s_%s_%s.npz" % ("T" if tarama else "", "komutlu" if yon_komutlu else "komutsuz", tur, ad))


def bev_hesapla(tur, ad, yon_komutlu=False, yeniden=False, tarama=True, tarama_pay=0.25):
    """Kaydın her karesi için BEV artımı (M2D yaw'ı kullanır). Önbellekli. tarama=True: özellikler zemin taramasının
    (kareden önceki son tarama, her 5. kare; 0-4 kare yaşlı) zemin sınırı içine kısıtlanır."""
    import konum_yon_onbellek as yon_o
    from kayit import Kayit
    from kamera import Kamera
    p = bev_onbellek_yolu(tur, ad, yon_komutlu, tarama)
    if os.path.exists(p) and not yeniden:
        z = np.load(p)
        return {k: z[k] for k in z.files}
    yz = yon_o.yukle(yon_komutlu, tur, ad)
    kay = Kayit(os.path.join(ko.KAYITLAR, tur, ad))
    kam = Kamera.bilgiden(kay.bilgi)
    tab = BevTablo(tur, kam)
    oz = BevOtelemeci(tab)
    n = len(kay)
    D = np.full((n, 2), np.nan)
    OK = np.zeros(n, bool)
    IC = np.zeros(n, np.int32)
    MS = np.zeros(n)
    t0 = time.time()
    tz = None
    if tarama:
        zz = np.load(os.path.join(ko.zo.CIKTI, "tarama", "model_gorunum", tur, ad + ".npz"))
        tz = {k: zz[k] for k in zz.files}
        tk = tz["kare"]
    for i in range(n):
        em = None
        if tz is not None:
            j = max(int(np.searchsorted(tk, i, side="right")) - 1, 0)       # NEDENSEL: yalnız kare i ve öncesindeki son tarama
            em = tarama_maskesi(tab, tz["mesafe"][j], tz["guven"][j], tarama_pay)
        ds, di, ok, ic = oz.guncelle(kay.resim(i), float(yz["yon"][i]), em)
        D[i] = (ds, di)
        OK[i] = ok
        IC[i] = ic
        MS[i] = oz.son_ms
        if i % 300 == 0:
            print("  bev %s %s %d/%d (%.0f sn)" % (tur, ad, i, n, time.time() - t0), flush=True)
    os.makedirs(ko.ONBELLEK, exist_ok=True)
    z = {"kare": kay.kare_numaralari, "d": D, "ok": OK, "ic": IC, "ms": MS}
    np.savez_compressed(p, **z)
    return z
