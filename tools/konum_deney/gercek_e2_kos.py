"""Gerçek kayıtlarda E (donmuş, nominal 0,55 m/sn + 197 derece/sn, Lkomutlu girdi) ve E2 (öğrenilen profil, Lkomutsuz girdi) KALİBRE geometri + sabit maske ile.
  python gercek_e2_kos.py [kayit ...]     -> kayitlar/sonuclar/gercek/e2/kos/<kayit>.npz
Girdi önbelleği: gercek_e2_hazirla.py (onbellek_gercek_kalibS). Tarama (harita) kayıt başına yalnız doluluk haritası için (harita_gercek_kalibS)."""
import os
import sys

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_e2_hazirla as hz  # noqa: E402

KOS = os.path.join(hz.ok.go.SONUC, "e2", "kos")
SERI = ("sigma", "guven", "k", "oran", "guven_b", "bev_kullan", "yaw_komut", "komut_sifir", "kare_red")


def calistir(ad, par2=None, etiket=""):
    bo, ko, zo = hz.kur()
    import birlesik_bev as bb
    import birlesik_kestirici as bk
    import birlesik_yon as by
    import birlesik2_kestirici as b2
    from kayit import Kayit
    s = bo.spec(ad)
    kay = Kayit(s["theta"])
    n = len(kay)
    t_ms = kay.t_ms
    komut = np.array([kay.komut(int(t)) for t in t_ms], float)
    out = dict(t_ms=t_ms, komut=komut)
    # ---- E
    yz = by.yukle(ad, True)
    bz = bb.bev_lcn(ad, etiket="LLkomutlu", yaw_kaynak="Lkomutlu", ilerleme=False)
    v_tam, w_tam = ko.nominal_hizlar(kay.bilgi)
    est = bk.Kestirici((0.0, 0.0, 0.0), v_tam, w_tam, bk.Param())
    P = np.zeros((n, 3))
    ser = {k: np.zeros(n) for k in SERI}
    for i in range(n):
        d, ok, ic = bz["d"][i], bz["ok"][i], bz["ic"][i]
        r = est.adim(int(t_ms[i]), float(yz["yon"][i]), int(yz["icerik"][i]), (d[0], d[1], ok, ic), tuple(komut[i]))
        P[i] = est.poz
        for k in SERI:
            ser[k][i] = r.get(k, np.nan)
    out["E_P"] = P
    for k in SERI:
        out["E_" + k] = ser[k]
    # ---- E2
    yz2 = by.yukle(ad, False)
    bz2 = bb.bev_lcn(ad, etiket="LLkomutsuz", yaw_kaynak="Lkomutsuz", ilerleme=False)
    est2 = b2.Kestirici((0.0, 0.0, 0.0), par2 or b2.Param())
    P2 = np.zeros((n, 3))
    ser2 = {k: np.zeros(n) for k in SERI + ("yaw_ogren", "hiz_ogren")}
    prof = np.zeros((n, 6))
    for i in range(n):
        d, ok, ic = bz2["d"][i], bz2["ok"][i], bz2["ic"][i]
        r = est2.adim(int(t_ms[i]), float(yz2["yon"][i]), int(yz2["icerik"][i]), (d[0], d[1], ok, ic), tuple(komut[i]))
        P2[i] = est2.poz
        for k in ser2:
            ser2[k][i] = r.get(k, np.nan)
        prof[i] = est2.profil_ozeti() if (i % 5 == 0 or i == n - 1) else prof[i - 1]
    out["E2_P"] = P2
    for k in ser2:
        out["E2_" + k] = ser2[k]
    out["E2_profil"] = prof
    out["yon_m2d_komutsuz"] = yz2["yon"]
    out["yon_m2d_komutlu"] = yz["yon"]
    out["ic_m2d_komutsuz"] = yz2["icerik"]
    out["v_noktalar"] = np.array(est2.tv.tum_noktalar())
    out["w_noktalar"] = np.array(est2.tw.tum_noktalar())
    os.makedirs(KOS, exist_ok=True)
    np.savez_compressed(os.path.join(KOS, ad + etiket + ".npz"), **out)
    return out


if __name__ == "__main__":
    adlar = sys.argv[1:] or hz.ADLAR
    for ad in adlar:
        r = calistir(ad)
        print("tamam", ad, "E2 son", r["E2_P"][-1], "profil", r["E2_profil"][-1], flush=True)
