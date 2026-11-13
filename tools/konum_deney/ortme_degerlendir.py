"""Örtme yöntemlerinin karşılaştırması: hareketli/zor durumlu sahnede HARITA, TARAMA, BIRLESIK x (taze | gerçekçi tarama).

Gerçek (doğruluk) örtme: GERÇEK haritada, GERÇEK kameradan nesnenin GERÇEK konumuna görüş hattı (gosterim_ar.geometri gizli_t).
Ölçü kümesi: nesne 6 m içinde, kestirilen görüntüde ekranda olan dilimler. Hata türleri: yanlış gizli (gerçekte görünür), yanlış görünür (gerçekte gizli).
Titreme: bir karar dizisinde gerçekte OLMAYAN görünürlük değişimi (gerçek dizide +-2 gösterilen kare içinde karşılığı olmayan) / nesnenin ekranda kaldığı sn.

Kullanım:  python tools/konum_deney/ortme_degerlendir.py [--varyant D A] [--yeniden]
Çıktı: kayitlar/sonuclar/gosterim/ortme_tablo_<varyant>.json ve .md, onbellek_arh_<varyant>.npz"""
import argparse
import json
import os
import sys
import time

import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
import gosterim_ar as ga  # noqa: E402
import ortme_veri as ov  # noqa: E402
import ortme_sahne as sa  # noqa: E402
import ortme_yontem as oy  # noqa: E402

MENZIL_M = 6.0
YONTEMLER = ["harita", "tarama_taze", "tarama_gercekci", "birlesik_taze", "birlesik_gercekci", "uyum_taze", "uyum_gercekci"]
AD = {"harita": "HARITA (mevcut)", "tarama_taze": "TARAMA taze", "tarama_gercekci": "TARAMA gerçekçi",
      "birlesik_taze": "BİRLEŞİK taze", "birlesik_gercekci": "BİRLEŞİK gerçekçi",
      "uyum_taze": "UYUM taze (alternatif)", "uyum_gercekci": "UYUM gerçekçi (alternatif)"}


def geo_hesapla(v, nes, yol=None, yeniden=False):
    """Gösterilen her kare ve nesne için geometri ve tüm yöntemlerin dilim kararları. Dönüş: dict of arrays (nq, K, ...)."""
    if yol and os.path.exists(yol) and not yeniden:
        z = np.load(yol)
        return {k: z[k] for k in z.files}
    ga.ENYAKIN = MENZIL_M
    nq, K, D = len(v.kareler), len(nes), ga.DILIM
    z = {}

    def yeni(ad, sekil, dt):
        z[ad] = np.zeros((nq, K) + sekil, dt)
    for ad in ("mesafe", "kayma_px", "kayma_yuzde", "kayma_cm", "ileri_e"):
        yeni(ad, (), np.float32)
    for ad in ("gorus_e", "gorus_t"):
        yeni(ad, (), bool)
    for ad in ("dilim_ekranda", "gizli_t") + tuple(YONTEMLER) + ("tarama_taze_bilinen", "tarama_gercekci_bilinen"):
        yeni(ad, (D,), bool)
    z["X"] = np.zeros((nq, K), np.float32)
    z["Y"] = np.zeros((nq, K), np.float32)
    t0 = time.time()
    for q, i in enumerate(v.kareler):
        pe, pt = v.Pe[i], v.Pt[i]
        ga.NESNELER = [n.sozluk(i) for n in nes]
        Me = v.harita_at(i)
        g = ga.geometri(pe, pt, Me, v.T, v.hm)
        tz = v.tarama_taze(i)
        tg = v.tarama_gercekci(i)
        pz = v.Pe[tz[2]]
        pg = v.Pe[tg[2]] if tg is not None else None
        for k, b in enumerate(g):
            z["X"][q, k], z["Y"][q, k] = nes[k].X[i], nes[k].Y[i]
            for ad in ("mesafe", "kayma_px", "kayma_yuzde", "kayma_cm", "ileri_e", "gorus_e", "gorus_t", "dilim_ekranda", "gizli_t"):
                z[ad][q, k] = b[ad]
            z["harita"][q, k] = b["gizli_e"]
            oe, _ = ga.dilim_noktalari(pe, ga.NESNELER[k])
            gt_, bt_ = oy.tarama_karar(tz, pz, oe)
            z["tarama_taze"][q, k], z["tarama_taze_bilinen"][q, k] = gt_, bt_
            z["birlesik_taze"][q, k] = oy.birlesik(gt_, bt_, b["gizli_e"])
            z["uyum_taze"][q, k] = oy.birlesik_uyum(gt_, bt_, b["gizli_e"])
            gr_, br_ = oy.tarama_karar(tg, pg, oe) if tg is not None else (np.zeros(D, bool), np.zeros(D, bool))
            z["tarama_gercekci"][q, k], z["tarama_gercekci_bilinen"][q, k] = gr_, br_
            z["birlesik_gercekci"][q, k] = oy.birlesik(gr_, br_, b["gizli_e"])
            z["uyum_gercekci"][q, k] = oy.birlesik_uyum(gr_, br_, b["gizli_e"])
        if q % 100 == 0:
            print("  geometri %s %d/%d (%.0f sn)" % (v.varyant, q, nq, time.time() - t0), flush=True)
    if yol:
        np.savez_compressed(yol, **z)
    return z


