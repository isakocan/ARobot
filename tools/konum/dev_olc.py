"""Telefon Dev kaydı ölçüm raporu (files/dev/<zaman>/: iz.csv kare başına, konum.csv robot konumu geldikçe).
Kullanıcı "AR kötüydü / konum kaydı" dediğinde ilk çalıştırılacak araç: sorunun nereden geldiğini sayıyla söyler.

python tools/konum/dev_olc.py <dev_kaydi> [--cizim cikti.png]

Bölümler:
  görüntü akışı : kare hızı, takılmalar (0,25 sn'den uzun boşluk), robotun atladığı kareler (X-Kare farkı)
  dönüş ölçümü  : görüntü pusulası kaç karede tutmadı, kaç karede motor tahminiyle geçildi
  gecikme       : robot konumunun ait olduğu kare ile ekrandaki son kare arası (ms)
  AR hatası     : robot işareti gördüğünde, telefonun o karede gösterdiği konuma göre işaretin açısı ne kadar yanlış
                  (derece; ekranda nesnenin yana kayması). Asıl kalite ölçüsü.
  durağan kayma : komut yokken (1,5 sn+) ekrandaki konumun ne kadar kaydığı (nesneler "kendi kendine kayıyor")
  düzeltmeler   : robot konumu geldikçe telefonun yaptığı yön düzeltmesinin büyüklüğü (büyükse telefon dönüşü kaçırıyor)
"""
import csv
import math
import os
import sys

import numpy as np


def oku(yol):
    if not os.path.exists(yol):
        return []
    return list(csv.DictReader(open(yol, encoding="utf-8")))


def f(r, k, v=float("nan")):
    try:
        return float(r.get(k, ""))
    except (TypeError, ValueError):
        return v


def yuzde(a, q):
    a = [x for x in a if not math.isnan(x)]
    return float(np.percentile(a, q)) if a else float("nan")


def da(a):
    return (a + 180.0) % 360.0 - 180.0


