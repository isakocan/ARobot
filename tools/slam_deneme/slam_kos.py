"""WSL içinde köprünün toplu kipini çalıştırır (Windows tarafından): ayar yaml'ı üretir, kareleri WSL'e kopyalar (yoksa), sonuçları kayitlar/slam/sonuc/<ad>/ altına alır.
Örnek: python slam_kos.py v1 --veri olcum_ham --yaml Feature.ini_fast_threshold=10 Feature.min_fast_threshold=4
       python slam_kos.py konum_ikinci --veri olcum_ham --bas 2243 --map-in harita_ilk_yari.msg --mod konum   (harita WSL /root/slam/cikti/<ad>/ içinde)"""
import argparse
import os
import re
import subprocess
import sys
import time

KLASOR = os.path.dirname(os.path.abspath(__file__))
KOK = os.path.abspath(os.path.join(KLASOR, "..", ".."))
WSL_KOK = "/mnt/c/" + KOK[3:].replace("\\", "/")
SONUC = os.path.join(KOK, "kayitlar", "slam", "sonuc")


def wsl(komut, **kw):
    return subprocess.run(["wsl", "-d", "Ubuntu-22.04", "-u", "root", "--", "bash", "-c", komut], capture_output=True, text=True, encoding="utf-8", errors="replace", env=dict(os.environ, MSYS_NO_PATHCONV="1"), **kw)


def yaml_uret(taban, degisiklik, hedef):
    """taban yaml metninde 'Bolum.anahtar=deger' değişikliklerini uygular (yoksa ekler)."""
    s = open(taban, encoding="utf-8").read().split("\n")
    for ch in degisiklik:
        k, v = ch.split("=", 1)
        bolum, anahtar = k.split(".", 1)
        i = next((j for j, l in enumerate(s) if l.startswith(bolum + ":")), None)
        if i is None:
            s += [bolum + ":", "  %s: %s" % (anahtar, v)]
            continue
        j = i + 1
        bulundu = False
        while j < len(s) and (s[j].startswith(" ") or s[j].strip() == ""):
            if re.match(r"\s+%s:" % re.escape(anahtar), s[j]):
                s[j] = "  %s: %s" % (anahtar, v)
                bulundu = True
                if v.strip() == "[]":                       # liste değeri boşaltılıyorsa altındaki '- ...' satırlarını sil
                    while j + 1 < len(s) and s[j + 1].lstrip().startswith("-"):
                        del s[j + 1]
                break
            j += 1
        if not bulundu:
            s.insert(i + 1, "  %s: %s" % (anahtar, v))
    open(hedef, "w", encoding="utf-8", newline="\n").write("\n".join(s))


def kos(ad, veri, yaml_degisiklik=(), bas=0, bitis=-1, map_in="", map_out=True, mod="harita", yaml_taban=None, log="info", gercek_zaman=True):
    yaml_taban = yaml_taban or os.path.join(KLASOR, "ayar_ekdortgen.yaml")
    yerel = os.path.join(SONUC, ad)
    os.makedirs(yerel, exist_ok=True)
    yy = os.path.join(yerel, "ayar.yaml")
    yaml_uret(yaml_taban, yaml_degisiklik, yy)
    r = wsl("mkdir -p /root/slam/cikti/%s && cp %s/kayitlar/slam/sonuc/%s/ayar.yaml /root/slam/cikti/%s/ayar.yaml; ls /root/slam/veri/%s >/dev/null 2>&1 && echo VAR || echo YOK" % (ad, WSL_KOK, ad, ad, veri))
    if "YOK" in r.stdout:
        r = wsl("cp -r %s/kayitlar/slam/ekdortgen_%s /root/slam/veri/%s && mv /root/slam/veri/%s/maske.png /root/slam/veri/%s.maske.png" % (WSL_KOK, veri, veri, veri, veri))
    c = "cd /root/slam; /usr/bin/time -v ./kopru/build/kopru --vocab orb_vocab.fbow --config cikti/%s/ayar.yaml --mask veri/%s.maske.png --dizin veri/%s --cikti cikti/%s --bas %d --bitis %d --mod %s --log %s --gercek-zaman %d" % (ad, veri, veri, ad, bas, bitis, mod, log, 1 if gercek_zaman else 0)
    if map_in:
        c += " --map-in cikti/%s" % map_in
    if map_out:
        c += " --map-out cikti/%s/harita.msg" % ad
    c += " > cikti/%s/log.txt 2> cikti/%s/zaman.txt" % (ad, ad)
    t0 = time.time()
    r = wsl(c)
    sure = time.time() - t0
    r = wsl("cd /root/slam/cikti/%s; grep -E 'toplu bitti|HATA|Percent of CPU|Maximum resident' zaman.txt; mkdir -p %s/kayitlar/slam/sonuc/%s; cp poz.jsonl harita.json log.txt zaman.txt %s/kayitlar/slam/sonuc/%s/; ls -la harita.msg 2>/dev/null; cp harita.msg %s/kayitlar/slam/sonuc/%s/ 2>/dev/null" % (ad, WSL_KOK, ad, WSL_KOK, ad, WSL_KOK, ad))
    print(r.stdout, r.stderr[:300])
    print("duvar saati %.0f sn" % sure)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("ad")
    ap.add_argument("--veri", default="olcum_ham")
    ap.add_argument("--yaml", nargs="*", default=[])
    ap.add_argument("--bas", type=int, default=0)
    ap.add_argument("--bitis", type=int, default=-1)
    ap.add_argument("--map-in", default="")
    ap.add_argument("--mod", default="harita")
    ap.add_argument("--yaml-taban", default=None)
    ap.add_argument("--map-out-yok", action="store_true")
    ap.add_argument("--hizli", action="store_true", help="gerçek zaman beklemesi yok (harita iş parçacığı geride kalır, sonuçlar tutarsız)")
    a = ap.parse_args()
    kos(a.ad, a.veri, a.yaml, a.bas, a.bitis, a.map_in, not a.map_out_yok, a.mod, a.yaml_taban, "info", not a.hizli)
