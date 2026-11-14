"""Gorev 2: duz gidis kare ciftlerinden kamera geometrisi OZ-KALIBRASYONU (zemine gore egim, ilerleme yonune gore yaw, mercek merkezleri, f).
  python gercek_kalib_coz.py [kume=2a+4]
Amac: duz gidiste TUM zemin noktalari ayni vektorle kaymali (katı oteleme). Parametreler theta icin GERI-PROJEKSIYON hatasi (piksel) azaltilir:
  her eslesme icin ham pikselden zemin noktasi (h = 0,17 m SABIT) -> cift basina ortak oteleme (0, tau_k) (ilerleme ekseni boyunca: yaw'i tanimlar) ->
  ilk noktanin oteleme sonrasi kamera pikseli tahmini; artik = tahmin - gozlenen (piksel). Kayip: Cauchy (yansima/sabit nokta aykirilari).
  h serbest DEGIL; 2,00 m gercegi kullanilmaz; yalniz sol=sag sabit komutlu duz gidis ciftleri.
Asamalar: A egim+yan_egim+yon (3) | B + mercek merkezleri (7) | C + f1,f2 (9).
Cikti: kalib/onbellek/coz_<kume>.json (asama basina parametreler, maliyet, zorlayici hata), kalib/coz_<kume>.log."""
import json
import os
import sys
import time

import numpy as np
from scipy.optimize import least_squares
from scipy.sparse import lil_matrix

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_kalib_model as km  # noqa: E402
import gercek_kalib_analiz as ka  # noqa: E402

ASAMALAR = {"A": ["egim", "yan_egim", "yon"], "B": ["egim", "yan_egim", "yon", "m1x", "m1y", "m2x", "m2y"],
            "C": ["egim", "yan_egim", "yon", "m1x", "m1y", "m2x", "m2y", "f1", "f2"]}
OLCEK = dict(egim=1.0, yan_egim=1.0, yon=1.0, m1x=5.0, m1y=5.0, m2x=5.0, m2y=5.0, f1=3.0, f2=3.0)
HIZ_MIN = 0.25          # cift on-sektor hizi (m/sn) altinda cift atilir
PIX_SABIT = 1.2         # ham pikselde yer degistirme bu degerin altindaysa (robota sabit nokta) kalibrasyondan hic alinmaz
CIFT_BASINA = 250


def veri_hazirla(kayitlar, tohum=0, cift_secimi=None):
    """Kayitlardan (nominal geometriyle on filtre: zemin adayi) eslesme dizileri. cift_secimi: {kayit: bool dizisi (cift)} verilirse yalniz o ciftler."""
    kam0 = km.kamera()
    rng = np.random.default_rng(tohum)
    L, U1, V1, U2, V2, C, REC = [], [], [], [], [], [], []
    tau0, sgn, pair_rec, pair_ci = [], [], [], []
    K = 0
    for ad in kayitlar:
        E = km.eslesme_yukle(ad)
        T = ka.tablo(E, kam0)
        for i in range(len(E["cift"])):
            if cift_secimi is not None and ad in cift_secimi and not cift_secimi[ad][i]:
                continue
            if not np.isfinite(T["vref"][i]) or abs(T["vref"][i]) < HIZ_MIN:
                continue
            m = np.where((T["c"] == i) & T["gec"] & (T["pix"] >= PIX_SABIT) & (T["r"] > 0.3) & (T["r"] < 1.6))[0]
            if len(m) < 60:
                continue
            if len(m) > CIFT_BASINA:
                m = rng.choice(m, CIFT_BASINA, replace=False)
            tau = float(np.median(T["D"][m, 1]))
            L.append(E["lens"][m]); U1.append(E["u1"][m]); V1.append(E["v1"][m]); U2.append(E["u2"][m]); V2.append(E["v2"][m])
            C.append(np.full(len(m), K)); REC.append(np.full(len(m), len(pair_rec)))
            tau0.append(tau); sgn.append(float(E["isaret"][i])); pair_rec.append(ad); pair_ci.append(i)
            K += 1
    cat = lambda x, t=None: np.concatenate(x).astype(t or np.float64)
    return dict(lens=cat(L, np.int8), u1=cat(U1), v1=cat(V1), u2=cat(U2), v2=cat(V2), c=cat(C, np.int64), tau0=np.array(tau0), sgn=np.array(sgn),
                pair_rec=pair_rec, pair_ci=np.array(pair_ci), K=K)