def main():
    arg = sys.argv[1:]
    cizim = None
    if "--cizim" in arg:
        i = arg.index("--cizim"); cizim = arg[i + 1]; del arg[i:i + 2]
    K = arg[0]
    iz = oku(os.path.join(K, "iz.csv")); kn = oku(os.path.join(K, "konum.csv"))
    print("== %s: %d kare, %d robot konumu" % (K, len(iz), len(kn)))
    if not iz:
        return
    t = np.array([f(r, "t") for r in iz])
    sure = t[-1] - t[0]
    dt = np.diff(t)
    takil = dt[dt > 0.25]
    print("görüntü akışı : %.1f kare/sn (%.0f sn), 0,25 sn'den uzun boşluk %d kez (en uzun %.2f sn)" % (
        len(iz) / max(sure, 1e-3), sure, len(takil), takil.max() if len(takil) else 0))
    rk = [f(r, "robot_kare", -1) for r in iz]
    if any(x >= 0 for x in rk):
        atl = [b - a - 1 for a, b in zip(rk, rk[1:]) if a >= 0 and b > a]
        print("                robotun kamera karelerinden telefona gelmeyen: ortalama %.1f / kare, en çok %d art arda" % (
            np.mean(atl) if atl else 0, max(atl) if atl else 0))
    else:
        print("                (eski kayıt: kare numarası yok)")
    tut = sum(1 for r in iz if r.get("pusula_tuttu") == "1"); tah = sum(1 for r in iz if r.get("tahmin_kullanildi") == "1")
    print("dönüş ölçümü  : görüntü pusulası %d karede tuttu, %d karede motor tahminiyle geçildi, %d karede ölçülemedi" % (
        tut, tah, len(iz) - tut - tah))
    if kn:
        gm = [f(r, "gecikme_ms") for r in kn if f(r, "gecikme_ms", -1) >= 0]
        es = sum(1 for r in kn if r.get("eslesti") == "1")
        print("gecikme       : robot konumu %d/%d karede kareyle eşleşti; gecikme ortanca %.0f ms, %%90 %.0f ms" % (
            es, len(kn), yuzde(gm, 50), yuzde(gm, 90)))
        ar = [f(r, "ar_hata") for r in kn if r.get("ar_hata", "") != ""]
        if ar:
            aa = np.abs(ar)
            print("AR hatası     : ortanca %.1f°, %%90 %.1f°, en çok %.1f° (%d ölçüm; 1° ~ 17 px yatay)" % (
                np.median(aa), np.percentile(aa, 90), aa.max(), len(aa)))
            kotu = [(f(r, "t"), f(r, "ar_hata")) for r in kn if r.get("ar_hata", "") != "" and abs(f(r, "ar_hata")) > 5]
            if kotu:
                print("                5°'den büyük: %d kez, ilk: %s" % (len(kotu), ", ".join("t=%.1f %.0f°" % q for q in kotu[:6])))
        else:
            print("AR hatası     : ölçüm yok (robot işareti görmedi ya da ev seçilmedi)")
        rh = [abs(f(r, "k_hata_yon")) for r in kn if r.get("k_hata_yon", "") != ""]
        if rh:
            print("robot         : işaretle kendi tahmini arası fark ortanca %.1f°, %%90 %.1f°; yeniden kilit %d; belirsizlik yön ortanca ±%.1f°" % (
                np.median(rh), np.percentile(rh, 90), int(max(f(r, "k_kilit", 0) for r in kn)), yuzde([f(r, "k_sy") for r in kn], 50)))
        # telefonun yön düzeltmeleri: fark_yaw'daki değişim
        fy = [f(r, "fark_yaw") for r in kn]
        dz = [abs(da(b - a)) for a, b in zip(fy, fy[1:]) if not (math.isnan(a) or math.isnan(b))]
        if dz:
            print("düzeltmeler   : telefon yönü robot konumu geldikçe ortanca %.2f°, %%90 %.1f°, en çok %.1f° düzeltildi" % (
                np.median(dz), np.percentile(dz, 90), max(dz)))
    # durağan kayma: komut 0 iken poz değişimi
    bloklar = []; bas = None
    for i, r in enumerate(iz):
        dur = r.get("komut_sol") == "0" and r.get("komut_sag") == "0"
        if dur and bas is None:
            bas = i
        if (not dur or i == len(iz) - 1) and bas is not None:
            if t[i] - t[bas] > 1.5:
                b = [q for q in range(bas, i + 1) if t[q] - t[bas] > 0.8]       # durduktan 0,8 sn sonrası (robot süzülür)
                if len(b) > 2:
                    x0, z0, y0 = f(iz[b[0]], "poz_x"), f(iz[b[0]], "poz_z"), f(iz[b[0]], "poz_yaw")
                    m = max(math.hypot(f(iz[q], "poz_x") - x0, f(iz[q], "poz_z") - z0) for q in b)
                    a = max(abs(da(f(iz[q], "poz_yaw") - y0)) for q in b)
                    bloklar.append((t[bas] - t[0], m, a, t[i] - t[bas]))
            bas = None
    kotu = [q for q in bloklar if q[1] > 0.05 or q[2] > 3]
    print("durağan kayma : %d duruş, en çok %.0f cm / %.1f°; 5 cm ya da 3°'yi geçen %d" % (
        len(bloklar), max([q[1] for q in bloklar] or [0]) * 100, max([q[2] for q in bloklar] or [0]), len(kotu)))
    for q in kotu[:6]:
        print("                t=%.1f sn: %.0f cm, %.1f° (%.1f sn duruşta)" % (q[0], q[1] * 100, q[2], q[3]))
    if cizim and kn:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        fig, ax = plt.subplots(3, 1, figsize=(13, 9), sharex=True)
        tk = [f(r, "t") - t[0] for r in kn]
        ax[0].plot([f(r, "t") - t[0] for r in kn if r.get("ar_hata", "") != ""], [f(r, "ar_hata") for r in kn if r.get("ar_hata", "") != ""], ".", ms=4)
        ax[0].set_ylabel("AR hatası (°)"); ax[0].axhline(0, color="k", lw=0.5); ax[0].set_ylim(-20, 20); ax[0].grid(alpha=0.3)
        ax[1].plot(t - t[0], [f(r, "poz_yaw") for r in iz], lw=1, label="telefon (ekran)")
        ax[1].plot(tk, [f(r, "k_yaw") for r in kn], ".", ms=3, label="robot (geldiğinde)")
        ax[1].set_ylabel("yön (°)"); ax[1].legend(); ax[1].grid(alpha=0.3)
        ax[2].plot(t - t[0], [f(r, "komut_sol") for r in iz], lw=1, label="komut sol")
        ax[2].plot(t - t[0], [f(r, "komut_sag") for r in iz], lw=1, label="komut sağ")
        ax[2].plot(tk, [f(r, "gecikme_ms") / 2 for r in kn], ".", ms=2, label="gecikme / 2 (ms)")
        ax[2].legend(); ax[2].grid(alpha=0.3); ax[2].set_xlabel("sn")
        fig.tight_layout(); fig.savefig(cizim, dpi=90)
        print("çizim:", cizim)


if __name__ == "__main__":
    main()
