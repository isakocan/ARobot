"""Yeniden bulucu (kidnapped robot) çekirdeği. Robotun haritalama sırasında SAKLADIĞI şeylerden yalnızca bunlar kullanılır:
  * kendi doluluk haritası (log-odds ızgara, kendi pozlarıyla kurulmuş),
  * anahtar kareler: ORB özellikleri (3B ışın + 32 bayt tanımlayıcı) + o an KESTİRİLEN poz (gerçek poz değil),
Sorgu: tek bir panorama karesi (ve onun zemin taraması). Gerçek poz sorguya HİÇ girmez.

Bileşenler (her biri ayrı ölçülür):
  K  Anahtar kare ile yer tanıma: sorgu tanımlayıcıları tüm anahtar karelerle Hamming + Lowe oran testiyle eşlenir (sayı = ilk sıralama),
     en iyi KONUM karelerinden birkaçı M2D (düzlemsel epipolar yaw+öteleme yönü, yontem_pusula_nokta._duzlem) ile geometrik doğrulanır.
     Yaw dönüşü TÜM 360 derece aranır (panorama, robot hangi yöne bakarsa baksın aynı yeri görür): yaw = anahtar_yaw + dyaw.
  P  Konum: anahtar kare konumu (en iyi iç nokta) ya da doğrulanan anahtar karelerin ağırlıklı ortalaması.
  S  Tarama inceltmesi: sorgu taraması haritaya, anahtar kare çevresinde küçük pencerede eşlenir (konum +-0,6 m, yaw +-4 derece).
  G  Genel tarama arama: tarama haritanın TÜM bilinen boş hücrelerinde ve tüm yaw değerlerinde taranır (görüntüsüz ikinci görüş).
  Q  Dizi süzgeci (ayrı sınıf, sinif Dizi): hareket telafili çoklu varsayım doğrulaması.
Karar: ayrı fonksiyon (karar), eşikler tek bir sabit kümede (ESIK)."""
import os
import sys
import time
from dataclasses import dataclass, field

import cv2
import numpy as np
from scipy import ndimage as ndi

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import konum_ortak as ko  # noqa: E402
import konum_tarama as kt  # noqa: E402
import yeniden_bul_ortak as yo  # noqa: E402


# ----------------------------------------------------------------------------- robotun hafızası
@dataclass
class Hafiza:
    iz: object                       # harita_kur.Izgara (log-odds)
    kf_poz: np.ndarray               # (K,3) kestirilen poz (x, y, yaw)
    kf_kare: np.ndarray              # (K,) kaynak kare numarası
    kf_t: np.ndarray                 # (K,) kaynak zaman ms
    kf_R: list = field(default_factory=list)
    kf_D: list = field(default_factory=list)
    F_kaba: np.ndarray = None
    F_ince: np.ndarray = None
    bos_hucre: np.ndarray = None     # (M,2) bilinen boş hücre indeksleri (r, c), 0,1 m aralıklı
    alan: np.ndarray = None          # haritalı alan maskesi

    def bayt(self):
        """Anahtar kare özellikleri + poz + harita (uint8 durum, 1 bayt/hücre) için saklama boyutu."""
        kf = sum(r.shape[0] * (12 + 32) for r in self.kf_R)            # 3 float32 ışın + 32 bayt tanımlayıcı
        kf_f16 = sum(r.shape[0] * (6 + 32) for r in self.kf_R)         # ışınlar float16 olsa
        return dict(anahtar_kare=len(self.kf_R), kf_bayt=kf, kf_bayt_f16=kf_f16, harita_bayt=int(self.iz.lo.size),
                    poz_bayt=len(self.kf_R) * 12)


def alan_hazirla(iz, f_kes, sigma):
    dolu = iz.lo >= 0.85
    d = ndi.distance_transform_edt(~dolu) * iz.h
    return np.where(d < f_kes, np.exp(-0.5 * (d / sigma) ** 2), 0.0).astype(np.float32)


