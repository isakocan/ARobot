"""Robotun buzzer seslerini tarayıcıda tek tek dinlemek için bir HTML sayfası üretir.

Notalar robot_kontrol.ino'dan okunur, sayfada Web Audio ile kare dalga olarak çalınır (Arduino'daki tone() gibi).
Her sesin yanında ne zaman çaldığı ve nota şeridi görünür. Kornalar basılı tuttukça çalar.

    python tools/ses_sayfasi.py                 # docs/ses-efektleri.html
    python tools/ses_sayfasi.py <çıktı.html>
"""
import json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ses_onizleme import INO, KOK, dizi  # noqa: E402

EFEKTLER = [
    ("E_ACILIS", "Açılış", "Robot açılıp hazır olunca (Arduino başlarken)", "efekt"),
    ("E_BAGLANDI", "Bağlandı", "Uygulama robota ilk bağlanınca ve kopan bağlantı geri gelince", "efekt"),
    ("E_KOPTU", "Koptu", "1,5 sn robottan haber alınamayınca (sesler susar, ışık söner)", "efekt"),
    ("E_KAPANIS", "Kapanış", "Robot panelinden Kapat ya da Yeniden başlat deyince", "efekt"),
    ("E_FOTO", "Fotoğraf", "Fotoğraf çekince", "efekt"),
    ("E_KAYIT_BASLA", "Kayıt başladı", "Video kaydı başlayınca", "efekt"),
    ("E_KAYIT_BITTI", "Kayıt bitti", "Video kaydı bitince", "efekt"),
    ("E_ALARM", "Alarm", "Jetson 80 °C'yi geçince ya da teker sıkışıp akım koruması devreye girince (her zaman çalar)", "efekt"),
]
KORNALAR = [("Düz", 0), ("Kalın", 1), ("Tiz", 2), ("Titrek", 3), ("Dü-düt", 4), ("Havalı", 5)]
EZGILI = [("E_CUCARACHA", "La Cucaracha"), ("E_DIXIE", "Dixie"), ("E_BABA", "Baba"),
          ("E_TRAS", "Ta-ta-ra-ta-ta")]


def sarkilar(kaynak):
    m = re.search(r"const uint16_t \*const MELODILER\[\] = \{(.*?)\};", kaynak, re.S)
    out = []
    for ad, grup, baslik in re.findall(r"(M_\w+),\s*//\s*([^|\n]*)\|([^\n]*)", m.group(1)):
        out.append({"ad": ad, "grup": grup.strip() or "Genel", "baslik": baslik.strip(),
                    "notalar": dizi(kaynak, ad)})
    return out


def main():
    hedef = sys.argv[1] if len(sys.argv) > 1 else os.path.join(KOK, "docs", "ses-efektleri.html")
    kaynak = open(INO, encoding="utf-8").read()
    klasor = os.path.dirname(INO)
    for ek in sorted(os.listdir(klasor)):   # şarkıların bir kısmı sarkilar*.h dosyalarında
        if ek.endswith(".h"):
            kaynak += "\n" + open(os.path.join(klasor, ek), encoding="utf-8").read()
    veri = {
        "efektler": [{"ad": ad, "baslik": b, "ne": ne, "notalar": dizi(kaynak, ad)} for ad, b, ne, _ in EFEKTLER],
        "geriBip": {"baslik": "Geri vites bipi", "ne": "Geri giderken (ayarlarda Geri vites bipi)"},
        "kornalar": [{"baslik": b, "stil": s} for b, s in KORNALAR],
        "ezgili": [{"ad": ad, "baslik": b, "notalar": dizi(kaynak, ad)} for ad, b in EZGILI],
        "sarkilar": sarkilar(kaynak),
    }
    sablon = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "ses_sayfasi.html"), encoding="utf-8").read()
    html = sablon.replace("/*VERI*/null", json.dumps(veri, ensure_ascii=False))
    os.makedirs(os.path.dirname(os.path.abspath(hedef)), exist_ok=True)
    open(hedef, "w", encoding="utf-8").write(html)
    print(hedef)


if __name__ == "__main__":
    main()
