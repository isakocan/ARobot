"""Kayıt biçimi v1 okuyucu ve yazıcı (sim ve robot kayıtları).

<kayit_klasoru>/
  bilgi.json            {"bicim":1,"kaynak":"sim"|"robot","ad":..,"kamera":{"tur":"esdikdortgen"|"cift_balikgozu",
                         "genislik":..,"yukseklik":..,"fps":15},"kamera_yuksekligi_m":..}
  kareler/000000.jpg .. (A: tek tek JPEG)   YA DA   kareler.mjpg (B: JPEG baytları art arda)
  kareler.csv           kare,t_ms   (B'de ek: ,ofset,bayt)
  komutlar.csv          t_ms,sol,sag (PWM -255..255, ileri +; sonrasındaki fazla sütunlar yok sayılır)
  gercek_konum.csv      kare,t_ms,x_m,y_m,yon_derece   (x doğu, y kuzey, yön kuzeyden saat yönünde 0..360)
  gercek_harita.png/.json, gercek_mesafe_zemin.csv, gercek_mesafe_duvar.csv   (yalnız sim)
Sözleşmeler: t_ms tam sayı ms (kayıt başından). Eşdikdörtgende orta sütun = robot ilerisi, sütunlar sağa artar.
Robota göre azimut k (mesafe dosyalarında): 0 = ileri, saat yönünde artar."""
import bisect
import csv
import json
import os
import shutil

import cv2
import numpy as np


