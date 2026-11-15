"""Çalışan geliştirici ekranının ekran görüntüsünü alır (başsız Chrome/Edge + DevTools protokolü; ek paket gerekmez, websockets yeter).
  python tools/gelistirici_ekrani/ekran_goruntusu.py --adres localhost:8765 --cikti kayitlar/gosterim [--bekle 25] [--hepsi-filtre] [--gorunum pano]
Masaüstü (1440x900) ve telefon (380x820, 2x) görünümü ayrı dosyalara yazılır."""
import argparse
import base64
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.request

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
TARAYICILAR = [r"C:\Program Files\Google\Chrome\Application\chrome.exe", r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
               "chrome", "msedge", "google-chrome", "chromium"]


def ana():
    ap = argparse.ArgumentParser()
    ap.add_argument("--adres", default="localhost:8765")
    ap.add_argument("--cikti", default="kayitlar/gosterim")
    ap.add_argument("--bekle", type=float, default=20.0, help="sayfa açıldıktan sonra beklenecek sn (akış dolsun)")
    ap.add_argument("--ad", default="gelistirici_ekrani")
    ap.add_argument("--hepsi-filtre", action="store_true", help="bütün katmanları aç")
    ap.add_argument("--gorunum", default=None, choices=["ham", "pano"])
    ap.add_argument("--ac", default="", help="açılacak katman kimlikleri, virgülle (ör. zemin_mesafe,govde)")
    ap.add_argument("--mod", default=None, choices=["2b", "3b"], help="harita paneli modu")
    ap.add_argument("--port", type=int, default=9333)
    a = ap.parse_args()
    exe = next((t for t in TARAYICILAR if os.path.exists(t) or shutil.which(t)), None)
    if exe is None:
        raise SystemExit("Chrome/Edge bulunamadı")
    from websockets.sync.client import connect
    profil = tempfile.mkdtemp(prefix="gk_profil_")
    p = subprocess.Popen([exe, "--headless=new", "--disable-gpu", "--no-first-run", "--remote-debugging-port=%d" % a.port, "--user-data-dir=" + profil,
                          "--remote-allow-origins=*", "about:blank"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        hedef = None
        for _ in range(60):
            try:
                hedef = [t for t in json.loads(urllib.request.urlopen("http://127.0.0.1:%d/json" % a.port, timeout=1).read()) if t["type"] == "page"][0]
                break
            except Exception:
                time.sleep(0.5)
        if hedef is None:
            raise SystemExit("tarayıcıya bağlanılamadı")
        os.makedirs(a.cikti, exist_ok=True)
        with connect(hedef["webSocketDebuggerUrl"], max_size=64 * 1024 * 1024) as ws:
            sayac = [0]

            def cdp(yontem, **par):
                sayac[0] += 1
                ws.send(json.dumps({"id": sayac[0], "method": yontem, "params": par}))
                while True:
                    m = json.loads(ws.recv(timeout=60))
                    if m.get("id") == sayac[0]:
                        if "error" in m:
                            raise RuntimeError(m["error"])
                        return m.get("result", {})

            cdp("Page.enable")
            for ad, w, h, olcek, mobil in (("masaustu", 1440, 900, 1, False), ("telefon", 380, 820, 2, True)):
                cdp("Emulation.setDeviceMetricsOverride", width=w, height=h, deviceScaleFactor=olcek, mobile=mobil)
                cdp("Page.navigate", url="http://%s/" % a.adres)
                time.sleep(3)
                js = ""
                if a.gorunum:
                    js += "document.querySelector('#gorunum [data-g=%s]').click();" % a.gorunum
                if a.hepsi_filtre:
                    js += "for (const t of S.tanim) S.acik[t.id]=true; filtreleriKur();"
                if a.ac:
                    js += "for (const id of %r.split(',')) S.acik[id]=true; filtreleriKur(); ciz();" % a.ac
                if a.mod:
                    js += "S.harMod=%r; harModGoster();" % a.mod
                if js:
                    cdp("Runtime.evaluate", expression=js)
                time.sleep(a.bekle)
                r = cdp("Page.captureScreenshot", format="png", captureBeyondViewport=True)
                yol = os.path.join(a.cikti, "%s_%s.png" % (a.ad, ad))
                with open(yol, "wb") as f:
                    f.write(base64.b64decode(r["data"]))
                ol = cdp("Runtime.evaluate", expression="document.documentElement.scrollWidth + ' ' + innerWidth", returnByValue=True)
                print("yazıldı:", yol, "(sayfa genişliği / görünüm genişliği:", ol["result"]["value"] + ")")
    finally:
        p.terminate()
        time.sleep(1)
        shutil.rmtree(profil, ignore_errors=True)


if __name__ == "__main__":
    ana()
