"""Sinama sonuçlarının bileşen analizi (yöntemi DEĞİŞTİRMEZ; yalnızca önbellek ve kayıt dosyalarını okur, ölçer).
Çıktı: kayitlar/sonuclar/sinama/analiz/*.txt ve *.png. Kullanım: python sinama_analiz.py [--gelistirme]  (gelistirme: aynı ölçüler geliştirme kümesi için)"""
import argparse
import os
import sys

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sinama_calistir as sc  # noqa: E402
import konum_ortak as ko  # noqa: E402
import zemin_ortak as zo  # noqa: E402
from kayit import Kayit  # noqa: E402

GEL = "--gelistirme" in sys.argv
if GEL:      # geliştirme kümesi: özgün yollar (salt okunur)
    KAY = os.path.join(sc.KOK, "kayitlar")
    KON = os.path.join(KAY, "sonuclar", "konum")
    TAR = os.path.join(KAY, "sonuclar", "harita")
    ADLAR = ["duz_git", "salon_turu", "ev_turu"]
    zo.KAYITLAR = KAY
    zo.CIKTI = TAR
else:
    KAY, KON, TAR, ADLAR = sc.SINAMA, os.path.join(sc.SONUC, "konum"), os.path.join(sc.SONUC, "harita"), sc.ADLAR
    zo.KAYITLAR = sc.SINAMA
    zo.CIKTI = TAR
CIK = os.path.join(sc.SONUC, "analiz")
os.makedirs(CIK, exist_ok=True)
ETIKET = "gelistirme" if GEL else "sinama"


def gt(ad):
    return Kayit(os.path.join(KAY, "theta", ad)).gercek_konum()


def tarama_olc():
    """Görünen zemin sınırı gerçeğine karşı tarama hatası: kayıt başına ve mesafe kutusuna göre (kaba hata = göreli > %30)."""
    out = []
    kut = [(0.3, 1.0), (1.0, 2.0), (2.0, 3.0), (3.0, 5.0)]
    for ad in ADLAR:
        z = np.load(os.path.join(TAR, "tarama", "model_gorunum", "theta", ad + ".npz"))
        k, T = zo.gercek_mesafe(ad, "gorunen_zemin")
        satir = {int(a): i for i, a in enumerate(k)}
        ii = [satir[int(a)] for a in z["kare"]]
        T = T[ii]
        E = z["mesafe"]
        G = z["guven"]
        import zemin_degerlendir as zd
        T = T.copy()
        T[T < zd.theta_en_yakin_gorunur()[None, :] + 0.03] = np.nan
        ok = np.isfinite(E) & np.isfinite(T) & (T >= 0.3) & (T < 5.0)
        nan_oran = float(np.mean(~np.isfinite(E)))
        s = "%-18s tarama %3d | NaN %.1f%% | guven ort %.2f" % (ad, len(z["kare"]), 100 * nan_oran, float(np.nanmean(G)))
        s2 = []
        for a, b in kut:
            m = ok & (T >= a) & (T < b)
            if m.sum() < 20:
                s2.append("%g-%g: -" % (a, b))
                continue
            rel = np.abs(E - T)[m] / T[m]
            kisa = np.mean((E - T)[m] < -0.3 * T[m])
            s2.append("%g-%g: n=%d gross %.0f%% (kisa %.0f%%) medrel %.0f%%" % (a, b, m.sum(), 100 * np.mean(rel > 0.3), 100 * kisa, 100 * np.median(rel)))
        m = ok
        rel = np.abs(E - T)[m] / T[m]
        out.append(s + " | toplam gross %.1f%% medrel %.1f%%\n      " % (100 * np.mean(rel > 0.3), 100 * np.median(rel)) + " | ".join(s2))
    return out


