"""Zemin sınırı, yöntem (b): hazır (eğitilmemiş-bizim-veride) anlamsal bölütleme, SegFormer-B0 / ADE20K.

Model: nvidia/segformer-b0-finetuned-ade-512-512 (Hugging Face), olduğu gibi, bizim veriyle HİÇ eğitilmedi.
ADE20K'nın "floor" ve "rug" (+ halı/paspas benzeri) sınıfları zemin sayılır; diğer her şey (duvar, tavan, mobilya...) zemin değil.

GÖRÜNÜMLER ve NEDEN: model perspektif fotoğrafla eğitildi, panoramanın alt şeridi ise eşdikdörtgen (yatayda gerilmiş,
çizgiler eğri). Bu yüzden panoramadan yatayda 8 yönde (45 derece aralık) 90x90 derece görüş alanlı, 25 derece aşağı eğik
perspektif görünüm çıkarılır (512x512; aralık ufkun +20 .. -70 derece altı: zemin sınırının yaşadığı 0..-45 aralığı ortada,
duvar bağlamı da var). Her yönü >=2 görünüm kapsar; olasılıklar panoramaya geri aktarılıp açı kosinüsü ile ağırlıklı
ortalanır. Alternatif "doğrudan eşdikdörtgen şerit" (zemin_model.py, mod='serit') ayrıca ölçülür.
Sınır, (a) ile aynı kesim/DP (zemin_klasik.kesim_maliyeti, dairesel_dp) ya da harfi harfine "dıştan ilk zemin-dışı" ile çıkarılır.
Gövde maskesi (theta): maskeli pikseller sütun boyunca üstteki son geçerli pikselle doldurulur (model siyah lekeyi engel sanmasın),
sınır kararında maskeli pikseller sayılmaz."""
import os
import time

import cv2
import numpy as np

import zemin_ortak as zo
import zemin_klasik as zk

MODEL_ADI = "nvidia/segformer-b0-finetuned-ade-512-512"
ZEMIN_ADLARI = ("floor", "rug", "carpet", "mat")    # ADE20K etiket adları (alt dize eşleşmesi)

GORUNUM = dict(n_yon=8, fov=90.0, boyut=512, egim=25.0)


def _R_yaw_pitch(yaw, pitch):
    """Görünüm kamerası (x sağ, y yukarı, z ileri) -> robot ışını. yaw: saat yönü derece; pitch: aşağı +."""
    y, p = np.radians(yaw), np.radians(pitch)
    Ry = np.array([[np.cos(y), 0, np.sin(y)], [0, 1, 0], [-np.sin(y), 0, np.cos(y)]])
    Rx = np.array([[1, 0, 0], [0, np.cos(p), np.sin(p)], [0, -np.sin(p), np.cos(p)]])   # aşağı eğ: z ileri -> y aşağı
    return Ry @ Rx


