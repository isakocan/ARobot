"""Zemin sınırı yöntemlerini çalıştırır (taramaları önbelleğe yazar) ve gerçek mesafelere göre ölçer.

Tek komutla yeniden çalıştırılabilir (gerçek veri dosyası değişirse):
    python tools/konum_deney/zemin_degerlendir.py --hepsi            # taramaları üret (varsa atla) + ölç + yazdır
    python tools/konum_deney/zemin_degerlendir.py --sadece_olc       # yalnız ölçüt tablosu (taramalar hazırsa)
    python tools/konum_deney/zemin_degerlendir.py --yontem klasik --tur sim --adim 5   # tek kombinasyon

Yöntemler: klasik | model_gorunum | model_gorunum_ilk | model_serit. Girdiler: sim | theta.
Gerçek: kayitlar/sim/<ad>/gercek_mesafe_gorunen_zemin.csv varsa o, yoksa eski gercek_mesafe_zemin.csv (KUTU, uyarı basılır).
Çıktı: kayitlar/sonuclar/harita/tarama/<yontem>/<tur>/<ad>.npz, olcut.json, olcut.md"""
import argparse
import json
import os
import sys
import time

import cv2
import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zemin_ortak as zo  # noqa: E402

YONTEMLER = ["klasik", "model_gorunum", "model_gorunum_ilk", "model_serit"]
KUTULAR = [(0.3, 1.0), (1.0, 2.0), (2.0, 3.0), (3.0, 5.0)]
_MODEL = {}


def yontem_olustur(ad):
    if ad == "klasik":
        import zemin_klasik as zk
        return zk.klasik_sinir
    import zemin_model as zm
    if "m" not in _MODEL:
        _MODEL["m"] = zm.Bolutleyici()
    b = _MODEL["m"]
    mod = "serit" if ad == "model_serit" else "gorunum"
    sinir = "ilk" if ad.endswith("_ilk") else "kesim"

    def f(g):
        Pk, gec = b.zemin_olasiligi(g, mod=("sentez" if mod == "serit" else "gorunum"))
        return zm.p_den_sinir(Pk, gec, g, sinir)
    return f


def tara_model_hepsi(tur, ad, adim=20, yeniden=False):
    """SegFormer'ı kare başına BİR KEZ çalıştırıp üç çıktı üretir: model_gorunum (görünümler + kesim/DP), model_gorunum_ilk
    (aynı olasılık, dıştan ilk zemin-dışı piksel), model_serit (doğrudan eşdikdörtgen şerit + kesim/DP). Süreler yöntem başına yazılır."""
    adlar = ["model_gorunum", "model_gorunum_ilk", "model_serit"]
    if all(os.path.exists(tarama_yolu(y, tur, ad)) for y in adlar) and not yeniden:
        return
    import zemin_model as zm
    if "m" not in _MODEL:
        _MODEL["m"] = zm.Bolutleyici()
    b = _MODEL["m"]
    kg = zo.KayitGirdisi(ad, tur)
    idx = list(range(0, kg.n, adim))
    cikti = {y: dict(M=np.full((len(idx), zo.N_AZ), np.nan, np.float32), G=np.zeros((len(idx), zo.N_AZ), np.float32), t=[]) for y in adlar}
    for i, sr in enumerate(idx):
        g = kg.girdi(sr)
        for y in adlar:
            t0 = time.perf_counter()
            if y == "model_gorunum":
                Pk, gec = b.zemin_olasiligi(g, mod="gorunum")
                P_gor, gec_gor = Pk, gec
                r = zm.p_den_sinir(Pk, gec, g, "kesim")
            elif y == "model_gorunum_ilk":
                r = zm.p_den_sinir(P_gor, gec_gor, g, "ilk")      # olasılık yukarıdaki çağrıdan; süre yalnızca sınır çıkarma
            else:
                Pk, gec = b.zemin_olasiligi(g, mod="sentez")
                r = zm.p_den_sinir(Pk, gec, g, "kesim")
            cikti[y]["M"][i], cikti[y]["G"][i] = r.mesafe, r.guven
            if b.cihaz == "cuda":
                b.torch.cuda.synchronize()
            cikti[y]["t"].append((time.perf_counter() - t0) * 1000)
        if i % 10 == 0:
            print("   model", tur, ad, i, "/", len(idx), flush=True)
        if b.cihaz == "cuda":
            b.torch.cuda.synchronize()
    if b.cihaz == "cuda":
        print("  tepe VRAM (MB): %.0f" % (b.torch.cuda.max_memory_allocated() / 1e6), flush=True)
    for y in adlar:
        yol = tarama_yolu(y, tur, ad)
        os.makedirs(os.path.dirname(yol), exist_ok=True)
        np.savez_compressed(yol, kare=np.array(idx), mesafe=cikti[y]["M"], guven=cikti[y]["G"], sure_ms=np.array(cikti[y]["t"]),
                            oku_ms=np.zeros(len(idx)), max_menzil=r.max_menzil, h=kg.h, cihaz=b.cihaz)
    print("  model %s/%s: %d kare, ms/kare gorunum %.0f, ilk(+sınır) %.0f, serit %.0f (cihaz %s)" % (
        tur, ad, len(idx), np.mean(cikti["model_gorunum"]["t"]), np.mean(cikti["model_gorunum_ilk"]["t"]), np.mean(cikti["model_serit"]["t"]), b.cihaz), flush=True)


