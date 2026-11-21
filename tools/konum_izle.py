"""Robotun konum bulmasını oyunsuz dener: evin işaretlerini (telefondaki ev.json) robota yollar, robotun bulduğu
konumu ekrana yazar. Robotu bu sırada Unity uygulamasıyla ya da tarayıcı sürüş sayfasıyla sürebilirsin.

Kullanım:
    python tools/konum_izle.py <robot_ip> <ev.json>
    (ev.json: telefonda Android/data/com.ekip.artarama/files/evler/<ev>/ev.json; adb pull ile alınır)

Robot sürüş köprüsü UDP 8081: {"t":"ev","isaretler":[[id,x,z,aci],...]} saniyede bir; durum için sıfır komutlu "d"
paketi (sürmez; başka uygulama sürerken onun komutunu ezmez).
"""
import json
import socket
import sys
import time


def isaretler(yol):
    m = json.load(open(yol, encoding="utf-8"))
    return [[i["id"], i["x"], i["z"], i["aci"]] for i in m.get("isaretler", [])]


def main():
    if len(sys.argv) < 3:
        print(__doc__); return 2
    ip, ev = sys.argv[1], sys.argv[2]
    liste = isaretler(ev)
    print("ev işaretleri:", [l[0] for l in liste])
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); s.settimeout(0.3)
    n, son_ev = 0, 0.0
    while True:
        simdi = time.time()
        if simdi - son_ev > 1.0:
            s.sendto(json.dumps({"t": "ev", "isaretler": liste}).encode(), (ip, 8081)); son_ev = simdi
        n += 1
        s.sendto(json.dumps({"t": "d", "n": n, "sol": 0, "sag": 0}).encode(), (ip, 8081))
        try:
            d = json.loads(s.recv(4096).decode())
        except (socket.timeout, ValueError):
            time.sleep(0.2); continue
        k = d.get("konum")
        if k is None:
            print("konum yok:", d.get("konum_hatasi", "bekleniyor"), end="\r")
        else:
            g = " ".join("#%d %.2fm %+.0f°" % tuple(x) for x in k.get("gorulen", []))
            print("%s  x=%6.2f z=%6.2f yön=%6.1f°  son işaret %s (%s sn)  %3d ms  görülen: %s        " % (
                "BİLİNİYOR" if k["var"] else "bilinmiyor", k["x"], k["z"], k["yaw"], k["isaret"], k["isaret_sn"], k["ms"], g), end="\r")
        time.sleep(0.2)


if __name__ == "__main__":
    sys.exit(main() or 0)
