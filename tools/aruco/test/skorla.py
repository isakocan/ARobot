import json, sys, numpy as np, collections
d = sys.argv[1]
gercek = {m["ad"]: m for m in json.load(open(d + "/gercek.json"))}
det = collections.defaultdict(list)
for l in open(d + "/sonuc.txt"):
    p = l.split(); det[p[0]].append((int(p[1]), int(p[2]), int(p[3]), np.array(p[4:12], float).reshape(4, 2)))
kovalar = [(40, 60), (60, 90), (90, 130), (130, 200)]
say = collections.Counter(); bulundu = collections.Counter(); idok = collections.Counter(); hatalar = []; yp = 0; ypyok = 0; ypid = 0
yanlis_id = 0; toplam_is = 0
for ad, m in gercek.items():
    kalan = list(det.get(ad, []))
    for g in m["isaretler"]:
        k = [b for b in kovalar if b[0] <= g["kenar"] < b[1]]
        kb = k[0] if k else (200, 999)
        say[kb] += 1; toplam_is += 1
        gc = np.array(g["kose"]).reshape(4, 2).mean(0)
        eslesen = None
        for ti, t in enumerate(kalan):
            if np.linalg.norm(t[3].mean(0) - gc) < 0.3 * g["kenar"]: eslesen = t; del kalan[ti]; break
        if eslesen is None: continue
        bulundu[kb] += 1
        if eslesen[0] == g["id"]:
            idok[kb] += 1
            hatalar.append(np.linalg.norm(eslesen[3] - np.array(g["kose"]).reshape(4, 2), axis=1).mean())
        else: yanlis_id += 1
    if not m["isaretler"]: yp += len(kalan)
    else: ypid += len(kalan)  # eslesmeyen (gercek isaret disi) tespitler
print("Isaret sayisi:", toplam_is)
for b in kovalar + [(200, 999)]:
    if say[b]: print(f"  kenar {b[0]}-{b[1]} px: {bulundu[b]}/{say[b]} bulundu ({100*bulundu[b]/say[b]:.0f}%), dogru id {idok[b]}")
tb = sum(bulundu.values()); print(f"Toplam bulunan {tb}/{toplam_is} = {100*tb/toplam_is:.1f}%  dogru id {sum(idok.values())}  yanlis id {yanlis_id}")
print("Kose hatasi (px, ort):", np.mean(hatalar) if hatalar else None, " medyan:", np.median(hatalar) if hatalar else None)
bos = sum(1 for m in gercek.values() if not m["isaretler"])
print(f"Isaretsiz goruntu: {bos}, yanlis pozitif: {yp}; isaretli goruntulerde fazladan tespit: {ypid}")