def tarama_yolu(yontem, tur, ad):
    return os.path.join(zo.CIKTI, "tarama", yontem, tur, ad + ".npz")


def tara(yontem, tur, ad, adim=5, yeniden=False):
    yol = tarama_yolu(yontem, tur, ad)
    if os.path.exists(yol) and not yeniden:
        return yol
    os.makedirs(os.path.dirname(yol), exist_ok=True)
    f = yontem_olustur(yontem)
    kg = zo.KayitGirdisi(ad, tur)
    idx = list(range(0, kg.n, adim))
    M = np.full((len(idx), zo.N_AZ), np.nan, np.float32)
    Gv = np.zeros((len(idx), zo.N_AZ), np.float32)
    sure, sure_oku = [], []
    for i, s in enumerate(idx):
        t0 = time.perf_counter()
        g = kg.girdi(s)
        t1 = time.perf_counter()
        r = f(g)
        if yontem != "klasik" and "cuda" in str(getattr(_MODEL.get("m"), "cihaz", "")):
            _MODEL["m"].torch.cuda.synchronize()
        t2 = time.perf_counter()
        M[i], Gv[i] = r.mesafe, r.guven
        sure.append((t2 - t1) * 1000)
        sure_oku.append((t1 - t0) * 1000)
    np.savez_compressed(yol, kare=np.array(idx), mesafe=M, guven=Gv, sure_ms=np.array(sure), oku_ms=np.array(sure_oku),
                        max_menzil=r.max_menzil, h=kg.h)
    print("  %s/%s/%s: %d kare, %.0f ms/kare (okuma %.0f)" % (yontem, tur, ad, len(idx), np.mean(sure), np.mean(sure_oku)))
    return yol


def tarama_yukle(yontem, tur, ad):
    z = np.load(tarama_yolu(yontem, tur, ad))
    return {k: z[k] for k in z.files}


def hata_istatistik(hata_m, gercek, goreli_gross=0.30):
    """Vektörler (aynı uzunlukta). Dönüş sözlük (n, abs_med, abs_p90, rel_med, rel_p90, gross)."""
    n = len(gercek)
    if n == 0:
        return dict(n=0)
    mut = np.abs(hata_m)
    rel = mut / gercek
    return dict(n=int(n), abs_med=float(np.median(mut)), abs_p90=float(np.percentile(mut, 90)),
                rel_med=float(np.median(rel)), rel_p90=float(np.percentile(rel, 90)),
                gross=float(np.mean(rel > goreli_gross)))


_DMIN = {}


