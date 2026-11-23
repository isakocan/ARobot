"""Konum ölçüm aracı (PC): robotun kaydını (~/konum_kayit/<zaman>/) konum koduyla yeniden oynatır, "konum kötü" demenin
sayısal karşılığını çıkarır. Kullanıcı "kötü oldu" dediğinde ilk bakılacak yer.

python olc_kayit.py <kayit> [<kayit> ...] [--eski REV] [--cizim cikti.png]
  --eski REV : aynı kaydı git'teki eski konum.py ile de oynatır (ör. --eski HEAD), yan yana karşılaştırır.

Ölçüler (kare başına, işaretin göründüğü karelerde):
  AR yön hatası   : bulunan konumdan hesaplanan "işaret şu açıda görünmeli" ile görüntüde görülen açı farkı (derece).
                    Ekranda AR nesnelerinin yana kayması tam olarak budur (110 derece görüşte 1 derece ~ 17 px / 1920).
  uzaklık hatası  : aynı, işarete uzaklık (cm). Nesnenin ileri / geri kaymasıdır.
  yön (normal)    : yakın (1,2 m) ve belirsiz olmayan işaretin baktığı yönden çıkan robot yönü ile bulunan yön farkı.
  durağan kayma   : motorlar 1,5 sn'den uzun dururken konumun en çok ne kadar gittiği (cm) ve döndüğü (derece). 0 olmalı.
  sıçrama         : bir karede 15 cm'den ya da 10 dereceden fazla değişen konum (dönüş komutu yokken).
"""
import importlib.util
import json
import math
import os
import subprocess
import sys
import tempfile

import cv2
import numpy as np

BURA = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, BURA)


def _aci(a):
    return (a + 180.0) % 360.0 - 180.0


def modul_yukle(rev):
    if rev is None:
        import konum
        return konum
    kod = subprocess.check_output(["git", "show", "%s:robot/jetson/konum/konum.py" % rev], cwd=BURA)
    yol = os.path.join(tempfile.mkdtemp(), "konum_%s.py" % rev.replace("~", "_").replace("/", "_"))
    open(yol, "wb").write(kod)
    sp = importlib.util.spec_from_file_location("konum_eski", yol)
    m = importlib.util.module_from_spec(sp); sp.loader.exec_module(m)
    return m


def oynat(K, modul):
    R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl"))]
    ev = json.load(open(os.path.join(K, "ev.json")))["isaretler"]
    kb = modul.KonumBulucu()
    kb.ev_ayarla(ev)
    evp = {int(e[0]): (float(e[1]), float(e[2]), float(e[3])) for e in ev}
    satir = []
    for r in R:
        g = cv2.imread(os.path.join(K, r["jpg"]), cv2.IMREAD_GRAYSCALE)
        if g is None:
            continue
        gor = kb.isle(g, r["t"], (r["sol"], r["sag"]))
        o = kb.ozet()
        satir.append({"t": r["t"], "var": o["var"], "x": o["x"], "z": o["z"], "yaw": o["yaw"], "sol": r["sol"], "sag": r["sag"],
                      "gor": [(gg["id"], gg["x"], gg["z"], gg["aci"], gg.get("belirsiz", False)) for gg in gor if gg["id"] in evp],
                      "sy": o.get("sy"), "kilit": o.get("kilit")})
    return satir, evp


