"""Parametre çalışması ölçütleri (TEK TANIM, tüm yapılandırmalar için aynı). Girdi: bir koşunun poz.jsonl + harita.json'u.
Koordinatlar: poz_cw alanı gerçekte T_wc'dir (kamera->dünya), yani konum = T[:3,3]. Dünya y AŞAĞI.
Ölçek: bant ölçeği s = 2,00 m / |c(14,4 sn) - c(ilk bant karesi)|. İlk bant karesi = ilk izlenen kare (en erken 3,4 sn: robot 3,4 sn'de bant 1'den kalkıyor).
  Not: SLAM genelde 3,8 sn'de başlıyor, yani ilk ~0,4 sn hareket (en çok ~0,1-0,15 m) bant mesafesine girmiyor, s biraz küçük çıkar (aynı yanlılık tüm yapılandırmalarda).
Referans yol: 2 m (bant arası) + yaklaşık 19 m tur = yaklaşık 21 m (kullanıcının verdiği yer gerçeği özeti, ölçülmüş değil).
Patlama: bant ölçekli toplam yol (başlatma..85 sn) > 3 x 21 m ya da 82,4 sn'de başlangıca uzaklık > 3 m ya da 20 sn'ye kadar başlamadı.
Yerinde dönüş: komutta sol ve sağ PWM zıt işaretli, toplamları küçük (|sol+sag| < 0,35 maks), her biri > 40, en az 1,0 sn süren aralıklar.
  Yarıçap = aralıktaki kamera merkezlerinin (bant ölçekli, xz) ağırlık merkezine ortanca uzaklığı. Gerçek değer en çok ~0,1 m (kameranın dönme merkezine kayıklığı)."""
import json
import os
import sys

import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(KLASOR, "..", "konum_deney"))
from kayit import Kayit  # noqa: E402

REF_YOL = 21.0
T_BAS, T_BANT, T_TUR_SON, T_SON = 3.4, 14.4, 82.4, 85.0
_KOM = {}


def komutlar():
    if "k" not in _KOM:
        k = Kayit(os.path.join(KLASOR, "..", "..", "kayitlar", "gercek", "olcum_ham"))
        t = k.t_ms / 1000.0
        kom = np.array([k.komut(int(x)) for x in k.t_ms]).astype(float)
        _KOM["k"] = (t, kom)
    return _KOM["k"]


def donus_araliklari(tmax, min_sure=1.0):
    t, kom = komutlar()
    sol, sag = kom[:, 0], kom[:, 1]
    ye = (sol * sag < 0) & (np.abs(sol + sag) < 0.35 * np.maximum(abs(sol), abs(sag))) & (np.minimum(abs(sol), abs(sag)) > 40)
    n = np.searchsorted(t, tmax)
    out, i = [], 0
    while i < n:
        if ye[i]:
            j = i
            while j < n and (ye[j] or (j + 8 < n and ye[j:j + 8].any())):
                j += 1
            if t[j - 1] - t[i] >= min_sure:
                out.append((float(t[i]), float(t[min(j, n - 1)])))
            i = j
        else:
            i += 1
    return out


def yukle(klasor):
    yol = os.path.join(klasor, "poz.jsonl")
    if os.path.exists(yol):
        P = [json.loads(s) for s in open(yol, encoding="utf-8")]
    else:
        import gzip
        P = [json.loads(s) for s in gzip.open(yol + ".gz", "rt", encoding="utf-8")]
    t = np.array([p["t_ms"] for p in P]) / 1000.0
    izl = np.array([bool(p.get("izleniyor")) for p in P])
    c = np.full((len(P), 3), np.nan)
    R = np.full((len(P), 3, 3), np.nan)
    for i, p in enumerate(P):
        if izl[i]:
            T = np.array(p.get("poz_wc") or p["poz_cw"]).reshape(4, 4)
            R[i], c[i] = T[:3, :3], T[:3, 3]
    return P, t, izl, c, R


