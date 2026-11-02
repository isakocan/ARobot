"""
Şarkıları robotun Arduino'suna ({frekans Hz, süre ms} dizisi) çevirir.

İki giriş biçimi:
  1) Nota listesi (robsoncouto/arduino-songs biçimi). Dosyada her şarkı şöyle başlar:
         @M_AD|tempo|Görünen ad
         NOTE_E5, 8, NOTE_B4, 16, REST, 4, NOTE_C5, -8, ...
     Sayı notanın bölenidir: 4 = dörtlük, 8 = sekizlik; eksi = noktalı (1,5 kat).
         python tools/melodi_cevir.py liste sarkilar.txt > sarkilar.h

  2) MIDI dosyası: en tiz sesi (genelde melodi) alıp tek sese indirir.
         python tools/melodi_cevir.py midi witcher.mid M_WITCHER "Witcher 3" [--kanal 0] [--en-cok 400]
     Çıktıyı robot_kontrol/sarkilar.h içine yapıştır, robot_kontrol.ino'daki MELODILER
     listesine ve surus.html'deki MELODILER adlarına ekle (sıra aynı olmalı).

Buzzer (piezo) kalın seslerde kısık kaldığı için ortanca nota 520 Hz'in altındaysa şarkı
oktav oktav yukarı taşınır.
"""

import re
import struct
import sys

HARF = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def frekans(midi_no):
    return int(round(440 * 2 ** ((midi_no - 69) / 12)))


def nota_adi_midi(ad):
    m = re.fullmatch(r"NOTE_([A-G])(S?)(\d)", ad)
    if not m:
        raise ValueError("nota tanınmadı: " + ad)
    return 12 * (int(m.group(3)) + 1) + HARF[m.group(1)] + (1 if m.group(2) else 0)