class Bolutleyici:
    def __init__(self, cihaz=None, yari_hassasiyet=True):
        import torch
        from transformers import SegformerForSemanticSegmentation
        self.torch = torch
        self.cihaz = cihaz or ("cuda" if torch.cuda.is_available() else "cpu")
        self.model = SegformerForSemanticSegmentation.from_pretrained(MODEL_ADI).to(self.cihaz).eval()
        self.yari = bool(yari_hassasiyet) and self.cihaz == "cuda"      # yarım duyarlık yalnız GPU'da; sonuç farkı zemin_gpu_olc ile ölçüldü
        if self.yari:
            self.model = self.model.half()
        id2 = self.model.config.id2label
        self.zemin_idx = [int(i) for i, n in id2.items() if any(z in n.lower().split(", ")[0] or z in n.lower() for z in ZEMIN_ADLARI)
                          and "ceiling" not in n.lower()]
        self.id2 = id2
        self.ort = np.array([0.485, 0.456, 0.406], np.float32)
        self.std = np.array([0.229, 0.224, 0.225], np.float32)
        self._tablolar = {}

    # ---- görünüm tabloları
    def _tablo(self, yuk, gen, gor):
        anahtar = (yuk, gen, tuple(sorted(gor.items())))
        if anahtar in self._tablolar:
            return self._tablolar[anahtar]
        n, fov, B, egim = gor["n_yon"], gor["fov"], gor["boyut"], gor["egim"]
        f = (B / 2.0) / np.tan(np.radians(fov / 2))
        uu, vv = np.meshgrid(np.arange(B) + 0.5 - B / 2, np.arange(B) + 0.5 - B / 2)
        kam_isin = np.stack([uu, -vv, np.full_like(uu, f)], -1)
        kam_isin /= np.linalg.norm(kam_isin, axis=-1, keepdims=True)
        gorunumler = []
        for i in range(n):
            yaw = i * 360.0 / n
            R = _R_yaw_pitch(yaw, egim)
            d = kam_isin @ R.T                       # robot ışını
            u, v = _isin_esd_piksel(d, gen, yuk)
            gorunumler.append(dict(yaw=yaw, R=R, mapx=u.astype(np.float32), mapy=v.astype(np.float32)))
        # geri aktarım: eşdikdörtgen alt yarı piksel merkezi ışını -> her görünümde (x, y) + ağırlık
        v0 = yuk // 2
        el = np.radians(90.0 - (np.arange(v0, yuk) + 0.5) / yuk * 180.0)
        az = np.radians((np.arange(gen) + 0.5) / gen * 360.0 - 180.0)
        AZ, EL = np.meshgrid(az, el)
        d = np.stack([np.cos(EL) * np.sin(AZ), np.sin(EL), np.cos(EL) * np.cos(AZ)], -1)
        geri = []
        for g_ in gorunumler:
            c = d @ g_["R"]                          # görünüm kamerası koordinatı (R ortogonal)
            z = c[..., 2]
            with np.errstate(divide="ignore", invalid="ignore"):
                x = f * c[..., 0] / z + B / 2.0
                y = -f * c[..., 1] / z + B / 2.0
            icinde = (z > 0.05) & (x > 4) & (x < B - 4) & (y > 4) & (y < B - 4)
            ag = np.where(icinde, (z ** 4), 0.0).astype(np.float32)      # kenara yaklaştıkça sönen ağırlık
            kenar = np.minimum(np.minimum(x, B - x), np.minimum(y, B - y)) / (B * 0.12)
            ag *= np.clip(kenar, 0, 1).astype(np.float32)
            geri.append((np.nan_to_num(x).astype(np.float32), np.nan_to_num(y).astype(np.float32), ag))
        self._tablolar[anahtar] = (gorunumler, geri)
        return self._tablolar[anahtar]

    def _tahmin(self, goruntuler, parca=2):
        """goruntuler (N,B,B,3) BGR uint8 -> zemin olasılığı (N,B,B) float32. VRAM için `parca` görüntülük gruplar halinde."""
        torch = self.torch
        sonuc = []
        for i in range(0, len(goruntuler), parca):
            g = goruntuler[i:i + parca]
            x = g[..., ::-1].astype(np.float32) / 255.0
            x = (x - self.ort) / self.std
            t = torch.from_numpy(np.ascontiguousarray(x.transpose(0, 3, 1, 2))).to(self.cihaz)
            if self.yari:
                t = t.half()
            with torch.no_grad():
                lg = self.model(pixel_values=t).logits
                lg = torch.nn.functional.interpolate(lg.float(), size=g.shape[1:3], mode="bilinear", align_corners=False)
                sonuc.append(lg.softmax(1)[:, self.zemin_idx].sum(1).cpu().numpy())
        return np.concatenate(sonuc)

    def zemin_olasiligi(self, g, gor=GORUNUM, mod="gorunum"):
        """Girdi -> P (H2, 360) zemin olasılığı (alt yarı), gec (H2,360)."""
        H, G = g.yuk, g.gen
        v0 = H // 2
        pan = _govdeyi_doldur(g.pan, g.gecersiz)
        if mod == "gorunum":
            gorunumler, geri = self._tablo(H, G, gor)
            goruntuler = np.stack([cv2.remap(pan, gv["mapx"], gv["mapy"], cv2.INTER_LINEAR, borderMode=cv2.BORDER_WRAP)
                                   for gv in gorunumler])
            pr = self._tahmin(goruntuler)
            top = np.zeros((H - v0, G), np.float32)
            agt = np.zeros_like(top)
            for pi, (x, y, ag) in zip(pr, geri):
                top += cv2.remap(pi, x, y, cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE) * ag
                agt += ag
            P = top / np.maximum(agt, 1e-6)
            P[agt < 1e-4] = 0.5
        else:   # "sentez": eşdikdörtgen şeridi (ufkun 20 derece üstü .. alt) doğrudan bölütle
            ust = int(round(H * 20.0 / 180.0))
            seri = pan[v0 - ust:]
            hh = int(np.ceil(seri.shape[0] * 2.0 / 32.0) * 32)       # model 32 katı ister
            ww = int(np.ceil(G / 32.0) * 32)
            seri = cv2.resize(seri, (ww, hh), interpolation=cv2.INTER_LINEAR)
            pr = self._tahmin(seri[None])[0]
            pr = cv2.resize(pr, (G, H - v0 + ust), interpolation=cv2.INTER_LINEAR)[ust:]
            P = pr
        Pk = zo.azimut_sutunlari(P)
        gec = zo.azimut_sutunlari(g.gecersiz[v0:].astype(np.float32)) > 0.15
        return Pk, gec


