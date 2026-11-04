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


E = {
    # açılış: yükselen akor + sonda parlayan kayış ("ta-da-dam-viiin")
    "E_ACILIS": nota(784, 110) + nota(988, 110) + nota(1175, 110) + kay(1175, 1568, 120) + nota(1568, 260),
    # bağlandı: yukarı kayış + net bip ("vuuup-bip")
    "E_BAGLANDI": kay(700, 1400, 150) + sus(50) + nota(1400, 120),
    # koptu: aşağı kayış ("viiuuuv")
    "E_KOPTU": kay(1400, 520, 360) + sus(30) + nota(520, 160),
    # kapanış: inen akor, sonda sönen kayış
    "E_KAPANIS": nota(1568, 140) + nota(1175, 140) + nota(988, 140) + kay(988, 494, 260),
    # vites: tok çift tık ("tı-tık")
    "E_VITES": nota(1300, 22) + sus(18) + nota(900, 30),
    # alarm: iki tonlu acil uyarı, 3 kez
    "E_ALARM": (nota(1760, 140) + nota(1175, 140)) * 3,
    # foto: deklanşör ("çık-çak")
    "E_FOTO": nota(2200, 18) + sus(45) + nota(1500, 35),
    # kayıt başladı / bitti: iki ton yukarı / aşağı
    "E_KAYIT_BASLA": nota(988, 110) + sus(25) + nota(1480, 170),
    "E_KAYIT_BITTI": nota(1480, 110) + sus(25) + nota(988, 170),
    # La Cucaracha kornası (sol sol sol do mi, iki kez)
    "E_CUCARACHA": (nota(784, 120) + sus(25) + nota(784, 120) + sus(25) + nota(784, 120) + sus(25) + nota(1047, 260)
                    + sus(30) + nota(1319, 360) + sus(120)) * 2,
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