def yukari_tasi(notalar):
    """notalar: [(midi_no ya da None, ms)]; ortanca 520 Hz'in altındaysa oktav ekler."""
    sesler = sorted(n for n, _ in notalar if n is not None)
    kaydir = 0
    if sesler:
        orta = sesler[len(sesler) // 2]
        while frekans(orta + kaydir) < 520 and frekans(max(sesler) + kaydir + 12) <= 4200:
            kaydir += 12
    return [(None if n is None else n + kaydir, ms) for n, ms in notalar], kaydir // 12


def c_dizisi(ad, baslik, notalar, aciklama=""):
    """[(midi_no|None, ms)] → PROGMEM dizisi metni. Art arda sessizlikler birleştirilir."""
    birlesik = []
    for n, ms in notalar:
        ms = max(1, min(65000, int(round(ms))))
        if n is None and birlesik and birlesik[-1][0] is None:
            birlesik[-1] = (None, birlesik[-1][1] + ms)
        else:
            birlesik.append((n, ms))
    ciftler = ["{}, {}".format(0 if n is None else frekans(n), ms) for n, ms in birlesik]
    satirlar = [", ".join(ciftler[i:i + 10]) for i in range(0, len(ciftler), 10)]
    return ("// {}{}\nconst uint16_t {}[] PROGMEM = {{\n  {},\n  0, 0 }};\n"
            .format(baslik, aciklama, ad, ",\n  ".join(satirlar)))


# ---------------------------------------------------------------- nota listesi

def liste_cevir(yol):
    metin = open(yol, encoding="utf-8").read()
    cikis = ["// Bu dosya tools/melodi_cevir.py ile üretildi; elle düzenleme.",
             "// Kaynak nota dökümleri: github.com/robsoncouto/arduino-songs ve",
             "// github.com/robsoncouto/arduino-songs/issues/23 (Megalovania).", ""]
    for blok in re.split(r"^@", metin, flags=re.M)[1:]:
        baslik_satiri, govde = blok.split("\n", 1)
        ad, tempo, baslik = baslik_satiri.strip().split("|")
        tam_nota = 60000 * 4 / int(tempo)
        govde = re.sub(r"//[^\n]*", "", govde)
        parcalar = [p.strip() for p in govde.replace("\n", ",").split(",") if p.strip()]
        notalar = []
        for nota, bolen in zip(parcalar[0::2], parcalar[1::2]):
            b = int(bolen)
            ms = tam_nota / abs(b) * (1.5 if b < 0 else 1)
            notalar.append((None if nota == "REST" else nota_adi_midi(nota), ms))
        notalar, oktav = yukari_tasi(notalar)
        ek = " (+{} oktav)".format(oktav) if oktav else ""
        cikis.append(c_dizisi(ad, baslik, notalar, ek))
        print("{}: {} nota{}".format(ad, len(notalar), ek), file=sys.stderr)
    return "\n".join(cikis)


# ---------------------------------------------------------------- MIDI

def _degisken_uzunluk(veri, i):
    deger = 0
    while True:
        b = veri[i]
        i += 1
        deger = (deger << 7) | (b & 0x7F)
        if not b & 0x80:
            return deger, i


def midi_oku(yol):
    """[(tik, tür, kanal, nota, hız)] + tempo değişimleri + tik/dörtlük."""
    veri = open(yol, "rb").read()
    if veri[:4] != b"MThd":
        raise ValueError("MIDI dosyası değil")
    _, _, iz_sayisi, bolum = struct.unpack(">IHHH", veri[4:14])
    if bolum & 0x8000:
        raise ValueError("SMPTE zamanlı MIDI desteklenmiyor")
    i = 8 + struct.unpack(">I", veri[4:8])[0]
    olaylar, tempolar = [], [(0, 500000)]
    for _ in range(iz_sayisi):
        uzunluk = struct.unpack(">I", veri[i + 4:i + 8])[0]
        j, son, tik, durum = i + 8, i + 8 + uzunluk, 0, 0
        while j < son:
            fark, j = _degisken_uzunluk(veri, j)
            tik += fark
            b = veri[j]
            if b == 0xFF:                       # meta
                tur = veri[j + 1]
                n, j = _degisken_uzunluk(veri, j + 2)
                if tur == 0x51:
                    tempolar.append((tik, int.from_bytes(veri[j:j + 3], "big")))
                j += n
                continue
            if b in (0xF0, 0xF7):               # sysex
                n, j = _degisken_uzunluk(veri, j + 1)
                j += n
                continue
            if b & 0x80:
                durum = b
                j += 1
            tur, kanal = durum & 0xF0, durum & 0x0F
            if tur in (0xC0, 0xD0):
                j += 1
                continue
            a, c = veri[j], veri[j + 1]
            j += 2
            if tur == 0x90 and c > 0:
                olaylar.append((tik, "ac", kanal, a))
            elif tur == 0x80 or (tur == 0x90 and c == 0):
                olaylar.append((tik, "kapa", kanal, a))
        i = son
    return olaylar, sorted(tempolar), bolum


def midi_cevir(yol, ad, baslik, kanal=None, en_cok=400):
    olaylar, tempolar, bolum = midi_oku(yol)
    if kanal is not None:
        olaylar = [o for o in olaylar if o[2] == kanal]
    olaylar.sort(key=lambda o: (o[0], o[1] == "ac"))

    def ms(tik):                               # tikten milisaniyeye, tempo değişimleriyle
        sonuc, onceki_tik, us = 0.0, 0, 500000
        for t, yeni in tempolar:
            if t >= tik:
                break
            sonuc += (t - onceki_tik) * us / bolum / 1000
            onceki_tik, us = t, yeni
        return sonuc + (tik - onceki_tik) * us / bolum / 1000

    # Her an çalan en tiz nota = melodi
    calan, parcalar, simdiki, bas = {}, [], None, 0
    for tik, tur, k, nota in olaylar:
        if tur == "ac":
            calan[(k, nota)] = calan.get((k, nota), 0) + 1
        elif calan.get((k, nota)):
            calan[(k, nota)] -= 1
            if not calan[(k, nota)]:
                del calan[(k, nota)]
        en_tiz = max((n for _, n in calan), default=None)
        if en_tiz != simdiki:
            if tik > bas:
                parcalar.append((simdiki, ms(tik) - ms(bas)))
            simdiki, bas = en_tiz, tik
    while parcalar and parcalar[0][0] is None:
        parcalar.pop(0)
    parcalar = [p for p in parcalar if p[1] >= 15][:en_cok]
    parcalar, oktav = yukari_tasi(parcalar)
    print("{}: {} nota{}".format(ad, len(parcalar), " (+{} oktav)".format(oktav) if oktav else ""), file=sys.stderr)
    return c_dizisi(ad, baslik, parcalar, " (MIDI'den)")


if __name__ == "__main__":
    if len(sys.argv) >= 3 and sys.argv[1] == "liste":
        sys.stdout.reconfigure(encoding="utf-8")
        print(liste_cevir(sys.argv[2]))
    elif len(sys.argv) >= 5 and sys.argv[1] == "midi":
        sys.stdout.reconfigure(encoding="utf-8")
        argv = sys.argv[5:]
        kanal = int(argv[argv.index("--kanal") + 1]) if "--kanal" in argv else None
        en_cok = int(argv[argv.index("--en-cok") + 1]) if "--en-cok" in argv else 400
        print(midi_cevir(sys.argv[2], sys.argv[3], sys.argv[4], kanal, en_cok))
    else:
        print(__doc__)
