"""A4 sayfalara yazdırılacak ArUco işaretleri (DICT_4X4_50) üretir. Yazdırırken ölçek %100 (gerçek boyut) seçilmeli."""
import sys
import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

DPI = 300
A4 = (2480, 3508)  # 300 dpi
KENAR_MM = 180
idler = [int(a) for a in sys.argv[1:]] or [0, 1, 2]
sozluk = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_4X4_50)
px = round(KENAR_MM / 25.4 * DPI)
sayfalar = []
try:
    yazi = ImageFont.truetype("arial.ttf", 48)
except OSError:
    yazi = ImageFont.load_default()
for i in idler:
    isaret = cv2.aruco.generateImageMarker(sozluk, i, px)
    s = Image.new("L", A4, 255)
    x = (A4[0] - px) // 2
    y = 500
    s.paste(Image.fromarray(isaret), (x, y))
    d = ImageDraw.Draw(s)
    d.text((x, y + px + 80), f"ArUco 4x4  no {i}", font=yazi, fill=0)
    d.text((x, y + px + 150), f"Siyah kare kenari {KENAR_MM} mm olmali, cetvelle olc.", font=yazi, fill=0)
    d.text((x, y + px + 220), "Yazdirirken olcek: %100 / Gercek boyut", font=yazi, fill=0)
    sayfalar.append(s.convert("RGB"))
cikti = "tools/aruco/aruco_isaretleri.pdf"
sayfalar[0].save(cikti, save_all=True, append_images=sayfalar[1:], resolution=DPI)
print(cikti, len(sayfalar), "sayfa")
