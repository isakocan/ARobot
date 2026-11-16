"""Parametre çalışması koşucusu.
  python parametre_calis.py veri [varyant ...]        dönüştürülmüş giriş kareleri (yoksa) kayitlar/slam/parametre/veri/<anahtar>/ altına
  python parametre_calis.py kos --tekrar 3 [--paralel 5] [varyant ...]     gerçek zamanlı koşular (WSL, köprü toplu kipi), ölçütler kayitlar/slam/sonuc/parametre_sonuclar_v2.jsonl'a eklenir
  python parametre_calis.py tablo                       özet tablo kayitlar/slam/sonuc/parametre_tablo.md
Segment: ilk 190 sn (bitis kare dizini). Ölçütlerin çoğu 0-85 sn'ye bakar (parametre_olcut.py), 150-182 sn yerinde dönüşler için dahil.
Kosu hep gerçek zaman hızında (--gercek-zaman 1) ve tekrarlıdır, çünkü sonuçlar koşudan koşuya değişiyor."""
import argparse
import csv
import gzip
import hashlib
import json
import os
import shutil
import subprocess
import sys
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from multiprocessing import Pool

import cv2
import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
sys.path.insert(0, os.path.join(KLASOR, "..", "konum_deney"))
from kayit import Kayit  # noqa: E402
from ekdortgen_donustur import Donusturucu  # noqa: E402
from slam_kos import yaml_uret, WSL_KOK  # noqa: E402
import parametre_liste as PL  # noqa: E402
import parametre_olcut as PO  # noqa: E402

KOK = os.path.abspath(os.path.join(KLASOR, "..", ".."))
VERI = os.path.join(KOK, "kayitlar", "slam", "parametre", "veri")
KOSU = os.path.join(KOK, "kayitlar", "slam", "parametre", "kosu_v2")
SONUC = os.path.join(KOK, "kayitlar", "slam", "sonuc")
JSONL = os.path.join(SONUC, "parametre_sonuclar_v2.jsonl")
KAYIT = os.path.join(KOK, "kayitlar", "gercek", "olcum_ham")
SEGMENT_SN = 190.0
_kilit = threading.Lock()


def donusum_anahtar(d):
    if not d:
        return "taban"
    return "d_" + hashlib.md5(json.dumps(d, sort_keys=True).encode()).hexdigest()[:8]


def donusturucu_uret(d):
    kw = {}
    for k, v in d.items():
        if k == "interp":
            kw["enterpolasyon"] = dict(cubic=cv2.INTER_CUBIC, lanczos=cv2.INTER_LANCZOS4, linear=cv2.INTER_LINEAR)[v]
        elif k == "codec":
            continue
        else:
            kw[k] = v
    return Donusturucu(**kw)


def kare_sayisi(k):
    return int(np.searchsorted(k.t_ms / 1000.0, SEGMENT_SN))


def _is(args):
    d, cikti, idxler = args
    if "dn" not in _is.__dict__:
        _is.dn = donusturucu_uret(d)
        _is.k = Kayit(KAYIT)
    for s, i in idxler:
        e = _is.dn.kare(_is.k.jpeg_bayt(i))
        cv2.imwrite(os.path.join(cikti, "%06d.jpg" % s), e, [cv2.IMWRITE_JPEG_QUALITY, 95])


def h264_cevir(k, n, hedef_mp4, mbps=4):
    """Ham çift balık gözü kareleri düşük gecikmeli x264 ile ~4 Mbit/sn kodla (donanım kodlayıcıya benzer ayar: B kare yok, kısa GOP)."""
    import imageio_ffmpeg
    ff = imageio_ffmpeg.get_ffmpeg_exe()
    cmd = [ff, "-y", "-f", "rawvideo", "-pix_fmt", "bgr24", "-s", "1280x720", "-r", "15", "-i", "-", "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency",
           "-b:v", "%dM" % mbps, "-maxrate", "%dM" % mbps, "-bufsize", "%dM" % mbps, "-g", "30", "-bf", "0", "-pix_fmt", "yuv420p", hedef_mp4]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE, stderr=subprocess.DEVNULL)
    for i in range(n):
        p.stdin.write(k.resim(i).tobytes())
    p.stdin.close()
    p.wait()
    return os.path.getsize(hedef_mp4)