# ---------------------------------------------------------------------------------------------------- ölçüler
def secim(z):
    """Ölçü kümesi (nq, K, D): 6 m içinde, nesne merkezi ekranda (kestirilen), dilim ekranda."""
    return z["gorus_e"][..., None] & (z["mesafe"][..., None] < MENZIL_M) & z["dilim_ekranda"]


def _yay(a, p=2):
    """Zaman ekseninde (0) +-p kare genişlet."""
    o = a.copy()
    for s in range(1, p + 1):
        o[s:] |= a[:-s]
        o[:-s] |= a[s:]
    return o


def titreme(z, ad, sel, dt=2.0 / ov.FPS_KAYIT):
    """Nesne başına: gerçekte olmayan görünürlük değişimi sayısı ve nesnenin (sel) ekranda kaldığı süre (sn).
    Nesne düzeyi: durum = ekrandaki dilimlerin çoğu gizli. Dilim düzeyi: tek tek dilim dizileri (dilim başına ortalama)."""
    nq, K, D = sel.shape
    cik = []
    for k in range(K):
        s = sel[:, k, :]
        var = s.any(1)
        sure = var.sum() * dt
        n_ekr = np.maximum(s.sum(1), 1)
        dur_s = (z[ad][:, k, :] & s).sum(1) > 0.5 * n_ekr
        dur_t = (z["gizli_t"][:, k, :] & s).sum(1) > 0.5 * n_ekr
        ik = var[1:] & var[:-1]
        fs = ik & (dur_s[1:] != dur_s[:-1])
        ft = ik & (dur_t[1:] != dur_t[:-1])
        sahte = fs & ~_yay(ft)
        # dilim düzeyi
        fs_d = s[1:] & s[:-1] & (z[ad][1:, k, :] != z[ad][:-1, k, :])
        ft_d = s[1:] & s[:-1] & (z["gizli_t"][1:, k, :] != z["gizli_t"][:-1, k, :])
        sahte_d = fs_d & ~_yay(ft_d)
        cik.append(dict(sure=float(sure), nesne_sahte=int(sahte.sum()), nesne_toplam=int(fs.sum()), nesne_gercek=int(ft.sum()),
                        dilim_sahte=int(sahte_d.sum()), dilim_gercek=int(ft_d.sum())))
    return cik


def tablo(z, nes, yontemler=YONTEMLER):
    sel = secim(z)
    out = {}
    kats = [n.kat for n in nes]
    gruplar = {kat: [k for k, c in enumerate(kats) if c == kat] for kat in sa.KATEGORILER}
    gruplar["TUMU"] = list(range(len(nes)))
    for ad in yontemler:
        out[ad] = {}
        tit = titreme(z, ad, sel)
        for g, ks in gruplar.items():
            m = sel[:, ks, :]
            dec, tr = z[ad][:, ks, :], z["gizli_t"][:, ks, :]
            top = int(m.sum())
            yg = int((m & dec & ~tr).sum())
            yy = int((m & ~dec & tr).sum())
            sure = sum(tit[k]["sure"] for k in ks)
            ns = sum(tit[k]["nesne_sahte"] for k in ks)
            ds = sum(tit[k]["dilim_sahte"] for k in ks)
            out[ad][g] = dict(dilim_karari=top, yanlis_gizli=yg, yanlis_gorunur=yy,
                              yuzde_yanlis=100.0 * (yg + yy) / max(top, 1), yuzde_yanlis_gizli=100.0 * yg / max(top, 1),
                              yuzde_yanlis_gorunur=100.0 * yy / max(top, 1), ekranda_sn=sure,
                              titreme_nesne_sn=ns / max(sure, 1e-9), titreme_dilim_sn=ds / max(sure * ga.DILIM, 1e-9),
                              titreme_nesne_sahte=ns, gercek_gizli_orani=100.0 * int((m & tr).sum()) / max(top, 1))
    return out