def hafiza_kur(aralik_m=0.30, yeni_yaricap_m=0.20, onbellek=True):
    """Haritalama koşusundan (ev_turu, D pozları) hafıza kurar. Anahtar kare: son anahtar kareden >= aralik_m yol ve
    mevcut hiçbir anahtar kareye < yeni_yaricap_m yakın değil."""
    import pickle
    anahtar = os.path.join(yo.ONBELLEK, "hafiza_%.2f_%.2f.pkl" % (aralik_m, yeni_yaricap_m))
    if onbellek and os.path.exists(anahtar):
        with open(anahtar, "rb") as f:
            return pickle.load(f)
    a = yo.HARITA_KAYDI
    iz = yo.robot_haritasi()
    d = yo.csv_poz(yo.KAYITLAR_TUM[a]["d"])
    sira = {int(k): i for i, k in enumerate(d["kare"])}
    dep = yo.OzDeposu(a)
    tar = yo.tarama_oku(a)
    poz, kare, tt, R, D = [], [], [], [], []
    son = None
    for k in tar["kare"]:
        i = sira[int(k)]
        p = np.array([d["x"][i], d["y"][i], d["yaw"][i]])
        if son is not None:
            if np.hypot(*(p[:2] - son)) < aralik_m:
                continue
            if np.hypot(*(np.array(poz)[:, :2] - p[:2]).T).min() < yeni_yaricap_m:
                continue
        r, dd = dep.al(k)
        poz.append(p)
        kare.append(int(k))
        tt.append(float(d["t_ms"][i]))
        R.append(r)
        D.append(dd)
        son = p[:2].copy()
    H = Hafiza(iz, np.array(poz), np.array(kare), np.array(tt), R, D)
    H.F_kaba = alan_hazirla(iz, 0.5, 0.18)
    H.F_ince = alan_hazirla(iz, 0.25, 0.07)
    bos = iz.lo <= -0.4
    rr, cc = np.where(bos)
    ok = (rr % 2 == 0) & (cc % 2 == 0)
    H.bos_hucre = np.stack([rr[ok], cc[ok]], 1)
    H.alan = yo.haritali_alan(iz, np.stack([d["x"], d["y"]], 1))
    if onbellek:
        os.makedirs(yo.ONBELLEK, exist_ok=True)
        with open(anahtar, "wb") as f:
            pickle.dump(H, f)
    return H


# ----------------------------------------------------------------------------- M2D (rotasyondan bağımsız)
def m2d_coz(oz_q, oz_k, y=None, oran=0.8):
    """Sorgu ve anahtar kare özellikleri arasında geometrik doğrulama. 360 derece yaw taranır:
    olası eşleşmelerin azimut farklarının histogramının en yüksek iki tepesinde M2D'nin düzlemsel epipolar çözümü çalışır.
    Dönüş: dict(dyaw (sorgu = anahtar + dyaw, saat yönü +), icerik, eslesme, phi) ya da icerik=0."""
    y = y or yo.pusula_yontemi()
    pc, dc = oz_q
    pk, dk = oz_k
    bos = dict(dyaw=0.0, icerik=0, eslesme=0, phi=0.0)
    if len(dc) < 8 or len(dk) < 8:
        return bos
    ml = y._bf.knnMatch(dc, dk, k=2)
    iyi = [m[0] for m in ml if len(m) == 2 and m[0].distance < oran * m[1].distance]
    if len(iyi) < 8:
        bos["eslesme"] = len(iyi)
        return bos
    b = pc[[m.queryIdx for m in iyi]]
    a = pk[[m.trainIdx for m in iyi]]
    N = len(a)
    az_a = np.degrees(np.arctan2(a[:, 0], a[:, 2]))
    az_b = np.degrees(np.arctan2(b[:, 0], b[:, 2]))
    cand = ((az_a - az_b + 180.0) % 360.0) - 180.0
    h, _ = np.histogram(cand % 360.0, bins=72, range=(0, 360))
    hh = h + np.roll(h, 1) + np.roll(h, -1)               # 15 derecelik pencere toplamı
    en = bos
    kullanilan = []
    for _ in range(2):
        j = int(np.argmax(hh))
        if hh[j] < 5:
            break
        merkez = ((j * 5.0 + 2.5 + 180.0) % 360.0) - 180.0
        hh[(np.arange(-3, 4) + j) % 72] = -1
        d, kal, tamam, bi = y._duzlem(a, b, merkez, 12.0, N)
        if bi["icerik"] > en["icerik"]:
            en = dict(dyaw=float(d), icerik=int(bi["icerik"]), eslesme=N, phi=float(bi["phi"]))
    if en["icerik"] == 0:
        en["eslesme"] = N
    return en


