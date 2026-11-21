"""Robotun evdeki konumu (Jetson; Python 3.6).

Kaynaklar:
  - ArUco işareti (isaret.py): evdeki yeri telefonla kaydedilmiş bir işaret 2,2 m içinde görülünce kesin konum + yön.
  - Görsel pusula (teta.Pusula): kareden kareye dönüş; durağanken kayma çok küçük (14 Kasım kaydında 150 sn'de <1 derece).
    Güveni düşük adımda (bozuk kare) motor komutundan dönüş tahmini kullanılır.
  - Motor komutu: ileri hız tahmini (sol+sag)/2 ile; işaretler arası kaba. İki işaret düzeltmesi arasında katsayı
    kendini ayarlar (gidilen yol / tahmin edilen yol).
Telefon evin işaretlerini (ev koordinatında id, x, z, aci) UDP "ev" paketiyle bildirir. Konum her durum cevabına eklenir.

Ev koordinatı: telefondaki Ev ile aynı (y=0 zemin, x sağ, z ileri; aci = +z'den +x'e, radyan).
"""
import json
import math
import os
import threading
import time

import cv2
import numpy as np

import hafiza
import isaret
import teta

ARALIK = 0.2               # sn: 5 Hz
ILK_MENZIL = 1.0           # m: bu kadar yakin isaretten ilk yon/konum hemen alinir (PC testi: 0,9 m icinde <5 cm, <3 derece)
ISARET_MENZIL = 2.5        # m: daha uzaktaki isaret kullanilmaz
ILK_GOZLEM = 5             # uzaktan ilk konum: son ILK_SURE sn'de bu kadar tutarli (pusulayla dengelenmis) yon gozlemi
ILK_SURE = 4.0
ILK_SAPMA = 8.0            # derece: gozlemlerin ortalamadan en buyuk sapmasi
HAFIZA_KAYIT_MENZIL = 1.5  # m: yalniz bu kadar yakin, belirsiz olmayan isaretle duzeltilen konumda anahtar kare saklanir
HAFIZA_YEREL = 0.6         # m: isaret yokken tahmini konumun bu cevresindeki anahtar karelerle eslesir
HAFIZA_YEREL_ESIK = 0.25   # benzerlik
HAFIZA_GENEL_ESIK = 0.45   # hic bilinmiyorken yalniz cok guclu ve tek anlamli eslesmeyle yer bulunur
DUVAR_PAYI = 0.15          # m: robot isaretin arkasinda (duvarin icinde) olamaz
HIZ_TAM = 0.45             # m/s: PWM 255'te ileri hız ilk tahmini (CLAUDE.md: ~1-2 km/s)
DONUS_TAM = 140.0          # derece/s: (sol-sag)=510'da donus ilk tahmini (21 Kasim kaydi: fark 408'de ~110 derece/s); surerken ogrenir


def _dondur(x, z, aci):
    c, s = math.cos(aci), math.sin(aci)
    return x * c + z * s, -x * s + z * c


class Poz2:
    """Telefondaki Poz2 ile aynı: yerel (lx, lz) -> dış."""

    def __init__(self, x, z, aci):
        self.x, self.z, self.aci = x, z, aci

    def uygula(self, lx, lz):
        dx, dz = _dondur(lx, lz, self.aci)
        return self.x + dx, self.z + dz

    def ters(self):
        c, s = math.cos(self.aci), math.sin(self.aci)
        return Poz2(-(self.x * c - self.z * s), -(self.x * s + self.z * c), -self.aci)

    def sonra(self, b):
        x, z = self.uygula(b.x, b.z)
        return Poz2(x, z, self.aci + b.aci)


def _aci_fark(a, b):
    return (a - b + math.pi) % (2 * math.pi) - math.pi


