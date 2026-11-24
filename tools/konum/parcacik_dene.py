"""Parçacık süzgeci konumunu robot kaydında dener (PC).

Ölçüt (işaretin yüzey yönüne güvenmeden): işaret görülen her karede, süzgecin o kareden ÖNCEKİ tahmininden işaretin
robota göre beklenen görülme yönü ve uzaklığı hesaplanır; görüntüde ölçülen (yön ±1,5 derece, uzaklık ±2 cm) ile
farkı alınır. İşaretsiz çeşitlerde işaret yalnız başlangıçta (ve uzun aradan sonra) kullanılır.

Kullanım: python parcacik_dene.py <kök> <robot_kaydı> <zemin.json> [çeşit ...]
çeşitler: hareket, hareket+sinir, hareket+isaret, hepsi
"""
import json, math, os, sys, time
import cv2
import numpy as np

KOK, K, ZJ = sys.argv[1:4]
CESIT = sys.argv[4:] or ["hareket", "hareket+sinir"]
sys.path.insert(0, os.path.join(KOK, "robot", "jetson", "konum"))
import teta, zemin_izi, sinir, parcacik

R = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
EV = {int(m[0]): (m[1], m[2], m[3]) for m in json.load(open(os.path.join(K, "ev.json")))["isaretler"]}
H = sinir.Harita(ZJ)
GY = {}
if os.path.exists(os.path.join(K, "gercek_yon.json")):
    _g = json.load(open(os.path.join(K, "gercek_yon.json"))); GY = dict(zip(_g["kare"], _g["yaw"]))
S = sinir.Sinir(teta)
DONUS_K, HIZ_K = 140.0, 0.45


def onisle():
    """Her kare için hareket (pusula + zemin izi, konum.py ile aynı kural) ve zemin sınırı tablosu."""
    pus = teta.Pusula(); izi = zemin_izi.ZeminIzi(); son_t = None; out = []; onceki = (0, 0)
    for f in R:
        bgr = cv2.imread(os.path.join(K, f["jpg"])); gri = cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY)
        t = f["t"]; dt = 0.0 if son_t is None else max(0.0, min(1.0, t - son_t)); ara = son_t is not None and t - son_t > 10
        son_t = t
        sol, sag = f["sol"], f["sag"]
        # goruntu komuttan ~1 kare gec (25 Kasim, jiroskopla olculdu): hareket onceki karenin komutuyla
        ksol, ksag = onceki; onceki = (sol, sag)
        komut = DONUS_K * (ksol - ksag) / 510.0 * dt
        d, guven = pus.guncelle(gri, komut)
        od = izi.guncelle(gri)
        d, yg = konum_donus(d, guven, od, komut)
        ileri, yan = (od[0], od[1]) if od else (HIZ_K * (ksol + ksag) / 510.0 * dt, 0.0)
        sol, sag = ksol, ksag
        G = S.hazirla(bgr).copy(); gec = S.gecerli.copy()
        out.append(dict(d=math.radians(d), yg=yg, ileri=ileri, yan=yan, dur=sol == 0 and sag == 0, G=G, gec=gec, ara=ara,
                        donen=abs(sol - sag) > 100 and sol * sag < 0))
    return out


def konum_donus(d, guven, od, komut):
    """Kare başı dönüş kaynağı seçimi (25 Kasım, jiroskop dayanağıyla seçildi): görsel pusula; pusula sıfıra yapıştı
    (robotun sabit parçalarının sahte tepesi, hızlı dönüşte bulanık kare) ama zemin izi ya da komut dönüyor diyorsa
    zemin izi, o da yoksa komut. -> (derece, güvenli mi)"""
    z = od[2] if od else None
    if guven < 0.08 or abs(d) > 60:
        return (z, True) if z is not None else (komut, False)
    if abs(d) < 1.0 and ((z is not None and abs(z) > 4) or abs(komut) > 4):
        return (z, True) if z is not None else (komut, False)
    return d, True


def tek_poz(g):
    E = EV[g[0]]; yaw = E[2] - g[3]
    c, s = math.cos(yaw), math.sin(yaw)
    return E[0] - (g[1] * c + g[2] * s), E[1] - (-g[1] * s + g[2] * c), yaw


def calistir(O, cesit, tohum=0):
    P = parcacik.Parcacik(H, tohum=tohum)
    kullan_isaret = "isaret" in cesit or cesit == "hepsi"
    kullan_sinir = "sinir" in cesit or cesit == "hepsi"
    hata = []; yh = []; t0 = time.time()
    for i, (f, o) in enumerate(zip(R, O)):
        gor = [g for g in f["gorulen"] if g[0] in EV and math.hypot(g[1], g[2]) < 2.5]
        if (not P.var or o["ara"]) and gor:
            x, z, yaw = tek_poz(min(gor, key=lambda g: math.hypot(g[1], g[2])))
            P.baslat(x, z, yaw, 0.15, math.radians(15)); continue
        if not P.var:
            continue
        P.hareket(o["ileri"], o["yan"], o["d"], o["yg"], o["dur"])
        P.harita()
        if gor:
            # ölçüt: güncellemeden önce
            x, z, yaw, sp, sy = P.ozet()
            for g in gor:
                E = EV[g[0]]; dx, dz = E[0] - x, E[1] - z
                c, s = math.cos(-yaw), math.sin(-yaw); lx = dx * c + dz * s; lz = -dx * s + dz * c
                hy = math.degrees((math.atan2(lx, lz) - math.atan2(g[1], g[2]) + math.pi) % (2 * math.pi) - math.pi)
                hu = math.hypot(lx, lz) - math.hypot(g[1], g[2])
                hata.append((i, hy, hu, sp, math.degrees(sy)))
        if kullan_sinir and not o["donen"]:
            S.G, S.gecerli = o["G"], o["gec"]
            P.sinir(S)
        if kullan_isaret:
            for g in gor:
                P.isaret(EV[g[0]], g[1], g[2], g[3], math.hypot(g[1], g[2]))
        P.yeniden()
        if i in GY:
            yaw_d = math.degrees(P.ozet()[2])
            yh.append((i, (yaw_d - GY[i] + 180) % 360 - 180))
    a = np.array(hata)
    calistir.yon = np.array(yh)
    return a, time.time() - t0


if __name__ == "__main__":
    t0 = time.time(); O = onisle(); print("onisleme %.0f sn, %d kare" % (time.time() - t0, len(O)))
    for c in CESIT:
        sonuc = []; yons = []
        for tohum in range(3):
            a, sure = calistir(O, c, tohum); sonuc.append(a); yons.append(calistir.yon)
        a = np.vstack(sonuc)
        if GY:
            y = np.abs(np.vstack(yons)[:, 1])
            print("%-16s GERCEK YON (jiro) hatasi: ortanca %.1f  %%90 %.1f  en cok %.1f der" % (c, np.median(y), np.percentile(y, 90), y.max()))
        ay = np.abs(a[:, 1]); au = np.abs(a[:, 2])
        print("%-16s olcum %3d | gorulme yonu hatasi ortanca %5.1f  %%90 %5.1f der | uzaklik hatasi ortanca %.2f %%90 %.2f m | 10 der + 20 cm icinde %%%d | sure %.1f ms/kare"
              % (c, len(a) // 3, np.median(ay), np.percentile(ay, 90), np.median(au), np.percentile(au, 90),
                 100 * np.mean((ay < 10) & (au < 0.2)), 1000 * sure / len(R)))