def goz_sayisi(oz_q, oz_k, y=None, oran=0.8):
    """Ucuz ilk sıralama: oran testini geçen eşleşme sayısı."""
    y = y or yo.pusula_yontemi()
    dc, dk = oz_q[1], oz_k[1]
    if len(dc) < 8 or len(dk) < 8:
        return 0
    ml = y._bf.knnMatch(dc, dk, k=2)
    return sum(1 for m in ml if len(m) == 2 and m[0].distance < oran * m[1].distance)


# ----------------------------------------------------------------------------- tarama skoru / inceltme
def _bilin(f, c, r):
    return kt._bilin(f, c, r)


def nokta_al(mesafe, guven, menzil, en_cok=140):
    p = kt.EsleParam()
    nok = kt.tarama_noktalari(mesafe, guven, menzil, p)
    if len(nok) > en_cok:
        nok = nok[np.linspace(0, len(nok) - 1, en_cok).astype(int)]
    return nok


def _dunya(nok, X, Y, YAW):
    """(K,) poz dizileri -> dünya noktaları (K,N)."""
    a = np.radians(YAW)[:, None]
    wx = X[:, None] + nok[None, :, 0] * np.cos(a) + nok[None, :, 1] * np.sin(a)
    wy = Y[:, None] - nok[None, :, 0] * np.sin(a) + nok[None, :, 1] * np.cos(a)
    return wx, wy


def skor_en_yakin(H, F, nok, X, Y, YAW):
    """En yakın hücre örneklemesi (hızlı, kaba arama). Dönüş (K,) nokta başına ortalama alan değeri."""
    iz = H.iz
    wx, wy = _dunya(nok, X, Y, YAW)
    r, c = iz.hucre_idx(wx, wy)
    ok = (r >= 0) & (r < iz.Hh) & (c >= 0) & (c < iz.W)
    v = F[np.clip(r, 0, iz.Hh - 1), np.clip(c, 0, iz.W - 1)] * ok
    return v.mean(1)


def skor_bilin(H, nok, X, Y, YAW):
    iz = H.iz
    wx, wy = _dunya(nok, X, Y, YAW)
    c = (wx - iz.x0) / iz.h - 0.5
    r = (iz.y0 + iz.Hh * iz.h - wy) / iz.h - 0.5
    return _bilin(H.F_ince, c, r).mean(1)


def incelt(H, nok, poz0, pen_xy=0.6, pen_yaw=4.0, oncul_sigma=0.5, oncul_w=0.05):
    """Küçük pencerede korelatif tarama eşleştirme: kaba (5 cm, 1 derece) sonra ince (1,25 cm, 0,25 derece).
    Dönüş (poz, skor (öncülsüz ortalama alan), toplam)."""
    n = len(nok)
    if n < 30:
        return poz0, 0.0, -1e9

    def tara(merkez, dxy, adim_xy, dyaw, adim_yaw):
        dx = np.arange(-dxy, dxy + 1e-9, adim_xy)
        dyw = np.arange(-dyaw, dyaw + 1e-9, adim_yaw)
        DX, DY, DW = np.meshgrid(dx, dx, dyw, indexing="ij")
        DX, DY, DW = DX.ravel(), DY.ravel(), DW.ravel()
        X, Y, W = merkez[0] + DX, merkez[1] + DY, merkez[2] + DW
        s = skor_bilin(H, nok, X, Y, W)
        d2 = (X - poz0[0]) ** 2 + (Y - poz0[1]) ** 2
        top = s - oncul_w * 0.5 * d2 / oncul_sigma ** 2
        j = int(np.argmax(top))
        return (X[j], Y[j], W[j]), float(s[j]), float(top[j])

    p1, s1, t1 = tara(poz0, pen_xy, 0.05, pen_yaw, 1.0)
    p2, s2, t2 = tara(p1, 0.05, 0.0125, 1.0, 0.25)
    return (float(p2[0]), float(p2[1]), float(p2[2] % 360.0)), s2, t2