def olc(satir, evp):
    yon, uzak, normal, ts = [], [], [], []
    for s in satir:
        if not s["var"]:
            continue
        yaw = math.radians(s["yaw"])
        for i, mx, mz, ma, bel in s["gor"]:
            ex, ez, ea = evp[i]
            dx, dz = ex - s["x"], ez - s["z"]
            c, sn = math.cos(-yaw), math.sin(-yaw)
            lx, lz = dx * c + dz * sn, -dx * sn + dz * c
            r = math.hypot(mx, mz)
            if r > 2.5:
                continue
            yon.append(_aci(math.degrees(math.atan2(lx, lz) - math.atan2(mx, mz))))
            uzak.append(math.hypot(lx, lz) - r)
            ts.append(s["t"])
            if r < 1.2 and not bel:
                normal.append(_aci(math.degrees(ea - ma) - s["yaw"]))
    # durağan bölümler
    kayma = []
    i = 0
    while i < len(satir):
        j = i
        while j + 1 < len(satir) and satir[j + 1]["sol"] == 0 and satir[j + 1]["sag"] == 0 and satir[i]["sol"] == 0 and satir[i]["sag"] == 0 \
                and satir[j + 1]["t"] - satir[j]["t"] < 1.0:
            j += 1
        if satir[j]["t"] - satir[i]["t"] >= 1.5 and satir[i]["var"]:
            # durduktan 0,5 sn sonrasından (robot o ana kadar sürüklenebilir); işaret duruşun ortasında yeniden görüldüyse
            # ilk düzeltme kayma sayılmaz (doğru davranış), ondan 1 sn sonrasından ölçülür
            bas = satir[i]["t"] + 0.5
            onceki_gordu = bool(satir[i]["gor"])
            for s in satir[i:j + 1]:
                if s["gor"] and not onceki_gordu:
                    bas = max(bas, s["t"] + 1.0)
                onceki_gordu = bool(s["gor"])
            b = [s for s in satir[i:j + 1] if s["t"] >= bas and s["var"]]
            if len(b) >= 3:
                kayma.append((b[0]["t"], max(math.hypot(s["x"] - b[0]["x"], s["z"] - b[0]["z"]) for s in b),
                              max(abs(_aci(s["yaw"] - b[0]["yaw"])) for s in b), b[-1]["t"] - b[0]["t"]))
        i = j + 1
    sicrama = 0
    for a, b in zip(satir, satir[1:]):
        if a["var"] and b["var"] and b["sol"] == b["sag"] and (math.hypot(b["x"] - a["x"], b["z"] - a["z"]) > 0.15 or
                                                               (b["sol"] == 0 and abs(_aci(b["yaw"] - a["yaw"])) > 10)):
            sicrama += 1
    return {"yon": np.abs(yon), "uzak": np.abs(uzak), "normal": np.abs(normal), "ts": ts, "yon_isaretli": yon,
            "kayma": kayma, "sicrama": sicrama, "kare": len(satir), "bilinen": sum(1 for s in satir if s["var"])}


def yuzde(a, q):
    return float(np.percentile(a, q)) if len(a) else float("nan")


def rapor(ad, m):
    print("  %-6s kare %4d (konum bilinen %4d)" % (ad, m["kare"], m["bilinen"]))
    print("         AR yön hatası (derece)   ortanca %5.1f   %%90 %5.1f   en çok %5.1f   (%d ölçüm)" % (
        yuzde(m["yon"], 50), yuzde(m["yon"], 90), m["yon"].max() if len(m["yon"]) else float("nan"), len(m["yon"])))
    print("         uzaklık hatası (cm)      ortanca %5.1f   %%90 %5.1f" % (yuzde(m["uzak"], 50) * 100, yuzde(m["uzak"], 90) * 100))
    print("         yön, yakın işarete göre  ortanca %5.1f   %%90 %5.1f   (%d ölçüm)" % (yuzde(m["normal"], 50), yuzde(m["normal"], 90), len(m["normal"])))
    k = m["kayma"]
    kotu = [q for q in k if q[1] > 0.05 or q[2] > 3]
    print("         durağan kayma: %d duruş, en çok %.0f cm / %.1f derece; 5 cm ya da 3 dereceyi geçen %d" % (
        len(k), max([q[1] for q in k] or [0]) * 100, max([q[2] for q in k] or [0]), len(kotu)))
    for q in kotu[:6]:
        print("             t=%.1f sn: %.0f cm, %.1f derece (%.1f sn duruşta)" % (q[0] - m["t0"], q[1] * 100, q[2], q[3]))
    print("         sıçrama (>15 cm / >10 derece, dönüş yokken): %d" % m["sicrama"])


def main():
    arg = sys.argv[1:]
    eski = None; cizim = None
    if "--eski" in arg:
        i = arg.index("--eski"); eski = arg[i + 1]; del arg[i:i + 2]
    if "--cizim" in arg:
        i = arg.index("--cizim"); cizim = arg[i + 1]; del arg[i:i + 2]
    surumler = [("yeni", None)] + ([("eski", eski)] if eski else [])
    moduller = {ad: modul_yukle(rev) for ad, rev in surumler}
    sonuc = {}
    for K in arg:
        print("== %s" % K)
        for ad, _ in surumler:
            satir, evp = oynat(K, moduller[ad])
            m = olc(satir, evp); m["t0"] = satir[0]["t"] if satir else 0
            sonuc[(K, ad)] = m
            rapor(ad, m)
    if cizim:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        fig, ax = plt.subplots(len(arg), 1, figsize=(13, 3.2 * len(arg)), squeeze=False)
        for k, K in enumerate(arg):
            for ad, _ in surumler:
                m = sonuc[(K, ad)]
                ax[k][0].plot(np.arange(len(m["ts"])), m["yon_isaretli"], ".", ms=3, label=ad)
            ax[k][0].set_title(os.path.basename(K.rstrip("/")) + ": AR yön hatası (derece), işaretin göründüğü kareler sırayla")
            ax[k][0].set_ylim(-45, 45); ax[k][0].axhline(0, color="k", lw=0.5); ax[k][0].legend(); ax[k][0].grid(alpha=0.3)
        fig.tight_layout(); fig.savefig(cizim, dpi=90)
        print("çizim:", cizim)


if __name__ == "__main__":
    main()
