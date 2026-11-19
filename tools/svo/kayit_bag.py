#!/usr/bin/env python3
# Robot kaydini (goruntu.h264 + kareler.csv) SVO icin rosbag'e cevirir (konteyner icinde calisir).
# - Ricoh Theta S 1280x720 cift balik gozu; 1. lens (sol yari) 640x640 kirpilir, mono8.
# - Zaman damgasi: kareler.csv'deki t_ms (kare sirasina gore, bag zamani = t_ms/1000 sn).
# - /cam0/image_raw (sensor_msgs/Image, mono8) yazar.
# - --maske: ~150 kareden zamansal std ile "sabit" pikselleri (robot govdesi) + RMax disini bulur,
#   theta_s_on_lens_maske.png (0 = maskeli) yazar.
# Kullanim:  kayit_bag.py KAYIT_KLASORU [--cikti lens1.bag] [--maske MASKE.png] [--en N]
import argparse, csv, os, subprocess, sys
import numpy as np
import cv2

W = H = 640
CX, CY, RMAX = 322.42, 319.04, 306.0


def kareleri_oku(h264, en=None):
    """ffmpeg ile h264'u ayristirip her kareyi 1. lens kirpimi (gri) olarak uretir."""
    import shutil
    if shutil.which("ffmpeg") is None:  # ffmpeg yoksa (Windows) OpenCV ile coz
        cap = cv2.VideoCapture(h264)
        n = 0
        while True:
            ok, f = cap.read()
            if not ok:
                break
            yield cv2.cvtColor(f[:H, :W], cv2.COLOR_BGR2GRAY)
            n += 1
            if en and n >= en:
                break
        return
    cmd = ["ffmpeg", "-loglevel", "error", "-f", "h264", "-i", h264, "-vsync", "0",
           "-vf", "crop=%d:%d:0:0,format=gray" % (W, H), "-f", "rawvideo", "-pix_fmt", "gray", "-"]
    p = subprocess.Popen(cmd, stdout=subprocess.PIPE)
    n = 0
    while True:
        buf = p.stdout.read(W * H)
        if len(buf) < W * H:
            break
        yield np.frombuffer(buf, np.uint8).reshape(H, W)
        n += 1
        if en and n >= en:
            p.kill()
            break
    p.wait()


def maske_uret(h264, cikti, adet=150, toplam=None):
    # tum kayittan esit aralikli 'adet' kare; pencere basina bir kare almak icin hepsini okuyup ornekle
    kareler = list(kareleri_oku(h264))
    idx = np.linspace(0, len(kareler) - 1, adet).astype(int)
    S = np.stack([kareler[i] for i in idx]).astype(np.float32)
    std = S.std(axis=0)
    ort = S.mean(axis=0)
    std = cv2.GaussianBlur(std, (0, 0), 3)
    # sabit + koyu: robot govdesi/antenler. Esikler goruntuye bakilarak ayarli
    sabit = (std < 12) & (cv2.GaussianBlur(ort, (0, 0), 3) < 110)
    yy, xx = np.mgrid[0:H, 0:W]
    dis = np.hypot(xx - CX, yy - CY) > RMAX - 4
    m = np.ones((H, W), np.uint8) * 255
    m[sabit | dis] = 0
    # robot govdesi (aku + kablo) lensin sag kenarinda sabit duruyor; otomatik std/koyuluk
    # etiketli parlak kismi yakalamadigi icin ortalama goruntuye bakilarak cizilmis cokgen
    govde = np.zeros((H, W), np.uint8)
    poly = np.array([(450, 150), (520, 112), (545, 135), (575, 215), (605, 300), (622, 400), (610, 450),
                     (570, 520), (545, 550), (480, 530), (437, 500), (435, 465), (458, 435), (452, 300)], np.int32)
    cv2.fillPoly(govde, [poly], 1)
    govde = cv2.dilate(govde, np.ones((9, 9), np.uint8))
    m = np.ones((H, W), np.uint8) * 255
    m[(govde > 0) | dis] = 0
    cv2.imwrite(cikti, m)
    cv2.imwrite(cikti.replace(".png", "_std.png"), np.clip(std * 8, 0, 255).astype(np.uint8))
    cv2.imwrite(cikti.replace(".png", "_ort.png"), ort.astype(np.uint8))
    print("maske yazildi:", cikti, "gecerli oran %.3f" % (m > 0).mean())


def bag_yaz(klasor, cikti, en=None):
    import rosbag, rospy
    from sensor_msgs.msg import Image
    ts = []
    with open(os.path.join(klasor, "kareler.csv")) as f:
        for r in csv.DictReader(f):
            ts.append(float(r["t_ms"]) / 1000.0)
    n = 0
    with rosbag.Bag(cikti, "w") as bag:
        for i, kare in enumerate(kareleri_oku(os.path.join(klasor, "goruntu.h264"), en)):
            if i >= len(ts):
                print("UYARI: csv'den fazla kare, kalan atlandi")
                break
            t = rospy.Time.from_sec(ts[i])
            m = Image()
            m.header.stamp = t
            m.header.frame_id = "cam0"
            m.height, m.width, m.encoding = H, W, "mono8"
            m.step = W
            m.data = kare.tobytes()
            bag.write("/cam0/image_raw", m, t)
            n += 1
    print("bag: %d kare (csv %d satir) -> %s" % (n, len(ts), cikti))


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("klasor")
    ap.add_argument("--cikti")
    ap.add_argument("--maske")
    ap.add_argument("--en", type=int)
    a = ap.parse_args()
    if a.maske:
        maske_uret(os.path.join(a.klasor, "goruntu.h264"), a.maske)
    else:
        bag_yaz(a.klasor, a.cikti or os.path.join(a.klasor, "svo", "lens1.bag"), a.en)