def genel_arama(H, nok, adim_yaw=4.0, ust=12, bastirma_m=0.5, bastirma_yaw=20.0):
    """Taramanın haritadaki tüm bilinen boş hücrelerde ve tüm yaw değerlerinde kaba taraması, sonra en iyi adayların
    inceltilmesi. Dönüş: aday listesi [(poz, skor)] azalan skorla, en iyi `ust` kadar (konum/yaw bastırmalı)."""
    iz = H.iz
    rc = H.bos_hucre
    X, Y = iz.hucre_merkezi(rc[:, 0], rc[:, 1])
    yaws = np.arange(0.0, 360.0, adim_yaw)
    sk = np.empty((len(yaws), len(X)), np.float32)
    for j, yw in enumerate(yaws):
        sk[j] = skor_en_yakin(H, H.F_kaba, nok, X, Y, np.full(len(X), yw))
    ak = sk.ravel()
    sira = np.argsort(-ak)
    secilen = []
    for s in sira[:4000]:
        j, i = divmod(int(s), len(X))
        if any(np.hypot(X[i] - q[0], Y[i] - q[1]) < bastirma_m and abs(ko.sar(yaws[j] - q[2])) < bastirma_yaw for q in secilen):
            continue
        secilen.append((float(X[i]), float(Y[i]), float(yaws[j]), float(ak[s])))
        if len(secilen) >= ust:
            break
    sonuc = []
    for (x, y, yw, s0) in secilen:
        p, s, t = incelt(H, nok, (x, y, yw), pen_xy=0.15, pen_yaw=5.0, oncul_w=0.0)
        sonuc.append((p, s, s0))
    sonuc.sort(key=lambda q: -q[1])
    return sonuc


# ----------------------------------------------------------------------------- ana sorgu
@dataclass
class Param:
    ust_k: int = 8                 # geometrik doğrulanacak anahtar kare sayısı
    min_eslesme_sira: int = 8      # ilk sıralamada en az bu kadar eşleşen anahtar kareler
    oran: float = 0.8
    kume_m: float = 1.0            # ağırlıklı ortalama için en iyi anahtar kareye en fazla bu kadar uzak anahtar kareler
    dogrulama_icerik: int = 12     # bir anahtar kare 'doğrulandı' sayılması için en az iç nokta
    yaw_uyum_der: float = 12.0     # doğrulanan anahtar kareler arasında yaw uyumu
    cizgi_sigma_der: float = 8.0   # öteleme yönü çizgisi açısal gürültüsü
    kutu_m: float = 3.0            # ızgara aramasında en iyi anahtar kareye en çok uzaklık
    cizgi_agirlik: float = 1.0
    tarama_agirlik: float = 1.0
    genel_yaw_adim: float = 6.0
    yayilim_min_der: float = 20.0   # çizgi yönleri arası en büyük açı bunun altındaysa (doğrusal dizilim, ör. koridor) üçgenleme zayıf: tarama inceltmesi kullanılır


