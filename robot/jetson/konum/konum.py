"""Robotun evdeki konumu (Jetson; Python 3.6).

Kaynaklar:
  - ArUco işareti (isaret.py): evdeki yeri telefonla kaydedilmiş bir işaret 2,2 m içinde görülünce kesin konum + yön.
  - Görsel pusula (teta.Pusula): kareden kareye dönüş; durağanken kayma çok küçük (14 Kasım kaydında 150 sn'de <1 derece).
    Güveni düşük adımda (bozuk kare) motor komutundan dönüş tahmini kullanılır.
  - Zemin izi (zemin_izi.py): robotun çevresindeki zeminin kuşbakışı görüntüsünde doku noktaları kareden kareye
    izlenir; ileri / yan hareket metre olarak (kamera yüksekliği bilindiği için) ölçülür. 22 Kasım'dan beri ileri
    hareketin asıl kaynağı; dönüşte pusula güvensizse dönüş de buradan alınır.
  - Motor komutu: zemin izi sonuç vermezse (çok hızlı dönüş, dokusuz zemin) ileri hız tahmini (sol+sag)/2 ile.
    İki işaret düzeltmesi arasında katsayılar kendini ayarlar (gidilen yol / tahmin edilen yol).
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
import zemin_izi

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
HIZ_TAM = 0.45             # m/s: PWM 255'te ileri hız ilk tahmini (CLAUDE.md: ~1-2 km/s)
DONUS_TAM = 140.0          # derece/s: (sol-sag)=510'da donus ilk tahmini (21 Kasim kaydi: fark 408'de ~110 derece/s); surerken ogrenir

# Birleştirme (24 Kasım): Kalman süzgeci. Durum (x, z, yaw) + belirsizlik (P). Hareket belirsizliği büyütür (dönüşte yön,
# gidişte konum; güvenilmez ölçümde çok), işaret küçültür. Böylece robot dururken işaretin gürültüsü konumu sürükleyemez,
# hızlı dönüşten sonra ise yön belirsiz olduğu için işaret yönü hemen düzeltir (eski kural: 30 dereceden büyük farkı hiç
# kabul etmiyordu, yön bir kez bozulunca robot işaretin önünde dururken konum 1 m kayıyordu).
S_YON = math.radians(1.5)          # işaretin robottan görülme açısı (kareden kareye 0,4 derece içinde)
S_UZAK = (0.02, 0.02)              # uzaklık: a + b * r
S_NORMAL = (math.radians(3.0), math.radians(5.0))   # işaretin baktığı yön: a + b * max(0, r - 0.8) (uzakta zayıf)
KAPI = 16.0                        # Mahalanobis² eşiği (3 serbestlik, %99,9): üstü aykırı sayılır
KILIT_GOZLEM = 3                   # aykırı ama birbiriyle tutarlı bu kadar gözlem KILIT_SURE içinde: konum yeniden kurulur
KILIT_SURE = 3.0
KILIT_SAPMA = (0.25, math.radians(12.0))   # işaretin baktığı yön 1,7 m'de kareden kareye ~9 derece oynuyor
P_TABAN = (0.015, math.radians(0.8))  # süzgeç bundan emin olmaz (işaret hataları birbirinden bağımsız değil; aşırı güven
                                      # küçük ama gerçek farkları reddettirir)


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
        self.izi = zemin_izi.ZeminIzi()
        self.olcek = 1.0           # zemin izi ölçeği (kamera yüksekliği hatası); işaret düzeltmeleriyle ayarlanır
        self.hareket = ""          # son adımda ileri hareketin kaynağı: goruntu | motor
        self.nokta = 0             # zemin izinde ortak harekete uyan nokta sayısı
        self._yol_goruntu = 0.0    # son düzeltmeden beri zemin iziyle ölçülen yol
        self.iz = []               # son konumlar (dev haritası için)
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
        self.pusula_guven = 0.0
        self.P = np.diag([1.0, 1.0, 1.0])     # belirsizlik (x, z m²; yaw rad²)
        self.kare = -1             # işlenen kamera karesinin numarası (köprünün sayacı; telefon görüntüyü bununla eşler)
        self.kare_t = 0.0
        self.hata = None           # son işaret ölçümünde düzeltmeden önceki fark: [yön derece, uzaklık m, kabul 0/1]
        self.kilit_sayisi = 0      # aykırı ama tutarlı gözlemlerle yeniden kurulma sayısı
        self._kilit = []           # (zaman, dx, dz, dyaw): aykırı gözlemlerin işaretten çıkan konumu - süzgecin konumu
        self.olcumler = []         # PC ölçüm aracı için kare başına kayıt (yalnız olcum_ac True iken)
        self.olcum_ac = False

    # ------------------------------------------------------------ telefon
    def ev_ayarla(self, liste):
        """liste: [[id, x, z, aci], ...] ya da telefonun ev.json biçimi [{"id":..,"x":..,"z":..,"aci":..}, ...]
        (ev koordinatı). Ev değiştiyse True (hafıza o evinkine geçer). Bozuk öğe atlanır, asla hata vermez
        (22 Kasım: ev.json'daki sözlük biçimi KeyError ile köprüyü açılışta çökertiyordu)."""
        yeni = {}
        for e in liste or []:
            try:
                if isinstance(e, dict):
                    yeni[int(e["id"])] = Poz2(float(e["x"]), float(e["z"]), float(e.get("aci", 0.0)))
                else:
                    yeni[int(e[0])] = Poz2(float(e[1]), float(e[2]), float(e[3]))
            except (TypeError, ValueError, IndexError, KeyError):
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
                    "kaynak": self.kaynak, "hafiza": len(self.hafiza), "hareket": self.hareket, "nokta": self.nokta,
                    "olcek": round(self.olcek, 2), "pusula": round(self.pusula_guven, 2),
                    "kare": self.kare, "sp": round(math.sqrt(max(0.0, (self.P[0, 0] + self.P[1, 1]) / 2)), 3),
                    "sy": round(math.degrees(math.sqrt(max(0.0, self.P[2, 2]))), 1),
                    "hata": self.hata or [], "kilit": self.kilit_sayisi}

    def hafiza_listesi(self):
        """Dev haritası için anahtar karelerin yerleri [[x, z, yaw_derece], ...] ve son iz."""
        with self.hafiza.kilit:
            p = [[round(float(a), 2), round(float(b), 2), round(math.degrees(float(c)))] for a, b, c in self.hafiza.poz]
        with self.kilit:
            iz = list(self.iz)
        return {"kareler": p, "iz": iz}

    # ------------------------------------------------------------ kare işleme
    def isle(self, ham_gri, simdi=None, sol_sag=None, kare=None):
        """Bir kareyi işler (PC testinde de doğrudan çağrılır). kare: köprünün kare numarası (telefon eşlemesi)."""
        t0 = time.time()
        simdi = simdi if simdi is not None else t0
        dt = 0.0 if self.son_t is None else max(0.0, min(1.0, simdi - self.son_t))
        self.son_t = simdi
        sol, sag = sol_sag if sol_sag is not None else (self.motor() if self.motor else (0, 0))
        # dönüş: görsel pusula, güvenilmezse zemin izi ya da komut
        komut_donus = self.donus_k * (sol - sag) / 510.0 * dt
        d_derece, guven = self.pusula.guncelle(ham_gri, komut_donus)
        # donus hizi katsayisini ogren (kesin donuste, guvenli olcumle)
        if dt > 0.05 and abs(sol - sag) > 150 and guven > 0.3 and abs(d_derece) > 3:
            olc = d_derece / dt / ((sol - sag) / 510.0)
            if 40 < olc < 400:
                self.donus_k = self.donus_k * 0.9 + olc * 0.1
        self.pusula_guven = guven
        od = self.izi.guncelle(ham_gri)
        self.nokta = od[3] if od else 0
        yon_guvenli = True
        if guven < 0.08 or abs(d_derece) > 60.0:
            d_derece = od[2] if od else komut_donus
            yon_guvenli = od is not None
        elif abs(sol - sag) > 100 and abs(d_derece - komut_donus) > max(10.0, 0.5 * abs(komut_donus)):
            yon_guvenli = False            # pusula komutla çelişiyor (hızlı dönüşte yanlış eşleşme olabilir): belirsiz say
        if od:
            ileri, yan = od[0] * self.olcek, od[1] * self.olcek
            self.hareket = "goruntu"
        else:
            ileri, yan = self.hiz_k * (sol + sag) / 510.0 * dt, 0.0
            self.hareket = "motor"
        gorulen = isaret.bul(ham_gri)
        F = hafiza.Hafiza.ozellik(self.pusula.onceki)
        with self.kilit:
            if kare is not None:
                self.kare, self.kare_t = int(kare), simdi
            self._tahmin(math.radians(d_derece), ileri, yan, yon_guvenli, od is not None, sol == 0 and sag == 0)
            self._yol_tahmin += math.hypot(ileri, yan)
            if od:
                self._yol_goruntu += math.hypot(ileri, yan)
            # işaret: evde kayıtlı, menzil içindeki en yakın
            en = None
            for g in gorulen:
                if g["id"] in self.ev and g["mesafe"] < ISARET_MENZIL and (en is None or g["mesafe"] < en["mesafe"]):
                    en = g
            self.hata = None
            duzeldi = False
            if en is not None:
                duzeldi = self._isaret(en, simdi)
            if duzeldi:
                yx, yz = self.x, self.z
                # hız katsayısı: iki düzeltme arası gerçek yol / tahmin edilen yol
                if self._son_fix is not None and self._yol_tahmin > 0.5 and en["mesafe"] < 1.5:
                    gercek = math.hypot(yx - self._son_fix[0], yz - self._son_fix[1])
                    oran = max(0.4, min(2.5, gercek / self._yol_tahmin))
                    if self._yol_goruntu > 0.8 * self._yol_tahmin:
                        self.olcek = max(0.7, min(1.4, self.olcek * (1.0 + 0.15 * (min(1.3, max(0.77, oran)) - 1.0))))
                    else:
                        self.hiz_k = self.hiz_k * 0.7 + self.hiz_k * oran * 0.3
                self.son_isaret = simdi; self.son_isaret_id = en["id"]
                self._son_fix = (self.x, self.z); self._yol_tahmin = 0.0; self._yol_goruntu = 0.0
                self.kaynak = "isaret"
                if en["mesafe"] < HAFIZA_KAYIT_MENZIL and not en.get("belirsiz") and self.P[2, 2] < math.radians(4) ** 2:
                    self.hafiza.ekle(F, self.x, self.z, self.yaw)
            elif en is None and simdi - self._son_hafiza > 1.0 and len(self.hafiza):
                self._son_hafiza = simdi
                self._hafizadan(F, simdi)
            # [id, uzaklık, görülme açısı, işaret merkezinin kameradan yüksekliği (m)]: telefon AR'da işaretin yerini çizer
            self.gorulen = [[g["id"], round(g["mesafe"], 2), round(g["yon"], 1), round(g["y"], 3)] for g in gorulen]
            if self.bilinen and (not self.iz or math.hypot(self.x - self.iz[-1][0], self.z - self.iz[-1][1]) > 0.05):
                self.iz.append([round(self.x, 3), round(self.z, 3)])
                del self.iz[:-400]
            if self.olcum_ac:
                self.olcumler.append({"t": simdi, "x": self.x, "z": self.z, "yaw": math.degrees(self.yaw), "var": self.bilinen,
                                      "sol": sol, "sag": sag, "hata": self.hata, "sp": math.sqrt((self.P[0, 0] + self.P[1, 1]) / 2),
                                      "sy": math.degrees(math.sqrt(self.P[2, 2])), "hareket": self.hareket, "guven": guven,
                                      "isaret": [[g["id"], g["mesafe"], g["yon"], math.degrees(g["aci"]), g.get("belirsiz", False)] for g in gorulen]})
        self.ms = (time.time() - t0) * 1000.0
        return gorulen

    # ------------------------------------------------------------ Kalman süzgeci
    def _tahmin(self, dyaw, ileri, yan, yon_guvenli, iz_var, duruyor):
        """Hareket adımı: durum ilerler, belirsizlik hareketle (ve güvenilmez ölçümde çok) büyür."""
        sn, cs = math.sin(self.yaw), math.cos(self.yaw)
        Fm = np.eye(3)
        Fm[0, 2] = cs * ileri - sn * yan
        Fm[1, 2] = -sn * ileri - cs * yan
        self.x += sn * ileri + cs * yan; self.z += cs * ileri - sn * yan
        self.yaw += dyaw
        yol = math.hypot(ileri, yan)
        if duruyor and abs(dyaw) < math.radians(0.5) and yol < 0.01:
            s_yaw, s_poz = math.radians(0.05), 0.001                   # duran robot: neredeyse hiç
        else:
            # büyük adım (hızlı dönüş, düşük kare hızı) daha belirsiz: 40 derecelik adımda ~14 derece
            s_yaw = (0.04 if yon_guvenli else 0.3) * abs(dyaw) + 0.46 * dyaw * dyaw + math.radians(0.3)
            s_poz = (0.06 if iz_var else 0.3) * yol + 0.005
        self.P = Fm @ self.P @ Fm.T + np.diag([s_poz ** 2, s_poz ** 2, s_yaw ** 2])

    def _olcum(self, s, E):
        """Durum s (x, z, yaw) iken işaret E için beklenen [görülme açısı, uzaklık, işaretin robot yönünde baktığı açı]."""
        lx, lz = _dondur(E.x - s[0], E.z - s[1], -s[2])
        return np.array([math.atan2(lx, lz), math.hypot(lx, lz), E.aci - s[2]])

    def _isaret(self, en, simdi):
        """İşaret gözlemi (kilit içinde). True: konum düzeltildi (ya da ilk kez / yeniden kuruldu)."""
        E = self.ev[en["id"]]
        belirsiz = en.get("belirsiz", False)
        r = en["mesafe"]
        zm = np.array([math.atan2(en["x"], en["z"]), r, en["aci"]])
        # yalnız işaretten çıkan konum (yönü işaretin normalinden): ilk kurulum ve yeniden kilit için
        tek = E.sonra(Poz2(en["x"], en["z"], en["aci"]).ters())
        if not self.bilinen:
            if r < ILK_MENZIL and not belirsiz:
                self._kur(tek, math.radians(3.0), 0.04, "ilk (yakin isaret)")
                return True
            if not belirsiz:
                self._kilit = [a for a in self._kilit if simdi - a[0] < ILK_SURE] + [(simdi, tek.x - self.x, tek.z - self.z, _aci_fark(tek.aci, self.yaw))]
                if self._tutarli(ILK_GOZLEM, ILK_SAPMA):
                    self._kilitle("ilk (uzak isaret, tutarli gozlemler)", en, E)
                    return True
            return False
        s = np.array([self.x, self.z, self.yaw])
        h = self._olcum(s, E)
        n = 2 if belirsiz else 3
        H = np.zeros((3, 3))
        for j in range(3):
            d = np.zeros(3); d[j] = 1e-4
            H[:, j] = (self._olcum(s + d, E) - h) / 1e-4
        y = zm - h
        y[0] = _aci_fark(y[0], 0.0); y[2] = _aci_fark(y[2], 0.0)
        Rm = np.diag([S_YON ** 2, (S_UZAK[0] + S_UZAK[1] * r) ** 2, (S_NORMAL[0] + S_NORMAL[1] * max(0.0, r - 0.8)) ** 2])
        H, y, Rm = H[:n], y[:n], Rm[:n, :n]
        S = H @ self.P @ H.T + Rm
        try:
            Si = np.linalg.inv(S)
        except np.linalg.LinAlgError:
            return False
        d2 = float(y @ Si @ y)
        self.hata = [round(math.degrees(y[0]), 2), round(float(y[1]), 3), 0]
        if d2 > KAPI:
            # süzgeç kendini fazla güvenilir sanıyor olabilir: her aykırı gözlemde belirsizliği biraz büyüt
            self.P[2, 2] += math.radians(6.0) ** 2
            self.P[0, 0] += 0.05 ** 2; self.P[1, 1] += 0.05 ** 2
            # aykırı: süzgeç kendinden çok emin ama işaret başka yer diyor. Birkaç tutarlı aykırı gözlem = süzgeç yanılmış.
            if not belirsiz:
                self._kilit = [a for a in self._kilit if simdi - a[0] < KILIT_SURE] + [(simdi, tek.x - self.x, tek.z - self.z, _aci_fark(tek.aci, self.yaw))]
                if self._tutarli(KILIT_GOZLEM, KILIT_SAPMA[1], KILIT_SAPMA[0]):
                    self.kilit_sayisi += 1
                    self._kilitle("yeniden kilit (%d aykiri gozlem, fark %.0f derece)" % (len(self._kilit), math.degrees(self._kilit[-1][3])), en, E)
                    return True
            return False
        self._kilit = []
        K = self.P @ H.T @ Si
        dx = K @ y
        self.x += dx[0]; self.z += dx[1]; self.yaw += dx[2]
        self.P = (np.eye(3) - K @ H) @ self.P
        self.P = (self.P + self.P.T) / 2
        self.P[0, 0] = max(self.P[0, 0], P_TABAN[0] ** 2); self.P[1, 1] = max(self.P[1, 1], P_TABAN[0] ** 2)
        self.P[2, 2] = max(self.P[2, 2], P_TABAN[1] ** 2)
        self.hata[2] = 1
        return True

    def _tutarli(self, adet, s_yaw, s_poz=None):
        if len(self._kilit) < adet:
            return False
        a = self._kilit[-adet:]
        c = sum(math.cos(q[3]) for q in a); s_ = sum(math.sin(q[3]) for q in a)
        ort = math.atan2(s_, c)
        if max(abs(_aci_fark(q[3], ort)) for q in a) > s_yaw:
            return False
        if s_poz is not None:
            mx = sum(q[1] for q in a) / adet; mz = sum(q[2] for q in a) / adet
            if max(math.hypot(q[1] - mx, q[2] - mz) for q in a) > s_poz:
                return False
        return True

    def _kilitle(self, neden, en=None, E=None):
        """Son tutarlı gözlemlerin ortalama yön farkı kadar döndür ve süzgeci yeniden başlat. Konum, yeni yönle şimdiki
        gözlemin açı + uzaklığından (bunlar kesin; işaretin baktığı yön uzakta gürültülü, yalnız ortalaması kullanılır)."""
        a = self._kilit[-KILIT_GOZLEM:] if self.bilinen else self._kilit[-ILK_GOZLEM:]
        n = len(a)
        c = sum(math.cos(q[3]) for q in a); s_ = sum(math.sin(q[3]) for q in a)
        yaw = self.yaw + math.atan2(s_, c)
        if en is not None:
            rx, rz = _dondur(en["x"], en["z"], yaw)
            p = Poz2(E.x - rx, E.z - rz, yaw)
        else:
            p = Poz2(self.x + sum(q[1] for q in a) / n, self.z + sum(q[2] for q in a) / n, yaw)
        self._kur(p, math.radians(4.0), 0.06, neden)

    def _kur(self, p, s_yaw, s_poz, neden):
        self.x, self.z, self.yaw = p.x, p.z, p.aci
        self.P = np.diag([s_poz ** 2, s_poz ** 2, s_yaw ** 2])
        self.bilinen = True
        self._kilit = []
        print("[konum] %s: (%.2f, %.2f) yon %.0f" % (neden, self.x, self.z, math.degrees(self.yaw)))

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
        self._kur(Poz2(mx, mz, myaw), math.radians(10.0), 0.3, "hafizadan yer bulundu (benzerlik %.2f)" % s)
        self.kaynak = "hafiza"

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
                            "kare": o["kare"], "sy": o["sy"], "sp": o["sp"], "hata": o["hata"], "kilit": o["kilit"],
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
                    gorulen = self.isle(gri, t_kare, sol_sag, sayac)
                    self._kaydet(jpeg, t_kare, sol_sag, gorulen)
                    time.sleep(max(0.0, ARALIK - (time.time() - bas)))
                except Exception as e:          # konum hatası sürüşü etkilemesin
                    print("[konum] hata:", repr(e)); time.sleep(1.0)
        threading.Thread(target=dongu, daemon=True).start()