def md_yaz(t, yol, baslik):
    sat = ["### " + baslik, ""]
    for g in sa.KATEGORILER + ["TUMU"]:
        pass
    sat += ["| Yöntem | grup | dilim kararı | yanlış % | yanlış gizli % | yanlış görünür % | titreme nesne/sn | titreme dilim/sn |", "|---|---|---:|---:|---:|---:|---:|---:|"]
    for g in ["TUMU"] + sa.KATEGORILER:
        for ad in t:
            r = t[ad][g]
            sat.append("| %s | %s | %d | %.1f | %.1f | %.1f | %.2f | %.2f |" % (AD[ad], g, r["dilim_karari"], r["yuzde_yanlis"], r["yuzde_yanlis_gizli"],
                                                                         r["yuzde_yanlis_gorunur"], r["titreme_nesne_sn"], r["titreme_dilim_sn"]))
    with open(yol, "w", encoding="utf-8") as f:
        f.write("\n".join(sat) + "\n")


def kayma_tablosu(z, nes, mesafe_lim=MENZIL_M):
    """Kategori başına ekran kayması (% ekran genişliği, cm yerde): ortanca / %90 / en çok. Çizilen = ekranda, 6 m içinde (tüm kareler,
    dilim kararından bağımsız: ölçü, nesnenin zemin noktasının tahmin ve gerçek poza göre ekran farkıdır)."""
    out = {}
    ok = z["gorus_e"] & (z["mesafe"] < mesafe_lim) & np.isfinite(z["kayma_yuzde"])
    for g in sa.KATEGORILER + ["TUMU"]:
        ks = [k for k, n in enumerate(nes) if n.kat == g] if g != "TUMU" else list(range(len(nes)))
        m = ok[:, ks]
        r = {}
        for ad in ("kayma_yuzde", "kayma_cm", "kayma_px"):
            a = z[ad][:, ks][m & np.isfinite(z[ad][:, ks])]
            r[ad] = dict(ortanca=float(np.median(a)), p90=float(np.percentile(a, 90)), max=float(a.max())) if len(a) else None
        r["ornek"] = int(m.sum())
        out[g] = r
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--varyant", nargs="*", default=["D", "A"])
    ap.add_argument("--yeniden", action="store_true")
    a = ap.parse_args()
    for var in a.varyant:
        v = ov.Veri(var)
        nes = sa.sahne(v)
        z = geo_hesapla(v, nes, os.path.join(ov.CIKTI, "onbellek_arh_%s.npz" % var), a.yeniden)
        t = tablo(z, nes)
        kt = kayma_tablosu(z, nes)
        with open(os.path.join(ov.CIKTI, "ortme_tablo_%s.json" % var), "w", encoding="utf-8") as f:
            json.dump(dict(ortme=t, kayma=kt), f, ensure_ascii=False, indent=1)
        md_yaz(t, os.path.join(ov.CIKTI, "ortme_tablo_%s.md" % var), "Örtme kararları, poz %s" % var)
        print("== poz", var)
        for ad in t:
            r = t[ad]["TUMU"]
            print("%-28s yanlış %5.1f%%  (gizli %4.1f, görünür %4.1f)  titreme nesne %.2f/sn dilim %.2f/sn" % (
                AD[ad], r["yuzde_yanlis"], r["yuzde_yanlis_gizli"], r["yuzde_yanlis_gorunur"], r["titreme_nesne_sn"], r["titreme_dilim_sn"]))


if __name__ == "__main__":
    main()
