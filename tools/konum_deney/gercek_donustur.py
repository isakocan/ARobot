"""Gerçek robot kayıtlarını (kayitlar/2026-11-14_*: goruntu.h264 + kareler.csv + durum.jsonl) KAYIT BİÇİMİ v1'e çevirir.

Çıktı: kayitlar/gercek/<ad>/  (bilgi.json, kareler.mjpg [varyant B], kareler.csv, komutlar.csv).  Kaynak klasörlere YAZILMAZ.
Zaman: kareler.csv.t_ms ve durum.jsonl.t aynı saatten (surus_kaydi.py: t0 = perf_counter, ms()); ikisi de ms, kayıt başından.
Karenin t_ms'si köprüde karenin ULAŞMA zamanıdır (kamera pozlama anı değil); gecikme/titreşim ayrı ölçülür (gercek_zaman.py).
Komut: durum.jsonl'daki motor.sol / motor.sag (robotun bildirdiği GERÇEK motor PWM'i, ~7 Hz). 'istenen' (kullanıcı isteği) yalnız kopru.log'da 5 sn'de bir.
JPEG kalitesi 95: H.264 çözülmüş kare yeniden sıkıştırılır (ek kayıp küçük).
Kullanım: python gercek_donustur.py [kayit_adi_parcasi ...]"""
import glob
import json
import os
import subprocess
import sys

import cv2
import imageio_ffmpeg
import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
from kayit import KayitYazici  # noqa: E402

KOK = os.path.abspath(os.path.join(KLASOR, "..", ".."))
HAM = r"C:\Users\PC\Desktop\ARobot\kayitlar"
CIKTI = os.path.join(KOK, "kayitlar", "gercek")
W, H = 1280, 720
KAM_H = 0.17


def kisa_ad(d):
    return os.path.basename(d)[20:] if os.path.basename(d)[:4] == "2026" else os.path.basename(d)   # 2026-11-14_15-10-04_1_bekleme -> 1_bekleme


def durum_komutlari(d):
    """durum.jsonl -> [(t_ms, sol, sag)] (yalnız geçerli örnekler; 'hata' satırları = HTTP zaman aşımı, ~1 sn boşluk)."""
    import json as _j
    L = []
    for l in open(os.path.join(d, "durum.jsonl"), encoding="utf-8"):
        try:
            j = _j.loads(l)
        except Exception:
            continue
        if "motor" in j and isinstance(j["motor"], dict) and "t" in j:
            L.append((float(j["t"]), j["motor"].get("sol", 0), j["motor"].get("sag", 0)))
    L.sort()
    return L


def uygulama_komutlari(d, L):
    """uygulama.log'un saniyede bir satırı ('robot motor a/b'; UDP kanalından, HTTP yoklamasından bağımsız) -> kayıt saatine oturtulmuş [(t_ms, sol, sag)].
    Ofset, durum örnekleriyle en küçük mutlak farkla (-3..3 sn) bulunur. Dönüş: (liste, ofset_sn, hata)."""
    import re
    U = []
    yol = os.path.join(d, "uygulama.log")
    if not os.path.exists(yol):
        return [], None, None
    for l in open(yol, encoding="utf-8"):
        m = re.match(r"(\d+):(\d+):(\d+\.\d+) .*robot motor (-?\d+)/(-?\d+)", l)
        if m:
            U.append((int(m[1]) * 3600 + int(m[2]) * 60 + float(m[3]), int(m[4]), int(m[5])))
    if not U or not L:
        return [], None, None
    U = np.array(U)
    g = np.array(L)
    gt = g[:, 0] / 1000.0
    best = None
    for off in np.arange(-3, 3, 0.02):
        tt = U[:, 0] - U[0, 0] + off
        e = np.mean(np.abs(np.interp(tt, gt, g[:, 1]) - U[:, 1]) + np.abs(np.interp(tt, gt, g[:, 2]) - U[:, 2]))
        if best is None or e < best[0]:
            best = (e, off)
    e, off = best
    tt = U[:, 0] - U[0, 0] + off
    return [(float(t * 1000), float(a), float(b)) for t, a, b in zip(tt, U[:, 1], U[:, 2])], float(off), float(e)


def komut_birlestir(L, U, bosluk_ms=450):
    """Durum örneklerinde > bosluk_ms boşluk varsa (HTTP zaman aşımı) uygulama.log örneklerini yalnız boşluğa koyar. Dönüş: (birleşik liste, boşluk sayısı, doldurulan örnek)."""
    L = list(L)
    t = np.array([x[0] for x in L])
    ek = []
    nb = 0
    for i in np.where(np.diff(t) > bosluk_ms)[0]:
        nb += 1
        for u in U:
            if t[i] + 150 < u[0] < t[i + 1] - 150:
                ek.append(u)
    return sorted(L + ek), nb, len(ek)


