"""SLAM ekranını başlatır (PC'de tek süreç; telefon aynı ağdan adresi açar). Varsayılan yöntem stella_vslam (WSL köprüsü).

  python tools/gelistirici_ekrani/calistir.py --kayit kayitlar/gercek/olcum_ham
  python tools/gelistirici_ekrani/calistir.py --kayit <klasor1> --kayit <klasor2> --robot 172.30.45.67
  python tools/gelistirici_ekrani/calistir.py --robot 172.30.45.67            (yalnız canlı; kaynak sayfadan seçilir)

Kaynaklar sayfada "kaynak" menüsünde: verilen her --kayit (gerçek zamanlı tekrar) ve --robot (canlı, YALNIZ OKUR: /goruntu.mjpg + /api/durum).
Canlı oturum en çok --canli-sinir sn (varsayılan 170): kamera ısınmasın. Robota komut/ayar/kayıt isteği hiçbir koşulda gitmez.
Başka bir yöntem: --arkauc modul:Sinif (arkauc.ArkaUc alt sınıfı; sayfa değişmez)."""
import argparse
import asyncio
import importlib
import os
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)


def ana():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--kayit", action="append", default=[], help="kayıt klasörü (birden çok verilebilir)")
    ap.add_argument("--robot", default=None, help="robot adresi, ör. 172.30.45.67 ya da 172.30.45.67:8080")
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument("--baslangic", choices=["kayit", "canli", "yok"], default=None, help="açılışta hangi kaynak (varsayılan: ilk kayıt, yoksa yok)")
    ap.add_argument("--hiz", type=float, default=1.0)
    ap.add_argument("--dongu", action="store_true", help="kayıt bitince başa sar")
    ap.add_argument("--canli-sinir", type=float, default=170.0, help="canlı oturum üst süresi (sn)")
    ap.add_argument("--canli-yol", choices=["h264", "mjpg"], default="mjpg", help="canlı görüntü yolu: mjpg (/goruntu.mjpg, ham JPEG, ~48 Mbit/s, varsayılan) ya da h264 (ws /goruntu-h264, ~4 Mbit/s, ffmpeg ile çözülür, biraz bulanık; zayıf Wi-Fi için)")
    ap.add_argument("--jpeg-kalite", type=int, default=70)
    ap.add_argument("--arkauc", action="append", default=[], help="yöntem (birden çok verilebilir, ilki başlangıçta çalışır, diğerleri sayfadan seçilince tembel başlar): "
                    "stella | konum_deney | modul:Sinif. Verilmezse yalnız stella (sayfada tek yöntem; eski konum_deney yalnız açıkça istenirse)")
    ap.add_argument("--stella-port", type=int, default=9760, help="stella_vslam köprüsünün (WSL) TCP portu; sunucu kendi köprüsünü bu portta başlatır")
    ap.add_argument("--tam-bolutleme", action="store_true", help="SegFormer olasılığını özgün (yavaş) yolla hesapla")
    ap.add_argument("--ayni-surecte-bolutleme", action="store_true", help="bölütlemeyi ayrı süreç yerine aynı süreçte (iş parçacığı) çalıştır")
    ap.add_argument("--bolutleme", action="store_true", help="zemin bölütlemeyi aç (SegFormer; doluluk ızgarası, zemin katmanları ve 3B sınır noktaları). Varsayılan KAPALI")
    a = ap.parse_args()
    if not a.kayit and not a.robot:
        ap.error("en az bir --kayit ya da --robot gerekli")

    secenekler = []
    for k in a.kayit:
        yol = os.path.abspath(k)
        if not os.path.exists(os.path.join(yol, "bilgi.json")):
            ap.error("kayıt klasörü değil (bilgi.json yok): " + yol)
        secenekler.append({"id": "kayit:" + os.path.basename(os.path.normpath(yol)), "tur": "tekrar", "ad": "Kayıt: " + os.path.basename(os.path.normpath(yol)), "yol": yol})
    if a.robot:
        secenekler.append({"id": "canli", "tur": "canli", "ad": "Canlı: robot " + a.robot, "adres": a.robot})

    ALIAS = {"stella": "arkauc_stella:Stella", "konum_deney": "arkauc_konum_deney:KonumDeneyi"}
    istekler = a.arkauc or ["stella"]

    def fabrika_yap(ad):
        modul, _, sinif = ALIAS.get(ad, ad).partition(":")

        def f():
            M = importlib.import_module(modul)
            S = getattr(M, sinif)
            kw = {}
            if S.__name__ == "KonumDeneyi":
                kw = dict(bolutleme=a.bolutleme, hizli_bolutleme=not a.tam_bolutleme, bolutleme_surec=not a.ayni_surecte_bolutleme)
            elif S.__name__ == "Stella":
                kw = dict(port=a.stella_port)
            print("yöntem yükleniyor: %s" % ad, flush=True)
            return S(**kw)
        etiket = {"stella": "stella_vslam (saf, tek kamera)", "konum_deney": "Zemin dokusu + yön (kendi yöntemimiz)"}.get(ad, ad)
        return (ad, etiket, f)

    fabrikalar = [fabrika_yap(x) for x in istekler]

    import tornado.ioloop
    import tornado.platform.asyncio  # noqa: F401
    from sunucu import Boru, uygulama_kur, yerel_adresler
    if sys.platform == "win32":
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())
    asyncio.set_event_loop(asyncio.new_event_loop())
    ioloop = tornado.ioloop.IOLoop.current()
    boru = Boru(None, secenekler, ioloop, fabrikalar=fabrikalar, yontem_id=fabrikalar[0][0], jpeg_kalite=a.jpeg_kalite, dongu=a.dongu, varsayilan_hiz=a.hiz, canli_sinir_sn=a.canli_sinir, canli_yol=a.canli_yol)
    app = uygulama_kur(boru)
    app.listen(a.port, address="0.0.0.0")

    # sayfa durum şeridi: kare gelmese de (duraklat, bitti, kesildi) sunucu durumu yayınla
    import json

    def durum_yayini():
        if not boru.istemciler:
            return
        m = json.dumps({"t": "durum", "sunucu": boru.sunucu_durumu(boru.son_paket)}, ensure_ascii=False)
        for c in list(boru.istemciler):
            try:
                c.write_message(m)
            except Exception:
                pass
    tornado.ioloop.PeriodicCallback(durum_yayini, 500).start()

    boru.yontem_sec(fabrikalar[0][0])               # ilk yöntem arka planda başlar: sayfa hemen açılır, "yöntem başlatılıyor" görünür
    baslangic = a.baslangic or ("kayit" if a.kayit else "yok")
    if baslangic == "kayit" and a.kayit:
        boru.kaynak_sec(secenekler[0]["id"])
    elif baslangic == "canli" and a.robot:
        boru.kaynak_sec("canli")
    print("")
    print("Geliştirici ekranı hazır:")
    print("  bu bilgisayar : http://localhost:%d/" % a.port)
    for ad in yerel_adresler():
        print("  telefon / ağ  : http://%s:%d/" % (ad, a.port))
    print("  durdurmak için Ctrl+C", flush=True)
    try:
        ioloop.start()
    except KeyboardInterrupt:
        pass
    finally:
        boru.kapat()


if __name__ == "__main__":
    ana()
