"""Robotla gidip gelme süresini UDP (uygulamanın kanalı, :8081) ve TCP (WebSocket /kontrol, :8080) üstünden ölçer.

    python tools/gecikme_olc.py <robot adresi> [saniye]

Her iki yolda da saniyede 20 küçük paket gider (uygulamanın sıklığı); ortalama, ortanca, %95 ve en kötü süre
ile kayıp yazılır. UDP paketlerinde motor alanı yoktur (robot sürülmez) ama köprü "dur" der: ölçerken sürme.
"""
import base64, json, os, socket, statistics, struct, sys, time


def ozet(ad, sureler, gonderilen):
    if not sureler:
        print("%-4s cevap yok" % ad)
        return
    s = sorted(sureler)
    print("%-4s ortalama %5.0f ms  ortanca %5.0f  %%95 %5.0f  en kötü %5.0f  kayıp %%%.1f  (%d paket)" % (
        ad, statistics.mean(s), s[len(s) // 2], s[int(len(s) * 0.95)], s[-1],
        100.0 * (gonderilen - len(s)) / gonderilen, gonderilen))


def udp(host, sure):
    k = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    k.settimeout(0.05)
    gonderilen, giden, sureler = 0, {}, []
    bitis = time.time() + sure
    n = 1
    while time.time() < bitis:
        z = time.time() * 1000
        k.sendto(json.dumps({"t": "d", "n": n, "z": z}).encode(), (host, 8081))
        giden[n] = z
        gonderilen += 1
        n += 1
        son = time.time() + 0.05
        while time.time() < son:
            try:
                veri, _ = k.recvfrom(65536)
                m = json.loads(veri.decode())
                if m.get("n") in giden:
                    sureler.append(time.time() * 1000 - giden.pop(m["n"]))
            except socket.timeout:
                pass
    ozet("UDP", sureler, gonderilen)


def ws_kare(veri):
    maske = os.urandom(4)
    govde = bytes(b ^ maske[i % 4] for i, b in enumerate(veri))
    return bytes([0x81, 0x80 | len(veri)]) + maske + govde


def ws_oku(k, tampon):
    while True:
        if len(tampon) >= 2:
            uz = tampon[1] & 0x7F
            bas = 2
            if uz == 126:
                if len(tampon) < 4:
                    pass
                else:
                    uz = struct.unpack(">H", tampon[2:4])[0]
                    bas = 4
            if uz != 126 and len(tampon) >= bas + uz:
                return tampon[bas:bas + uz], tampon[bas + uz:]
        tampon += k.recv(65536)


def tcp(host, sure):
    k = socket.create_connection((host, 8080), timeout=3)
    k.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    anahtar = base64.b64encode(os.urandom(16)).decode()
    k.sendall(("GET /kontrol HTTP/1.1\r\nHost: %s\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
               "Sec-WebSocket-Key: %s\r\nSec-WebSocket-Version: 13\r\n\r\n" % (host, anahtar)).encode())
    tampon = b""
    while b"\r\n\r\n" not in tampon:
        tampon += k.recv(4096)
    tampon = tampon.split(b"\r\n\r\n", 1)[1]
    gonderilen, sureler = 0, []
    bitis = time.time() + sure
    k.settimeout(2)
    while time.time() < bitis:
        z = time.time() * 1000
        k.sendall(ws_kare(json.dumps({"t": "ping", "z": z}).encode()))
        gonderilen += 1
        try:
            while True:
                veri, tampon = ws_oku(k, tampon)
                m = json.loads(veri.decode())
                if m.get("t") == "pong" and m.get("z") == z:
                    sureler.append(time.time() * 1000 - z)
                    break
        except (socket.timeout, ValueError):
            pass
        kalan = 0.05 - (time.time() * 1000 - z) / 1000
        if kalan > 0:
            time.sleep(kalan)
    k.close()
    ozet("TCP", sureler, gonderilen)


if __name__ == "__main__":
    host = sys.argv[1]
    sure = float(sys.argv[2]) if len(sys.argv) > 2 else 20
    udp(host, sure)
    tcp(host, sure)