def _cizgi_terimi(P, azim_der, w, X, Y, sigma_der, taban_m=0.5):
    """Doğrulanmış anahtar kare çizgileri: her biri P_k noktasından azim (derece, mod 180) yönünde. Izgara (X, Y) için
    ağırlıklı ortalama uyum 0..1. Çizgi açısı = sin(theta) ~ |vektör x yön| / max(|vektör|, taban)."""
    sig = np.sin(np.radians(sigma_der))
    top = np.zeros_like(X)
    for (px, py), az, wi in zip(P, azim_der, w):
        ux, uy = np.sin(np.radians(az)), np.cos(np.radians(az))
        vx, vy = X - px, Y - py
        e = np.abs(vx * uy - vy * ux) / np.maximum(np.hypot(vx, vy), taban_m)
        top += wi * np.exp(-0.5 * (e / sig) ** 2)
    return top / max(np.sum(w), 1e-9)


def konum_coz(H, nok, dogrulu, par, yaw):
    """Doğrulanmış anahtar kareler (kf, dyaw, phi, icerik) + tarama -> konum (ızgara + inceltme).
    Dönüş dict: tri (yalnız çizgiler), son (çizgi + tarama + inceltme), cizgi, skor."""
    en = dogrulu[0]
    pk0 = H.kf_poz[en["kf"]]
    P, az, w = [], [], []
    for c in dogrulu:
        q = H.kf_poz[c["kf"]]
        P.append((q[0], q[1]))
        az.append(yaw + c["phi"])
        w.append(min(1.0, c["icerik"] / 60.0))
    dx = np.arange(-par.kutu_m, par.kutu_m + 1e-9, 0.05)
    X0, Y0 = np.meshgrid(pk0[0] + dx, pk0[1] + dx)
    X, Y = X0.ravel(), Y0.ravel()
    cz = _cizgi_terimi(P, az, w, X, Y, par.cizgi_sigma_der)
    d0 = np.hypot(X - pk0[0], Y - pk0[1])
    oncul = 0.05 * (d0 / 1.5) ** 2
    j = int(np.argmax(cz - oncul))
    cikt = dict(tri=(float(X[j]), float(Y[j]), float(yaw)), cizgi_tri=float(cz[j]))
    if len(nok) >= 30:
        cikt["tri_skor"] = float(skor_bilin(H, nok, np.array([X[j]]), np.array([Y[j]]), np.array([yaw]))[0])
        sc = skor_bilin(H, nok, X, Y, np.full(len(X), yaw))
        top = par.cizgi_agirlik * cz + par.tarama_agirlik * sc - oncul
        j = int(np.argmax(top))
        p0 = (float(X[j]), float(Y[j]), float(yaw))
        p, s, _ = incelt(H, nok, p0, pen_xy=0.10, pen_yaw=0.0, oncul_w=0.0)
        cizgi_son = float(_cizgi_terimi(P, az, w, np.array([p[0]]), np.array([p[1]]), par.cizgi_sigma_der)[0])
        cikt.update(son_tarama=(p[0], p[1], float(yaw)), tarama_skor=float(s), cizgi=cizgi_son, ham=p0)
    else:
        cikt.update(son_tarama=cikt["tri"], tarama_skor=float("nan"), cizgi=cikt["cizgi_tri"], ham=cikt["tri"])
    # çizgi yönlerinin açısal yayılımı (mod 180): hepsi neredeyse paralelse (koridor ekseni) çizgilerin kesişimi belirsiz
    ag = [a % 180.0 for a, wi in zip(az, w) if wi >= 0.2]
    yay = 0.0
    for i in range(len(ag)):
        for j in range(i + 1, len(ag)):
            d = abs(ag[i] - ag[j]) % 180.0
            yay = max(yay, min(d, 180.0 - d))
    cikt["yayilim"] = float(yay)
    cikt["son"] = cikt["tri"] if yay >= par.yayilim_min_der else cikt["son_tarama"]
    return cikt


