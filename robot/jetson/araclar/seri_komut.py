#!/usr/bin/env python3
"""
Arduino'ya seri porttan komut gönderir, cevapları yazar.

  python3 seri_komut.py "L 1" "bekle 2" "tekrar 2 M 120 120" "L 0"

"bekle <sn>" o kadar bekler. "tekrar <sn> <komut>" komutu o süre boyunca 100 ms'de bir
gönderir (sürüş komutu yenilenmezse Arduino motorları durdurur). Port açılınca Uno
yeniden başlar, bu yüzden önce "HAZIR" satırı beklenir. Arduino'nun düzenli "D ..."
durum satırları yalnızca --durum verilirse yazılır.
"""

import sys
import time

import serial

PORT = "/dev/ttyACM0"
HIZ = 115200


DURUM_YAZ = "--durum" in sys.argv


def oku(p, sure):
    son = time.time() + sure
    while time.time() < son:
        satir = p.readline().decode("utf-8", "replace").strip()
        if satir and (DURUM_YAZ or not satir.startswith("D ")):
            print("  <", satir, flush=True)


def tekrarla(p, sure, komut):
    print(">", komut, "({} sn boyunca)".format(sure), flush=True)
    son = time.time() + sure
    while time.time() < son:
        p.write((komut + "\n").encode())
        oku(p, 0.1)


def main():
    with serial.Serial(PORT, HIZ, timeout=0.1) as p:
        son = time.time() + 5
        while time.time() < son:
            satir = p.readline().decode("utf-8", "replace").strip()
            if satir:
                print("  <", satir, flush=True)
            if satir.startswith("HAZIR"):
                break
        else:
            print("Arduino HAZIR demedi; yine de devam ediliyor.")

        for komut in sys.argv[1:]:
            if komut == "--durum":
                continue
            if komut.startswith("bekle"):
                oku(p, float(komut.split()[1]))
                continue
            if komut.startswith("tekrar"):
                _, sure, asil = komut.split(" ", 2)
                tekrarla(p, float(sure), asil)
                continue
            print(">", komut, flush=True)
            p.write((komut + "\n").encode())
            oku(p, 0.3)
        oku(p, 1.5)
        p.write(b"S\n")


if __name__ == "__main__":
    main()