def veri_hazirla(d, pool_is=4):
    anahtar = donusum_anahtar(d)
    cikti = os.path.join(VERI, anahtar)
    if os.path.exists(os.path.join(cikti, "hazir")):
        return anahtar
    os.makedirs(cikti, exist_ok=True)
    k = Kayit(KAYIT)
    n = kare_sayisi(k)
    with open(os.path.join(cikti, "zamanlar.csv"), "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["sira", "kare", "t_ms"])
        for s in range(n):
            w.writerow([s, int(k.kare_numaralari[s]), int(k.t_ms[s])])
    dn = donusturucu_uret(d)
    cv2.imwrite(os.path.join(cikti, "maske.png"), dn.maske)
    if d.get("codec"):
        mp4 = os.path.join(cikti, "girdi.mp4")
        bayt = h264_cevir(k, n, mp4)
        json.dump(dict(mp4_bayt=bayt, mbit_sn=bayt * 8 / (n / 15.0) / 1e6), open(os.path.join(cikti, "codec.json"), "w"))
        cap = cv2.VideoCapture(mp4)
        for s in range(n):
            ok, im = cap.read()
            cv2.imwrite(os.path.join(cikti, "%06d.jpg" % s), dn.kare(im), [cv2.IMWRITE_JPEG_QUALITY, 95])
    else:
        isler = list(enumerate(range(n)))
        parca = [isler[j::pool_is * 6] for j in range(pool_is * 6)]
        with Pool(pool_is) as p:
            p.map(_is, [(d, cikti, pc) for pc in parca if pc])
    open(os.path.join(cikti, "hazir"), "w").write("ok")
    return anahtar


def wsl(c):
    return subprocess.run(["wsl", "-d", "Ubuntu-22.04", "-u", "root", "--", "bash", "-c", c], capture_output=True, text=True, encoding="utf-8", errors="replace",
                          env=dict(os.environ, MSYS_NO_PATHCONV="1"))


_wsl_kilit = threading.Lock()


def wsl_veri(anahtar):
    """Kareleri WSL'in yerel diskine tek tar akışıyla aktar (9p üzerinden doğrudan okuma gerçek zamanı tutturamıyor)."""
    hedef = "/root/slam/veri/p_%s" % anahtar
    with _wsl_kilit:
        if "VAR" in wsl("test -f %s/hazir && echo VAR" % hedef).stdout:
            return hedef
        wsl("rm -rf %s; mkdir -p %s" % (hedef, hedef))
        kaynak = os.path.join(VERI, anahtar)
        p1 = subprocess.Popen(["tar", "-cf", "-", "-C", kaynak, "."], stdout=subprocess.PIPE)
        subprocess.run(["wsl", "-d", "Ubuntu-22.04", "-u", "root", "--", "bash", "-c", "tar -xf - -C %s" % hedef], stdin=p1.stdout, env=dict(os.environ, MSYS_NO_PATHCONV="1"))
        p1.wait()
    return hedef


def tek_kos(ad, tekrar):
    v = PL.V[ad]
    anahtar = donusum_anahtar(v["donusum"])
    while not os.path.exists(os.path.join(VERI, anahtar, "hazir")):
        time.sleep(10)
    isim = "%s__%d" % (ad, tekrar)
    yerel = os.path.join(KOSU, isim)
    if os.path.exists(os.path.join(yerel, "olcut.json")):
        eski = json.load(open(os.path.join(yerel, "olcut.json")))
        if "hata" not in eski:
            return eski
    os.makedirs(yerel, exist_ok=True)
    yaml_uret(os.path.join(KLASOR, "ayar_ekdortgen.yaml"), v["yaml"], os.path.join(yerel, "ayar.yaml"))
    w = lambda p: WSL_KOK + "/" + p
    k = Kayit(KAYIT)
    n = kare_sayisi(k)
    vd = wsl_veri(anahtar)
    c = ("mkdir -p /root/slam/cikti/p/%s && cd /root/slam && ./" + v.get("ikili", "kopru/build/kopru") + " --vocab orb_vocab.fbow --config %s --mask %s --dizin %s --cikti cikti/p/%s --bas 0 --bitis %d "
         "--mod harita --log info --gercek-zaman %g " + v.get("ekstra", "") + " > cikti/p/%s/log.txt 2>&1") % (isim, w("kayitlar/slam/parametre/kosu_v2/%s/ayar.yaml" % isim), vd + "/maske.png", vd, isim, n, v.get("hiz", 1.0), isim)
    t0 = time.time()
    r = wsl(c)
    wsl("cp /root/slam/cikti/p/%s/poz.jsonl /root/slam/cikti/p/%s/harita.json /root/slam/cikti/p/%s/log.txt %s/ && rm -rf /root/slam/cikti/p/%s" % (isim, isim, isim, w("kayitlar/slam/parametre/kosu_v2/%s" % isim), isim))
    sure = time.time() - t0
    try:
        o = PO.olc(yerel)
    except Exception as e:  # noqa: BLE001
        o = dict(hata=str(e))
    o.update(varyant=ad, tekrar=tekrar, grup=v["grup"], duvar_sn=round(sure, 1))
    json.dump(o, open(os.path.join(yerel, "olcut.json"), "w"), indent=1)
    with _kilit:
        with open(JSONL, "a", encoding="utf-8") as f:
            f.write(json.dumps(o, ensure_ascii=False) + "\n")
    p = os.path.join(yerel, "poz.jsonl")
    if os.path.exists(p):
        with open(p, "rb") as fi, gzip.open(p + ".gz", "wb") as fo:
            shutil.copyfileobj(fi, fo)
        os.remove(p)
    print("tamam", isim, {a: (round(b, 2) if isinstance(b, float) else b) for a, b in o.items() if a in ("baslatma_sn", "patladi", "yol_toplam_m", "son_baslangic_m", "donus_yaricap_m_ort", "eslesen_medyan")}, flush=True)
    return o


def tablo():
    S = {}
    for s in open(JSONL, encoding="utf-8"):
        o = json.loads(s)
        if "hata" in o:
            continue
        S.setdefault(o["varyant"], []).append(o)
    kolonlar = [("n", None), ("patlayan", None), ("baslatma_sn", ".1f"), ("izlenen_oran", ".3f"), ("kf_85", ".0f"), ("nokta_85", ".0f"), ("eslesen_medyan", ".0f"),
                ("nokta_alti_orani", ".2f"), ("nokta_1_5m_orani", ".2f"), ("nokta_zemin_orani", ".2f"), ("yol_toplam_m", ".1f"), ("son_baslangic_m", ".2f"),
                ("yon_kapanis_derece", ".1f"), ("donus_yaricap_m_ort", ".3f"), ("takip_ms_medyan", ".1f")]
    L = ["| varyant | " + " | ".join(k for k, _ in kolonlar) + " |", "|" + "---|" * (len(kolonlar) + 1)]
    for ad in PL.V:
        if ad not in S:
            continue
        R = S[ad]
        bozuk = lambda r: bool(r.get("patladi")) or (r.get("yol_orani") is not None and r["yol_orani"] < 0.5)     # patladı ya da yol 21 m'nin yarısından kısa (çöktü)
        sat = [ad, str(len(R)), "%d/%d" % (sum(1 for r in R if bozuk(r)), len(R))]
        iyi = [r for r in R if not bozuk(r)]
        for k, f in kolonlar[2:]:
            vals = [r[k] for r in iyi if r.get(k) is not None]
            if not vals:
                sat.append("-")
            else:
                vals = np.array(vals, float)
                sat.append(("%s (%s..%s)" % (format(np.median(vals), f), format(vals.min(), f), format(vals.max(), f))))
        L.append("| " + " | ".join(sat) + " |")
    yol = os.path.join(SONUC, "parametre_tablo.md")
    open(yol, "w", encoding="utf-8").write("Hücre = ortanca (min..maks) yalnız BOZUK OLMAYAN koşular üzerinde. n = tekrar sayısı, patlayan = bozuk/toplam (bozuk: bant ölçekli yol 63 m üstü ya da 82,4 sn başlangıca uzaklık > 3 m ya da başlama > 20 sn ya da yol 10,5 m altı).\n\n" + "\n".join(L) + "\n")
    print("\n".join(L))


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("komut", choices=["veri", "kos", "tablo"])
    ap.add_argument("varyant", nargs="*")
    ap.add_argument("--tekrar", type=int, default=3)
    ap.add_argument("--paralel", type=int, default=5)
    ap.add_argument("--is", dest="is_sayisi", type=int, default=3)
    a = ap.parse_intermixed_args()
    adlar = a.varyant or list(PL.V)
    os.makedirs(VERI, exist_ok=True); os.makedirs(KOSU, exist_ok=True)
    if a.komut == "veri":
        for ad in adlar:
            t0 = time.time()
            veri_hazirla(PL.V[ad]["donusum"], a.is_sayisi)
            print("veri", ad, round(time.time() - t0), "sn", flush=True)
    elif a.komut == "kos":
        isler = [(ad, r) for r in range(a.tekrar) for ad in adlar]
        with ThreadPoolExecutor(a.paralel) as ex:
            list(ex.map(lambda x: tek_kos(*x), isler))
    else:
        tablo()
