#!/usr/bin/env python3
"""
Robot paneli (Jetson, root olarak çalışır): Wi-Fi kurulumu ve güvenli kapatma.

Akış:
  - Açılışta NetworkManager kayıtlı ağları kendisi dener.
  - BEKLEME saniye boyunca hiçbir ağa bağlanamazsa robot kendi ağını açar (kurulum modu).
  - Telefon o ağa girince kurulum sayfası kendiliğinden açılır (DNS her adı 10.42.0.1'e çevirir).
  - Sayfadan ağ seçilir, şifre yazılır; robot o ağa geçer ve onu en öncelikli ağ yapar.
  - Bağlanamazsa kurulum ağını hata mesajıyla yeniden açar.
  - Çalışan bir ağdan başka ağa geçilince, yeni ağda ONAY_SURESI boyunca kimse panele
    ulaşmazsa robot önceki ağa döner; seçilen ağ ancak ulaşılınca öncelikli olur.
  - Kurulum ağında BOS_AP_SURE boyunca kimse yoksa kayıtlı ağları yeniden dener.
  - Bağlıyken sayfadaki "Başka ağa geç" düğmesi robotu kurulum moduna alır.
  - Giriş sayfası isteyen ağlarda (okul Wi-Fi'ı) kayıtlı bilgilerle kendisi giriş yapar,
    oturum düşerse yeniden girer (ag_girisi.py).
  - Güç: sayfadan kapat / yeniden başlat (gücü kesmeden önce SD kart güvenle kapanır).

Python 3.6 uyumlu (Ubuntu 18.04), yalnızca standart kütüphane + nmcli.
"""

import json
import os
import subprocess
import threading
import time
from http.server import BaseHTTPRequestHandler, HTTPServer
from socketserver import ThreadingMixIn

import ag_girisi

ARAYUZ = "wlan0"
AP_PROFIL = "Kurulum-Agi"
AYAR_DOSYASI = "/etc/robot/kurulum_agi.json"
SAYFA = os.path.join(os.path.dirname(os.path.abspath(__file__)), "panel.html")
PORTAL_IP = "10.42.0.1"
PORT = 80

BEKLEME = 45          # sn: bağlantı yoksa bu kadar bekleyip kurulum ağını aç
BOS_AP_SURE = 300     # sn: kurulum ağında kimse yoksa kayıtlı ağları yeniden dene
BAGLANMA_SURESI = 40  # sn: seçilen ağa bağlanma için üst sınır
ONAY_SURESI = 300     # sn: yeni ağda bu sürede kimse panele ulaşmazsa önceki ağa dönülür
INTERNET_ARALIGI = 10   # sn: internet varken kontrol aralığı
INTERNET_KESIK_ARALIGI = 3  # sn: yokken / giriş beklerken (geri geldiği an görünsün)
INTERNET_GECMIS = 40    # panelde gösterilecek son durum değişimi sayısı
CANLI_TUT_ARALIGI = 240 # sn: giriş yapılmış oturumu canlı tutma


def log(*parca):
    print(time.strftime("%H:%M:%S"), *parca, flush=True)


def nmcli(*arg, sure=60):
    """nmcli çalıştırır, (dönüş kodu, çıktı) verir."""
    try:
        p = subprocess.run(["nmcli"] + list(arg), stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                           universal_newlines=True, timeout=sure)
        return p.returncode, p.stdout.strip()
    except subprocess.TimeoutExpired:
        return 124, "zaman aşımı"


def terse_bol(satir):
    """nmcli -t çıktısını ':' ile böler; '\\:' kaçışlarını korur."""
    alanlar, parca, kacis = [], "", False
    for ch in satir:
        if kacis:
            parca += ch
            kacis = False
        elif ch == "\\":
            kacis = True
        elif ch == ":":
            alanlar.append(parca)
            parca = ""
        else:
            parca += ch
    alanlar.append(parca)
    return alanlar


# ---------------------------------------------------------------- ağ bilgisi

def kayitli_wifi_profilleri():
    """{ssid: (profil adı, öncelik)} — kurulum ağı hariç."""
    _, cikti = nmcli("-t", "-f", "NAME,TYPE", "con", "show")
    profiller = {}
    for satir in cikti.splitlines():
        ad, tur = terse_bol(satir)[:2]
        if tur != "802-11-wireless" or ad == AP_PROFIL:
            continue
        _, bilgi = nmcli("-g", "802-11-wireless.ssid,connection.autoconnect-priority", "con", "show", ad)
        satirlar = bilgi.splitlines()
        if len(satirlar) >= 2:
            try:
                oncelik = int(satirlar[1])
            except ValueError:
                oncelik = 0
            profiller[satirlar[0]] = (ad, oncelik)
    return profiller