class KonumBulucu:
    def __init__(self, kamera=None, motor=None, hafiza_klasoru=None):
        """kamera: sonraki_kare(sayac) -> (sayac, jpeg) veren nesne; motor: () -> (sol, sag) PWM.
        hafiza_klasoru: anahtar karelerin saklandığı yer (None: yalnız bellekte; PC testi)."""
        self.kamera, self.motor = kamera, motor
        self.kilit = threading.Lock()
        self.ev = {}               # id -> Poz2 (işaret yerel -> ev)
        self.x = self.z = self.yaw = 0.0
        self.bilinen = False
        self.son_isaret = 0.0
        self.son_isaret_id = -1
        self.gorulen = []
        self.pusula = teta.Pusula()
        self.hiz_k = HIZ_TAM
        self.donus_k = DONUS_TAM
        self._yol_tahmin = 0.0     # son işaret düzeltmesinden beri tahmin edilen yol
        self._son_fix = None       # (x, z) son düzeltmedeki konum (tahmin edilmeden önceki)
        self._aday = []            # uzaktan ilk konum icin (zaman, isaret yonu - pusula yonu)
        self.hafiza = hafiza.Hafiza(hafiza_klasoru)
        self.kaynak = ""           # son duzeltmenin kaynagi: isaret | hafiza
        self._son_hafiza = 0.0
        self.aktif = False
        self.ms = 0.0
        self.son_t = None

    # ------------------------------------------------------------ telefon
    def ev_ayarla(self, liste):
        """liste: [[id, x, z, aci], ...] (ev koordinatı). Ev değiştiyse True (hafıza o evinkine geçer)."""
        yeni = {}
        for e in liste or []:
            try:
                yeni[int(e[0])] = Poz2(float(e[1]), float(e[2]), float(e[3]))
            except (TypeError, ValueError, IndexError):
                continue
        with self.kilit:
            degisti = hafiza.ev_kodu({i: (p.x, p.z, p.aci) for i, p in yeni.items()}) !=                 hafiza.ev_kodu({i: (p.x, p.z, p.aci) for i, p in self.ev.items()})
            if degisti:
                print("[konum] ev işaretleri:", sorted(yeni))
            self.ev = yeni
        if degisti:
            self.hafiza.ev_ayarla({i: (p.x, p.z, p.aci) for i, p in yeni.items()})
        return degisti

    def ozet(self):
        with self.kilit:
            return {"var": self.bilinen, "x": round(self.x, 3), "z": round(self.z, 3), "yaw": round(math.degrees(self.yaw), 1),
                    "isaret_sn": round(time.time() - self.son_isaret, 1) if self.son_isaret else -1,
                    "isaret": self.son_isaret_id, "gorulen": self.gorulen, "ms": round(self.ms),
                    "kaynak": self.kaynak, "hafiza": len(self.hafiza)}

    # ------------------------------------------------------------ kare işleme
    def isle(self, ham_gri, simdi=None, sol_sag=None):
        """Bir kareyi işler (PC testinde de doğrudan çağrılır)."""
        t0 = time.time()
        simdi = simdi if simdi is not None else t0
        dt = 0.0 if self.son_t is None else max(0.0, min(1.0, simdi - self.son_t))
        self.son_t = simdi
        sol, sag = sol_sag if sol_sag is not None else (self.motor() if self.motor else (0, 0))
        # dönüş: görsel pusula, güvenilmezse komut
        komut_donus = self.donus_k * (sol - sag) / 510.0 * dt
        d_derece, guven = self.pusula.guncelle(ham_gri, komut_donus)
        # donus hizi katsayisini ogren (kesin donuste, guvenli olcumle)
        if dt > 0.05 and abs(sol - sag) > 150 and guven > 0.3 and abs(d_derece) > 3:
            olc = d_derece / dt / ((sol - sag) / 510.0)
            if 40 < olc < 400:
                self.donus_k = self.donus_k * 0.9 + olc * 0.1
        if guven < 0.08 or abs(d_derece) > 60.0:
            d_derece = komut_donus
        ileri = self.hiz_k * (sol + sag) / 510.0 * dt
        gorulen = isaret.bul(ham_gri)
        F = hafiza.Hafiza.ozellik(self.pusula.onceki)
        with self.kilit:
            self.yaw += math.radians(d_derece)
            dx, dz = math.sin(self.yaw) * ileri, math.cos(self.yaw) * ileri
            self.x += dx; self.z += dz; self._yol_tahmin += abs(ileri)
            # işaret düzeltmesi: evde kayıtlı, menzil içindeki en yakın işaret
            en = None
            ilk = not self.bilinen or simdi - self.son_isaret > 20.0
            for g in gorulen:
                if g["id"] in self.ev and g["mesafe"] < ISARET_MENZIL and (en is None or g["mesafe"] < en["mesafe"]):
                    en = g
            if en is not None:
                E = self.ev[en["id"]]
                S = Poz2(en["x"], en["z"], en["aci"])          # işaret yerel -> robot
                robot_ev = E.sonra(S.ters())                     # robot -> ev (işaretin yönüyle)
                belirsiz = en.get("belirsiz", False)
                # İşaretin YERİ uzakta da doğru (1,5 m'de 3,7 cm), baktığı YÖN uzakta gürültülü. Bu yüzden yön
                # işaretten yalnız yavaşça (ya da ilk konumda), konum ise pusulayla izlenen yön + işaretin göreli yerinden.
                fark = _aci_fark(robot_ev.aci, self.yaw)
                if ilk:
                    w_yaw = None
                    if en["mesafe"] < ILK_MENZIL and not belirsiz:
                        w_yaw = 1.0
                    elif not belirsiz:
                        # uzaktan: robot dururken ya da dönerken birkaç gözlem pusulayla aynı yönü gösterirse
                        self._aday = [a for a in self._aday if simdi - a[0] < ILK_SURE] + [(simdi, fark)]
                        if len(self._aday) >= ILK_GOZLEM:
                            c = sum(math.cos(a[1]) for a in self._aday); s_ = sum(math.sin(a[1]) for a in self._aday)
                            ort = math.atan2(s_, c)
                            if max(abs(_aci_fark(a[1], ort)) for a in self._aday) < math.radians(ILK_SAPMA):
                                fark, w_yaw = ort, 1.0
                    if w_yaw is None:
                        en = None
                elif belirsiz or abs(fark) > math.radians(30):
                    w_yaw = 0.0                                   # pusulayla celisen ya da belirsiz yon: yalniz konum
                else:
                    w_yaw = 0.25 if en["mesafe"] < 1.0 else (0.08 if en["mesafe"] < 1.6 else 0.02)
            if en is not None:
                self.yaw += fark * w_yaw
                rx, rz = _dondur(en["x"], en["z"], self.yaw)     # işaretin robota göre yeri, ev yönünde
                ex, ez = E.x, E.z
                yx, yz = ex - rx, ez - rz
                # robot işaretin arkasında olamaz: oraya düşüyorsa pusula yönü kaymıştır, yön işaretten güçlü alınır
                # (21 Kasım ikinci kaydı, 62. sn: 1,57 m'deki işaretle konum duvarın 20 cm arkasına düşüyordu)
                if not belirsiz and E.ters().uygula(yx, yz)[1] < DUVAR_PAYI and w_yaw < 0.6:
                    self.yaw += _aci_fark(robot_ev.aci, self.yaw) * 0.6
                    rx, rz = _dondur(en["x"], en["z"], self.yaw)
                    yx, yz = ex - rx, ez - rz
                w_poz = 1.0 if ilk else (0.7 if en["mesafe"] < 1.5 else (0.35 if en["mesafe"] < 2.0 else 0.2))
                # hız katsayısı: iki düzeltme arası gerçek yol / tahmin edilen yol
                if self._son_fix is not None and self._yol_tahmin > 0.5:
                    gercek = math.hypot(yx - self._son_fix[0], yz - self._son_fix[1])
                    oran = max(0.4, min(2.5, gercek / self._yol_tahmin))
                    self.hiz_k = self.hiz_k * 0.7 + self.hiz_k * oran * 0.3
                self.x += (yx - self.x) * w_poz; self.z += (yz - self.z) * w_poz
                self.bilinen = True; self.son_isaret = simdi; self.son_isaret_id = en["id"]
                self._son_fix = (self.x, self.z); self._yol_tahmin = 0.0; self._aday = []
                self.kaynak = "isaret"
                if en["mesafe"] < HAFIZA_KAYIT_MENZIL and not belirsiz:
                    self.hafiza.ekle(F, self.x, self.z, self.yaw)
            elif simdi - self._son_hafiza > 1.0 and len(self.hafiza):
                self._son_hafiza = simdi
                self._hafizadan(F, simdi)
            self.gorulen = [[g["id"], round(g["mesafe"], 2), round(g["yon"], 1)] for g in gorulen]
        self.ms = (time.time() - t0) * 1000.0
        return gorulen

    def _hafizadan(self, F, simdi):
        """İşaret görünmüyorken anahtar karelerle düzeltme (kilit içinde çağrılır).

        Tek odada 360 derece şerit eşleşmesi yeri iyi ayırt etmiyor (21 Kasım kayıtları: yanlış eşleşmenin benzerliği
        doğrusundan yüksek olabiliyor), bu yüzden temkinli: bilinen konumda yalnız yakın çevredeki karelerle ve hafifçe;
        hiç bilinmiyorken yalnız çok güçlü ve tek anlamlı eşleşmeyle."""
        if self.bilinen:
            if simdi - self.son_isaret < 3.0:
                return
            m = self.hafiza.eslestir(F, (self.x, self.z), HAFIZA_YEREL)
            if not m or m[0][0] < HAFIZA_YEREL_ESIK:
                return
            s, mx, mz, myaw = m[0]
            fark = _aci_fark(myaw, self.yaw)
            if abs(fark) > math.radians(20):
                return
            self.yaw += fark * 0.1
            self.x += (mx - self.x) * 0.1; self.z += (mz - self.z) * 0.1
            self.kaynak = "hafiza"
            return
        m = self.hafiza.eslestir(F)
        if not m or m[0][0] < HAFIZA_GENEL_ESIK:
            return
        s, mx, mz, myaw = m[0]
        ikinci = next((q[0] for q in m[1:] if math.hypot(q[1] - mx, q[2] - mz) > 0.8), 0.0)
        if ikinci > 0.7 * s:
            return
        self.x, self.z, self.yaw = mx, mz, myaw
        self.bilinen = True; self.kaynak = "hafiza"
        print("[konum] hafizadan yer bulundu: (%.2f, %.2f) benzerlik %.2f" % (mx, mz, s))

    # ------------------------------------------------------------ kayıt (PC'de tekrar oynatmak için)
    KAYIT_ANAHTARI = os.path.expanduser("~/robot/konum_kayit_ac")

    def _kaydet(self, jpeg, t, sol_sag, gorulen):
        """~/robot/konum_kayit_ac dosyası varken işlenen her kare + motor + sonuç ~/konum_kayit/<zaman>/ altına yazılır."""
        if not os.path.exists(self.KAYIT_ANAHTARI):
            self._kayit = None
            return
        if getattr(self, "_kayit", None) is None:
            k = os.path.expanduser(time.strftime("~/konum_kayit/%Y%m%d_%H%M%S"))
            os.makedirs(k, exist_ok=True)
            with open(os.path.join(k, "ev.json"), "w") as f:
                json.dump({"isaretler": [[i, p.x, p.z, p.aci] for i, p in self.ev.items()]}, f)
            self._kayit = [k, 0, open(os.path.join(k, "kareler.jsonl"), "a")]
            print("[konum] kayıt başladı:", k)
        k, n, f = self._kayit
        ad = "k%05d.jpg" % n
        with open(os.path.join(k, ad), "wb") as g:
            g.write(jpeg)
        o = self.ozet()
        f.write(json.dumps({"t": t, "jpg": ad, "sol": sol_sag[0], "sag": sol_sag[1], "x": o["x"], "z": o["z"], "yaw": o["yaw"],
                            "gorulen": [[g2["id"], g2["x"], g2["z"], g2["aci"]] for g2 in gorulen]}) + "\n")
        f.flush()
        self._kayit[1] = n + 1

    # ------------------------------------------------------------ Jetson döngüsü
    def baslat(self, etkin_mi):
        """etkin_mi(): konum istenen bir istemci var mı (yoksa kamera açık tutulmaz)."""
        def dongu():
            sayac = -1; izliyor = False; son_kayit = time.time()
            while True:
                try:
                    if time.time() - son_kayit > 30.0:
                        son_kayit = time.time(); self.hafiza.kaydet()
                    if not etkin_mi():
                        if izliyor:
                            self.kamera.izle(-1); izliyor = False
                        self.son_t = None
                        time.sleep(0.5); continue
                    if not izliyor:
                        self.kamera.izle(1); izliyor = True
                    bas = time.time()
                    sayac, jpeg = self.kamera.sonraki_kare(sayac, 1.0)
                    if jpeg is None:
                        continue
                    gri = cv2.imdecode(np.frombuffer(jpeg, np.uint8), cv2.IMREAD_GRAYSCALE)
                    if gri is None or gri.shape[1] != teta.GEN:
                        continue
                    sol_sag = self.motor() if self.motor else (0, 0)
                    t_kare = time.time()
                    gorulen = self.isle(gri, t_kare, sol_sag)
                    self._kaydet(jpeg, t_kare, sol_sag, gorulen)
                    time.sleep(max(0.0, ARALIK - (time.time() - bas)))
                except Exception as e:          # konum hatası sürüşü etkilemesin
                    print("[konum] hata:", repr(e)); time.sleep(1.0)
        threading.Thread(target=dongu, daemon=True).start()