def theta_en_yakin_gorunur():
    """Theta girdisinde gövde maskesinin hemen ötesindeki en yakın görünür zemin mesafesi (360,), m. Daha yakın engel görünmez."""
    if "d" not in _DMIN:
        kam = zo.Kamera()
        g = zo.theta_maskesi(kam)
        H = g.shape[0]
        v0 = H // 2
        gec = zo.azimut_sutunlari(g[v0:].astype(np.float32)) > 0.15
        a = zo.satir_aci(v0 + np.arange(H - v0) + 0.5, H)[:, None]
        a_low = np.where(~gec, a, -1.0).max(0)
        _DMIN["d"] = zo.aci_mesafe(a_low, 0.255)
    return _DMIN["d"]


def olc(yontem, tur, adlar=None, ortak=None):
    """Yöntem+girdi için tüm kayıtlarda havuzlanmış ölçüt, kutu bazında. (sözlük, kullanılan gerçek türü listesi)"""
    adlar = adlar or zo.KAYIT_ADLARI
    hepsi_e, hepsi_t, hepsi_g, tur_adlari = [], [], [], set()
    kayit_basi = {}
    for ad in adlar:
        if not os.path.exists(tarama_yolu(yontem, tur, ad)):
            continue
        z = tarama_yukle(yontem, tur, ad)
        ts, kn, tr = zo.gercek_zemin_dosyasi(ad)
        tur_adlari.add(ts)
        satir = {int(k): i for i, k in enumerate(kn)}
        izinli = None
        if ortak and os.path.exists(tarama_yolu(ortak, tur, ad)):
            izinli = set(int(k) for k in np.load(tarama_yolu(ortak, tur, ad))["kare"])
        sel = [satir[int(k)] for k in z["kare"] if int(k) in satir and (izinli is None or int(k) in izinli)]
        ii = [i for i, k in enumerate(z["kare"]) if int(k) in satir and (izinli is None or int(k) in izinli)]
        e = z["mesafe"][ii]
        t = tr[sel].copy()
        if tur == "theta":
            t[t < theta_en_yakin_gorunur()[None, :] + 0.03] = np.nan     # gövdenin gizlediği yakın engeller değerlendirme dışı
        hepsi_e.append(e); hepsi_t.append(t); hepsi_g.append(z["guven"][ii])
        m = np.isfinite(e) & np.isfinite(t) & (t >= 0.3) & (t < 5.0)
        kayit_basi[ad] = hata_istatistik((e - t)[m], t[m])
    if not hepsi_e:
        return None, tur_adlari
    E = np.concatenate(hepsi_e).ravel(); T = np.concatenate(hepsi_t).ravel(); G = np.concatenate(hepsi_g).ravel()
    ok = np.isfinite(E) & np.isfinite(T)
    sonuc = {"kutu": {}, "kayit": kayit_basi}
    for a, b in KUTULAR:
        m = ok & (T >= a) & (T < b)
        sonuc["kutu"]["%g-%g" % (a, b)] = hata_istatistik((E - T)[m], T[m])
    m = ok & (T >= 0.3) & (T < 5.0)
    sonuc["kutu"]["0.3-5"] = hata_istatistik((E - T)[m], T[m])
    # işaretli yanılgı: kısa (engel sanma) mı, uzun mu
    sonuc["kisa_oran"] = float(np.mean((E - T)[m] < -0.3 * T[m])) if m.any() else None
    sonuc["uzun_oran"] = float(np.mean((E - T)[m] > 0.3 * T[m])) if m.any() else None
    # güvenle süzme: güven>=0.3 ve menzil içinde
    return sonuc, tur_adlari


def kuramsal_tablo():
    """Bir pikselin (satır) farklı mesafelerdeki hatası (m)."""
    h = 0.255
    satirlar = []
    girdiler = [("sim 1536x768 (4,27 px/derece)", 768, None), ("theta eşdikdörtgen 1280x640 (3,56 px/derece)", 640, None),
                ("theta ham balık gözü (3,16 px/derece)", 640, 180.0 / np.pi * 0 + 1.0 / 3.16)]
    for ad, Y, aci in girdiler:
        satirlar.append((ad, [float(zo.piksel_hatasi(d, Y, h, aci)) for d in (0.5, 1, 2, 3, 4, 5)],
                         zo.guvenilir_menzil(Y, h, 0.20, aci), zo.guvenilir_menzil(Y, h, 0.10, aci)))
    return satirlar


