"""Zemin sınırı, yöntem (a): klasik, öğrenilmiş model yok.

Tasarım (tek sabit parametre kümesi, odaya özel eşik/renk yok):
 1. Panorama Lab renk uzayına çevrilir ve azimut x satır (polar) görünüme alınır (zemin_ortak.azimut_sutunlari).
 2. ZEMİN GÖRÜNÜMÜ O KAREDEN ÖĞRENİLİR: robotun etrafındaki en yakın görünür zemin halkası (ufkun 25-45 derece altı,
    yaklaşık 0,25-0,55 m; gövde maskesi dışında). Bu halkadan k-means (4 küme, her biri ortalama + kovaryans) çıkarılır;
    az görülen kümeler (<%5) atılır (halkaya giren tek tük eşya/duvar zemin sayılmasın). Güneş lekesi ve gölge halkada
    varsa ikisi de zemin kümesi olur.
 3. Her piksel için "zemin olasılığı" p: en yakın kümeye Mahalanobis uzaklığı, halkanın kendi %97 uzaklığına bölünerek
    (ölçek de kareden öğrenilir) yumuşak eşiklenir.
 4. Her azimutta sınır satırı = zemin olasılığı haritasına en iyi uyan kesim:  maliyet(r) = sum_{v>=r}(1-p) + w * sum_{v<r} p
    (r altı zemin, üstü zemin değil). Parlak yansıma/çizgi gibi KÜÇÜK zemin-dışı lekeler, ardındaki uzun zemini kesmeye
    yetmediği için sınırı kısaltmaz. Komşu azimutlar arası kısaltılmış doğrusal ceza ile dairesel dinamik programlama
    (Viterbi) yapılır.
 5. Satır -> mesafe: d = h / tan(a).
Bilinen zayıflıklar (ölçülerek rapora yazılır): zemin ile duvar benzer renkteyse (gri karo-gri duvar) sınır kaçar;
kapı eşiğinde zemin malzemesi değişirse yeni zemin "engel" görünür; robot duvara 0,5 m'den yakınken halka duvarla karışır."""
import numpy as np
import cv2
from sklearn.cluster import KMeans

import zemin_ortak as zo

AYAR = dict(
    halka_alt=45.0, halka_kalinlik=10.0,   # zemin halkası: her azimutta en yakın görünür zemin; alt sınır min(maskesiz son satır, 45 derece), kalınlık 10 derece
    k=4, kume_min_oran=0.05, l_agirlik=0.5,
    ust_aci=0.7,                 # sınırın en uzak olabileceği ufuk altı açı (derece) -> ~20 m
    w_ust=1.0,                   # sınırın üstündeki zemin-benzeri piksellerin cezası
    lam=0.25, trunc=40,          # komşu azimut cezası: lam * min(|dr|, trunc) satır
    sigma_dikey=1.0,             # dikey yumuşatma (piksel)
    kenar_pencere=4,             # sınır kenar kontrastı: sınırın altındaki ve üstündeki 4'er satırın ortalama rengi farkı
    gama=25.0,                   # kenar kontrastı varsa sınıra verilen ödül (satır cinsinden)
    p_yuzdelik=97.0, p_c=1.5, p_us=4,   # zemin olasılığı: ölçek = halka uzaklıklarının bu yüzdeliği, p = 1/(1+(d/(c*s))^us)
    e0_yuzdelik=50.0, e0_carpan=2.0,
    pi_ufuk=12.0,                 # "ufka kadar engel yok" kararının cezası (kapalı mekânda sonsuz zemin olmaz)
)


def _lab(pan):
    f = pan.astype(np.float32) / 255.0
    return cv2.cvtColor(f, cv2.COLOR_BGR2LAB)


def halka_maskesi(gec, v0, H, ayar):
    """Her azimutta robota EN YAKIN görünür zemin bandı (H2,N) bool: alt sınır = min(maskesiz son satır, halka_alt derece)."""
    H2, N = gec.shape
    a = zo.satir_aci(v0 + np.arange(H2) + 0.5, H)[:, None]
    gecerli_a = np.where(~gec, a, -1.0)
    a_alt = np.minimum(gecerli_a.max(0), ayar["halka_alt"])[None, :]
    return (a <= a_alt) & (a >= np.maximum(a_alt - ayar["halka_kalinlik"], 18.0)) & ~gec   # 18 derece = ~0,8 m: halka hep robotun yakınında kalsın


