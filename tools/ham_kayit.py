"""Robotta ham kayıt (kameranın orijinal JPEG kareleri + motor komutları + Arduino durumu) başlatır, durdurur, indirir.

    python tools/ham_kayit.py <robot-adresi> baslat [ad] [sure_sn]   kaydı başlat (varsayılan 180 sn, en çok 900)
    python tools/ham_kayit.py <robot-adresi> durdur                  kaydı bitir
    python tools/ham_kayit.py <robot-adresi> durum                   kayıt sürüyor mu, kaç kare, boş disk
    python tools/ham_kayit.py <robot-adresi> indir                   robottaki kayıtları listeler
    python tools/ham_kayit.py <robot-adresi> indir <klasor|son>      o kaydı kayitlar/<klasor>/ içine indirir

Robot adresi "192.168.1.20" ya da "robot.local" ya da "adres:8080" olabilir (port yoksa 8080).
Robotta silme yok (elle silinir). Kayıt sürerken indirme yapılmaz (köprü reddeder). Yarım kalan indirme
aynı komutla kaldığı yerden sürer. Biçim: robot/jetson/surus_koprusu/ham_kayit.py başındaki açıklama.
"""
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

KOK = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
DOSYALAR = ("bilgi.json", "kareler.mjpg", "kareler.csv", "komutlar.csv", "durum.csv")


def temel(adres):
    if "://" in adres:
        adres = adres.split("://", 1)[1]
    adres = adres.rstrip("/")
    return "http://" + (adres if ":" in adres else adres + ":8080")


def getir(taban, yol, zaman=15):
    try:
        with urllib.request.urlopen(taban + yol, timeout=zaman) as r:
            return json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        try:
            return json.loads(e.read().decode("utf-8"))
        except ValueError:
            raise SystemExit("robot hata verdi: HTTP {}".format(e.code))
    except (urllib.error.URLError, OSError) as e:
        raise SystemExit("robota ulaşılamadı ({}): {}".format(taban, e))


def durum_yaz(d):
    if d.get("hata"):
        print("HATA:", d["hata"])
    if d.get("aktif"):
        print("kayıt SÜRÜYOR: {}".format(d.get("klasor")))
        print("  {} sn / en çok {} sn, {} kare, {} atılan kare, {} MB yazıldı".format(
            d.get("sure_sn"), d.get("en_cok_sn"), d.get("kare"), d.get("atilan_kare"), d.get("mb")))
        print("  beklenen boyut: {}-{} MB, boş disk: {} MB".format(
            (d.get("beklenen_boyut_mb") or ["?", "?"])[0], (d.get("beklenen_boyut_mb") or ["?", "?"])[1], d.get("bos_disk_mb")))
    elif d.get("kapaniyor"):
        print("kayıt kapanıyor, dosyalar yazılıyor...")
    else:
        print("kayıt yok. boş disk: {} MB (kayıt 2-6 MB/sn yer tutar)".format(d.get("bos_disk_mb")))
        s = d.get("son")
        if s:
            print("son kayıt: {} - {} kare, {} atılan, {} MB, {} sn, neden: {}".format(
                os.path.basename(s.get("klasor") or ""), s.get("kare"), s.get("atilan_kare"), s.get("mb"),
                s.get("sure_sn"), s.get("bitis_nedeni")))


def liste(taban):
    d = getir(taban, "/api/kayitlar")
    if not d["kayitlar"]:
        print("robotta kayıt yok")
    for k in d["kayitlar"]:
        mb = sum(k["dosyalar"].values()) / 1e6
        print("{}  {} kare, {:.0f} MB, {} sn, {}".format(k["ad"], k.get("kare_sayisi"), mb, k.get("sure_sn"), k.get("bitis_nedeni")))
    return d["kayitlar"]


def indir_dosya(taban, klasor, dosya, boy, hedef):
    yol = os.path.join(hedef, dosya)
    var = os.path.getsize(yol) if os.path.exists(yol) else 0
    if var == boy:
        print("  {} zaten tam ({:.1f} MB)".format(dosya, boy / 1e6))
        return
    if var > boy:
        var = 0
    ist = urllib.request.Request("{}/kayitlar/{}/{}".format(taban, klasor, dosya))
    if var:
        ist.add_header("Range", "bytes={}-".format(var))
    try:
        r = urllib.request.urlopen(ist, timeout=30)
    except urllib.error.HTTPError as e:
        raise SystemExit("{} indirilemedi: HTTP {} {}".format(dosya, e.code, e.read().decode("utf-8", "replace")[:200]))
    if var and r.status != 206:
        var = 0                         # sunucu Range'i uygulamadı: baştan
    yazilan = var
    with r, open(yol, "ab" if var else "wb") as f:
        while True:
            parca = r.read(1 << 20)
            if not parca:
                break
            f.write(parca)
            yazilan += len(parca)
            print("\r  {} {:.1f}/{:.1f} MB".format(dosya, yazilan / 1e6, boy / 1e6), end="", flush=True)
    print()
    if yazilan != boy:
        raise SystemExit("{}: {} bayt bekleniyordu, {} geldi (yeniden çalıştırırsan kaldığı yerden sürer)".format(dosya, boy, yazilan))


def dogrula(hedef):
    """kareler.csv'deki son (ofset+bayt) kareler.mjpg boyuna eşit mi?"""
    try:
        with open(os.path.join(hedef, "kareler.csv")) as f:
            satirlar = f.read().strip().split("\n")[1:]
        if not satirlar:
            print("uyarı: kareler.csv boş")
            return
        k = satirlar[-1].split(",")
        beklenen = int(k[2]) + int(k[3])
        gercek = os.path.getsize(os.path.join(hedef, "kareler.mjpg"))
        print("doğrulama: {} kare, mjpg boyu {} ({})".format(len(satirlar), gercek, "TAMAM" if beklenen == gercek else "UYUMSUZ, beklenen {}".format(beklenen)))
    except (OSError, ValueError, IndexError) as e:
        print("doğrulanamadı:", e)


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        raise SystemExit(1)
    taban, komut, arg = temel(sys.argv[1]), sys.argv[2], sys.argv[3:]
    if komut == "baslat":
        q = {"ac": "1"}
        if len(arg) > 0:
            q["ad"] = arg[0]
        if len(arg) > 1:
            q["sure"] = arg[1]
        d = getir(taban, "/api/kayit?" + urllib.parse.urlencode(q))
        durum_yaz(d)
        if not d.get("aktif"):
            raise SystemExit(1)
        print("durdurmak için: python tools/ham_kayit.py {} durdur".format(sys.argv[1]))
    elif komut == "durdur":
        durum_yaz(getir(taban, "/api/kayit?ac=0", zaman=45))
    elif komut == "durum":
        durum_yaz(getir(taban, "/api/kayit"))
    elif komut == "indir":
        kayitlar = liste(taban)
        if not arg:
            return
        ad = kayitlar[0]["ad"] if arg[0] == "son" and kayitlar else arg[0]
        k = next((x for x in kayitlar if x["ad"] == ad), None)
        if not k:
            raise SystemExit("robotta '{}' diye kayıt yok".format(ad))
        hedef = os.path.join(KOK, "kayitlar", ad)
        os.makedirs(hedef, exist_ok=True)
        print("indiriliyor:", hedef)
        for dosya in DOSYALAR:
            if dosya in k["dosyalar"]:
                indir_dosya(taban, ad, dosya, k["dosyalar"][dosya], hedef)
        dogrula(hedef)
    else:
        print(__doc__)
        raise SystemExit(1)


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    main()
