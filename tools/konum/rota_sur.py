"""rota_planla.py'nin rotasını robota yavaşça sürdürür (PC; robot sürüş uygulaması KAPALI olmalı).

Köprünün UDP sürüş kanalını kullanır (uygulamanın yaptığının aynısı, 20 Hz) ve her pakette konum ister; robotun kendi
konumuna (işaret + görüntü pusulası + zemin izi) bakarak:
  1. başta konum kurulana kadar bekler (robot başlangıçta ilk işarete bakıyor olmalı),
  2. her noktaya önce yerinde döner (yön farkı küçülünce kısa vuruşlarla), sonra yönünü düzelterek ileri gider;
     "tur" işaretli noktalarda yerinde bir tam tur atar. Kayıtta her satırda hangi test parçasında olduğu yazar.
GÜVENLİK: herhangi bir tuş ya da Ctrl+C = hemen dur. Konum 1,5 sn gelmezse, robot planlanan çizgiden 0,45 m saparsa,
bir parça beklenenden uzun sürerse durur ve çıkar. Paket kesilince köprü de motorları ~1 sn içinde durdurur.
Kayıt: <rota klasörü>/surus_<zaman>.csv
Kullanım: python rota_sur.py <robot_adresi> <rota_klasörü>   (HIZ=120 ileri PWM, DONUS=150, TUR=0 turları atla, KURU=1 motorsuz)"""
import csv, json, math, os, socket, sys, time
try:
    import msvcrt
except ImportError:
    msvcrt = None

ADR, KL = sys.argv[1:3]
HIZ = int(os.environ.get("HIZ", "130")); DONUS = int(os.environ.get("DONUS", "150"))
TUR = os.environ.get("TUR", "1") == "1"; KURU = os.environ.get("KURU", "0") == "1"
BASA_EN_COK = float(os.environ.get("BASA_EN_COK", "1.5"))   # m: robot başlangıca bundan yakınsa önce oraya kendisi gider
ROTA = json.load(open(os.path.join(KL, "rota.json"), encoding="utf-8"))
DUR_DOSYASI = os.path.join(KL, "DUR")
CANLI = os.path.join(KL, "canli.json")            # rota_arayuz.py DUR düğmesi bu dosyayı yaratır
P = ROTA["noktalar"]

s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); s.setblocking(False)
n = 0; son = {"t": 0.0, "k": None, "kare": None, "taze": 0.0}
kayit = open(os.path.join(KL, "surus_%s.csv" % time.strftime("%H%M%S")), "w", newline="", encoding="utf-8")
yaz = csv.writer(kayit); yaz.writerow(["t", "utc", "evre", "hedef", "sol", "sag", "var", "x", "z", "yaw", "sy", "kaynak"])
t0 = time.time(); evre = ["bekle"]; hedef_no = [0]


def da(a): return (a + 180.0) % 360.0 - 180.0


class Dur(Exception): pass


def gonder(sol, sag):
    global n
    n += 1
    if KURU: sol = sag = 0
    s.sendto(json.dumps({"t": "d", "n": n, "sol": int(sol), "sag": int(sag), "konum": 1}).encode(), (ADR, 8081))
    while True:
        try:
            m = json.loads(s.recv(65535).decode())
        except (BlockingIOError, OSError, ValueError):
            break
        km = m.get("kamera")                       # köprü: kapali | yok | baslatiliyor | aktif
        if km == "aktif": son["kamera_ok"] = time.time()
        elif km == "yok" or (km is not None and son.get("kamera_ok") and time.time() - son["kamera_ok"] > 2.0):
            son["kamera_yok"] = True
        k = m.get("konum")
        if isinstance(k, dict):
            son["k"] = k; son["t"] = time.time()
            if k.get("kare") != son["kare"]:
                son["kare"] = k.get("kare"); son["taze"] = time.time()   # yeni kare işlendi
                if k.get("var"):                                          # arayüz için anlık konum (gecikmesiz)
                    try:
                        with open(CANLI + ".tmp", "w", encoding="utf-8") as f_:
                            json.dump(dict(x=k["x"], z=k["z"], yaw=k["yaw"], evre=evre[0], t=time.time(),
                                           gorulen=[g[0] for g in k.get("gorulen") or []]), f_)
                        os.replace(CANLI + ".tmp", CANLI)
                    except OSError:
                        pass
    k = son["k"] or {}
    yaz.writerow(["%.2f" % (time.time() - t0), "%.3f" % time.time(), evre[0], hedef_no[0], sol, sag, k.get("var"), k.get("x"), k.get("z"), k.get("yaw"), k.get("sy"), k.get("kaynak")])
    if msvcrt and msvcrt.kbhit():
        msvcrt.getch(); raise Dur("tuşa basıldı")
    kayit.flush()
    if son.get("kamera_yok"): raise Dur("robotun kamerası kayboldu (konum donar)")
    if os.path.exists(DUR_DOSYASI): raise Dur("arayüzden durduruldu")


def bekle(sn, sol=0, sag=0):
    b = time.time()
    while time.time() - b < sn:
        gonder(sol, sag); time.sleep(0.05)


def poz():
    k = son["k"]
    if not k or not k.get("var") or time.time() - son["t"] > 1.5 or time.time() - son["taze"] > 1.2: return None
    return k["x"], k["z"], k["yaw"]


def poz_bekle(sure=1.5):
    b = time.time()
    while True:
        p = poz()
        if p: return p
        if time.time() - b > sure: raise Dur("konum gelmiyor")
        bekle(0.1)


HIZ_DONUS = 80.0      # der/sn: DONUS gücünde yerinde dönüş (28 Kas kaydı: 0,15 sn vuruş ~10-15 der)


