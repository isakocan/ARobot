"""M2D birikimli yon kaymasinin kaynagi: ardisik kare cifti (aralik ARALIK kare) DOGRUDAN M2D eslesmesi ile birikimli zincirin farki (kalibre geometri).
  python gercek_e2_yon_kaynak.py <kayit> [aralik_kare=15]  -> e2/yon_kaynak_<kayit>.npz + yazdirir
Her cift (i, i+aralik): dogrudan dyaw (yontem_pusula_nokta, komutsuz, +-180), zincir dyaw = yon[i+aralik]-yon[i] (komutsuz ve komutlu LCN M2D onbellegi).
Birikimli fark = sum(dogrudan - zincir): zincirin dogrudana gore kaymasi; ani sicrama (tek cift) mi, duzgun birikim mi. Dusuk ic (<40) ciftler ayri raporlanir."""
import os
import sys

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gercek_e2_hazirla as hz  # noqa: E402


def sar(x):
    return (np.asarray(x) + 180.0) % 360.0 - 180.0


def ana(ad, aralik):
    bo, ko, zo = hz.kur()
    import yontem_pusula_nokta as ypn
    import birlesik_yon as by
    from kayit import Kayit
    from kamera import Kamera
    kay = Kayit(os.path.join(hz.ok.go.GERCEK, ad))
    kam = Kamera.bilgiden(kay.bilgi)
    y = ypn.Yontem(dict(kay.bilgi), kam, kip="duzlem", komut_oncel=False)
    yk = by.yukle(ad, False)["yon"]
    yl = by.yukle(ad, True)["yon"]
    t = kay.t_ms / 1000.0
    idx = list(range(0, len(kay) - aralik, aralik))
    cache = {}

    def oz(i):
        if i not in cache:
            gri, gec = y.don(kay.resim(i))
            cache[i] = y._cikar(gri, gec)
        return cache[i]
    D, ICs, Zk, Zl = [], [], [], []
    for i in idx:
        j = i + aralik
        dy, kal, tamam, b = y._esle(oz(j), oz(i), 0.0, 180.0)
        D.append(float(dy))
        ICs.append(int(b.get("icerik", 0)))
        Zk.append(float(sar(yk[j] - yk[i])))
        Zl.append(float(sar(yl[j] - yl[i])))
        cache.pop(i - aralik, None)
    D, ICs, Zk, Zl = map(np.array, (D, ICs, Zk, Zl))
    ti = t[idx]
    np.savez_compressed(os.path.join(hz.ok.go.SONUC, "e2", "yon_kaynak_%s.npz" % ad), t=ti, dogrudan=D, ic=ICs, zincir_komutsuz=Zk, zincir_komutlu=Zl)
    iyi = ICs >= 40
    for nm, Z in (("komutsuz", Zk), ("komutlu", Zl)):
        fark = sar(D - Z)
        print("%s %s: %d cift (iyi ic>=40: %d); toplam dogrudan %.0f, zincir %.0f derece; birikimli fark (iyi ciftler) %.1f; (tum) %.1f; |fark|>3 olan cift %d, maks |fark| %.1f (t=%.1f)" % (
            ad, nm, len(D), iyi.sum(), D[iyi].sum(), Z[iyi].sum(), fark[iyi].sum(), fark.sum(), (abs(fark[iyi]) > 3).sum(), abs(fark[iyi]).max(), ti[iyi][np.argmax(abs(fark[iyi]))]))
        # ceyrek ceyrek birikim
        c = np.cumsum(np.where(iyi, fark, 0.0))
        q = np.linspace(0, len(c) - 1, 6).astype(int)
        print("   birikimli fark (derece) zamana gore:", [(round(float(ti[k]), 0), round(float(c[k]), 1)) for k in q])
        print("   yontem sapmasi: zincir/dogrudan oran %.3f" % (Z[iyi].sum() / max(D[iyi].sum(), 1e-9)))


if __name__ == "__main__":
    ana(sys.argv[1], int(sys.argv[2]) if len(sys.argv) > 2 else 15)