def donustur(d):
    ad = kisa_ad(d)
    cikis = os.path.join(CIKTI, ad)
    if os.path.exists(os.path.join(cikis, "bilgi.json")):
        print("var, atlandı:", ad)
        return cikis
    import csv
    satir = list(csv.DictReader(open(os.path.join(d, "kareler.csv"))))
    cmd = [imageio_ffmpeg.get_ffmpeg_exe(), "-loglevel", "error", "-i", os.path.join(d, "goruntu.h264"),
           "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", "bgr24", "-"]
    p = subprocess.Popen(cmd, stdout=subprocess.PIPE)
    n = 0
    ts = []
    bilgi = {"bicim": 1, "kaynak": "gercek", "ad": ad,
             "kamera": {"tur": "cift_balikgozu", "genislik": W, "yukseklik": H, "fps": 15},
             "kamera_yuksekligi_m": KAM_H,
             "kamera_yuksekligi_kaynak": "kullanici olcumu (iki kez), 14 Kasim 2026",
             "ham_klasor": os.path.basename(d), "zaman_aciklama": "t_ms = karenin kopruye ulasma zamani (surus_kaydi.py)",
             "kayma_modeli": {"komut_donusumu": {"pwm_255_dogrusal_hiz_m_sn": 0.55, "pwm_255_donus_derece_sn": 197,
                              "not": "SIM URETECINDEN KOPYA VARSAYIM (yontem_komut._nominal bunu okur); GERCEK ROBOTTA OLCULMEDI. Bantla olculmus 2 m ve yerinde 360 donus gelince degistirilmeli."}},
             "komut_aciklama": "komutlar.csv = durum.jsonl motor.sol/sag (robotun bildirdigi motor PWM'i)"}
    with KayitYazici(cikis, bilgi, "B") as y:
        while True:
            b = p.stdout.read(W * H * 3)
            if len(b) < W * H * 3:
                break
            im = np.frombuffer(b, np.uint8).reshape(H, W, 3)
            t = float(satir[n]["t_ms"]) if n < len(satir) else float("nan")
            y.kare_ekle(int(round(t)) if t == t else 0, im, 95)
            n += 1
        p.wait()
        L = durum_komutlari(d)
        U, off, hata = uygulama_komutlari(d, L)
        L, nb, nek = komut_birlestir(L, U)
        y.bilgi["komut_bosluk"] = {"bosluk_sayisi": nb, "uygulama_log_ile_doldurulan": nek, "uygulama_log_ofset_sn": off, "ofset_ort_mutlak_fark_pwm": hata,
                                   "aciklama": "durum.jsonl'da >0,45 sn bosluk (HTTP zaman asimi) uygulama.log 1 Hz motor degeriyle dolduruldu; kalan bosluklarda Kayit.komut son degeri tutar"}
        y.komutlar_yaz([int(round(a)) for a, _, _ in L], [b for _, b, _ in L], [c for _, _, c in L])
        y.bilgi["kare_sayisi_h264"] = n
        y.bilgi["kare_sayisi_csv"] = len(satir)
    print("%s: h264 %d kare, csv %d satir, durum %d" % (ad, n, len(satir), len(L)))
    return cikis


def komutlari_yeniden(d):
    """Yalnız komutlar.csv ve bilgi.json'daki komut alanlarını yeniden yazar (kareler dokunulmaz)."""
    ad = kisa_ad(d)
    cikis = os.path.join(CIKTI, ad)
    L = durum_komutlari(d)
    U, off, hata = uygulama_komutlari(d, L)
    L, nb, nek = komut_birlestir(L, U)
    import csv
    with open(os.path.join(cikis, "komutlar.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["t_ms", "sol", "sag"])
        for t, a, b in L:
            w.writerow([int(round(t)), "%g" % a, "%g" % b])
    bj = os.path.join(cikis, "bilgi.json")
    b = json.load(open(bj, encoding="utf-8"))
    b["komut_bosluk"] = {"bosluk_sayisi": nb, "uygulama_log_ile_doldurulan": nek, "uygulama_log_ofset_sn": off, "ofset_ort_mutlak_fark_pwm": hata,
                         "aciklama": "durum.jsonl'da >0,45 sn bosluk (HTTP zaman asimi) uygulama.log 1 Hz motor degeriyle dolduruldu; kalan bosluklarda Kayit.komut son degeri tutar"}
    json.dump(b, open(bj, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    print("%s: komut bosluk %d, uygulama.log ile doldurulan %d, ofset %s sn, ort mutlak fark %s PWM" % (ad, nb, nek, off if off is None else round(off, 2), hata if hata is None else round(hata, 1)))


if __name__ == "__main__":
    if "--komut" in sys.argv:
        for d in sorted(glob.glob(os.path.join(HAM, "2026-11-14_*"))):
            komutlari_yeniden(d)
        sys.exit(0)
    os.makedirs(CIKTI, exist_ok=True)
    dl = sorted(glob.glob(os.path.join(HAM, "2026-11-14_*")))
    if len(sys.argv) > 1:
        dl = [d for d in dl if any(a in d for a in sys.argv[1:])]
    for d in dl:
        donustur(d)
