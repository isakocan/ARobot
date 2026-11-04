"""Robotun buzzer seslerini (efektler, kornalar, şarkılar) bilgisayarda dinlemek için WAV dosyasına çevirir.

Notalar doğrudan robot_kontrol.ino'dan okunur; ses Arduino'daki tone() gibi kare dalgadır. Piezo buzzer
kulaklıktan daha tiz ve cızırtılı duyulur, kalın sesleri daha kısık çalar: buradaki ses yaklaşık bir fikir verir.

    python tools/ses_onizleme.py <çıktı klasörü>                 # efektler + kornalar + hepsi_sirayla.wav
    python tools/ses_onizleme.py <çıktı klasörü> --sarki M_GOREV  # bir şarkı
"""
import math, os, re, struct, sys, wave

KOK = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
INO = os.path.join(KOK, "robot", "arduino", "robot_kontrol", "robot_kontrol.ino")
ORNEK = 22050

# Hangi durumda çaldığı (dinleme listesinde sırayla)
EFEKTLER = [
    ("E_ACILIS", "Açılış", "Robot açılınca (Efekt sesleri açıksa)"),
    ("E_BAGLANDI", "Bağlandı", "Uygulama robota bağlanınca"),
    ("E_KOPTU", "Koptu", "Bağlantı kopunca"),
    ("E_KAPANIS", "Kapanış", "Robot panelinden kapatırken"),
    ("E_VITES", "Vites", "Her vites geçişinde"),
    ("E_FOTO", "Fotoğraf", "Fotoğraf çekince"),
    ("E_KAYIT_BASLA", "Kayıt başladı", "Video kaydı başlayınca"),
    ("E_KAYIT_BITTI", "Kayıt bitti", "Video kaydı bitince"),
    ("E_ALARM", "Alarm", "Jetson 80°C'yi geçince (her zaman açık)"),
]
KORNALAR = ["Düz", "Kalın", "Tiz", "Titrek", "Dü-düt"]
EZGILI = [("E_CUCARACHA", "La Cucaracha"), ("E_DIXIE", "Dixie"), ("E_BABA", "Baba"),
          ("E_NURI", "Nuri Alço"), ("E_TRAS", "Ta-ta-ra-ta-ta")]


def dizi(kaynak, ad):
    m = re.search(r"const uint16_t %s\[\] PROGMEM = \{(.*?)\};" % ad, kaynak, re.S)
    assert m, ad
    sabit = dict(re.findall(r"\b(N_[A-G]S?\d)\s*=\s*(\d+)", kaynak))
    sayilar = []
    for t in re.sub(r"//[^\n]*", "", m.group(1)).replace("\n", " ").split(","):
        t = t.strip()
        if t:
            sayilar.append(int(sabit.get(t, t)))
    return [(sayilar[i], sayilar[i + 1]) for i in range(0, len(sayilar) - 1, 2) if sayilar[i + 1] > 0]


def kare(parcalar, bosluk=False):
    """(hz, ms) listesinden örnekler. bosluk: melodi gibi her notanın son 20 ms'i sessiz."""
    out, faz = [], 0.0
    for hz, ms in parcalar:
        n = int(ORNEK * ms / 1000)
        sesli = n if not bosluk or ms <= 60 else int(ORNEK * (ms - 20) / 1000)
        for i in range(n):
            if hz and i < sesli:
                faz = (faz + hz / ORNEK) % 1.0
                out.append(0.28 if faz < 0.5 else -0.28)
            else:
                out.append(0.0)
    # piezo gibi hafif yumuşatma (tek kutuplu alçak geçiren), tıkırtı olmasın
    y, s = 0.0, []
    for v in out:
        y += (v - y) * 0.55
        s.append(y)
    return s


def korna(stil, ms=900):
    if stil < 3:
        return [((440, 330, 660)[stil], ms)]
    if stil == 3:
        return [(420 if (t // 4) % 2 else 500, 4) for t in range(0, ms, 4)]
    p = []
    for t in range(0, ms, 10):
        x = t % 520
        p.append((740 if x < 170 or 230 <= x < 400 else 0, 10))
    return p


def yaz(yol, ornekler):
    with wave.open(yol, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(ORNEK)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, v)) * 32767)) for v in ornekler))


def main():
    hedef = sys.argv[1]
    os.makedirs(hedef, exist_ok=True)
    kaynak = open(INO, encoding="utf-8").read()
    if "--sarki" in sys.argv:
        ad = sys.argv[sys.argv.index("--sarki") + 1]
        yaz(os.path.join(hedef, ad.lower() + ".wav"), kare(dizi(kaynak, ad), bosluk=True))
        return
    hepsi, sira, liste = [], 1, []
    sessiz = [0.0] * int(ORNEK * 1.2)

    def ekle(dosya, ornekler, aciklama):
        nonlocal sira
        ad = "%02d_%s.wav" % (sira, dosya.translate(str.maketrans("çğıöşüÇĞİÖŞÜ -", "cgiosuCGIOSU__")))
        yaz(os.path.join(hedef, ad), ornekler)
        hepsi.extend(ornekler + sessiz)
        liste.append("%2d. %-26s %s" % (sira, dosya, aciklama))
        sira += 1

    for e, ad, ne_zaman in EFEKTLER:
        ekle(ad, kare(dizi(kaynak, e)), ne_zaman)
    for i, ad in enumerate(KORNALAR):
        ekle("Korna " + ad, kare(korna(i)), "korna (basılı tuttukça)")
    for e, ad in EZGILI:
        ekle("Korna " + ad, kare(dizi(kaynak, e)), "ezgili korna (basınca bir kez)")
    ekle("Sarki Gorevimiz Tehlike", kare(dizi(kaynak, "M_GOREV"), bosluk=True), "şarkı listesinde")
    yaz(os.path.join(hedef, "00_hepsi_sirayla.wav"), hepsi)
    print("\n".join(liste))


if __name__ == "__main__":
    main()