def olc(klasor):
    P, t, izl, c, R = yukle(klasor)
    ok = np.where(izl)[0]
    r = dict(kare=len(P))
    if len(ok) == 0:
        r.update(baslatma_sn=None, patladi=True, neden="baslamadi")
        return r
    ilk = ok[0]
    r["baslatma_sn"] = float(t[ilk])
    r["izlenen_oran"] = float(izl[ilk:].mean())
    i0 = ok[np.argmax(t[ok] >= T_BAS)] if (t[ok] >= T_BAS).any() else ilk
    ib = ok[np.argmin(np.abs(t[ok] - T_BANT))]
    d = float(np.linalg.norm(c[ib] - c[i0]))
    s = 2.0 / d if d > 1e-9 else np.inf
    r["olcek_m_birim"] = float(s)
    seg = ok[(t[ok] >= t[ilk]) & (t[ok] <= T_SON)]
    yol = float(np.linalg.norm(np.diff(c[seg], axis=0), axis=1).sum() * s)
    it = ok[np.argmin(np.abs(t[ok] - T_TUR_SON))]
    son_bas = float(np.linalg.norm(c[it] - c[i0]) * s)
    r["yol_toplam_m"] = yol
    r["yol_orani"] = yol / REF_YOL
    r["son_baslangic_m"] = son_bas
    r["patladi"] = bool(yol > 3 * REF_YOL or son_bas > 3.0 or t[ilk] > 20)
    # eşleşen nokta, anahtar kare, nokta (t=85 sn anı ve son)
    es = np.array([p["eslesen_sayisi"] for p in P])[ilk:]
    r["eslesen_medyan"] = float(np.median(es))
    r["nokta_kare_medyan"] = float(np.median([p["nokta_sayisi"] for p in P[ilk:]]))
    j85 = int(np.argmin(np.abs(t - T_SON)))
    r["kf_85"] = P[j85]["anahtar_kare_sayisi"]
    r["nokta_85"] = P[j85]["harita_nokta_sayisi"]
    r["kf_son"] = P[-1]["anahtar_kare_sayisi"]
    r["nokta_son"] = P[-1]["harita_nokta_sayisi"]
    r["takip_ms_medyan"] = float(np.median([p["sureler_ms"]["takip"] for p in P[ilk:]]))
    # yön kapanışı
    def yaw(Rm):
        f = Rm @ np.array([0, 0, 1.0])
        return np.degrees(np.arctan2(f[0], f[2]))
    r["yon_kapanis_derece"] = float((yaw(R[it]) - yaw(R[i0]) + 180) % 360 - 180)
    # yer imleri (son harita)
    hj = os.path.join(klasor, "harita.json")
    if os.path.exists(hj):
        hm = json.load(open(hj))
        L = np.array(hm["noktalar"]).reshape(-1, 5)[:, :3]
        KF = np.array([np.array(k["poz_wc"]).reshape(4, 4)[:3, 3] for k in hm["anahtar_kareler"]])
        if len(L) and len(KF):
            dd = np.linalg.norm(L[:, None, :] - KF[None, :, :], axis=2)
            en = np.argmin(dd, axis=1)
            dmin = dd[np.arange(len(L)), en]
            r["nokta_alti_orani"] = float((L[:, 1] > KF[en, 1]).mean())          # y aşağı: kamera seviyesinin altı
            r["nokta_1_5m_orani"] = float((dmin * s <= 1.5).mean())
            r["nokta_zemin_orani"] = float(((L[:, 1] - KF[en, 1]) * s > 0.08).mean())   # kameradan >=8 cm aşağıda (zemin ~17 cm)
            r["nokta_sayisi_harita"] = int(len(L))
    # yerinde dönüş yarıçapı
    rs, yaws = [], []
    for a, b in donus_araliklari(t[-1]):
        m = ok[(t[ok] >= a) & (t[ok] <= b)]
        if len(m) < 8:
            continue
        xz = c[m][:, [0, 2]] * s
        rs.append(float(np.median(np.linalg.norm(xz - xz.mean(0), axis=1))))
        ya = np.unwrap(np.radians([yaw(R[i]) for i in m]))
        yaws.append(float(np.degrees(abs(ya[-1] - ya[0]))))
    r["donus_sayisi"] = len(rs)
    r["donus_yaricap_m_ort"] = float(np.mean(rs)) if rs else None
    r["donus_yaricap_m_liste"] = rs
    r["donus_yaw_derece"] = yaws
    return r


def tutarlilik(klasor, uzun=(22.0, 40.0)):
    """Bant mesafesinden BAĞIMSIZ, sıfırlama/yeniden başlatmaya dayanıklı ölçek tutarlılığı.
    c = (başlatmadan 1 sn sonrasından 14,4 sn'ye kadar SLAM yolu / komuttan beklenen yol) / (22-40 sn için aynı oran). Sabit ölçekte c ~ 1 (iyi koşularda 0,98-1,03),
    kötü başlatmada ilk gidiş kısa ölçeklendiği için c ~ 0,4-0,6. 'son başlatma' = izlemeye son (yeniden) giriş. İyi: 0,75 <= c <= 1,33."""
    P, t, izl, c, R = yukle(klasor)
    tt, kom = komutlar()
    pw = np.array([abs(kom[np.argmin(abs(tt - x)), :].sum() / 2) / 255 for x in t])
    gir_tum = np.where(izl & ~np.r_[False, izl[:-1]])[0]       # izlemeye giriş kareleri (tüm koşu)
    gir = gir_tum[t[gir_tum] <= T_SON + 3.0]                    # yalnız 0-88 sn bölümü
    r = dict(sifirlama=int(max(len(gir) - 1, 0)), sifirlama_tum=int(max(len(gir_tum) - 1, 0)), giris_sn=[round(float(t[i]), 1) for i in gir_tum])
    if len(gir) == 0:
        r.update(son_baslatma_sn=None, tut=None)
        return r
    t_son = float(t[gir[-1]])
    r["son_baslatma_sn"] = t_son

    def oran(a, b):
        m = np.where(izl & (t >= a) & (t < b))[0]
        if len(m) < 8:
            return None
        bek = pw[m[:-1]].sum() / 15.0
        if bek < 0.5:
            return None
        return float(np.linalg.norm(np.diff(c[m], axis=0), axis=1).sum() / bek)

    ilk, tur = oran(max(t_son + 1.0, 4.0), 14.4), oran(*uzun)
    r["oran_ilk"], r["oran_tur"] = ilk, tur
    r["tut"] = (ilk / tur) if (ilk and tur) else None
    return r