def tahmin_piksel(kam, V, P, tau):
    """Her eslesme icin ikinci karedeki PREDICTED piksel (u, v) ve gecerlilik. tau: eslesme basina oteleme (zemin z kaymasi)."""
    n = len(V["lens"])
    u = np.zeros(n); v = np.zeros(n); g = np.zeros(n, bool)
    for k in (0, 1):
        m = V["lens"] == k
        if not m.any():
            continue
        x, z, r, gec = km.yer(kam, k, V["u1"][m], V["v1"][m], km.H, 2.0)
        d = np.stack([x, np.full_like(x, -km.H), z + tau[m]], -1)
        d = d / np.linalg.norm(d, axis=1, keepdims=True)
        uu, vv, _ = kam.robot_to_lens(d, k)
        u[m], v[m], g[m] = uu, vv, gec
    return u, v, g


def kur(isimler, taban):
    def kamera_x(xg):
        P = dict(taban)
        for k, val in zip(isimler, xg):
            P[k] = val
        return km.kamera(P), P
    return kamera_x


def coz(V, isimler, baslangic, mesaj="", f_scale=2.0, max_nfev=60):
    ng = len(isimler)
    K = V["K"]
    N = len(V["u1"])
    kam_x = kur(isimler, baslangic)
    x0 = np.concatenate([[baslangic[k] for k in isimler], V["tau0"]])
    xs = np.concatenate([[OLCEK[k] for k in isimler], np.full(K, 0.05)])
    S = lil_matrix((2 * N, ng + K), dtype=int)
    rows = np.arange(N)
    for j in range(ng):
        S[2 * rows, j] = 1
        S[2 * rows + 1, j] = 1
    S[2 * rows, ng + V["c"]] = 1
    S[2 * rows + 1, ng + V["c"]] = 1

    def res(x):
        kam, _ = kam_x(x[:ng])
        u, v, g = tahmin_piksel(kam, V, None, x[ng:][V["c"]])
        r = np.empty(2 * N)
        r[0::2] = np.where(g, u - V["u2"], 12.0)
        r[1::2] = np.where(g, v - V["v2"], 12.0)
        return r

    t0 = time.time()
    s = least_squares(res, x0, jac_sparsity=S, loss="cauchy", f_scale=f_scale, x_scale=xs, max_nfev=max_nfev, method="trf")
    P = dict(baslangic)
    for k, val in zip(isimler, s.x[:ng]):
        P[k] = float(val)
    r = res(s.x)
    e = np.hypot(r[0::2], r[1::2])
    print("  %s: %d nfev %.0f sn, maliyet %.1f, artik medyan %.2f px, <2px orani %.3f, ort(Cauchy) %.3f" % (mesaj, s.nfev, time.time() - t0, s.cost, np.median(e), float((e < 2).mean()), float(np.mean(np.log1p((e / f_scale) ** 2)))), flush=True)
    return P, dict(maliyet=float(s.cost), artik_medyan_px=float(np.median(e)), ic_oran_2px=float((e < 2).mean()), nfev=int(s.nfev), n=int(N), cift=int(K))


def sektor_olc(P, kayitlar, kapali_maske=None):
    """Parametrelerle her kayit icin sektor hizlari (tum noktalar ve ana mod) ve yayilim."""
    kam = km.kamera(P)
    sonuc = {}
    for ad in kayitlar:
        T = ka.tablo(km.eslesme_yukle(ad), kam)
        g = T["gec"] & np.isfinite(T["q"]) & (np.abs(T["vref"][T["c"]]) > 0.3) & (T["pix"] >= PIX_SABIT)
        sh = ka.sektor_hiz(T, g)
        sonuc[ad] = dict(sektor={k: v[0] for k, v in sh.items()}, yayilim=ka.yayilim(sh), yan={k: v[1] for k, v in sh.items()})
    return sonuc


if __name__ == "__main__":
    kume = sys.argv[1] if len(sys.argv) > 1 else "2a+4"
    kal = {"2a+4": ["2a_vitessiz_duz", "4_parke_vitessiz"], "2a": ["2a_vitessiz_duz"], "2a+2b+4": ["2a_vitessiz_duz", "2b_otomatik_duz", "4_parke_vitessiz"]}[kume]
    V = veri_hazirla(kal)
    print("kalibrasyon kumesi", kal, "cift", V["K"], "eslesme", len(V["u1"]), flush=True)
    P = km.nominal()
    sonuc = dict(kume=kal, nominal=dict(P))
    for a in ("A", "B", "C"):
        P, ozet = coz(V, ASAMALAR[a], P, "asama " + a)
        sonuc[a] = dict(parametre=dict(P), **ozet)
        print("   ", {k: round(P[k], 3) for k in km.ISIM}, flush=True)
    json.dump(sonuc, open(os.path.join(km.ONB, "coz_%s.json" % kume), "w", encoding="utf-8"), indent=1)