def aktif_baglanti():
    """wlan0 üzerindeki etkin profil adı, yoksa None."""
    _, cikti = nmcli("-t", "-f", "DEVICE,STATE,CONNECTION", "dev")
    for satir in cikti.splitlines():
        alan = terse_bol(satir)
        if alan[0] == ARAYUZ and alan[1] == "connected":
            return alan[2]
    return None


def ip_adresi():
    _, cikti = nmcli("-g", "IP4.ADDRESS", "dev", "show", ARAYUZ)
    ilk = cikti.split("|")[0].strip() if cikti else ""
    return ilk.split("/")[0] if ilk else ""


def frekans():
    """Bağlı olunan erişim noktasının frekansı (MHz), bağlı değilse None."""
    try:
        cikti = subprocess.run(["iw", "dev", ARAYUZ, "link"], stdout=subprocess.PIPE,
                               stderr=subprocess.DEVNULL, timeout=3).stdout.decode()
    except (OSError, subprocess.SubprocessError):
        return None
    for satir in cikti.splitlines():
        if satir.strip().startswith("freq:"):
            return int(float(satir.split()[1]))
    return None


def bant_tercihi(ad):
    """Profilin bant ayarı: '' (otomatik), 'bg' (2,4 GHz) ya da 'a' (5 GHz)."""
    _, cikti = nmcli("-g", "802-11-wireless.band", "con", "show", ad)
    return cikti.strip()


def ag_tara():
    nmcli("dev", "wifi", "rescan", "ifname", ARAYUZ, sure=15)
    time.sleep(3)
    _, cikti = nmcli("-t", "-f", "SSID,SIGNAL,SECURITY", "dev", "wifi", "list", "ifname", ARAYUZ)
    aglar = {}
    for satir in cikti.splitlines():
        alan = terse_bol(satir)
        if len(alan) < 3 or not alan[0]:
            continue
        ssid, sinyal, guvenlik = alan[0], int(alan[1] or 0), alan[2]
        if ssid not in aglar or aglar[ssid]["sinyal"] < sinyal:
            aglar[ssid] = {"ssid": ssid, "sinyal": sinyal, "guvenlik": guvenlik,
                           "kurumsal": "802.1X" in guvenlik}
    return sorted(aglar.values(), key=lambda a: -a["sinyal"])


def ap_istemci_sayisi():
    try:
        p = subprocess.run(["iw", "dev", ARAYUZ, "station", "dump"], stdout=subprocess.PIPE,
                           universal_newlines=True, timeout=5)
        return p.stdout.count("Station ")
    except Exception:
        return 0


# ---------------------------------------------------------------- kurulum ağı

def ayarlar():
    with open(AYAR_DOSYASI) as f:
        return json.load(f)


def ap_profilini_hazirla():
    a = ayarlar()
    nmcli("con", "delete", AP_PROFIL)
    kod, cikti = nmcli(
        "con", "add", "type", "wifi", "ifname", ARAYUZ, "con-name", AP_PROFIL, "autoconnect", "no",
        "ssid", a["ssid"], "mode", "ap",
        "802-11-wireless.band", "bg", "802-11-wireless.channel", "6",
        "ipv4.method", "shared", "ipv6.method", "ignore",
        "wifi-sec.key-mgmt", "wpa-psk", "wifi-sec.psk", a["sifre"],
        "wifi-sec.proto", "rsn", "wifi-sec.pairwise", "ccmp", "wifi-sec.group", "ccmp")
    if kod != 0:
        log("kurulum profili oluşturulamadı:", cikti)


