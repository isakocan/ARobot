"""Köprü sürecini WSL'de başlatıp 'KOPRU_HAZIR' satırını bekleyen yardımcı (testler ve geliştirici ekranı arka ucu için)."""
import os
import subprocess
import time

KLASOR = os.path.dirname(os.path.abspath(__file__))
KOK = os.path.abspath(os.path.join(KLASOR, "..", ".."))
WSL_KOK = "/mnt/c/" + KOK[3:].replace("\\", "/")


class KopruSurec:
    def __init__(self, port=9760, mod="harita", harita="", ayar=None, maske="veri/olcum_ham.maske.png", bekleme=60):
        ayar = ayar or (WSL_KOK + "/tools/slam_deneme/ayar_ekdortgen_paralaks2.yaml")
        c = "cd /root/slam && exec ./kopru/build/kopru --vocab orb_vocab.fbow --config %s --mask %s --port %d --mod %s --log warn" % (ayar, maske, port, mod)
        if harita:
            c += " --map-in " + harita
        self.p = subprocess.Popen(["wsl", "-d", "Ubuntu-22.04", "-u", "root", "--", "bash", "-c", c], stderr=subprocess.PIPE, stdout=subprocess.DEVNULL,
                                  text=True, env=dict(os.environ, MSYS_NO_PATHCONV="1"))
        t0 = time.time()
        self.port = port
        while time.time() - t0 < bekleme:
            satir = self.p.stderr.readline()
            if "KOPRU_HAZIR" in satir:
                return
            if not satir and self.p.poll() is not None:
                break
        self.durdur()
        raise RuntimeError("köprü başlamadı")

    def durdur(self):
        try:
            self.p.terminate()
            self.p.wait(10)
        except Exception:
            self.p.kill()

    def __enter__(self):
        return self

    def __exit__(self, *a):
        self.durdur()
