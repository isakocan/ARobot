"""Sürüş testinin bir adımını kaydeder: görüntü + durum (surus_kaydi.py) ve o zaman aralığının günlükleri.

    python tools/adim_kaydi.py <robot-adresi> <saniye> <etiket>

kayitlar/<tarih>_<etiket>/ içine surus_kaydi.py çıktısına ek olarak:
  uygulama.log   bilgisayardaki uygulamanın surus.log'undan bu adımın satırları
  kopru.log      robottaki köprü günlüğünden bu adımın satırları (ssh jetson)
"""
import glob, os, subprocess, sys, time

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HOST, SURE, ETIKET = sys.argv[1], sys.argv[2], sys.argv[3]
KOK = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
UYGULAMA_LOG = os.path.join(os.path.expanduser("~"), "AppData", "LocalLow", "ARaki", "Robot Sürüş", "surus.log")

bas = time.strftime("%H:%M:%S")
subprocess.run([sys.executable, os.path.join(KOK, "tools", "surus_kaydi.py"), HOST, SURE, ETIKET])
son = time.strftime("%H:%M:%S")

klasor = max(glob.glob(os.path.join(KOK, "kayitlar", "*_" + ETIKET)), key=os.path.getmtime)


def aralik(satirlar):
    return [s for s in satirlar if bas <= s[:8] <= son]


try:
    with open(UYGULAMA_LOG, encoding="utf-8", errors="replace") as f:
        uyg = aralik(f.readlines())
except OSError:
    uyg = []
with open(os.path.join(klasor, "uygulama.log"), "w", encoding="utf-8") as f:
    f.writelines(uyg)
kopru = subprocess.run(["ssh", "jetson", "tail -n 400 ~/robot/surus_koprusu/kopru.log"], capture_output=True)
satirlar = kopru.stdout.decode("utf-8", errors="replace").splitlines(True)
with open(os.path.join(klasor, "kopru.log"), "w", encoding="utf-8") as f:
    f.writelines(aralik(satirlar))
print("adım", ETIKET, bas, "-", son, "→", klasor, "| uygulama", len(uyg), "satır")
