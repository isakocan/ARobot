"""Çalışan geliştirici ekranı sunucusunu bir istemci gibi dener (sayfa, durum, kare, WebSocket akışı); doğrular ve ölçer.
  python tools/gelistirici_ekrani/istemci_dene.py [--adres localhost:8765] [--sure 20] [--gorunum ham|pano] [--kaydet klasor]
Çıkış kodu 0 = hepsi geçerli."""
import argparse
import json
import sys
import time
import urllib.request

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def kontrol(kosul, mesaj):
    print(("  ok   " if kosul else "  HATA ") + mesaj, flush=True)
    if not kosul:
        kontrol.hata += 1


kontrol.hata = 0


def ana():
    ap = argparse.ArgumentParser()
    ap.add_argument("--adres", default="localhost:8765")
    ap.add_argument("--sure", type=float, default=20.0)
    ap.add_argument("--gorunum", default="ham")
    ap.add_argument("--kaydet", default=None, help="son kare JPEG ve durum JSON bu klasöre yazılır")
    a = ap.parse_args()
    from websockets.sync.client import connect
    import cv2
    taban = "http://" + a.adres

    html = urllib.request.urlopen(taban + "/", timeout=10).read().decode("utf-8")
    kontrol("<canvas" in html and "WebSocket" in html and "Geliştirici ekranı" in html, "sayfa HTML geldi (%d bayt)" % len(html))
    d0 = json.loads(urllib.request.urlopen(taban + "/api/durum", timeout=10).read().decode("utf-8"))
    kontrol("isleme_fps" in d0, "/api/durum geçerli JSON: kaynak=%s" % ((d0.get("kaynak") or {}).get("ad")))

    kareler = []
    with connect("ws://%s/ws" % a.adres, max_size=64 * 1024 * 1024) as ws:
        ws.send(json.dumps({"t": "ayar", "gorunum": a.gorunum}))
        t0 = time.time()
        merhaba = None
        yorunge_n = 0
        izgara = None
        sonuncu = None
        while time.time() - t0 < a.sure:
            m = ws.recv(timeout=10)
            if isinstance(m, str):
                j = json.loads(m)
                if j["t"] == "merhaba":
                    merhaba = j
                continue
            n = int.from_bytes(m[:4], "big")
            meta = json.loads(m[4:4 + n].decode("utf-8"))
            jpeg = m[4 + n:]
            kareler.append((time.time(), meta))
            for op in meta.get("harita_islem", []):
                if op.get("islem") == "temizle":
                    yorunge_n = 0
                elif op.get("katman") == "yorunge":
                    yorunge_n += len(op["xy"]) // 2
                elif op.get("katman") == "doluluk":
                    izgara = op
            sonuncu = (meta, jpeg)
    kontrol(merhaba is not None and merhaba["katmanlar"], "merhaba mesajı: %d katman tanımı" % len((merhaba or {}).get("katmanlar", [])))
    kontrol(len(kareler) >= 5, "%d kare mesajı alındı (%.0f sn)" % (len(kareler), a.sure))
    meta, jpeg = sonuncu
    im = cv2.imdecode(np.frombuffer(jpeg, np.uint8), cv2.IMREAD_COLOR)
    kontrol(im is not None and im.shape[1] == meta["w"], "JPEG çözüldü: %dx%d (%s), %d KB" % (im.shape[1], im.shape[0], meta["gorunum"], len(jpeg) // 1024))
    d = meta["durum"]
    for k in ("kare", "t_ms", "poz", "katmanlar", "sure_ms", "bilgi"):
        kontrol(k in d, "durum.%s var" % k)
    kontrol(all(np.isfinite(float(d["poz"][k])) for k in ("x", "y", "yon")), "poz sayısal: x=%.3f y=%.3f yon=%.1f" % (d["poz"]["x"], d["poz"]["y"], d["poz"]["yon"]))
    kontrol(yorunge_n > 0, "yörünge noktası biriktirildi: %d" % yorunge_n)
    if izgara:
        g = cv2.imdecode(np.frombuffer(__import__("base64").b64decode(izgara["png"]), np.uint8), cv2.IMREAD_GRAYSCALE)
        kontrol(g is not None and g.shape == (izgara["h"], izgara["w"]), "doluluk ızgarası PNG geçerli: %dx%d hücre, dolu=%d boş=%d" % (izgara["w"], izgara["h"], int((g == 0).sum()), int((g == 255).sum())))
    else:
        print("  (bu sürede ızgara op'u gelmedi)")
    # katman biçimleri
    for kid, liste in d["katmanlar"].items():
        for o in liste:
            gecerli = o.get("tur") in ("noktalar", "cizgiler", "kirik", "resim", "ok") and o.get("uzay") in ("pano", "ham", "harita")
            if o["tur"] in ("noktalar", "cizgiler", "kirik"):
                xy = o["xy"]
                gecerli = gecerli and len(xy) % 2 == 0 and all(np.isfinite(xy))
                if o["uzay"] != "harita" and xy:
                    gecerli = gecerli and -0.05 <= min(xy) and max(xy) <= 1.05
            if not gecerli:
                kontrol(False, "katman %s öğesi geçersiz (%s/%s)" % (kid, o.get("tur"), o.get("uzay")))
    print("  katmanlar:", {k: sum(len(o.get("xy", [0])) // 2 if o["tur"] != "resim" else 1 for o in v) for k, v in d["katmanlar"].items()})
    sd = meta["sunucu"]
    # kare başına ölçüm
    ts = np.array([t for t, _ in kareler])
    print("  alınan kare hızı: %.1f /sn (istemci); sunucu işleme fps %.1f, kaynak fps %.1f, atılan %d/%d" % (
        (len(ts) - 1) / (ts[-1] - ts[0]), sd["isleme_fps"], sd["kaynak_fps"], sd["kayip"], sd["gelen"]))
    sm = {}
    for _, mt in kareler[5:]:
        for k, v in mt["durum"]["sure_ms"].items():
            sm.setdefault(k, []).append(v)
    print("  aşama süreleri ms (ort / p95): " + ", ".join("%s %.1f/%.1f" % (k, np.mean(v), np.percentile(v, 95)) for k, v in sm.items()))
    print("  kare yaşı ms (işleme sonu): %.0f; ekrana varış yaşı %.0f; görüntü üretimi %.0f ms" % (sd["yas_ms"], sd["gonderim_yas_ms"], sd["goruntu_ms"]))
    if a.kaydet:
        import os
        os.makedirs(a.kaydet, exist_ok=True)
        open(os.path.join(a.kaydet, "son_kare_%s.jpg" % meta["gorunum"]), "wb").write(jpeg)
        json.dump(d, open(os.path.join(a.kaydet, "son_durum.json"), "w", encoding="utf-8"), ensure_ascii=False)
    # HTTP yedekleri
    sj = json.loads(urllib.request.urlopen(taban + "/api/son_durum", timeout=10).read().decode("utf-8"))
    kontrol("poz" in sj and "seq" in sj, "/api/son_durum geçerli JSON")
    kj = urllib.request.urlopen(taban + "/kare.jpg?gorunum=pano", timeout=10).read()
    kontrol(cv2.imdecode(np.frombuffer(kj, np.uint8), cv2.IMREAD_COLOR) is not None, "/kare.jpg?gorunum=pano geçerli JPEG (%d KB)" % (len(kj) // 1024))
    print("SONUÇ:", "HEPSİ GEÇERLİ" if kontrol.hata == 0 else "%d HATA" % kontrol.hata)
    return 1 if kontrol.hata else 0


if __name__ == "__main__":
    sys.exit(ana())
