"""E kestiricisini kayıtlar üstünde çalıştırır, A/C/D ile karşılaştırır, ölçütleri ve tabloları yazar.

Kullanım:
  python birlesik_degerlendir.py kos --kume gel sin [--bev L] [--cikti ad]      E'yi çalıştır, kısa tablo yaz
  (ek parametre geçersiz kılma: --par anahtar=değer ...)
Girdiler önbellekten okunur (birlesik_ortak/birlesik_bev). Çıktı: kayitlar/sonuclar/birlesik/<cikti>/ (tahmin CSV, ölçüt, seri)."""
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
import konum_ortak as ko  # noqa: E402
import olc  # noqa: E402
from kayit import Kayit  # noqa: E402


class Girdi:
    """Bir kaydın E girdileri (önbellekten)."""

    def __init__(self, ad, bev_etiket=None, yaw_kaynak="Lkomutlu"):
        import birlesik_bev as bb
        bev_etiket = bev_etiket or ("L" if yaw_kaynak == "komutlu" else "L" + yaw_kaynak)
        self.ad = ad
        self.s = bo.spec(ad)
        self.kay = Kayit(self.s["theta"])
        self.n = len(self.kay)
        self.t_ms = self.kay.t_ms
        self.yz = bb.yaw_oku(ad, yaw_kaynak)
        if bev_etiket == "K":
            self.bz = bo.npz(bo.bev_yolu(self.s))
        else:
            self.bz = bo.npz(bb.bev_yolu(ad, bev_etiket))
        self.gk = self.kay.gercek_konum()
        v, w = ko.nominal_hizlar(self.kay.bilgi)
        self.v_tam, self.w_tam = v, w
        self.komut = np.array([self.kay.komut(int(t)) for t in self.t_ms], float)
        self.poz0 = (float(self.gk["x_m"][0]), float(self.gk["y_m"][0]), float(self.gk["yon_derece"][0]))
        # M2D: başlangıç yaw'ı gerçek başlangıç yaw'ına oturtulur (A/C/D ile aynı kural)
        self.yaw0 = float(self.gk["yon_derece"][0]) - float(self.yz["yon"][0])


def kos_E(g, par=None, duzelt=False, gecikme=4, harita=False):
    """E'yi çalıştır. Dönüş: P (n,3) poz, seri sözlüğü, ms (kare başına süre) [, harita ızgarası, log].
    duzelt=True: D'deki gibi tarama-harita düzeltmesi (konum_tarama.eslestir, kazanç 0,15/0,05, 60 tarama gecikmeli harita) UYGULANIR;
    ama düzeltme sonucu `gecikme` kare (varsayılan 4 = ~0,27 sn, SegFormer gecikmesi) sonra dünya kayması olarak eklenir.
    harita=True: E'nin kendi pozlarıyla doluluk haritası da kurulur (kosucu ile aynı kurallar)."""
    est = bk.Kestirici(g.poz0, g.v_tam, g.w_tam, par)
    n = g.n
    P = np.zeros((n, 3))
    seri = {k: np.zeros(n) for k in ("sigma", "guven", "k", "oran", "guven_b", "bev_kullan", "yaw_komut", "komut_sifir", "kare_red")}
    ms = np.zeros(n)
    iz = iz_e = None
    if duzelt or harita:
        import konum_slam as ks
        import konum_tarama as kt
        tar = bo.npz(g.s["tar"])
        sira = {int(k): i for i, k in enumerate(tar["kare"])}
        menzil = float(tar["max_menzil"])
        iz = ks.buyuk_izgara(g.s["sim"])
        iz_e = ks.buyuk_izgara(g.s["sim"]) if duzelt else None
        ep = kt.EsleParam()
        kuyruk = []
        bekle = []
    log = {"kabul": 0, "deneme": 0, "esle_ms": []}
    for i in range(n):
        a = time.perf_counter()
        d, ok, ic = g.bz["d"][i], g.bz["ok"][i], g.bz["ic"][i]
        r = est.adim(int(g.t_ms[i]), float(g.yz["yon"][i]) + g.yaw0, int(g.yz["icerik"][i]), (d[0], d[1], ok, ic), tuple(g.komut[i]))
        if duzelt:
            for q in [q for q in bekle if q[0] <= i]:
                bekle.remove(q)
                x, y, yw = est.poz
                est.poz = (x + q[1], y + q[2], (yw + q[3]) % 360.0)
        ms[i] = (time.perf_counter() - a) * 1000.0
        poz = est.poz
        j = sira.get(int(g.kay.kare_numaralari[i])) if iz is not None else None
        if j is not None:
            mes, gv = tar["mesafe"][j], tar["guven"][j]
            if duzelt and i > 0:
                a = time.perf_counter()
                sn = kt.eslestir(iz_e, poz, mes, gv, menzil, ep, ay_xy=ep.ay_xy, ay_yaw=ep.ay_yaw)
                log["esle_ms"].append((time.perf_counter() - a) * 1000.0)
                log["deneme"] += 1
                if kt.kabul(sn, ep):
                    dx, dy, dyaw = sn["d"]
                    bekle.append((i + gecikme, ep.kazanc_xy * dx, ep.kazanc_xy * dy, ep.kazanc_yaw * dyaw))
                    log["kabul"] += 1
            iz.tara_ekle(poz[0], poz[1], poz[2], mes, gv, menzil, guven_min=0.2)
            if iz_e is not None:
                kuyruk.append((poz, mes, gv))
                while len(kuyruk) > ep.gecikme_tarama:
                    q = kuyruk.pop(0)
                    iz_e.tara_ekle(q[0][0], q[0][1], q[0][2], q[1], q[2], menzil, guven_min=0.2)
        P[i] = est.poz
        r["x"], r["y"], r["yaw"] = est.poz
        for k in seri:
            seri[k][i] = r.get(k, np.nan)
    if harita:
        return P, seri, ms, iz, log
    return P, seri, ms