class Durum:
    def __init__(self):
        self.kilit = threading.Lock()
        self.mod = "istemci"       # istemci | kurulum | baglaniyor
        self.son_bagli = time.time()
        self.ap_bos_baslangic = None
        self.aglar = []
        self.son_hata = ""
        self.son_deneme = ""
        self.onceki_ag = None      # kurulum moduna geçmeden önceki ağ (geri dönüş için)
        self.onay = None           # yeni ağa geçiş onay bekliyorsa: ad, ssid, yeni, onceki, bitis
        self.internet = None       # var | portal | yok
        self.internet_beri = None  # şu anki internet durumu ne zamandan beri
        self.internet_gecikme = None  # ms, son başarılı kontrol
        self.internet_gecmis = ()  # {t, durum, ag, not}; tümüyle değiştirilir, okuyan kilit istemez
        self.portal = None         # giriş sayfası adresi
        self.giris_hata = ""
        self.canli_tut = None      # FortiGate oturum adresi
        self.giris_istendi = threading.Event()


D = Durum()


def internet_kaydet(durum, ag=None, not_=""):
    """İnternet durumunu yazar; değiştiyse zamanıyla geçmişe ekler."""
    if durum != D.internet:
        yeni = {"t": time.time(), "durum": durum, "ag": ag, "not": not_}
        D.internet_gecmis = (D.internet_gecmis + (yeni,))[-INTERNET_GECMIS:]
        D.internet_beri = time.time()
        log("internet:", durum or "ağ yok", ag or "", not_)
    D.internet = durum


def giris_dene(ssid):
    b = ag_girisi.bilgi(ssid)
    if not b or not D.portal:
        return
    if ag_girisi.portal_turu(D.portal) != "fortigate":
        D.giris_hata = "Bu ağın giriş sayfası tanınmıyor. Jetson ekranından tarayıcıyla giriş yapılabilir."
        return
    log("ağ girişi deneniyor:", ssid, b["kullanici"])
    tamam, sonuc = ag_girisi.fortigate_giris(D.portal, b["kullanici"], b["sifre"])
    if tamam:
        log("ağ girişi başarılı")
        internet_kaydet("var", ssid, "robot giriş yaptı")
        D.portal, D.giris_hata, D.canli_tut = None, "", sonuc
    else:
        log("ağ girişi başarısız:", sonuc)
        D.giris_hata = sonuc


def internet_dongusu():
    """İnterneti kontrol eder; giriş sayfasına takılınca kayıtlı bilgilerle girer."""
    son_canli = 0
    son_ssid = None
    while True:
        if not son_ssid:
            aralik = 5
        elif D.internet == "var":
            aralik = INTERNET_ARALIGI
        else:
            aralik = INTERNET_KESIK_ARALIGI
        elle = D.giris_istendi.wait(aralik)
        D.giris_istendi.clear()
        ssid = aktif_baglanti()
        if D.mod != "istemci" or not ssid or ssid == AP_PROFIL:
            internet_kaydet(None)
            D.portal = D.canli_tut = son_ssid = None
            continue
        if ssid != son_ssid:
            D.giris_hata, D.canli_tut, son_ssid = "", None, ssid
        bas = time.time()
        durum, adres = ag_girisi.internet_kontrol()
        D.internet_gecikme = int((time.time() - bas) * 1000) if durum == "var" else None
        internet_kaydet(durum, ssid)
        # Yeni ağda internete çıkabiliyorsak ağ çalışıyordur: biri panele girmese de geçişi onayla
        # (telefon kurulum ağında kalınca robot 5 dk sonra eski ağına dönüyordu)
        if durum == "var" and D.onay and D.onay["ad"] == ssid:
            gecisi_onayla()
        D.portal = adres if durum == "portal" else None
        if durum == "portal":
            D.canli_tut = None
            # Hatalı bilgiyle okul sunucusunu yormamak için: bir deneme başarısız olduysa
            # yeniden yalnızca sayfadan istenince dene
            if elle or not D.giris_hata:
                giris_dene(ssid)
        elif durum == "var" and D.canli_tut and time.time() - son_canli > CANLI_TUT_ARALIGI:
            ag_girisi.oturumu_canli_tut(D.canli_tut)
            son_canli = time.time()


def oncelik_ver(ad):
    """En son onaylanan ağ en öncelikli olsun."""
    profiller = kayitli_wifi_profilleri()
    en_yuksek = max([o for p, o in profiller.values() if p != ad] + [0])
    # permissions boş: profil kimsenin oturumuna bağlı olmasın (masaüstü kapalıyken de bağlansın)
    nmcli("con", "modify", ad, "connection.autoconnect", "yes",
          "connection.autoconnect-priority", str(en_yuksek + 1), "connection.permissions", "")


