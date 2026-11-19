"""Theta S çift balık gözü kaydını equirect (eşdikdörtgen) videoya ve maskeye çevirir.

stella_vslam_dense (tek kameralı equirect SLAM) için hazırlık.
Kamera modeli Unity gölgelendiricisiyle (ARakiDualFisheye.shader, RobotCameraMount.cs) aynıdır.

Kullanım:
  python esdikdortgen.py <kayit_klasoru> [--genislik 1280] [--bas 0] [--bitis 60]

Çıktılar (<kayit>\\harita\\):
  esdikdortgen.mp4, maske.png, maske_onizleme.jpg, ornek_kare.jpg
Eksenler: robot +Z ileri, +X sağ, +Y yukarı. Görüntü ortası = robotun önü.
"""
import argparse
import math
import os
import subprocess
import sys
import time

import cv2
import numpy as np
import imageio_ffmpeg

KARE_G, KARE_Y, FPS = 1280, 720, 15
LENS1 = (322.42, 319.04, 180.87)
LENS2 = (956.67, 320.15, 180.30)
RMAX = 306.0
BAND = 6.0
LENS2_DONME = (-0.141, 0.984, 0.683)  # x, y, z derece


def rx(a):
    c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def ry(a):
    c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def rz(a):
    c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def tablolari_hazirla(W, yon, egim, yatma):
    """Her equirect pikseli için iki lensin remap tabloları ve ağırlıklar."""
    H = W // 2
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    lon = (u - 0.5) * 2 * np.pi
    lat = (0.5 - v) * np.pi
    lon, lat = np.meshgrid(lon, lat)
    dx = np.cos(lat) * np.sin(lon)
    dy = np.sin(lat)
    dz = np.cos(lat) * np.cos(lon)
    dirs = np.stack([dx, dy, dz], -1)  # H,W,3

    M = rz(yatma) @ rx(egim) @ ry(yon)
    L2 = rz(LENS2_DONME[2]) @ ry(LENS2_DONME[1]) @ rx(LENS2_DONME[0])
    d = dirs @ M.T
    c2 = np.stack([-d[..., 0], d[..., 1], -d[..., 2]], -1) @ L2.T

    def piksel(c, lens):
        theta = np.arccos(np.clip(c[..., 2], -1, 1))
        n = np.maximum(np.hypot(c[..., 0], c[..., 1]), 1e-6)
        px = lens[0] + c[..., 0] / n * theta * lens[2]
        py = lens[1] - c[..., 1] / n * theta * lens[2]
        return px.astype(np.float32), py.astype(np.float32), theta

    x1, y1, t1 = piksel(d, LENS1)
    x2, y2, t2 = piksel(c2, LENS2)
    x = np.clip((np.degrees(t1) - (90 - BAND)) / (2 * BAND), 0, 1)
    w1 = np.where(t1 * LENS1[2] > RMAX, 0.0, 1 - x * x * (3 - 2 * x))
    w2 = np.where(t2 * LENS2[2] > RMAX, 0.0, 1 - w1)
    toplam = w1 + w2
    kapsam = toplam > 0
    n = np.maximum(toplam, 1e-4)
    w1 = (w1 / n).astype(np.float32)[..., None]
    w2 = (w2 / n).astype(np.float32)[..., None]
    return (x1, y1, x2, y2, w1, w2, kapsam, lat)


def kare_cevir(kare, T):
    x1, y1, x2, y2, w1, w2, _, _ = T
    a = cv2.remap(kare, x1, y1, cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT)
    b = cv2.remap(kare, x2, y2, cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT)
    out = a.astype(np.float32) * w1 + b.astype(np.float32) * w2
    return np.clip(out + 0.5, 0, 255).astype(np.uint8)


def kareleri_oku(h264, bas, bitis):
    ff = imageio_ffmpeg.get_ffmpeg_exe()
    cmd = [ff, "-v", "error", "-r", str(FPS), "-i", h264, "-f", "rawvideo", "-pix_fmt", "bgr24", "-"]
    p = subprocess.Popen(cmd, stdout=subprocess.PIPE, bufsize=KARE_G * KARE_Y * 3 * 4)
    n = KARE_G * KARE_Y * 3
    i = 0
    try:
        while True:
            b = p.stdout.read(n)
            if len(b) < n:
                break
            t = i / FPS
            i += 1
            if t < bas:
                continue
            if bitis is not None and t > bitis:
                break
            yield i - 1, np.frombuffer(b, np.uint8).reshape(KARE_Y, KARE_G, 3)
    finally:
        p.stdout.close()
        p.kill()