def zemin_modeli(P, gec, v0, H, ayar):
    """P (H2,360,3) Lab polar, gec (H2,360) bool geçersiz. Halka piksellerinden küme modeli -> (kumeler, s)."""
    H2 = P.shape[0]
    halka = halka_maskesi(gec, v0, H, ayar)
    F = P[halka].reshape(-1, 3).copy()
    if len(F) < 200:
        return None, None
    F[:, 0] *= ayar["l_agirlik"]
    rng = np.random.RandomState(0)
    if len(F) > 6000:
        F = F[rng.choice(len(F), 6000, replace=False)]
    k = min(ayar["k"], 4)
    km = KMeans(n_clusters=k, n_init=3, random_state=0).fit(F)
    kumeler = []
    for c in range(k):
        Fc = F[km.labels_ == c]
        if len(Fc) < ayar["kume_min_oran"] * len(F) or len(Fc) < 10:
            continue
        mu = Fc.mean(0)
        cov = np.cov(Fc.T) + np.eye(3) * 4.0
        kumeler.append((mu, np.linalg.inv(cov)))
    if not kumeler:
        return None, None
    d = _mahal(F, kumeler)
    s = max(float(np.percentile(d, ayar["p_yuzdelik"])), 1.5)
    return kumeler, s


def _mahal(F, kumeler):
    d = np.full(len(F), np.inf, np.float32)
    for mu, ic in kumeler:
        x = F - mu
        d = np.minimum(d, np.sqrt(np.maximum(np.einsum("ij,jk,ik->i", x, ic, x), 0)))
    return d


def zemin_olasiligi(P, kumeler, s, l_agirlik, c=1.5, us=4):
    F = P.reshape(-1, 3).copy()
    F[:, 0] *= l_agirlik
    d = _mahal(F, kumeler).reshape(P.shape[:2])
    return 1.0 / (1.0 + (d / (c * s)) ** us)


def kesim_maliyeti(p, gec, w_ust):
    """p (H2,N) zemin olasılığı; satır v=0 üstte. Durum r in [0, H2]: v>=r zemin. Dönüş U (H2+1, N).
    Geçersiz (maskeli) pikseller her iki yönde de maliyet katmaz."""
    q = np.where(gec, 0.0, 1.0 - p)       # zemin diye seçilirse maliyet
    z = np.where(gec, 0.0, w_ust * p)     # zemin-dışı seçilirse maliyet
    H2, N = p.shape
    alt = np.zeros((H2 + 1, N), np.float32)       # alt[r] = sum_{v>=r} q
    alt[:H2] = np.cumsum(q[::-1], axis=0)[::-1]
    ust = np.zeros((H2 + 1, N), np.float32)       # ust[r] = sum_{v<r} z
    ust[1:] = np.cumsum(z, axis=0)
    return alt + ust


def kenar_kontrasti(P, gec, w, l_agirlik):
    """P (H2,N,3) Lab polar. Dönüş dE (H2+1, N): dE[r] = r'nin altındaki w satır ile üstündeki w satırın ortalama renk farkı
    (sınır r kenarında). Pencere maskeye değerse ya da dışarı taşarsa 0."""
    H2, N, _ = P.shape
    Q = P * np.array([l_agirlik, 1, 1], np.float32)
    Q = np.where(gec[..., None], 0.0, Q)
    c = np.concatenate([np.zeros((1, N, 3), np.float32), np.cumsum(Q, axis=0)])
    gc = np.concatenate([np.zeros((1, N), np.float32), np.cumsum((~gec).astype(np.float32), axis=0)])
    dE = np.zeros((H2 + 1, N), np.float32)
    for r in range(w, H2 - w + 1):
        n_alt = gc[r + w] - gc[r]
        n_ust = gc[r] - gc[r - w]
        ok = (n_alt == w) & (n_ust == w)
        m_alt = (c[r + w] - c[r]) / w
        m_ust = (c[r] - c[r - w]) / w
        dE[r] = np.where(ok, np.linalg.norm(m_alt - m_ust, axis=-1), 0.0)
    return dE