def don(hedef_yaw, tolerans=8.0):
    """Kısa vuruşlarla dön: vuruş süresi hatayla orantılı (en çok 0,35 sn), sonra konum otursun diye 0,7 sn bekle.
    (28 Kas: sürekli dönüp 0,15 sn'de bir bakınca ~0,4 sn gecikmeli konum yüzünden ileri geri salındı.)"""
    b = time.time()
    while True:
        p = poz_bekle(); e = da(hedef_yaw - p[2])
        if abs(e) < tolerans: return
        if time.time() - b > 40: raise Dur("dönüş 40 sn'de bitmedi")
        yon = 1 if e > 0 else -1                      # sol > sag: sağa (+) döner
        sure = max(0.06, min(0.35, 0.8 * abs(e) / HIZ_DONUS))
        bekle(sure, DONUS * yon, -DONUS * yon); bekle(0.7)


def git(a, b):
    L = math.hypot(b[0] - a[0], b[1] - a[1]); ux, uz = (b[0] - a[0]) / max(L, 1e-6), (b[1] - a[1]) / max(L, 1e-6)
    bas = time.time(); sinir = 15 + L / 0.04; izle = None
    while True:
        p = poz_bekle()
        ilerleme = (p[0] - a[0]) * ux + (p[1] - a[1]) * uz
        sapma = abs((p[0] - a[0]) * uz - (p[1] - a[1]) * ux)
        kalan = math.hypot(b[0] - p[0], b[1] - p[1])
        if kalan < 0.12 or ilerleme >= L: return
        if sapma > 0.45: raise Dur("çizgiden %.2f m saptı" % sapma)
        if time.time() - bas > sinir: raise Dur("parça %.0f sn'de bitmedi" % sinir)
        hedef = math.degrees(math.atan2(b[0] - p[0], b[1] - p[1])); e = da(hedef - p[2])
        if abs(e) > 20:
            bekle(0.3); don(hedef); izle = None; continue
        d = max(-25, min(25, 1.5 * e))                # iki taraf yakın kalsın (kızaklı: yavaş taraf takılıyor)
        bekle(0.1, HIZ + d, HIZ - d)
        # takılma: 2 sn ileri komutuna rağmen 4 cm'den az yer değiştirdiyse dur
        if izle is None or time.time() - izle[0] > 2.0:
            if izle is not None and math.hypot(p[0] - izle[1], p[1] - izle[2]) < 0.04: raise Dur("takıldı: 2 sn ileri komutuna rağmen yer değişmedi")
            izle = (time.time(), p[0], p[1])


def tam_tur(ad):
    """Yerinde bir tam tur (sağa), sonra başladığı yöne dön."""
    evre[0] = ad; print("  " + ad)
    y0 = poz_bekle()[2]; toplam = 0.0; onceki = y0; b = time.time()
    while toplam < 350:
        if time.time() - b > 60: raise Dur("tur 60 sn'de bitmedi")
        bekle(0.15, DONUS, -DONUS); bekle(0.25)
        q = poz_bekle(); toplam += abs(da(q[2] - onceki)); onceki = q[2]
    don(y0)


try:
    print("Robot: %s  rota %d nokta, %.1f m  HIZ %d DONUS %d%s. Durdurmak için HERHANGİ BİR TUŞ." % (ADR, len(P), ROTA["toplam_m"], HIZ, DONUS, "  (KURU: motor yok)" if KURU else ""))
    evre[0] = "bekle"; print("konum bekleniyor (robot başlangıçta ilk işarete baksın)...")
    b = time.time()
    while poz() is None:
        bekle(0.2)
        if time.time() - b > 40: raise Dur("40 sn'de konum kurulamadı")
    p = poz(); print("konum: x %.2f z %.2f yön %.0f" % p)
    uz = math.hypot(p[0] - P[0]["x"], p[1] - P[0]["z"])
    if uz > BASA_EN_COK:
        raise Dur("robot başlangıç noktasından %.2f m uzakta (en çok %.1f)" % (uz, BASA_EN_COK))
    if uz > 0.12:
        # önce başlangıca git (düz çizgi, açık zeminde olduğu kullanıcıya gösterilmiş olmalı), sonra ilk işarete dön
        evre[0] = "başlangıca"; print("başlangıca gidiliyor: %.2f m" % uz)
        don(math.degrees(math.atan2(P[0]["x"] - p[0], P[0]["z"] - p[1])))
        q = poz_bekle(); git((q[0], q[1]), (P[0]["x"], P[0]["z"]))
    evre[0] = "başlangıca"; don(ROTA.get("bas_yon", 0.0)); p = poz_bekle(); print("başlangıçta: x %.2f z %.2f yön %.0f" % p)
    if TUR and P[0].get("tur"): tam_tur("tur: " + P[0]["ad"])
    for j in range(1, len(P)):
        a, b_ = (P[j - 1]["x"], P[j - 1]["z"]), (P[j]["x"], P[j]["z"])
        et = P[j].get("test", ""); hedef_no[0] = j; print("-> %d %s %s (%.2f, %.2f)" % (j, et, P[j]["ad"], b_[0], b_[1]))
        evre[0] = "don: " + et; q = poz_bekle(); don(math.degrees(math.atan2(b_[0] - q[0], b_[1] - q[1])))
        evre[0] = "git: " + et; git(a, b_)
        if TUR and P[j].get("tur"): tam_tur("tur: " + P[j]["ad"])
    evre[0] = "son"; don(ROTA.get("bas_yon", 0.0)); print("rota bitti")
except Dur as e:
    print("DURDU:", e)
except KeyboardInterrupt:
    print("DURDU: Ctrl+C")
finally:
    evre[0] = "dur"
    for _ in range(20):
        try: gonder(0, 0)
        except Dur: pass
        time.sleep(0.05)
    kayit.close()