def gecisi_onayla():
    o, D.onay = D.onay, None
    if o:
        oncelik_ver(o["ad"])
        log("geçiş onaylandı:", o["ssid"])


def gecisi_geri_al():
    """Yeni ağda ONAY_SURESI boyunca kimse robota ulaşmadıysa önceki ağa döner."""
    o, D.onay = D.onay, None
    with D.kilit:
        log("yeni ağda kimse ulaşamadı, geri dönülüyor:", o["ssid"], "→", o["onceki"])
        if o["yeni"]:
            nmcli("con", "delete", o["ad"])
        kod, cikti = nmcli("--wait", str(BAGLANMA_SURESI), "con", "up", o["onceki"], sure=BAGLANMA_SURESI + 10)
        D.son_bagli = time.time()
        if kod == 0:
            D.son_hata = "“{}” ağında {} dakika boyunca robota kimse ulaşamadı; “{}” ağına geri dönüldü.".format(
                o["ssid"], ONAY_SURESI // 60, o["onceki"])
        else:
            log("önceki ağa da dönülemedi:", cikti)
            D.son_hata = "“{}” ağında robota kimse ulaşamadı ve önceki ağ bulunamadı.".format(o["ssid"])
            kurulum_modunu_ac("geri dönüş başarısız")


def bant_degistir(bant):
    """Bağlı ağın bandını değiştirip yeniden bağlanır; olmazsa eski ayara döner.
    Aynı ağ adı iki bantta da yayınlanıyorsa 2,4 GHz duvardan daha iyi geçer, 5 GHz daha hızlıdır."""
    with D.kilit:
        ad = aktif_baglanti()
        if not ad or ad == AP_PROFIL:
            return
        onceki = bant_tercihi(ad)
        log("bant:", ad, repr(onceki), "→", repr(bant))
        time.sleep(1.5)           # sayfaya verilen yanıt ulaşsın
        nmcli("con", "modify", ad, "802-11-wireless.band", bant)
        kod, cikti = nmcli("--wait", str(BAGLANMA_SURESI), "con", "up", ad, sure=BAGLANMA_SURESI + 10)
        if kod != 0:
            log("bu bantta bağlanılamadı:", cikti)
            nmcli("con", "modify", ad, "802-11-wireless.band", onceki)
            nmcli("--wait", str(BAGLANMA_SURESI), "con", "up", ad, sure=BAGLANMA_SURESI + 10)
            D.son_hata = "“{}” bu bantta bulunamadı; önceki ayara dönüldü.".format(ad)
        else:
            D.son_hata = ""
        D.son_bagli = time.time()


def kurulum_modunu_ac(neden):
    log("kurulum ağı açılıyor:", neden)
    D.aglar = ag_tara()   # AP açılınca tarama yapılamıyor, listeyi önceden al
    kod, cikti = nmcli("con", "up", AP_PROFIL, sure=30)
    if kod != 0:
        log("kurulum ağı açılamadı:", cikti)
        return
    D.mod = "kurulum"
    D.ap_bos_baslangic = time.time()


def kurulum_modunu_kapat():
    nmcli("con", "down", AP_PROFIL, sure=20)
    D.mod = "istemci"
    D.son_bagli = time.time()   # kayıtlı ağlara bağlanmak için yeniden BEKLEME süresi tanı


def baglan(ssid, sifre):
    """Kurulum ağını kapatıp seçilen ağa geçer. Ayrı iş parçacığında çalışır."""
    with D.kilit:
        onceki = aktif_baglanti()
        if onceki in (None, AP_PROFIL):
            onceki = D.onceki_ag
        D.onay = None
        D.mod = "baglaniyor"
        D.son_deneme = ssid
        time.sleep(1.5)           # sayfaya verilen yanıt telefona ulaşsın
        nmcli("con", "down", AP_PROFIL, sure=20)
        time.sleep(2)

        profiller = kayitli_wifi_profilleri()
        yeni_profil = ssid not in profiller
        if not yeni_profil:
            ad = profiller[ssid][0]
            if sifre:
                nmcli("con", "modify", ad, "wifi-sec.key-mgmt", "wpa-psk", "wifi-sec.psk", sifre)
            kod, cikti = nmcli("--wait", str(BAGLANMA_SURESI), "con", "up", ad, sure=BAGLANMA_SURESI + 10)
        else:
            nmcli("dev", "wifi", "rescan", "ifname", ARAYUZ, sure=15)
            time.sleep(4)
            arg = ["--wait", str(BAGLANMA_SURESI), "dev", "wifi", "connect", ssid]
            if sifre:
                arg += ["password", sifre]
            kod, cikti = nmcli(*(arg + ["ifname", ARAYUZ]), sure=BAGLANMA_SURESI + 10)

        if kod == 0 and aktif_baglanti():
            ad = kayitli_wifi_profilleri().get(ssid, (ssid, 0))[0]
            log("bağlandı:", ssid, ip_adresi())
            D.son_hata = ""
            D.mod = "istemci"
            D.son_bagli = time.time()
            if onceki and onceki != ad:
                # Önceliği ancak biri robota bu ağdan ulaşınca ver; ulaşılamazsa önceki ağa dön.
                # (Ör. giriş sayfası isteyen okul ağı: bağlanılır ama kimse robotu göremez.)
                D.onay = {"ad": ad, "ssid": ssid, "yeni": yeni_profil, "onceki": onceki,
                          "bitis": time.time() + ONAY_SURESI}
            else:
                oncelik_ver(ad)
            return

        log("bağlanamadı:", ssid, cikti)
        if yeni_profil:
            # Yanlış şifreyle oluşmuş profili bırakma
            ad = kayitli_wifi_profilleri().get(ssid, (None,))[0]
            if ad:
                nmcli("con", "delete", ad)
        D.son_hata = "“{}” ağına bağlanılamadı. Şifreyi kontrol edip tekrar dene.".format(ssid)
        kurulum_modunu_ac("bağlantı başarısız")


def ana_dongu():
    while True:
        time.sleep(2)
        if D.kilit.locked():
            continue
        if D.mod == "istemci":
            if D.onay and time.time() > D.onay["bitis"]:
                gecisi_geri_al()
                continue
            etkin = aktif_baglanti()
            if etkin and etkin != AP_PROFIL:
                D.son_bagli = time.time()
            elif time.time() - D.son_bagli > BEKLEME:
                D.onceki_ag = None
                kurulum_modunu_ac("{} sn boyunca bağlı ağ yok".format(BEKLEME))
        elif D.mod == "kurulum":
            if aktif_baglanti() != AP_PROFIL:
                kurulum_modunu_ac("kurulum ağı kapanmış")
                continue
            if ap_istemci_sayisi() > 0:
                D.ap_bos_baslangic = time.time()
            elif time.time() - D.ap_bos_baslangic > BOS_AP_SURE and kayitli_wifi_profilleri():
                log("kurulum ağında kimse yok, kayıtlı ağlar yeniden deneniyor")
                kurulum_modunu_kapat()


# ---------------------------------------------------------------- web sunucusu

class Sunucu(ThreadingMixIn, HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


class Istek(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def _json(self, veri, kod=200):
        govde = json.dumps(veri, ensure_ascii=False).encode("utf-8")
        self.send_response(kod)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(govde)))
        self.end_headers()
        self.wfile.write(govde)

    def _govde(self):
        uzunluk = int(self.headers.get("Content-Length") or 0)
        try:
            return json.loads(self.rfile.read(uzunluk).decode("utf-8") or "{}")
        except ValueError:
            return {}

    def do_GET(self):
        host = (self.headers.get("Host") or "").split(":")[0]
        yol = self.path.split("?")[0]
        # Kurulum modunda telefonun internet kontrolü (generate_204 vb.) sayfaya yönlendirilir
        if D.mod == "kurulum" and host not in (PORTAL_IP, "") and not yol.startswith("/api/"):
            self.send_response(302)
            self.send_header("Location", "http://{}/".format(PORTAL_IP))
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        if yol == "/api/durum":
            # Yeni ağdan panele ulaşan biri geçişi onaylamış olur
            if D.onay and D.mod == "istemci" and not self.client_address[0].startswith("127.") \
                    and aktif_baglanti() == D.onay["ad"]:
                gecisi_onayla()
            kayitli = kayitli_wifi_profilleri()
            aglar = [dict(a, kayitli=a["ssid"] in kayitli) for a in D.aglar]
            ag = aktif_baglanti() if D.mod == "istemci" else None
            giris = ag_girisi.bilgi(ag) if ag else None
            self._json({
                "mod": D.mod,
                "ag": ag,
                "internet": D.internet,
                "internet_beri": D.internet_beri,
                "internet_gecikme": D.internet_gecikme,
                "internet_gecmis": list(D.internet_gecmis),
                "simdi": time.time(),
                "portal_turu": ag_girisi.portal_turu(D.portal) if D.portal else None,
                "giris_kullanici": giris["kullanici"] if giris else None,
                "giris_hata": D.giris_hata,
                "ip": ip_adresi() if D.mod == "istemci" else PORTAL_IP,
                "frekans": frekans() if ag else None,
                "bant": bant_tercihi(ag) if ag else None,
                "kurulum_agi": ayarlar()["ssid"],
                "aglar": aglar,
                "son_hata": D.son_hata,
                "son_deneme": D.son_deneme,
            })
            return
        if yol in ("/", "/index.html"):
            with open(SAYFA, "rb") as f:
                govde = f.read()
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Content-Length", str(len(govde)))
            self.end_headers()
            self.wfile.write(govde)
            return
        self.send_error(404)

    def do_POST(self):
        yol = self.path.split("?")[0]
        if yol == "/api/baglan":
            veri = self._govde()
            ssid = (veri.get("ssid") or "").strip()
            sifre = veri.get("sifre") or ""
            if not ssid:
                self._json({"hata": "Ağ adı boş."}, 400)
                return
            if sifre and not 8 <= len(sifre) <= 63:
                self._json({"hata": "Wi-Fi şifresi 8-63 karakter olmalı."}, 400)
                return
            if D.kilit.locked():
                self._json({"hata": "Zaten bir ağa bağlanılıyor."}, 409)
                return
            threading.Thread(target=baglan, args=(ssid, sifre), daemon=True).start()
            self._json({"tamam": True})
            return
        if yol == "/api/kurulum-modu":
            if D.mod != "istemci":
                self._json({"hata": "Robot zaten kurulum modunda."}, 409)
                return
            D.onceki_ag = aktif_baglanti()
            self._json({"tamam": True})
            threading.Timer(1.5, kurulum_modunu_ac, args=("sayfadan istendi",)).start()
            return
        if yol == "/api/guc":
            komut = {"kapat": "poweroff", "yeniden": "reboot"}.get(self._govde().get("islem"))
            if not komut:
                self._json({"hata": "Bilinmeyen işlem."}, 400)
                return
            log("güç:", komut)
            self._json({"tamam": True})
            threading.Timer(1.0, subprocess.run, args=(["systemctl", komut],)).start()
            return
        if yol == "/api/ag-girisi":
            veri = self._govde()
            ssid = aktif_baglanti() if D.mod == "istemci" else None
            kullanici = (veri.get("kullanici") or "").strip()
            sifre = veri.get("sifre") or ""
            if not ssid or not kullanici or not sifre:
                self._json({"hata": "Kullanıcı adı ve şifre gerekli."}, 400)
                return
            ag_girisi.kaydet(ssid, kullanici, sifre)
            D.giris_hata = ""
            D.giris_istendi.set()
            self._json({"tamam": True})
            return
        if yol == "/api/ag-girisi-sil":
            ssid = aktif_baglanti()
            if ssid:
                ag_girisi.sil(ssid)
            D.giris_hata = ""
            self._json({"tamam": True})
            return
        if yol == "/api/bant":
            bant = self._govde().get("bant")
            if bant not in ("", "bg", "a") or D.mod != "istemci":
                self._json({"hata": "Geçersiz bant."}, 400)
                return
            if D.kilit.locked():
                self._json({"hata": "Robot şu an ağ değiştiriyor."}, 409)
                return
            threading.Thread(target=bant_degistir, args=(bant,), daemon=True).start()
            self._json({"tamam": True})
            return
        if yol == "/api/tara" and D.mod == "istemci":
            D.aglar = ag_tara()
            self._json({"tamam": True})
            return
        self.send_error(404)


def main():
    ap_profilini_hazirla()
    # Servis yeniden başlarsa kurulum ağı açık kalmış olabilir
    if aktif_baglanti() == AP_PROFIL:
        D.mod = "kurulum"
        D.ap_bos_baslangic = time.time()
    else:
        D.aglar = ag_tara()
    threading.Thread(target=ana_dongu, daemon=True).start()
    threading.Thread(target=internet_dongusu, daemon=True).start()
    log("Robot paneli hazır, port", PORT)
    Sunucu(("0.0.0.0", PORT), Istek).serve_forever()


if __name__ == "__main__":
    main()
