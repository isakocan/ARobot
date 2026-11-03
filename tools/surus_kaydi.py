"""Robotun kamerasını ve durumunu birlikte kaydeder (sonradan demo, test ve görüntü işleme denemeleri için).

    python tools/surus_kaydi.py <robot-adresi> [saniye] [ad]

kayitlar/<tarih>_<ad>/ içine yazar:
  goruntu.h264   köprünün gönderdiği H.264 (Annex-B; ffplay / VLC açar, ffmpeg -i goruntu.h264 -c copy x.mp4)
  kareler.csv    her karenin zamanı (ms, kayıt başından), anahtar kare mi, boyu
  durum.jsonl    10 Hz robot durumu: motor değerleri ve akımları, ışık, dörtlü, melodi, Wi-Fi, sıcaklık
Motor komutu göndermez; robotu kim sürüyorsa (uygulama, tarayıcı) o sürer. Ctrl+C ile erken durur.
"""
import base64, json, os, socket, struct, sys, threading, time, urllib.request

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HOST = sys.argv[1]
SURE = float(sys.argv[2]) if len(sys.argv) > 2 else 600
AD = sys.argv[3] if len(sys.argv) > 3 else "kayit"
KLASOR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "kayitlar", time.strftime("%Y-%m-%d_%H-%M-%S") + "_" + AD)
os.makedirs(KLASOR, exist_ok=True)
t0 = time.perf_counter()
ms = lambda: round((time.perf_counter() - t0) * 1000, 1)
bitti = threading.Event()


def ws_baglan(yol):
    s = socket.create_connection((HOST, 8080), timeout=5)
    key = base64.b64encode(os.urandom(16)).decode()
    s.sendall(("GET %s HTTP/1.1\r\nHost: %s\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
               "Sec-WebSocket-Key: %s\r\nSec-WebSocket-Version: 13\r\n\r\n" % (yol, HOST, key)).encode())
    tampon = b""
    while b"\r\n\r\n" not in tampon:
        tampon += s.recv(4096)
    if b" 101 " not in tampon.split(b"\r\n", 1)[0]:
        raise SystemExit("görüntü akışı açılamadı: " + tampon.split(b"\r\n", 1)[0].decode(errors="replace"))
    return s, tampon.split(b"\r\n\r\n", 1)[1]


def oku(s, tampon, n):
    while len(tampon) < n:
        parca = s.recv(1 << 16)
        if not parca:
            raise EOFError
        tampon += parca
    return tampon[:n], tampon[n:]


def mesajlar(s, tampon):
    while True:
        bas, tampon = oku(s, tampon, 2)
        n = bas[1] & 0x7F
        if n == 126:
            ek, tampon = oku(s, tampon, 2); n = struct.unpack(">H", ek)[0]
        elif n == 127:
            ek, tampon = oku(s, tampon, 8); n = struct.unpack(">Q", ek)[0]
        govde, tampon = oku(s, tampon, n)
        if bas[0] & 0x0F == 2:   # ikili
            yield govde


def durum_dongusu():
    with open(os.path.join(KLASOR, "durum.jsonl"), "w", encoding="utf-8") as f:
        while not bitti.is_set():
            try:
                d = json.load(urllib.request.urlopen("http://%s:8080/api/durum" % HOST, timeout=1))
                d["t"] = ms()
                f.write(json.dumps(d, ensure_ascii=False) + "\n")
            except Exception as e:
                f.write(json.dumps({"t": ms(), "hata": str(e)}) + "\n")
            time.sleep(0.1)


threading.Thread(target=durum_dongusu, daemon=True).start()
s, artik = ws_baglan("/goruntu-h264")
print("kaydediliyor:", os.path.abspath(KLASOR), "(Ctrl+C ile dur)")
kare = bayt = 0
try:
    with open(os.path.join(KLASOR, "goruntu.h264"), "wb") as h, open(os.path.join(KLASOR, "kareler.csv"), "w") as c:
        c.write("t_ms,anahtar,bayt\n")
        for m in mesajlar(s, artik):
            h.write(m[1:])
            c.write("%s,%d,%d\n" % (ms(), m[0], len(m) - 1))
            kare += 1
            bayt += len(m) - 1
            if kare % 150 == 0:
                print("  %4.0f sn  %d kare  %.1f MB" % (ms() / 1000, kare, bayt / 1e6))
            if ms() / 1000 > SURE:
                break
except (KeyboardInterrupt, EOFError):
    pass
bitti.set()
time.sleep(0.2)
print("bitti: %d kare, %.1f MB, %.0f sn" % (kare, bayt / 1e6, ms() / 1000))