def sorgula(H, oz, tarama_satiri, par=None, haric=None, genel=True, y=None):
    """Tek sorgu. oz = (R, D); tarama_satiri = (mesafe, guven, menzil) ya da None; haric = anahtar kare dışlama maskesi (bool (K,)).
    Dönüş dict: ozellikler ve aday konumlar (karar ayrı). Süreler ms."""
    par = par or Param()
    y = y or yo.pusula_yontemi()
    t0 = time.perf_counter()
    K = len(H.kf_R)
    ok = np.ones(K, bool) if haric is None else ~haric
    sayi = np.zeros(K, int)
    for i in range(K):
        if ok[i]:
            sayi[i] = goz_sayisi(oz, (H.kf_R[i], H.kf_D[i]), y, par.oran)
    t1 = time.perf_counter()
    sira = [i for i in np.argsort(-sayi)[:par.ust_k] if sayi[i] >= par.min_eslesme_sira]
    dog = []
    for i in sira:
        c = m2d_coz(oz, (H.kf_R[i], H.kf_D[i]), y, par.oran)
        c["kf"] = int(i)
        c["sira_sayi"] = int(sayi[i])
        dog.append(c)
    t2 = time.perf_counter()
    nok = nokta_al(*tarama_satiri, en_cok=100) if tarama_satiri is not None else np.zeros((0, 2))
    cikt = dict(sayi_max=int(sayi.max()) if K else 0, n_nokta=len(nok),
                dog=[(c["kf"], c["icerik"], c["eslesme"], c["dyaw"], c["phi"]) for c in dog])
    dogrulu = [c for c in dog if c["icerik"] >= par.dogrulama_icerik]
    dogrulu.sort(key=lambda c: -c["icerik"])
    cikt["n_dogrulu"] = len(dogrulu)
    if dogrulu:
        en = dogrulu[0]
        pk = H.kf_poz[en["kf"]]
        yaw_en = (pk[2] + en["dyaw"]) % 360.0
        uyumlu = []
        for c in dogrulu:
            q = H.kf_poz[c["kf"]]
            if abs(ko.sar((q[2] + c["dyaw"]) - yaw_en)) < par.yaw_uyum_der:
                uyumlu.append(c)
        wv = np.array([c["icerik"] for c in uyumlu], float)
        ang = np.radians([(H.kf_poz[c["kf"]][2] + c["dyaw"]) for c in uyumlu])
        yaw = float(np.degrees(np.arctan2((wv * np.sin(ang)).sum(), (wv * np.cos(ang)).sum())) % 360.0)
        cikt["en"] = dict(kf=en["kf"], icerik=en["icerik"], eslesme=en["eslesme"], poz=(float(pk[0]), float(pk[1]), float(yaw_en)))
        cikt["n_uyumlu"] = len(uyumlu)
        cikt["toplam_icerik"] = int(wv.sum())
        w, px, py = 0.0, 0.0, 0.0
        for c in uyumlu:
            q = H.kf_poz[c["kf"]]
            if np.hypot(q[0] - pk[0], q[1] - pk[1]) <= par.kume_m:
                w += c["icerik"]
                px += c["icerik"] * q[0]
                py += c["icerik"] * q[1]
        cikt["kume_poz"] = (px / w, py / w, yaw)
        cikt.update(konum_coz(H, nok, uyumlu, par, yaw))
    t3 = time.perf_counter()
    if genel and len(nok) >= 30:
        g = genel_arama(H, nok[np.linspace(0, len(nok) - 1, min(len(nok), 80)).astype(int)], adim_yaw=par.genel_yaw_adim, ust=8)
        cikt["genel"] = [(p, float(s)) for p, s, _ in g[:5]]
        if len(g) > 1:
            b = g[0]
            ikinci = max([q[1] for q in g[1:] if np.hypot(q[0][0] - b[0][0], q[0][1] - b[0][1]) > 0.5] or [0.0])
            cikt["genel_ayirt"] = float(b[1] - ikinci)
    t4 = time.perf_counter()
    cikt["ms"] = dict(sira=(t1 - t0) * 1000, dogrula=(t2 - t1) * 1000, konum=(t3 - t2) * 1000, genel=(t4 - t3) * 1000)
    return cikt