def _csv_oku(yol):
    with open(yol, newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


class HaritaIzgara:
    """gercek_harita.png: 0 = dolu, 255 = boş zemin, 128 = dışarı (kuzey yukarı, satır 0 = en kuzey).
    Sim json biçimi: hucre_m, x0_m, y0_m (sol-ALT köşenin dünya konumu), genislik, yukseklik;
      x = x0_m + (sutun+0.5)*hucre_m,  y = y0_m + (yukseklik-1-satir+0.5)*hucre_m   (hücre merkezi).
    Eski/başka biçim (origin + sutun_yonu/satir_yonu) yedek olarak desteklenir. Piksel koordinatı = hücre merkezi tam sayıda."""

    def __init__(self, resim, bilgi):
        self.resim = resim
        self.bilgi = bilgi
        self.hucre = float(bilgi.get("hucre_m", 0.05))
        H = resim.shape[0] if resim is not None else int(bilgi.get("yukseklik", 0))
        if "x0_m" in bilgi and "y0_m" in bilgi:
            self.x_c0 = float(bilgi["x0_m"]) + 0.5 * self.hucre             # 0. sütun merkezinin x'i
            self.y_r0 = float(bilgi["y0_m"]) + (H - 0.5) * self.hucre       # 0. satır merkezinin y'si
            self.sx, self.sy = 1.0, -1.0                                    # sütun doğuya, satır güneye artar
        else:
            o = bilgi.get("origin", bilgi.get("orijin", [0.0, 0.0]))
            if isinstance(o, dict):
                o = [o.get("x_m", o.get("x", 0.0)), o.get("y_m", o.get("y", 0.0))]
            self.x_c0, self.y_r0 = float(o[0]), float(o[1])
            self.sx = 1.0 if str(bilgi.get("sutun_yonu", "dogu")).lower().startswith("d") else -1.0
            self.sy = -1.0 if str(bilgi.get("satir_yonu", "guney")).lower().startswith("g") else 1.0

    def dunya_to_piksel(self, x, y):
        s = (np.asarray(x, float) - self.x_c0) / (self.hucre * self.sx)
        r = (np.asarray(y, float) - self.y_r0) / (self.hucre * self.sy)
        return s, r

    def piksel_to_dunya(self, s, r):
        return self.x_c0 + np.asarray(s, float) * self.hucre * self.sx, self.y_r0 + np.asarray(r, float) * self.hucre * self.sy

    def kapsam(self):
        """Görüntünün dünya kapsamı (x_sol, x_sag, y_alt, y_ust), hücre kenarlarına göre."""
        H, W = self.resim.shape[:2]
        xa, ya = self.piksel_to_dunya(-0.5, -0.5)
        xb, yb = self.piksel_to_dunya(W - 0.5, H - 0.5)
        return float(min(xa, xb)), float(max(xa, xb)), float(min(ya, yb)), float(max(ya, yb))

    def serbest(self, x, y):
        """(x, y) dünya noktası boş zeminde mi (255)?"""
        s, r = self.dunya_to_piksel(x, y)
        s = int(np.round(s)); r = int(np.round(r))
        H, W = self.resim.shape[:2]
        return 0 <= s < W and 0 <= r < H and self.resim[r, s] == 255


class Kayit:
    def __init__(self, klasor):
        self.klasor = klasor
        with open(os.path.join(klasor, "bilgi.json"), encoding="utf-8") as f:
            self.bilgi = json.load(f)
        self._kareler = self._kare_dizini()
        self._komut_t = self._komut_sol = self._komut_sag = None
        self._komutlari_yukle()
        self._gk = None

    # ---- kareler ---------------------------------------------------------------
    def _kare_dizini(self):
        """[(kare_no, t_ms, kaynak)] kaynak: dosya yolu ya da (ofset, bayt)."""
        k = self.klasor
        csv_yol = os.path.join(k, "kareler.csv")
        satirlar = _csv_oku(csv_yol) if os.path.exists(csv_yol) else None
        mjpg = os.path.join(k, "kareler.mjpg")
        if os.path.exists(mjpg):
            self._mjpg = mjpg
            if satirlar and "ofset" in satirlar[0] and "bayt" in satirlar[0]:
                return [(int(s["kare"]), int(float(s["t_ms"])), (int(s["ofset"]), int(s["bayt"]))) for s in satirlar]
            # ofset yok: JPEG başlangıç/bitiş imlerinden böl
            with open(mjpg, "rb") as f:
                veri = f.read()
            konumlar = []
            i = veri.find(b"\xff\xd8\xff")
            while i >= 0:
                konumlar.append(i)
                i = veri.find(b"\xff\xd8\xff", i + 3)
            konumlar.append(len(veri))
            fps = float((self.bilgi.get("kamera") or {}).get("fps", 15))
            sonuc = []
            for n in range(len(konumlar) - 1):
                t = int(float(satirlar[n]["t_ms"])) if satirlar and n < len(satirlar) else int(round(n * 1000 / fps))
                sonuc.append((n, t, (konumlar[n], konumlar[n + 1] - konumlar[n])))
            return sonuc
        klasor = os.path.join(k, "kareler")
        if not os.path.isdir(klasor):
            if satirlar:      # görüntüsüz kayıt (kareler henüz yazılmamış): zaman ve gerçek veri okunabilir
                return [(int(r["kare"]), int(float(r["t_ms"])), None) for r in satirlar]
            return []
        dosyalar = sorted(d for d in os.listdir(klasor) if d.lower().endswith((".jpg", ".jpeg", ".png")))
        fps = float((self.bilgi.get("kamera") or {}).get("fps", 15))
        zaman = {}
        if satirlar:
            for s in satirlar:
                zaman[int(s["kare"])] = int(float(s["t_ms"]))
        sonuc = []
        for d in dosyalar:
            n = int(os.path.splitext(d)[0])
            sonuc.append((n, zaman.get(n, int(round(n * 1000 / fps))), os.path.join(klasor, d)))
        return sonuc

    def __len__(self):
        return len(self._kareler)

    @property
    def kare_sayisi(self):
        return len(self._kareler)

    @property
    def t_ms(self):
        return np.array([t for _, t, _ in self._kareler], dtype=np.int64)

    @property
    def kare_numaralari(self):
        return np.array([n for n, _, _ in self._kareler], dtype=np.int64)

    def jpeg_bayt(self, sira):
        """sira: 0..kare_sayisi-1 (kare numarası değil, sıra). Ham dosya baytları."""
        kaynak = self._kareler[sira][2]
        if isinstance(kaynak, tuple):
            with open(self._mjpg, "rb") as f:
                f.seek(kaynak[0])
                return f.read(kaynak[1])
        with open(kaynak, "rb") as f:
            return f.read()

    def resim(self, sira):
        if self._kareler[sira][2] is None:
            return None
        b = self.jpeg_bayt(sira)
        im = cv2.imdecode(np.frombuffer(b, np.uint8), cv2.IMREAD_COLOR)
        if im is None:
            raise ValueError("kare çözülemedi: sıra %d" % sira)
        return im

    def kareler(self, bas=0, bitis=None, adim=1):
        """(kare_no, t_ms, BGR resim) üretir."""
        for i in range(bas, len(self._kareler) if bitis is None else min(bitis, len(self._kareler)), adim):
            n, t, _ = self._kareler[i]
            yield n, t, self.resim(i)

    # ---- komutlar --------------------------------------------------------------
    def _komutlari_yukle(self):
        yol = os.path.join(self.klasor, "komutlar.csv")
        if not os.path.exists(yol):
            return
        s = _csv_oku(yol)
        if not s:
            return
        self._komut_t = np.array([int(float(r["t_ms"])) for r in s], dtype=np.int64)
        self._komut_sol = np.array([float(r["sol"]) for r in s])
        self._komut_sag = np.array([float(r["sag"]) for r in s])

    def komut(self, t_ms):
        """Sıfırıncı derece tutma: t'den önceki ya da t'deki son komut (sol, sag). Önceki yoksa (0, 0)."""
        if self._komut_t is None:
            return 0.0, 0.0
        i = bisect.bisect_right(self._komut_t, t_ms) - 1
        if i < 0:
            return 0.0, 0.0
        return float(self._komut_sol[i]), float(self._komut_sag[i])

    @property
    def komut_dizisi(self):
        """(t_ms, sol, sag) dizileri ya da None."""
        if self._komut_t is None:
            return None
        return self._komut_t, self._komut_sol, self._komut_sag

    # ---- gerçek veri -----------------------------------------------------------
    def gercek_konum(self):
        """dict(kare, t_ms, x_m, y_m, yon_derece) numpy dizileri, yoksa None."""
        if self._gk is None:
            yol = os.path.join(self.klasor, "gercek_konum.csv")
            if not os.path.exists(yol):
                return None
            s = _csv_oku(yol)
            self._gk = {
                "kare": np.array([int(float(r["kare"])) for r in s]),
                "t_ms": np.array([int(float(r["t_ms"])) for r in s], dtype=np.int64),
                "x_m": np.array([float(r["x_m"]) for r in s]),
                "y_m": np.array([float(r["y_m"]) for r in s]),
                "yon_derece": np.array([float(r["yon_derece"]) for r in s]),
            }
        return self._gk

    def harita(self):
        """HaritaIzgara ya da None."""
        png = os.path.join(self.klasor, "gercek_harita.png")
        js = os.path.join(self.klasor, "gercek_harita.json")
        if not os.path.exists(png):
            return None
        bilgi = {}
        if os.path.exists(js):
            with open(js, encoding="utf-8") as f:
                bilgi = json.load(f)
        return HaritaIzgara(cv2.imread(png, cv2.IMREAD_GRAYSCALE), bilgi)

    def mesafe(self, tur="zemin"):
        """gercek_mesafe_<tur>.csv -> (kare_numaralari, (N,360) metre) ya da None."""
        yol = os.path.join(self.klasor, "gercek_mesafe_%s.csv" % tur)
        if not os.path.exists(yol):
            return None
        with open(yol, encoding="utf-8") as f:      # sim dosyalarında başlık satırı yok; varsa atla
            ilk = f.readline().strip()
        baslik = 0 if ilk[:1].isdigit() or ilk[:1] == "-" else 1
        a = np.loadtxt(yol, delimiter=",", skiprows=baslik)
        a = np.atleast_2d(a)
        return a[:, 0].astype(int), a[:, 1:361]


class KayitYazici:
    """Kayıt biçimi v1 yazıcısı. varyant 'A' (kareler/*.jpg) ya da 'B' (kareler.mjpg + ofset/bayt)."""

    def __init__(self, klasor, bilgi, varyant="A"):
        self.klasor = klasor
        os.makedirs(klasor, exist_ok=True)
        self.bilgi = dict(bilgi)
        self.bilgi.setdefault("bicim", 1)
        self.varyant = varyant.upper()
        self._satirlar = []
        self._n = 0
        self._mjpg = None
        self._ofset = 0
        if self.varyant == "A":
            os.makedirs(os.path.join(klasor, "kareler"), exist_ok=True)
        else:
            self._mjpg = open(os.path.join(klasor, "kareler.mjpg"), "wb")

    def kare_ekle(self, t_ms, resim_ya_da_jpeg, kalite=92):
        """resim: BGR dizi (JPEG'e kodlanır) ya da hazır JPEG baytı. Yazılan JPEG bayt sayısını döndürür."""
        if isinstance(resim_ya_da_jpeg, (bytes, bytearray)):
            b = bytes(resim_ya_da_jpeg)
        else:
            ok, kod = cv2.imencode(".jpg", resim_ya_da_jpeg, [cv2.IMWRITE_JPEG_QUALITY, int(kalite)])
            b = kod.tobytes()
        if self.varyant == "A":
            with open(os.path.join(self.klasor, "kareler", "%06d.jpg" % self._n), "wb") as f:
                f.write(b)
            self._satirlar.append((self._n, int(t_ms)))
        else:
            self._mjpg.write(b)
            self._satirlar.append((self._n, int(t_ms), self._ofset, len(b)))
            self._ofset += len(b)
        self._n += 1
        return len(b)

    def komutlar_yaz(self, t_ms, sol, sag):
        with open(os.path.join(self.klasor, "komutlar.csv"), "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f)
            w.writerow(["t_ms", "sol", "sag"])
            for t, a, b in zip(t_ms, sol, sag):
                w.writerow([int(t), "%g" % a, "%g" % b])

    def gercek_konum_yaz(self, kare, t_ms, x, y, yon):
        with open(os.path.join(self.klasor, "gercek_konum.csv"), "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f)
            w.writerow(["kare", "t_ms", "x_m", "y_m", "yon_derece"])
            for k, t, a, b, c in zip(kare, t_ms, x, y, yon):
                w.writerow([int(k), int(t), "%.5f" % a, "%.5f" % b, "%.4f" % (c % 360.0)])

    def dosya_kopyala(self, kaynak_klasor, ad):
        """Başka bir kayıttan dosyayı olduğu gibi kopyalar (yoksa False)."""
        yol = os.path.join(kaynak_klasor, ad)
        if not os.path.exists(yol):
            return False
        shutil.copyfile(yol, os.path.join(self.klasor, ad))
        return True

    def kapat(self):
        if self._mjpg:
            self._mjpg.close()
        with open(os.path.join(self.klasor, "kareler.csv"), "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f)
            w.writerow(["kare", "t_ms"] + (["ofset", "bayt"] if self.varyant == "B" else []))
            w.writerows(self._satirlar)
        with open(os.path.join(self.klasor, "bilgi.json"), "w", encoding="utf-8") as f:
            json.dump(self.bilgi, f, indent=1, ensure_ascii=False)

    def __enter__(self):
        return self

    def __exit__(self, *a):
        self.kapat()