def maske_olustur(ornekler, kapsam):
    """Sabit kalan (robot gövdesi) pikselleri bulur. ornekler: yarı çözünürlük gri kareler."""
    H, W = kapsam.shape
    st = np.stack(ornekler).astype(np.float32)
    std = st.std(axis=0)
    std = cv2.GaussianBlur(std, (0, 0), 3)
    std8 = cv2.normalize(std, None, 0, 255, cv2.NORM_MINMAX).astype(np.uint8)
    esik, _ = cv2.threshold(std8, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    # Gövde hem sabit hem koyu; düz renkli zemin de sabit görünür ama aydınlıktır
    ort = cv2.GaussianBlur(st.mean(axis=0), (0, 0), 3)
    koyu_esik = min(cv2.threshold(ort.astype(np.uint8), 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)[0], 90)
    dur = ((std8 <= esik) & (ort <= koyu_esik)).astype(np.uint8)
    # Ufkun üstündeki + 5 dereceye kadar bakılır; üstü (gökyüzü/tavan) duruyor sayılsa da alınmaz
    h2, w2 = dur.shape
    alt_sinir = int(h2 * (0.5 - 5 / 180.0))
    dur[:alt_sinir] = 0
    # bileşenler: alta değen ve yeterince büyük olanlar
    n, etiket, ist, _ = cv2.connectedComponentsWithStats(dur, connectivity=8)
    m = np.zeros_like(dur)
    for k in range(1, n):
        x, y, w, h, alan = ist[k]
        if alan >= 0.002 * w2 * h2 and (y + h >= h2 - 2):
            m[etiket == k] = 1
    ker = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (25, 25))
    m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, ker)
    # alt bölgede (akü etiketi gibi parlak boşluklar) büyük kapama
    alt = int(h2 * 0.74)
    bu = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (61, 61))
    m[-6:] = 1  # en alt satırlar robotun altı; gövde sayılır
    pad = cv2.copyMakeBorder(m[alt:], 30, 30, 0, 0, cv2.BORDER_CONSTANT, value=1)
    m[alt:] = cv2.morphologyEx(pad, cv2.MORPH_CLOSE, bu)[30:-30]
    # dar dikey yarıkları kapat (yatay geniş kapama)
    m[alt:] = cv2.morphologyEx(m[alt:], cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_RECT, (41, 1)))
    # her sütunda gövdenin üst kenarından aşağısı gövdedir (etiket gibi parlak yerler dahil)
    m[alt:] = np.maximum.accumulate(m[alt:], axis=0)
    # delikleri doldur
    ff = m.copy()
    cv2.floodFill(ff, np.zeros((h2 + 2, w2 + 2), np.uint8), (0, 0), 2)
    m[ff == 0] = 1
    m = cv2.resize(m, (W, H), interpolation=cv2.INTER_NEAREST)
    m[~kapsam] = 1
    m = cv2.dilate(m, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (13, 13)))
    return m.astype(bool), std8, esik


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("kayit")
    ap.add_argument("--genislik", type=int, default=1280)
    ap.add_argument("--bas", type=float, default=0.0, help="başlangıç (sn)")
    ap.add_argument("--bitis", type=float, default=None, help="bitiş (sn)")
    ap.add_argument("--yon", type=float, default=0.0)
    ap.add_argument("--egim", type=float, default=0.0)
    ap.add_argument("--yatma", type=float, default=90.0)
    a = ap.parse_args()

    h264 = os.path.join(a.kayit, "goruntu.h264")
    if not os.path.isfile(h264):
        sys.exit("goruntu.h264 yok: " + h264)
    cikis = os.path.join(a.kayit, "harita")
    os.makedirs(cikis, exist_ok=True)
    W = a.genislik
    H = W // 2
    t0 = time.time()
    T = tablolari_hazirla(W, a.yon, a.egim, a.yatma)
    kapsam = T[6]

    ff = imageio_ffmpeg.get_ffmpeg_exe()
    mp4 = os.path.join(cikis, "esdikdortgen.mp4")
    enc = subprocess.Popen(
        [ff, "-y", "-v", "error", "-f", "rawvideo", "-pix_fmt", "bgr24", "-s", f"{W}x{H}", "-r", str(FPS),
         "-i", "-", "-c:v", "libx264", "-crf", "14", "-preset", "medium", "-pix_fmt", "yuv420p", "-r", str(FPS), mp4],
        stdin=subprocess.PIPE)

    gri_kareler = []  # her 5 karede bir, yarı çözünürlük
    ornek30 = None
    sayac = 0
    for idx, kare in kareleri_oku(h264, a.bas, a.bitis):
        e = kare_cevir(kare, T)
        enc.stdin.write(e.tobytes())
        if sayac % 5 == 0:
            g = cv2.cvtColor(e, cv2.COLOR_BGR2GRAY)
            g = cv2.GaussianBlur(g, (0, 0), 1.5)
            gri_kareler.append(cv2.resize(g, (W // 2, H // 2), interpolation=cv2.INTER_AREA))
        if ornek30 is None and idx / FPS >= 30.0:
            ornek30 = e.copy()
        if ornek30 is None:
            son = e
        sayac += 1
        if sayac % 300 == 0:
            print(f"  {sayac} kare ({time.time() - t0:.0f} sn)")
    enc.stdin.close()
    enc.wait()
    if sayac == 0:
        sys.exit("kare okunamadı")
    if ornek30 is None:
        ornek30 = son
    print(f"Video yazıldı: {mp4} ({sayac} kare)")

    cv2.imwrite(os.path.join(cikis, "ornek_kare.jpg"), ornek30, [cv2.IMWRITE_JPEG_QUALITY, 92])

    # ~150 kareye indir
    if len(gri_kareler) > 150:
        sec = np.linspace(0, len(gri_kareler) - 1, 150).astype(int)
        gri_kareler = [gri_kareler[i] for i in sec]
    maske, std8, esik = maske_olustur(gri_kareler, kapsam)
    cv2.imwrite(os.path.join(cikis, "maske.png"), np.where(maske, 0, 255).astype(np.uint8))
    cv2.imwrite(os.path.join(cikis, "maske_std.png"), std8)
    on = ornek30.copy()
    kirmizi = np.zeros_like(on)
    kirmizi[..., 2] = 255
    on[maske] = (0.5 * on[maske] + 0.5 * kirmizi[maske]).astype(np.uint8)
    cv2.imwrite(os.path.join(cikis, "maske_onizleme.jpg"), on, [cv2.IMWRITE_JPEG_QUALITY, 90])
    print(f"Maskelenen oran: %{100 * maske.mean():.1f} (Otsu eşiği {esik:.0f})")
    print(f"Bitti: {time.time() - t0:.0f} sn")


if __name__ == "__main__":
    main()