def dairesel_dp(U, lam, trunc):
    """U (S,N) durum maliyeti; komşu sütunlar arası lam*min(|dr|,trunc). Dairesel: 3 kez döşenip ortası alınır."""
    S, N = U.shape
    U3 = np.concatenate([U, U, U], axis=1)
    M = U3.shape[1]
    D = np.empty_like(U3)
    D[:, 0] = U3[:, 0]
    r = np.arange(S, dtype=np.float32)
    for j in range(1, M):
        prev = D[:, j - 1]
        f = lam * r + np.minimum.accumulate(prev - lam * r)               # min_{r'<=r}
        g = -lam * r + np.minimum.accumulate((prev + lam * r)[::-1])[::-1]  # min_{r'>=r}
        t = prev.min() + lam * trunc
        D[:, j] = U3[:, j] + np.minimum(np.minimum(f, g), t)
    yol = np.empty(M, np.int32)
    yol[-1] = int(np.argmin(D[:, -1]))
    for j in range(M - 2, -1, -1):
        c = D[:, j] + lam * np.minimum(np.abs(r - yol[j + 1]), trunc)
        yol[j] = int(np.argmin(c))
    return yol[N:2 * N]


def klasik_sinir(g, ayar=AYAR):
    """Girdi -> SinirSonucu."""
    H = g.yuk
    v0 = H // 2
    H2 = H - v0
    lab = _lab(g.pan)[v0:]
    if ayar["sigma_dikey"] > 0:
        lab = cv2.GaussianBlur(lab, (0, 0), sigmaX=0.1, sigmaY=ayar["sigma_dikey"])
    P = zo.azimut_sutunlari(lab)
    gec = zo.azimut_sutunlari(g.gecersiz[v0:].astype(np.float32)) > 0.15
    kumeler, s = zemin_modeli(P, gec, v0, H, ayar)
    if kumeler is None:
        return zo.SinirSonucu(np.full(zo.N_AZ, np.nan), np.zeros(zo.N_AZ), zo.guvenilir_menzil(H, g.h))
    p = zemin_olasiligi(P, kumeler, s, ayar["l_agirlik"], ayar["p_c"], ayar["p_us"])
    # sınırın en uzak olabileceği satır (ufuk altı ust_aci)
    r_ust = int(np.ceil(zo.aci_satir(ayar["ust_aci"], H) - v0))
    r_ust = min(max(r_ust, 0), H2 - 1)
    p_ = p[r_ust:]
    gec_ = gec[r_ust:]
    U = kesim_maliyeti(p_, gec_, ayar["w_ust"])
    # kenar kontrastı ödülü: eşik halkadaki kendi doku düzeyinden öğrenilir (renk farkı birimi: Lab)
    dE = kenar_kontrasti(P, gec, ayar["kenar_pencere"], ayar["l_agirlik"])
    hm = halka_maskesi(gec, v0, H, ayar)
    hm = np.concatenate([hm, hm[-1:]], 0)
    ref = dE[hm]
    ref = ref[ref > 0]
    e0 = max(ayar["e0_carpan"] * float(np.percentile(ref, ayar["e0_yuzdelik"])), 3.0) if len(ref) else 3.0
    bonus = ayar["gama"] * np.clip((dE[r_ust:] - e0) / e0, 0.0, 1.0)
    U = U - bonus
    U[0] += ayar["pi_ufuk"]                                  # ufka kadar engel yok
    r = dairesel_dp(U, ayar["lam"], ayar["trunc"]) + r_ust       # polar satır (v0'a göre), sınır KENARI
    v_kenar = v0 + r
    mesafe = zo.satirdan_mesafe(v_kenar, H, g.h)
    # güven: sınırın altı ile üstü arasındaki zemin olasılığı farkı + sınırın uçlara dayanması
    guven = np.zeros(zo.N_AZ)
    for k in range(zo.N_AZ):
        rk = int(r[k])
        alt = p[rk:min(rk + 4, H2), k]
        ust = p[max(rk - 4, 0):rk, k]
        if len(alt) == 0:
            guven[k] = 0.0                                   # sınır maske kenarında: gövde engelliyor
            continue
        c = float(alt.mean() - (ust.mean() if len(ust) else 0.0))
        guven[k] = np.clip(c, 0, 1)
        if rk <= r_ust + 1:
            guven[k] = 0.0                                   # ufka dayandı: uzak, belirsiz
    return zo.SinirSonucu(mesafe, guven, zo.guvenilir_menzil(H, g.h), {"p": p, "r": r, "v0": v0})
