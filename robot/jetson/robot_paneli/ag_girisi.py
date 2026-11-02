"""
Giriş sayfası isteyen ağlar (okul/otel Wi-Fi'ı) için internet kontrolü ve otomatik giriş.

- internet_kontrol(): Google'ın 204 adresine bakar. 204 gelirse internet var; başka bir şey
  gelirse ağ bir giriş sayfasına yönlendiriyordur ("portal").
- Şu an FortiGate girişi destekleniyor (adreste "fgtauth" olan sayfalar, ör. Yıldız EDUROAM).
- Bilgiler ağ adına (SSID) göre /etc/robot/ag_girisleri.json içinde saklanır (yalnızca root okur).
"""

import http.client
import json
import os
import re
import urllib.parse
import urllib.request

GIRIS_DOSYASI = "/etc/robot/ag_girisleri.json"


def internet_kontrol():
    """("var", None) | ("portal", giriş adresi ya da None) | ("yok", None)"""
    try:
        c = http.client.HTTPConnection("connectivitycheck.gstatic.com", 80, timeout=6)
        c.request("GET", "/generate_204")
        r = c.getresponse()
        govde = r.read(8192).decode("utf-8", "replace")
        c.close()
    except Exception:
        return "yok", None
    if r.status == 204:
        return "var", None
    adres = r.getheader("Location")
    if not adres:
        m = re.search(r"""(?:location(?:\.href)?\s*=\s*|url=)["']?(https?://[^"'>\s;]+)""", govde, re.I)
        adres = m.group(1) if m else None
    return "portal", adres


def portal_turu(adres):
    if adres and "fgtauth" in adres:
        return "fortigate"
    return "bilinmiyor"


def fortigate_giris(adres, kullanici, sifre):
    """(başarılı mı, oturumu canlı tutma adresi ya da hata mesajı)"""
    try:
        sayfa = urllib.request.urlopen(adres, timeout=8).read().decode("utf-8", "replace")
    except Exception as e:
        return False, "Giriş sayfası açılamadı: {}".format(e)
    alanlar = dict(re.findall(r'<input[^>]*type="hidden"[^>]*name="([^"]+)"[^>]*value="([^"]*)"', sayfa))
    if "magic" not in alanlar:
        return False, "Giriş formu tanınmadı."
    alanlar.update(username=kullanici, password=sifre)
    p = urllib.parse.urlsplit(adres)
    try:
        yanit = urllib.request.urlopen("{}://{}/".format(p.scheme, p.netloc),
                                       data=urllib.parse.urlencode(alanlar).encode(), timeout=10)
        govde = yanit.read().decode("utf-8", "replace") + " " + yanit.geturl()
    except Exception as e:
        return False, "Giriş isteği başarısız: {}".format(e)
    if internet_kontrol()[0] != "var":
        return False, "Giriş kabul edilmedi. Kullanıcı adını ve şifreyi kontrol et."
    m = re.search(r"""(https?://[^"'<>\s]*keepalive\?[^"'<>\s]*)""", govde)
    return True, m.group(1) if m else None


def oturumu_canli_tut(adres):
    try:
        urllib.request.urlopen(adres, timeout=8).read()
        return True
    except Exception:
        return False


# ---------------------------------------------------------------- saklanan bilgiler

def _oku():
    try:
        with open(GIRIS_DOSYASI) as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def bilgi(ssid):
    return _oku().get(ssid)


def kaydet(ssid, kullanici, sifre):
    veri = _oku()
    veri[ssid] = {"kullanici": kullanici, "sifre": sifre}
    eski = os.umask(0o077)
    try:
        with open(GIRIS_DOSYASI, "w") as f:
            json.dump(veri, f, ensure_ascii=False)
    finally:
        os.umask(eski)


def sil(ssid):
    veri = _oku()
    if veri.pop(ssid, None) is not None:
        with open(GIRIS_DOSYASI, "w") as f:
            json.dump(veri, f, ensure_ascii=False)
