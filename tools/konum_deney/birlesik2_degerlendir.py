"""E2'yi sim kayıtlarında (gel 9 + sin 6 + sin2 5) E ile yan yana ölçer.
  python birlesik2_degerlendir.py [--kume gel sin sin2] [--kayit ad ...]
Satırlar: E (donmuş, Lkomutlu girdi) | E2 (Lkomutsuz girdi, bev_olcek 1,0) | E2 + bev_olcek 0,98 | E2 komutlu girdiyle (ablasyon).
Çıktı: kayitlar/sonuclar/gercek/e2/sim_tablo.json (+ öğrenilen profil: simde 0,55 m/sn ve 197 derece/sn'ye yakınsama)."""
import argparse
import json
import os
import sys
import time

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import birlesik_ortak as bo  # noqa: E402
import birlesik_kestirici as bk  # noqa: E402
import birlesik_degerlendir as be  # noqa: E402
import birlesik2_kestirici as b2  # noqa: E402

CIKTI = os.path.join(bo.SONUC, "gercek", "e2")


def kos_E2(g, par=None):
    est = b2.Kestirici(g.poz0, par)
    n = g.n
    P = np.zeros((n, 3))
    seri = {k: np.zeros(n) for k in ("sigma", "guven", "k", "oran", "guven_b", "bev_kullan", "yaw_komut", "komut_sifir", "kare_red", "yaw_ogren", "hiz_ogren")}
    prof = np.zeros((n, 6))
    for i in range(n):
        d, ok, ic = g.bz["d"][i], g.bz["ok"][i], g.bz["ic"][i]
        r = est.adim(int(g.t_ms[i]), float(g.yz["yon"][i]) + g.yaw0, int(g.yz["icerik"][i]), (d[0], d[1], ok, ic), tuple(g.komut[i]))
        P[i] = est.poz
        for k in seri:
            seri[k][i] = r.get(k, np.nan)
        if i % 5 == 0 or i == n - 1:
            prof[i] = est.profil_ozeti()
        else:
            prof[i] = prof[i - 1]
    return P, seri, prof, est


def yasal(ad, sin_etiket):
    return ad


def calistir(adlar):
    sonuc = {}
    for ad in adlar:
        t0 = time.time()
        gE = be.Girdi(ad, None, "Lkomutlu")
        PE, sE, _ = be.kos_E(gE, bk.Param())
        rE = be.metrik(gE, PE)
        g2 = be.Girdi(ad, None, "Lkomutsuz")
        P2, s2, prof, est = kos_E2(g2, b2.Param())
        r2 = be.metrik(g2, P2)
        P3, s3, _, _ = kos_E2(g2, b2.Param(bev_olcek=0.98))
        r3 = be.metrik(g2, P3)
        P4, s4, _, _ = kos_E2(gE, b2.Param())
        r4 = be.metrik(gE, P4)
        o = lambda r: dict(rmse=float(r["konum_rmse_m"]), maks=float(r["_hata"].max()), son=float(r["konum_son_m"]), yaw=float(r["yaw_ort_mutlak_derece"]),
                           yol=float(r.get("gidilen_mesafe_m", 0)))
        sonuc[ad] = dict(E=o(rE), E2=o(r2), E2_098=o(r3), E2_komutlu=o(r4),
                         E_bev=float(sE["bev_kullan"].mean()), E2_bev=float(s2["bev_kullan"].mean()), E_yawk=float(sE["yaw_komut"].mean()), E2_yawk=float(s2["yaw_komut"].mean()),
                         profil_son=[float(x) for x in prof[-1]],
                         v_noktalar=[[r, round(a, 1), round(b, 3)] for r, a, b in est.tv.tum_noktalar()], w_noktalar=[[r, round(a, 1), round(b, 1)] for r, a, b in est.tw.tum_noktalar()])
        print("%-26s E %.3f/%.3f  E2 %.3f/%.3f  E2(0,98) %.3f  E2(komutlu) %.3f   v255 %.3f w255 %.0f   bev %.2f/%.2f yawk %.2f/%.2f  (%.0f sn)" % (
            ad, sonuc[ad]["E"]["rmse"], sonuc[ad]["E"]["maks"], sonuc[ad]["E2"]["rmse"], sonuc[ad]["E2"]["maks"], sonuc[ad]["E2_098"]["rmse"], sonuc[ad]["E2_komutlu"]["rmse"],
            prof[-1][0], prof[-1][2], sonuc[ad]["E_bev"], sonuc[ad]["E2_bev"], sonuc[ad]["E_yawk"], sonuc[ad]["E2_yawk"], time.time() - t0), flush=True)
    return sonuc


def ana():
    ap = argparse.ArgumentParser()
    ap.add_argument("--kume", nargs="*", default=["gel", "sin", "sin2"])
    ap.add_argument("--kayit", nargs="*", default=None)
    ap.add_argument("--cikti", default="sim_tablo.json")
    a = ap.parse_args()
    adlar = a.kayit or sum([{"gel": bo.GEL, "sin": bo.SIN, "sin2": bo.SIN2}[k] for k in a.kume], [])
    s = calistir(adlar)
    os.makedirs(CIKTI, exist_ok=True)
    json.dump(s, open(os.path.join(CIKTI, a.cikti), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    for k in ("E", "E2", "E2_098", "E2_komutlu"):
        for kume, ls in (("gel", bo.GEL), ("sin", bo.SIN), ("sin2", bo.SIN2)):
            v = [s[x][k]["rmse"] for x in ls if x in s]
            if v:
                print("ort RMSE %-10s %-5s %.3f (n=%d)" % (k, kume, np.mean(v), len(v)))


if __name__ == "__main__":
    ana()