def tablo_yaz(sonuclar):
    s = []
    for (yontem, tur), (r, tn) in sonuclar.items():
        if r is None:
            continue
        s.append("\n### %s | girdi: %s | gerçek: %s" % (yontem, tur, ", ".join(sorted(tn))))
        s.append("| mesafe (m) | n | mut. hata ort. (cm) | mut. hata %90 (cm) | göreli ort. (%) | göreli %90 (%) | ağır hata >%30 (%) |")
        s.append("|---|---|---|---|---|---|---|")
        for k, v in r["kutu"].items():
            if v["n"] == 0:
                continue
            s.append("| %s | %d | %.1f | %.1f | %.1f | %.1f | %.1f |" % (k, v["n"], v["abs_med"] * 100, v["abs_p90"] * 100,
                                                                      v["rel_med"] * 100, v["rel_p90"] * 100, v["gross"] * 100))
        s.append("(0.3-5 m: tahmin gerçekten >%%30 KISA %.1f%%, >%%30 UZUN %.1f%%)" % (100 * r["kisa_oran"], 100 * r["uzun_oran"]))
    return "\n".join(s)


def ana():
    ap = argparse.ArgumentParser()
    ap.add_argument("--yontem", nargs="*", default=None)
    ap.add_argument("--tur", nargs="*", default=["sim", "theta"])
    ap.add_argument("--kayit", nargs="*", default=None)
    ap.add_argument("--adim", type=int, default=5)
    ap.add_argument("--hepsi", action="store_true")
    ap.add_argument("--sadece_olc", action="store_true")
    ap.add_argument("--yeniden", action="store_true")
    a = ap.parse_args()
    yontemler = a.yontem or YONTEMLER
    adlar = a.kayit or zo.KAYIT_ADLARI
    for ad in adlar:
        ts = zo.gercek_zemin_dosyasi(ad)[0]
        if ts != "gorunen_zemin" and ad == adlar[0]:
            print("UYARI: gercek_mesafe_gorunen_zemin.csv yok; ESKİ KUTU GERÇEĞİ (gercek_mesafe_zemin.csv) kullanılıyor. "
                  "Masa altı zemini 'dolu' sayıldığı için sonuçlar eşya yanında kötü görünür.")
    if not a.sadece_olc:
        if any(y.startswith("model") for y in yontemler):
            for t in a.tur:
                for ad in adlar:
                    tara_model_hepsi(t, ad, a.adim, a.yeniden)
        for y in [y for y in yontemler if not y.startswith("model")]:
            for t in a.tur:
                for ad in adlar:
                    tara(y, t, ad, a.adim, a.yeniden)
    sonuclar = {}
    for y in yontemler:
        for t in a.tur:
            sonuclar[(y, t)] = olc(y, t, adlar, ortak="model_gorunum")
    md = tablo_yaz(sonuclar)
    print(md)
    os.makedirs(zo.CIKTI, exist_ok=True)
    with open(os.path.join(zo.CIKTI, "olcut.md"), "w", encoding="utf-8") as f:
        f.write(md)
    with open(os.path.join(zo.CIKTI, "olcut.json"), "w", encoding="utf-8") as f:
        json.dump({"%s|%s" % k: v[0] for k, v in sonuclar.items() if v[0]}, f, indent=1, ensure_ascii=False)
    print("\nKuramsal tek piksel hatası (m) 0.5/1/2/3/4/5 m ve güvenilir menzil (hata<=20cm | <=10cm):")
    for ad, hs, m20, m10 in kuramsal_tablo():
        print("  %-48s %s  %.2f m | %.2f m" % (ad, " ".join("%.3f" % x for x in hs), m20, m10))


if __name__ == "__main__":
    ana()