def _isin_esd_piksel(d, G, Y):
    az = np.degrees(np.arctan2(d[..., 0], d[..., 2]))
    el = np.degrees(np.arcsin(np.clip(d[..., 1], -1, 1)))
    return (az + 180.0) / 360.0 * G - 0.5, (90.0 - el) / 180.0 * Y - 0.5


def _govdeyi_doldur(pan, gec):
    """Maskeli piksellere sütun boyunca üstteki son geçerli pikseli kopyalar (siyah gövde engel sanılmasın)."""
    if not gec.any():
        return pan
    out = pan.copy()
    H = pan.shape[0]
    idx = np.where(~gec, np.arange(H)[:, None], 0)
    idx = np.maximum.accumulate(idx, axis=0)
    cols = np.arange(pan.shape[1])[None, :]
    out = pan[idx, cols]
    return out


def p_den_sinir(Pk, gec, g, yontem="kesim", ayar=None, esik=0.5):
    """Zemin olasılığı (H2,360) -> SinirSonucu.
    yontem 'kesim': klasik yöntemle aynı kesim maliyeti + dairesel DP.  'ilk': sütunda içeriden dışarıya ilk zemin-dışı piksel."""
    ayar = ayar or zk.AYAR
    H = g.yuk
    v0 = H // 2
    H2 = H - v0
    r_ust = int(np.ceil(zo.aci_satir(ayar["ust_aci"], H) - v0))
    r_ust = min(max(r_ust, 0), H2 - 1)
    if yontem == "kesim":
        U = zk.kesim_maliyeti(Pk[r_ust:], gec[r_ust:], ayar["w_ust"])
        r = zk.dairesel_dp(U, ayar["lam"], ayar["trunc"]) + r_ust
    else:
        r = np.zeros(zo.N_AZ, int)
        for k in range(zo.N_AZ):
            gecerli = np.where(~gec[:, k])[0]
            alt = gecerli.max() if len(gecerli) else H2 - 1
            rk = alt + 1
            for v in range(alt, r_ust - 1, -1):          # içeriden (alttan) dışarı (yukarı)
                if gec[v, k]:
                    continue
                if Pk[v, k] < esik:
                    break
                rk = v
            else:
                rk = r_ust
            r[k] = rk
    mesafe = zo.satirdan_mesafe(v0 + r, H, g.h)
    guven = np.zeros(zo.N_AZ)
    for k in range(zo.N_AZ):
        rk = int(r[k])
        alt = Pk[rk:min(rk + 4, H2), k]
        ust = Pk[max(rk - 4, 0):rk, k]
        if len(alt) == 0 or rk <= r_ust + 1:
            continue
        guven[k] = np.clip(float(alt.mean() - (ust.mean() if len(ust) else 0.0)), 0, 1)
    return zo.SinirSonucu(mesafe, guven, zo.guvenilir_menzil(H, g.h), {"p": Pk, "r": r, "v0": v0})


class ModelYontemi:
    def __init__(self, mod="gorunum", sinir="kesim", cihaz=None):
        self.b = Bolutleyici(cihaz)
        self.mod, self.sinir = mod, sinir

    def __call__(self, g):
        Pk, gec = self.b.zemin_olasiligi(g, mod=self.mod)
        return p_den_sinir(Pk, gec, g, self.sinir)