def metrik(g, P):
    """olc.py ile aynı ölçüt (hizalama dahil)."""
    gercek = g.gk
    tahmin = {"kare": g.kay.kare_numaralari, "t_ms": g.t_ms, "x_m": P[:, 0], "y_m": P[:, 1], "yon_derece": P[:, 2] % 360.0}
    h = olc.hizala(tahmin, gercek)
    s = olc.olc_hesapla(h)
    hata = np.hypot(h["tx"] - h["gx"], h["ty"] - h["gy"])
    yhata = olc.sar(h["tyon"] - h["gyon"])
    r = {k: v for k, v in s.items() if not k.startswith("_")}
    r["_hata"] = hata
    r["_yhata"] = np.asarray(yhata)
    r["_hizali"] = h
    return r


def a_c_d_oku(ad, v):
    """Önceden hesaplanmış A/C/D tahmin CSV'sinden aynı ölçüt (hesap tekrarlanmaz)."""
    s = bo.spec(ad)
    yol = os.path.join(s["tahmin"], "%s_theta_%s.csv" % (v, ad))
    if not os.path.exists(yol):
        return None
    kay = Kayit(s["theta"])
    h = olc.hizala(olc.tahmin_oku(yol), kay.gercek_konum())
    r = olc.olc_hesapla(h)
    out = {k: x for k, x in r.items() if not k.startswith("_")}
    out["_hata"] = np.hypot(h["tx"] - h["gx"], h["ty"] - h["gy"])
    out["_yhata"] = np.asarray(olc.sar(h["tyon"] - h["gyon"]))
    return out


def satir(ad, v, r):
    return "%-3s %-28s yol %5.1f  RMSE %.3f  maks %.3f  son %.3f  yaw %.2f/%.2f" % (
        v, ad, r.get("gidilen_mesafe_m", 0), r["konum_rmse_m"], r["konum_maks_m"], r["konum_son_m"],
        r["yaw_ort_mutlak_derece"], r["yaw_maks_derece"])


def ana():
    ap = argparse.ArgumentParser()
    ap.add_argument("--kume", nargs="*", default=["gel", "sin"])
    ap.add_argument("--kayit", nargs="*", default=None)
    ap.add_argument("--bev", default=None)
    ap.add_argument("--yaw", default="Lkomutlu", choices=["komutlu", "komutsuz", "Lkomutlu", "Lkomutsuz"])
    ap.add_argument("--par", nargs="*", default=[])
    ap.add_argument("--abcd", action="store_true", help="A/C/D'yi de yaz")
    a = ap.parse_args()
    par = bk.Param(**{k: float(v) for k, v in (x.split("=") for x in a.par)})
    adlar = a.kayit or sum([{"gel": bo.GEL, "sin": bo.SIN, "sin2": bo.SIN2}[k] for k in a.kume], [])
    top = []
    for ad in adlar:
        g = Girdi(ad, a.bev, a.yaw)
        P, seri, ms = kos_E(g, par)
        r = metrik(g, P)
        top.append(r["konum_rmse_m"])
        print(satir(ad, "E", r), " bev%%=%.0f yawk%%=%.0f k=%.2f ms=%.2f" % (100 * seri["bev_kullan"].mean(), 100 * seri["yaw_komut"].mean(), seri["k"][-1], ms.mean()), flush=True)
        if a.abcd:
            for v in "ACD":
                q = a_c_d_oku(ad, v)
                if q:
                    print(satir(ad, v, q))
    print("ortalama RMSE %.3f" % np.mean(top))


if __name__ == "__main__":
    ana()
