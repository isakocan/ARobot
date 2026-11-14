"""Robot kayıtlarını (kayitlar/*/goruntu.h264) Windows'ta çift tıkla izlenen mp4'e çevirir.

Kullanım:
    pip install imageio-ffmpeg        (yönetici izni gerekmez, ffmpeg ikilisini getirir)
    python tools/kayit_videoya.py                       # kayitlar/*/ hepsi
    python tools/kayit_videoya.py kayitlar/2026-11-14_19-28-35_4_parke_vitessiz ...

Her klasöre iki dosya yazar:
    izle.mp4      ham çift balık gözü görüntü (1280x720, 15 fps)
    izle_360.mp4  1920x960 equirectangular 360° video; robotun önü ortada, zemin altta

Yönelim (parke kaydında ileri sürüşün optik akışıyla doğrulandı): kamera robota yan yatık
takılı (roll 90, RobotCameraMount ile aynı). v360 çıktısında robotun ARKASI (anten, gövde)
ortada çıkar; bu yüzden ikinci adımda 180° döndürülüp ön ortaya alınır.
Mevcut dosyalar --yeniden verilmedikçe atlanır."""
import subprocess, sys
from pathlib import Path

import imageio_ffmpeg

FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
KOK = Path(__file__).resolve().parent.parent
FPS = "15"

# Lens çemberleri 640 piksel yüksekliğinde; alttaki 80 siyah satır kırpılır (merkez y~319).
V360 = ("crop=1280:640:0:0,"
        "v360=input=dfisheye:output=equirect:ih_fov=203:iv_fov=203:w=1920:h=960:roll=90,"
        "v360=input=equirect:output=equirect:yaw=180,"
        "format=yuv420p")


def calistir(args):
    r = subprocess.run([FFMPEG, "-y", "-loglevel", "error", *args], capture_output=True, text=True)
    if r.returncode:
        raise RuntimeError(r.stderr.strip())


def cevir(klasor: Path, yeniden: bool):
    kaynak = klasor / "goruntu.h264"
    if not kaynak.exists():
        print("atlandı (goruntu.h264 yok):", klasor.name)
        return
    hedef1, hedef2 = klasor / "izle.mp4", klasor / "izle_360.mp4"
    giris = ["-r", FPS, "-i", str(kaynak)]
    ortak = ["-c:v", "libx264", "-preset", "medium", "-pix_fmt", "yuv420p", "-r", FPS, "-movflags", "+faststart"]
    if yeniden or not hedef1.exists():
        calistir([*giris, "-vf", "format=yuv420p", *ortak, "-crf", "18", str(hedef1)])
    if yeniden or not hedef2.exists():
        calistir([*giris, "-vf", V360, *ortak, "-crf", "20", str(hedef2)])
    print("tamam:", klasor.name)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    yeniden = "--yeniden" in sys.argv
    klasorler = [Path(a) for a in args] or sorted((KOK / "kayitlar").glob("*/"))
    for k in klasorler:
        cevir(k, yeniden)


if __name__ == "__main__":
    main()