# ----------------------------------------------------------------------------- karar ve dizi süzgeci
@dataclass
class Esik:
    """TEK sabit eşik kümesi (tüm deneylerde aynen kullanılır)."""
    en_icerik: int = 50            # en iyi anahtar kare ile M2D iç nokta sayısı
    n_uyumlu: int = 2              # yaw'ı uyumlu doğrulanan anahtar kare sayısı
    cizgi: float = 0.55            # öteleme yönü çizgilerinin üçgenleme uyumu (0..1)
    tri_skor: float = 0.0          # (kullanılmıyorsa 0) tarama skoru alt sınırı
    zayif_en_icerik: int = 25      # dizi süzgecine aday olmak için gevşek eşik
    zayif_n_uyumlu: int = 2
    zayif_cizgi: float = 0.45
    dizi_n: int = 3                # dizide kabul için art arda uyumlu aday
    dizi_konum_m: float = 0.6
    dizi_yaw_der: float = 15.0


ESIK = Esik()


def karar(r, e=ESIK):
    """Tek kare kararı. Dönüş (tanindi: bool, poz ya da None, güven 0..1)."""
    if "tri" not in r:
        return False, None, 0.0
    ok = (r["en"]["icerik"] >= e.en_icerik and r["n_uyumlu"] >= e.n_uyumlu and r["cizgi_tri"] >= e.cizgi
          and (not np.isfinite(r.get("tri_skor", np.nan)) or r["tri_skor"] >= e.tri_skor))
    guven = min(1.0, r["en"]["icerik"] / (2.0 * e.en_icerik)) * min(1.0, r["n_uyumlu"] / (2.0 * e.n_uyumlu)) * min(1.0, r["cizgi_tri"] / 0.9)
    return bool(ok), tuple(r["son"]), float(guven)


def zayif_aday(r, e=ESIK):
    if "tri" not in r:
        return False
    return r["en"]["icerik"] >= e.zayif_en_icerik and r["n_uyumlu"] >= e.zayif_n_uyumlu and r["cizgi_tri"] >= e.zayif_cizgi


class Dizi:
    """Hareket telafili çoklu varsayım. Her adımda tek kare sonucu (r) ve önceki kareden bu kareye KENDİ görsel odometrisi
    (d_sag, d_ileri, d_yaw; kamera çerçevesi, konum_ortak.poz_ekle sözleşmesi) verilir. Her varsayım odometriyle ilerletilir;
    yeni zayıf aday bir varsayıma (0,6 m, 15 derece) uyuyorsa onu destekler, yoksa yeni varsayım açar; 4 adım desteksiz kalan
    varsayım silinir. Cevap: tek kare 'kesin' ise o; yoksa >= dizi_n art arda destekli en iyi varsayım."""

    def __init__(self, e=ESIK):
        self.e = e
        self.h = []
        self.adim_no = 0

    def adim(self, r, vo):
        e = self.e
        self.adim_no += 1
        for q in self.h:
            if vo is not None:
                q["poz"] = ko.poz_ekle(q["poz"], vo[0], vo[1], vo[2])
            q["bos"] += 1
        tanindi, poz, guven = karar(r, e) if r is not None else (False, None, 0.0)
        if r is not None and (tanindi or zayif_aday(r, e)):
            p = tuple(r["son"])
            en = None
            for q in self.h:
                if np.hypot(q["poz"][0] - p[0], q["poz"][1] - p[1]) <= e.dizi_konum_m and abs(ko.sar(q["poz"][2] - p[2])) <= e.dizi_yaw_der:
                    en = q
                    break
            if en is not None:
                en["destek"] += 1
                en["bos"] = 0
                en["poz"] = (0.5 * en["poz"][0] + 0.5 * p[0], 0.5 * en["poz"][1] + 0.5 * p[1], p[2])
            else:
                self.h.append(dict(poz=p, destek=1, bos=0))
        self.h = [q for q in self.h if q["bos"] <= 4]
        if tanindi:
            return True, tuple(p), guven, "tek"
        if self.h:
            q = max(self.h, key=lambda t: t["destek"])
            if q["destek"] >= e.dizi_n and q["bos"] <= 1:
                return True, tuple(q["poz"]), min(1.0, 0.5 + 0.1 * q["destek"]), "dizi"
        return False, None, 0.0, ""