def bev_olc():
    out = []
    for ad in ADLAR:
        z = np.load(os.path.join(KON, "onbellek", "bevT_komutsuz_theta_%s.npz" % ad))
        g = gt(ad)
        n = len(g["x_m"])
        # gerçek kare başı kamera çerçevesi öteleme (sag, ileri)
        dx = np.diff(g["x_m"]); dy = np.diff(g["y_m"]); yaw = np.radians(g["yon_derece"][:-1])
        sag = dx * np.cos(yaw) - dy * np.sin(yaw)
        ileri = dx * np.sin(yaw) + dy * np.cos(yaw)
        D = z["d"][1:]
        okm = z["ok"][1:]
        hiz = np.hypot(sag, ileri) * 15
        hareket = hiz > 0.10
        est = np.where(np.isfinite(D), D, 0.0)
        W = 30
        oran, yan, sup = [], [], []
        for a in range(0, n - 1 - W, W // 2):
            sl = slice(a, a + W)
            if hareket[sl].mean() < 0.8:
                continue
            tl = np.hypot(sag[sl].sum(), ileri[sl].sum())
            el = np.hypot(est[sl, 0].sum(), est[sl, 1].sum())
            if tl < 0.1:
                continue
            oran.append(el / tl)
            sup.append(float(okm[sl].mean()))
            # yönel hata
            yan.append(float(np.degrees(np.arctan2(est[sl, 0].sum(), est[sl, 1].sum()) - np.arctan2(sag[sl].sum(), ileri[sl].sum()))))
        oran = np.array(oran); sup = np.array(sup)
        top_t = np.hypot(sag, ileri)[hareket].sum()
        top_e = np.hypot(est[:, 0], est[:, 1])[hareket].sum()
        out.append("%-18s hareketli kare %d | BEV ok orani (hareketli) %.0f%% | ok (durgun) %.0f%% | ic medyan %d | 2 sn pencere olcek (tahmin/gercek) medyan %.2f p10 %.2f p90 %.2f (n=%d) | toplam yol olcegi %.2f | yon hatasi pencere |medyan| %.1f derece" % (
            ad, hareket.sum(), 100 * okm[hareket].mean(), 100 * okm[~hareket].mean() if (~hareket).any() else -1, np.median(z["ic"][1:][hareket]),
            np.median(oran) if len(oran) else np.nan, np.percentile(oran, 10) if len(oran) else np.nan, np.percentile(oran, 90) if len(oran) else np.nan,
            len(oran), top_e / top_t, np.median(np.abs(yan)) if len(yan) else np.nan))
    return out


def esle_olc():
    out = []
    if GEL:
        return ["(geliştirme için log kaydedilmedi; yalnızca sinama)"]
    import olc as _o  # noqa: F401
    for ad in ADLAR:
        g = gt(ad)
        for v in ("D",):
            z = np.load(os.path.join(KON, "log_%s_%s.npz" % (v, ad)), allow_pickle=True)
            P = z["P"]
            kare = z["kare"].astype(int)
            durum = z["durum"]
            d = z["d"]
            of = z["ort_f"]
            ay = z["ayirt"]
            ok = (of >= 0.30) & (ay >= 0.0) & (durum == "ok")
            # kabul edilen düzeltmenin öncesi: poz - K d
            xy = P[kare, :2]
            pre = xy - 0.15 * d[:, :2]
            tr = np.stack([g["x_m"][kare], g["y_m"][kare]], 1)
            e_pre = tr - pre
            e_son = np.hypot(*(tr - xy).T)
            e_on = np.hypot(*e_pre.T)
            corr = d[:, :2]
            # tam düzeltme (K=1) etkisi: |e - d| < |e| ?  ve karışımla gerçek etki: e_son < e_on
            iyi = np.hypot(*(e_pre - corr).T) < e_on
            m = ok
            s = "%-18s deneme %d | durum: %s | kabul %d | kabul edilende tam duzeltme gercek hatayi azaltir: %.0f%% | karisimli sonuc hatayi azaltir: %.0f%% | ort. |d| kabul %.3f m, ort. gercek |e| onceki %.3f m" % (
                ad, len(kare), ",".join("%s=%d" % (u, (durum == u).sum()) for u in np.unique(durum)), m.sum(),
                100 * np.mean(iyi[m]) if m.any() else np.nan, 100 * np.mean(e_son[m] < e_on[m]) if m.any() else np.nan,
                np.mean(np.hypot(*corr[m].T)) if m.any() else np.nan, np.mean(e_on[m]) if m.any() else np.nan)
            out.append(s)
            # reddedilenlerde tam düzeltme doğru mu (kaçırılan fırsat)
            r = (~ok) & (durum == "ok")
            if r.any():
                out.append("      reddedilen %d: tam duzeltme gercek hatayi azaltirdi: %.0f%%; ort_f medyan kabul %.2f / red %.2f; ayirt medyan kabul %.2f / red %.2f" % (
                    r.sum(), 100 * np.mean(iyi[r]), np.median(of[m]) if m.any() else np.nan, np.median(of[r]), np.median(ay[m]) if m.any() else np.nan, np.median(ay[r])))
            # sinyal -> doğruluk: iyi/kötü ayrımı
            if m.sum() > 10 and 0 < iyi[m].sum() < m.sum():
                from scipy.stats import mannwhitneyu
                for nm, sg in (("ort_f", of), ("ayirt", ay)):
                    A, B = sg[m][iyi[m]], sg[m][~iyi[m]]
                    if len(A) > 2 and len(B) > 2:
                        out.append("      %s: dogru duzeltme medyan %.2f (n=%d) vs yanlis %.2f (n=%d), AUC %.2f" % (nm, np.median(A), len(A), np.median(B), len(B), mannwhitneyu(A, B).statistic / (len(A) * len(B))))
    return out


def sinyal_olc():
    """Bir dahili sinyal, yerel konum/yaw hatası artışını ayırır mı? 1 sn pencerede (15 kare): sinyal ortalaması ile gerçek artımla tahmin artımı farkı."""
    from scipy.stats import spearmanr
    out = []
    for ad in ADLAR:
        g = gt(ad)
        yc = np.load(os.path.join(KON, "onbellek", "yon_komutsuz_theta_%s.npz" % ad))
        bz = np.load(os.path.join(KON, "onbellek", "bevT_komutsuz_theta_%s.npz" % ad))
        n = len(g["x_m"])
        dx = np.diff(g["x_m"]); dy = np.diff(g["y_m"]); yaw = np.radians(g["yon_derece"][:-1])
        sag = dx * np.cos(yaw) - dy * np.sin(yaw); ileri = dx * np.sin(yaw) + dy * np.cos(yaw)
        D = np.where(np.isfinite(bz["d"][1:]), bz["d"][1:], 0.0)
        W = 15
        yaw_est = np.diff(yc["yon"]); yaw_tr = np.diff(g["yon_derece"])
        yaw_err = ko.sar(yaw_est - yaw_tr)
        rows = []
        for a in range(0, n - 1 - W, W):
            sl = slice(a, a + W)
            moving = np.hypot(sag[sl].sum(), ileri[sl].sum()) > 0.10
            te = np.hypot(D[sl, 0].sum() - sag[sl].sum(), D[sl, 1].sum() - ileri[sl].sum())
            rows.append((a, moving, te, abs(yaw_err[sl].sum()), yc["icerik"][1:][sl].mean(), bz["ic"][1:][sl].mean(), bz["ok"][1:][sl].mean(),
                         float(np.mean(yc["durum"][1:][sl] == 2))))
        R = np.array(rows, float)
        mv = R[:, 1] > 0
        s = ["%-18s pencere %d (hareketli %d)" % (ad, len(R), mv.sum())]
        for nm, col in (("M2D icerik", 4), ("BEV ic", 5), ("BEV ok orani", 6), ("M2D tut orani", 7)):
            if mv.sum() > 8:
                rt = spearmanr(R[mv, col], R[mv, 2])[0]
                ry = spearmanr(R[:, col], R[:, 3])[0]
                s.append("%s: rho(oteleme hatasi)=%.2f rho(yaw hatasi)=%.2f" % (nm, rt, ry))
        out.append(" | ".join(s))
    return out


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--gelistirme", action="store_true")
    ap.parse_args()
    bol = [("TARAMA (gorunen zemin gercegine karsi)", tarama_olc), ("BEV (zeminden metrik oteleme)", bev_olc),
           ("TARAMA-HARITA DUZELTMESI (D)", esle_olc), ("SINYAL - HATA ILISKISI (1 sn pencere, Spearman)", sinyal_olc)]
    metin = []
    for b, f in bol:
        metin.append("== %s ==" % b)
        metin += f()
        metin.append("")
    s = "\n".join(metin)
    print(s)
    with open(os.path.join(CIK, "analiz_%s.txt" % ETIKET), "w", encoding="utf-8") as fh:
        fh.write(s)
