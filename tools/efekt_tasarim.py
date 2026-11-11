"""Buzzer efektlerini üretir: kayan tonlar (sweep) küçük adımlarla, notalar legato. Çıktı: Arduino dizileri."""
import re, sys

ino = sys.argv[1]


def kay(f0, f1, ms, adim=12):
    """f0'dan f1'e logaritmik kayış (kulak için düzgün), adim ms'lik parçalar."""
    n = max(2, ms // adim)
    out = []
    for i in range(n):
        f = f0 * (f1 / f0) ** (i / (n - 1))
        out += [int(round(f)), adim]
    return out


def nota(f, ms):
    return [f, ms]


def sus(ms):
    return [0, ms]


NOTA = {"C": 523, "D": 587, "E": 659, "F": 698, "G": 784, "A": 880, "B": 988}


def havali(ms):
    out = []
    t = 0
    while t < ms:
        bas = t < 40   # açılırken biraz alçaktan
        out += [415 if bas else 440, 4, 523 if bas else 554, 4]
        t += 8
    return out


def dizi_cal(notalar, ms, son_ms):
    """"F G G F ..." (5. oktav): her nota ms çalar, 25 ms susar (aynı nota art arda ayrı duyulsun); sonuncusu son_ms."""
    adlar = notalar.split()
    out = []
    for i, ad in enumerate(adlar):
        out += nota(NOTA[ad], son_ms if i == len(adlar) - 1 else ms) + sus(25)
    return out


E = {
    # açılış: yükselen akor + sonda parlayan kayış ("ta-da-dam-viiin")
    "E_ACILIS": nota(784, 110) + nota(988, 110) + nota(1175, 110) + kay(1175, 1568, 120) + nota(1568, 260),
    # bağlandı: yukarı kayış + net bip ("vuuup-bip")
    "E_BAGLANDI": kay(700, 1400, 150) + sus(50) + nota(1400, 120),
    # koptu: aşağı kayış ("viiuuuv")
    "E_KOPTU": kay(1400, 520, 360) + sus(30) + nota(520, 160),
    # kapanış: inen akor, sonda sönen kayış
    "E_KAPANIS": nota(1568, 140) + nota(1175, 140) + nota(988, 140) + kay(988, 494, 260),
    # vites: kalın, tok, arka planda kalan tek "tok" (kavrama oturuyor gibi kısa kalın ses + daha kalın vuruş)
    "E_VITES": nota(392, 18) + nota(262, 34),
    # alarm: iki tonlu acil uyarı, 3 kez
    "E_ALARM": (nota(1760, 140) + nota(1175, 140)) * 3,
    # foto: gür deklanşör "çık-ÇAK": piezonun en gür çaldığı 2-2,5 kHz'te, duyulacak kadar uzun
    "E_FOTO": nota(2489, 35) + sus(45) + nota(1865, 70),
    # kayıt başladı / bitti: iki ton yukarı / aşağı
    "E_KAYIT_BASLA": nota(988, 110) + sus(25) + nota(1480, 170),
    "E_KAYIT_BITTI": nota(1480, 110) + sus(25) + nota(988, 170),
    # La Cucaracha kornası (sol sol sol do mi, iki kez)
    "E_CUCARACHA": (nota(784, 120) + sus(25) + nota(784, 120) + sus(25) + nota(784, 120) + sus(25) + nota(1047, 260)
                    + sus(30) + nota(1319, 360) + sus(120)) * 2,
    # Dixie (General Lee kornası): sol mi do do do re mi fa sol sol sol mi
    "E_DIXIE": nota(784, 160) + nota(659, 80) + nota(523, 280) + sus(20) + nota(523, 130) + sus(15) + nota(523, 80)
               + nota(587, 80) + nota(659, 80) + nota(698, 80) + nota(784, 260) + sus(20) + nota(784, 260) + sus(20)
               + nota(784, 160) + nota(659, 420),
    # Baba (The Godfather aşk teması): sol do mib re do mib do re do lab sib sol
    "E_BABA": nota(784, 180) + nota(1047, 180) + nota(1245, 180) + nota(1175, 180) + nota(1047, 520) + nota(1245, 180)
              + nota(1047, 180) + nota(1175, 180) + nota(1047, 180) + nota(831, 180) + nota(932, 180) + nota(784, 800),
    "E_TRAS": nota(1047, 260) + sus(20) + nota(784, 120) + sus(15) + nota(784, 120) + nota(880, 260) + sus(20)
              + nota(784, 260) + sus(280) + nota(988, 260) + sus(20) + nota(1047, 300),
}

s = open(ino, encoding="utf-8").read()
for ad, dizi in E.items():
    deger = ", ".join(str(v) for v in dizi + [0, 0])
    s, n = re.subn(r"const uint16_t %s\[\] PROGMEM = \{[^}]*\};" % ad, "const uint16_t %s[] PROGMEM = { %s };" % (ad, deger), s)
    assert n == 1, ad
s = s.replace("const uint16_t DUDUT_HZ = 415;                       // 4: dü-düt",
              "const uint16_t DUDUT_HZ = 740;                       // 4: dü-düt")
s = s.replace("// Kısa ses efektleri (şarkı listesinde görünmez). Biçim melodilerle aynı: hz, ms çiftleri; hz 0 = sessizlik.",
              "// Kısa ses efektleri (şarkı listesinde görünmez). Biçim melodilerle aynı: hz, ms çiftleri; hz 0 = sessizlik.\n"
              "// Piezo kalın seste cılız, çok kısa seste anlaşılmaz: efektler 500-2200 Hz'te, kayan tonlar 12 ms'lik adımlarla.\n"
              "// tools/efekt_tasarim.py ile üretildi.")
open(ino, "w", encoding="utf-8").write(s)
print("ok", {k: len(v) // 2 for k, v in E.items()})
