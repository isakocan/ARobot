"""Gösterim videosu (AR, hareketli ve zor durumlu nesneler): konum kameradan kestirilirken sanal nesneler oyuncunun göreceği gibi nasıl görünür (yalnızca sim).

Mevcut gosterim_ar.py'nin çizicisini (perspektif görünüm, nesne geometrisi, dolu/çizgi çizimi) yeniden kullanır; nesneler ortme_sahne.py'de
(8 nesne: önden giden, devriye, kapıdan geçen, yörünge, uzak, çok yakın, duvar dibi, bilinmeyen bölge), örtme yöntemleri ortme_yontem.py'de.
Çizim sözleşmesi: beyaz çizgi = gerçek poza ve gerçek örtmeye göre olması gereken yer; dolu = sistemin çizdiği (kestirilen poz); soluk = sistemin gizlediği dilimler.

DÜRÜSTLÜK: konum ve harita kameradan; yalnızca başlangıç konumu verildi (varyant tanımları konum_slam.py'de). Hatalar süslenmez.

Kullanım (depo kökünden):
    python tools/konum_deney/gosterim_ar_hareketli.py --onizleme            # yalnız önizleme resimleri (onizleme_arh_*.png)
    python tools/konum_deney/gosterim_ar_hareketli.py --video               # V1 + V2 (H.264)
    python tools/konum_deney/gosterim_ar_hareketli.py --video --kareler 0 40   # hata ayıklama: gösterilen kare aralığı
Çıktı: kayitlar/sonuclar/gosterim/ (ar_hareketli_D.mp4, ar_ortme_karsilastirma.mp4, onizleme_arh_*.png)."""
import argparse
import os
import subprocess
import sys
import time

import cv2
import numpy as np
from PIL import Image, ImageDraw

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
import konum_ortak as ko  # noqa: E402
import gosterim_ar as ga  # noqa: E402
import ortme_veri as ov  # noqa: E402
import ortme_sahne as sa  # noqa: E402
import ortme_degerlendir as od  # noqa: E402
from kayit import Kayit  # noqa: E402
from kamera import Kamera  # noqa: E402

CIKTI = ov.CIKTI
YONTEM = "birlesik_gercekci"                 # V1 ve V2 sağ: gerçekçi (3 Hz, 0,2 sn gecikmeli) tarama + harita
YONTEM_AD = {"harita": "yalnız harita", "birlesik_gercekci": "canlı tarama + harita"}
W, H = 1280, 720
BASLIK_H = 52
VX, VY, VW, VH = 0, BASLIK_H, ga.VW, ga.VH   # 840 x 540 ana görüntü
PX, PW = 840, 440
ALT_Y = VY + VH
HARF = "abcdefgh"
font, bgr, virgul = ga.font, ga.bgr, ga.virgul
ARKA, PANEL, BASLIK, YAZI, SOLUK, VURGU = ga.ARKA, ga.PANEL, ga.BASLIK, ga.YAZI, ga.SOLUK, ga.VURGU
GERCEK_R, TAHMIN_R, HATA_IYI, HATA_KOTU = ga.GERCEK_R, ga.TAHMIN_R, ga.HATA_IYI, ga.HATA_KOTU
SOLUK_ALFA = ga.SOLUK_ALFA


def ffmpeg_yazici(yol, w, h, fps=15, crf=23):
    import imageio_ffmpeg
    exe = imageio_ffmpeg.get_ffmpeg_exe()
    cmd = [exe, "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "bgr24", "-s", "%dx%d" % (w, h), "-r", str(fps), "-i", "-",
           "-c:v", "libx264", "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-crf", str(crf), "-preset", "medium", yol]
    return subprocess.Popen(cmd, stdin=subprocess.PIPE)


class Hareketli:
    def __init__(self, varyant="D"):
        self.v = v = ov.Veri(varyant)
        self.nes = sa.sahne(v)
        self.K = len(self.nes)
        self.z = od.geo_hesapla(v, self.nes, os.path.join(CIKTI, "onbellek_arh_%s.npz" % varyant))
        self.kay = Kayit(os.path.join(ko.KAYITLAR, ov.TUR, ov.AD))
        self.kam = Kamera.bilgiden(self.kay.bilgi)
        self.tabl = ga.perspektif_tablolari(self.kam)
        self.kareler = v.kareler
        self.nq = len(self.kareler)
        self.sel = od.secim(self.z)
        self._harita_hazirla()
        self._istatistik_hazirla("harita")
        self._istatistik_hazirla(YONTEM)

    # ------------------------------------------------------------------ istatistik (kümülatif)
    def _istatistik_hazirla(self, ad):
        z, sel = self.z, self.sel
        dec, tr = z[ad], z["gizli_t"]
        top = sel.sum((1, 2))
        yg = (sel & dec & ~tr).sum((1, 2))
        yy = (sel & ~dec & tr).sum((1, 2))
        kum = dict(top=np.cumsum(top), yg=np.cumsum(yg), yy=np.cumsum(yy))
        cizilen = z["gorus_e"] & (z["mesafe"] < od.MENZIL_M) & ~dec.all(-1) & np.isfinite(z["kayma_yuzde"])
        orta = np.full(self.nq, np.nan)
        mx = np.full(self.nq, np.nan)
        havuz = []
        for q in range(self.nq):
            havuz += list(z["kayma_yuzde"][q][cizilen[q]])
            if havuz:
                orta[q], mx[q] = float(np.median(havuz)), float(np.max(havuz))
        kum["kayma_orta"], kum["kayma_max"] = orta, mx
        if not hasattr(self, "kum"):
            self.kum = {}
        self.kum[ad] = kum

    # ------------------------------------------------------------------ harita paneli için yol çizimleri
    def _harita_hazirla(self):
        v = self.v
        self.yol_cizgi = {}
        wp = [(-0.4, 1.9), (-0.35, 0.95), (-0.35, 0.05), (-1.0, -0.3), (-1.4, -1.0), (-1.5, -1.7)]
        a = np.linspace(0, 2 * np.pi, 72)
        self.yol_cizgi["kapi"] = np.array(wp)
        self.yol_cizgi["devriye"] = np.array([(0.8, -0.38), (-1.2, -0.38)])
        self.yol_cizgi["yorunge"] = np.stack([3.1 + 1.25 * np.cos(a), 2.15 + 1.25 * np.sin(a)], 1)

    def dp(self, x, y, S=2):
        v = self.v
        return ((np.asarray(x) - v.x0) / v.h - v.c0) * S, ((v.Hh - (np.asarray(y) - v.y0) / v.h) - v.r0) * S

    def harita_paneli(self, q, i, pe, pt):
        v, S = self.v, 2
        M = v.harita_at(i)
        if M is None:
            M = np.full((v.Hh, v.Wg), 128, np.uint8)
        E = M[v.r0:v.r1, v.c0:v.c1]
        lut = np.zeros((256, 3), np.uint8)
        lut[:] = (128, 124, 120)
        lut[255] = (238, 236, 228)
        lut[0] = (28, 30, 36)
        im = cv2.resize(lut[E], (E.shape[1] * S, E.shape[0] * S), interpolation=cv2.INTER_NEAREST)
        im = np.ascontiguousarray(im[:, :, ::-1])
        px_, py_ = self.dp(pt[0], pt[1])
        kama = [(px_, py_)]
        for dg in np.linspace(-ga.HFOV / 2, ga.HFOV / 2, 13):
            t = np.radians(pt[2] + dg)
            kama.append((px_ + np.sin(t) * 2.0 / v.h * S, py_ - np.cos(t) * 2.0 / v.h * S))
        kat = im.copy()
        cv2.fillPoly(kat, [np.array(kama, np.int32)], (255, 255, 255), cv2.LINE_AA)
        im = cv2.addWeighted(kat, 0.28, im, 0.72, 0)
        for P, renk, kal in ((self.v.Pe, TAHMIN_R, 2), (self.v.Pt, GERCEK_R, 1)):
            ux, uy = self.dp(P[:i + 1, 0], P[:i + 1, 1])
            pts = np.stack([ux, uy], 1).astype(np.int32).reshape(-1, 1, 2)
            if len(pts) > 1:
                cv2.polylines(im, [pts], False, bgr(renk), kal, cv2.LINE_AA)
        # nesne yolları (ince, nesne renginde)
        kat = im.copy()
        for k, n in enumerate(self.nes):
            if n.kat == "onden":
                ux, uy = self.dp(n.X[:i + 1], n.Y[:i + 1])
                pts = np.stack([ux, uy], 1).astype(np.int32).reshape(-1, 1, 2)
                cv2.polylines(kat, [pts], False, bgr(n.renk), 1, cv2.LINE_AA)
            elif n.kat in self.yol_cizgi:
                yl = self.yol_cizgi[n.kat]
                ux, uy = self.dp(yl[:, 0], yl[:, 1])
                pts = np.stack([ux, uy], 1).astype(np.int32).reshape(-1, 1, 2)
                cv2.polylines(kat, [pts], n.kat == "yorunge", bgr(n.renk), 2, cv2.LINE_AA)
        im = cv2.addWeighted(kat, 0.85, im, 0.15, 0)
        harf = []
        for k, n in enumerate(self.nes):
            ox, oy = self.dp(n.X[i], n.Y[i])
            cv2.circle(im, (int(ox), int(oy)), 7, (0, 0, 0), -1, cv2.LINE_AA)
            cv2.circle(im, (int(ox), int(oy)), 5, bgr(n.renk), -1, cv2.LINE_AA)
            harf.append((ox + 9, oy - 11, HARF[k]))
        ex, ey = self.dp(pe[0], pe[1])
        cv2.circle(im, (int(ex), int(ey)), 6, (0, 0, 0), 2, cv2.LINE_AA)
        cv2.circle(im, (int(ex), int(ey)), 5, bgr(TAHMIN_R), 2, cv2.LINE_AA)
        t = np.radians(pt[2])
        s_, c_ = np.sin(t), np.cos(t)
        boy = 11
        tri = np.array([(px_ + s_ * boy, py_ - c_ * boy),
                        (px_ - s_ * boy * 0.7 - c_ * boy * 0.7, py_ + c_ * boy * 0.7 - s_ * boy * 0.7),
                        (px_ - s_ * boy * 0.7 + c_ * boy * 0.7, py_ + c_ * boy * 0.7 + s_ * boy * 0.7)], np.int32)
        cv2.fillPoly(im, [tri], (255, 255, 255), cv2.LINE_AA)
        cv2.polylines(im, [tri], True, (0, 0, 0), 2, cv2.LINE_AA)
        return im, harf

    # ------------------------------------------------------------------ oyuncu görüntüsü (840x540, etiket konumlarıyla)
    def oyuncu(self, q, ad, gor_taban=None):
        """ad: kullanılacak örtme yöntemi. Dönüş: (görüntü 840x540 BGR, etiketler [(x, y, metin, soluk)]). gor_taban: önceden hesaplanmış perspektif."""
        v, z = self.v, self.z
        i = int(self.kareler[q])
        pe, pt = v.Pe[i], v.Pt[i]
        ga.NESNELER = [n.sozluk(i) for n in self.nes]
        self.mesh = [ga.mesh_dunya(d) for d in ga.NESNELER]
        gor = gor_taban.copy() if gor_taban is not None else ga.perspektif(self.kay.resim(i), self.tabl)
        geo = [dict(gizli_e=z[ad][q, k], gizli_t=z["gizli_t"][q, k]) for k in range(self.K)]
        sira = sorted(range(self.K), key=lambda k: -np.hypot(ga.NESNELER[k]["x"] - pe[0], ga.NESNELER[k]["y"] - pe[1]))
        for k in sira:
            ga.Cizici.dolu_ciz(self, gor, pe, k, geo[k])
        ga.Cizici.cizgiler_ciz(self, gor, pt, geo)
        etik = []
        for k in range(self.K):
            d = ga.NESNELER[k]
            ust = ga.kam_koord(pe, np.array([[d["x"], d["y"], 0.42]]))[0]
            if ust[2] < 0.25 or np.hypot(d["x"] - pe[0], d["y"] - pe[1]) > od.MENZIL_M:
                continue
            p = ga.piksel(ust[None])[0]
            if 0 <= p[0] <= ga.VW and 0 <= p[1] <= ga.VH:
                w2_ = 5.6 * len(d["ad"]) + 6
                etik.append((float(np.clip(p[0], w2_, ga.VW - w2_)), float(p[1]), d["ad"], bool(z[ad][q, k].sum() >= 4),
                             float(np.hypot(d["x"] - pe[0], d["y"] - pe[1]))))
        # çakışma: yakın nesne yerinde kalır, uzak olan yukarı kayar
        yerlesik = []
        cik = []
        for x, y, m, soluk, dist in sorted(etik, key=lambda e: e[4]):
            w = 11.0 * len(m) + 10
            yy = y
            for _ in range(8):
                if any(abs(x - x2) < (w + w2) / 2 and abs(yy - y2) < 22 for x2, y2, w2 in yerlesik):
                    yy -= 22
                else:
                    break
            yerlesik.append((x, yy, w))
            cik.append((x, yy, m, soluk))
        etik = cik
        return gor, etik

    cizgi_noktalari = ga.Cizici.cizgi_noktalari

    # ------------------------------------------------------------------ okuma paneli bilgisi
    def en_yakin(self, q):
        z = self.z
        adaylar = [k for k in range(self.K) if z["gorus_e"][q, k] and z["mesafe"][q, k] < od.MENZIL_M]
        if not adaylar:
            return None
        return min(adaylar, key=lambda k: z["mesafe"][q, k])

    def orte_durumu(self, q, k, ad):
        sl = self.sel[q, k]
        yg = int((sl & self.z[ad][q, k] & ~self.z["gizli_t"][q, k]).sum())
        yy = int((sl & ~self.z[ad][q, k] & self.z["gizli_t"][q, k]).sum())
        return int(sl.sum()), yg, yy

    # ------------------------------------------------------------------ V1 karesi
    def ciz_v1(self, q, gor_taban=None):
        v, z = self.v, self.z
        i = int(self.kareler[q])
        pe, pt = v.Pe[i], v.Pt[i]
        gor, etik = self.oyuncu(q, YONTEM, gor_taban)
        cv = np.zeros((H, W, 3), np.uint8)
        cv[:] = bgr(ARKA)
        cv[:BASLIK_H] = bgr(BASLIK)
        cv[VY:VY + VH, VX:VX + VW] = gor
        cv[VY + VH:H, :PX] = bgr(PANEL)
        cv[VY:H, PX:] = bgr(PANEL)
        hm, harf = self.harita_paneli(q, i, pe, pt)
        cv[VY:VY + hm.shape[0], PX:PX + hm.shape[1]] = hm
        yazi = []
        yazi.append((20, BASLIK_H // 2, "Hareketli sanal nesneler · konum tamamen kameradan (sim)", 32, YAZI, True, "lm", False))
        yazi.append((W - 20, BASLIK_H // 2, "%dx hız" % ga.ADIM, 32, VURGU, True, "rm", False))
        yazi.append((16, VY + 24, "oyuncunun ekranı", 26, YAZI, True, "lm", True))
        yazi.append((PX + 10, VY + 20, "robotun kendi haritası", 24, (30, 30, 36), True, "lm", False))
        yazi.append((PX + PW - 8, VY + 330, "▬ gerçek yol", 20, GERCEK_R, True, "rm", True))
        yazi.append((PX + PW - 8, VY + 350, "▬ kestirilen yol", 20, TAHMIN_R, True, "rm", True))
        for x, y, h_ in harf:
            yazi.append((PX + x, VY + y, h_, 18, (20, 20, 20), True, "mm", "kontur_acik"))
        for x, y, m, soluk in etik:
            yazi.append((VX + x, VY + y - 14, m, 22, (170, 175, 182) if soluk else (255, 255, 255), True, "mb", True))
        # alt şerit: açıklama + nesne anahtarı
        ly = ALT_Y + 16
        cv2.rectangle(cv, (20, ly - 10), (50, ly + 10), (255, 255, 255), 2, cv2.LINE_AA)
        r0 = self.nes[2].renk
        cv2.rectangle(cv, (20, ly + 27 - 10), (50, ly + 27 + 10), bgr(r0), -1)
        kat = cv.copy()
        cv2.rectangle(kat, (20, ly + 54 - 10), (50, ly + 54 + 10), bgr(r0), -1)
        yy0, yy1 = ly + 54 - 11, ly + 54 + 11
        cv[yy0:yy1, 19:52] = (kat[yy0:yy1, 19:52].astype(np.float32) * SOLUK_ALFA + cv[yy0:yy1, 19:52].astype(np.float32) * (1 - SOLUK_ALFA)).astype(np.uint8)
        yazi.append((62, ly, "beyaz çizgi: olması gereken yer", 22, YAZI, False, "lm", False))
        yazi.append((62, ly + 27, "dolu: sistemin çizdiği", 22, YAZI, False, "lm", False))
        yazi.append((62, ly + 54, "soluk: sistemin gizlediği", 22, YAZI, False, "lm", False))
        yazi.append((20, ly + 84, "örtme: canlı zemin taraması (3 Hz, 0,2 sn geç) + harita", 19, SOLUK, False, "lm", False))
        for k, n in enumerate(self.nes):
            cx_, cy_ = 450 + (k % 2) * 195, ALT_Y + 16 + (k // 2) * 25
            cv2.circle(cv, (cx_, cy_), 8, (0, 0, 0), -1, cv2.LINE_AA)
            cv2.circle(cv, (cx_, cy_), 6, bgr(n.renk), -1, cv2.LINE_AA)
            yazi.append((cx_ + 14, cy_, "%s %s" % (HARF[k], n.etiket), 20, YAZI, False, "lm", False))
        yazi.append((20, H - 12, "Konum ve harita kameradan; yalnızca başlangıç konumu verildi (sim)", 22, VURGU, True, "lm", False))
        # okuma paneli
        x0 = PX + 18
        yo = VY + hm.shape[0] + 10
        k = self.en_yakin(q)
        yazi.append((x0, yo + 6, "EN YAKIN NESNE", 20, SOLUK, True, "lm", False))
        if k is None:
            yazi.append((x0, yo + 38, "görünür nesne yok", 30, SOLUK, True, "lm", False))
            v_y = v_c = "–"
            renk_k = SOLUK
            durum, d_renk = "", SOLUK
        else:
            n = self.nes[k]
            cv2.circle(cv, (x0 + 11, yo + 38), 11, (0, 0, 0), -1, cv2.LINE_AA)
            cv2.circle(cv, (x0 + 11, yo + 38), 9, bgr(n.renk), -1, cv2.LINE_AA)
            yazi.append((x0 + 30, yo + 38, "%s %s · %s m" % (HARF[k], n.etiket, virgul(float(z["mesafe"][q, k]), 1)), 28, YAZI, True, "lm", False))
            ky = float(z["kayma_yuzde"][q, k])
            kc = float(z["kayma_cm"][q, k])
            v_y = "%%%s" % virgul(ky, 1) if np.isfinite(ky) else "–"
            v_c = "%d cm" % round(kc) if np.isfinite(kc) else "–"
            renk_k = HATA_IYI if (np.isfinite(ky) and ky <= 2.0) else HATA_KOTU
            top, yg, yy_ = self.orte_durumu(q, k, YONTEM)
            if top == 0:
                durum, d_renk = "örtme: ekranda dilim yok", SOLUK
            elif yg + yy_ == 0:
                durum, d_renk = "örtme: doğru (%d dilim)" % top, HATA_IYI
            else:
                durum, d_renk = "örtme: %d/%d dilim yanlış %s" % (yg + yy_, top, "gizli" if yg >= yy_ else "görünür"), HATA_KOTU
        yazi.append((x0, yo + 76, "EKRANDA KAYMA", 20, SOLUK, True, "lm", False))
        yazi.append((x0 + 230, yo + 76, "YERDE KAYMA", 20, SOLUK, True, "lm", False))
        yazi.append((x0, yo + 108, v_y, 40, renk_k, True, "lm", False))
        yazi.append((x0 + 230, yo + 108, v_c, 40, renk_k, True, "lm", False))
        yazi.append((x0, yo + 140, durum, 22, d_renk, True, "lm", False))
        yazi.append((x0, yo + 176, "ŞİMDİYE DEK (6 m içi)", 20, SOLUK, True, "lm", False))
        km = self.kum[YONTEM]
        if np.isfinite(km["kayma_orta"][q]):
            yazi.append((x0, yo + 204, "kayma: ortanca %%%s · en çok %%%s" % (virgul(km["kayma_orta"][q], 1), virgul(km["kayma_max"][q], 1)), 24, YAZI, True, "lm", False))
        else:
            yazi.append((x0, yo + 204, "kayma: ölçüm yok", 24, SOLUK, True, "lm", False))
        t_ = max(int(km["top"][q]), 1)
        yazi.append((x0, yo + 232, "örtme yanlış: gizli %%%s · görünür %%%s" % (virgul(100.0 * km["yg"][q] / t_, 1), virgul(100.0 * km["yy"][q] / t_, 1)), 22, YAZI, True, "lm", False))
        return self._yazi_bas(cv, yazi)

    # ------------------------------------------------------------------ V2 karesi (iki görünüm yan yana)
    def ciz_v2(self, q, gor_taban=None):
        i = int(self.kareler[q])
        gt = gor_taban if gor_taban is not None else ga.perspektif(self.kay.resim(i), self.tabl)
        VW2, VH2 = 636, 409
        s = VW2 / float(ga.VW)
        cv = np.zeros((H, W, 3), np.uint8)
        cv[:] = bgr(ARKA)
        cv[:BASLIK_H] = bgr(BASLIK)
        yazi = []
        yazi.append((20, BASLIK_H // 2, "Aynı sahne, iki örtme yöntemi (sim)", 32, YAZI, True, "lm", False))
        yazi.append((W - 20, BASLIK_H // 2, "%dx hız" % ga.ADIM, 32, VURGU, True, "rm", False))
        for yon, (ad, x_) in enumerate((("harita", 2), (YONTEM, 642))):
            gor, etik = self.oyuncu(q, ad, gt)
            kucuk = cv2.resize(gor, (VW2, VH2), interpolation=cv2.INTER_AREA)
            cv[BASLIK_H + 6:BASLIK_H + 6 + VH2, x_:x_ + VW2] = kucuk
            yazi.append((x_ + 10, BASLIK_H + 6 + 28, "yalnız harita" if yon == 0 else "canlı tarama + harita", 30, YAZI, True, "lm", True))
            for x, y, m, soluk in etik:
                yazi.append((x_ + x * s, BASLIK_H + 6 + y * s - 12, m, 24, (170, 175, 182) if soluk else (255, 255, 255), True, "mb", True))
            km = self.kum[ad]
            t_ = max(int(km["top"][q]), 1)
            yb = BASLIK_H + 6 + VH2 + 22
            yazi.append((x_ + 6, yb, "yanlış karar (şimdiye dek)", 24, SOLUK, True, "lm", False))
            yazi.append((x_ + 6, yb + 38, "%%%s" % virgul(100.0 * (km["yg"][q] + km["yy"][q]) / t_, 1), 46, YAZI, True, "lm", False))
            yazi.append((x_ + 150, yb + 38, "gizli %%%s · görünür %%%s" % (virgul(100.0 * km["yg"][q] / t_, 1), virgul(100.0 * km["yy"][q] / t_, 1)), 26, YAZI, False, "lm", False))
        # bu karedeki farklar
        fark = []
        for k in range(self.K):
            sl = self.sel[q, k]
            if not sl.any():
                continue
            a, b = self.z["harita"][q, k] & sl, self.z[YONTEM][q, k] & sl
            if (a != b).any():
                tr = self.z["gizli_t"][q, k] & sl
                dogru_a, dogru_b = int((a == tr).sum()), int((b == tr).sum())
                fark.append((self.nes[k].etiket, dogru_a, dogru_b, int(sl.sum())))
        yf = BASLIK_H + 6 + VH2 + 22 + 92
        if fark:
            e, da, db, n_ = max(fark, key=lambda f: abs(f[1] - f[2]))
            iyi = "sağ doğru" if db > da else ("sol doğru" if da > db else "ikisi de aynı oranda")
            yazi.append((10, yf, "şimdi fark: %s · %s" % (e, iyi), 28, VURGU, True, "lm", False))
        else:
            yazi.append((10, yf, "şimdi iki yöntem aynı karar veriyor", 28, SOLUK, True, "lm", False))
        yazi.append((10, yf + 36, "beyaz çizgi: olması gereken · dolu: çizilen · soluk: gizlenen", 24, YAZI, False, "lm", False))
        yazi.append((10, H - 14, "Konum ve harita kameradan; yalnızca başlangıç konumu verildi (sim)", 24, VURGU, True, "lm", False))
        return self._yazi_bas(cv, yazi)

    # ------------------------------------------------------------------ metin
    @staticmethod
    def _yazi_bas(cv, yazi):
        pil = Image.fromarray(cv[:, :, ::-1])
        d = ImageDraw.Draw(pil)
        for (x, y, m, bs, r, kl, an, kont) in yazi:
            if kont == "kontur_acik":
                d.text((x, y), m, font=font(bs, kl), fill=r, anchor=an, stroke_width=3, stroke_fill=(255, 255, 255))
            elif kont:
                koyu = sum(r) < 300
                d.text((x, y), m, font=font(bs, kl), fill=r, anchor=an, stroke_width=3 if koyu else 2, stroke_fill=(255, 255, 255) if koyu else (0, 0, 0))
            else:
                d.text((x, y), m, font=font(bs, kl), fill=r, anchor=an)
        return np.asarray(pil)[:, :, ::-1].copy()

    # ------------------------------------------------------------------ önizleme kareleri
    def _ekran_x(self):
        """(nq, K) tahmini kameradan nesne merkezinin ekran x'i ve ilerisi."""
        px = np.zeros((self.nq, self.K))
        il = np.zeros((self.nq, self.K))
        for q, i in enumerate(self.kareler):
            G = np.stack([self.z["X"][q], self.z["Y"][q], np.full(self.K, 0.15)], 1)
            c = ga.kam_koord(self.v.Pe[i], G)
            il[q] = c[:, 2]
            px[q] = ga.piksel(np.where(c[:, 2:3] > ga.FN, c, c * 0 + np.array([0, 0, ga.FN])))[:, 0]
        return px, il

    def onizleme_secimi(self):
        z, sel, v = self.z, self.sel, self.v
        ind = {n.kat: k for k, n in enumerate(self.nes)}
        dec = z[YONTEM]
        px, il = self._ekran_x()
        yanlis = sel & (dec != z["gizli_t"])
        ort = np.abs(px - ga.VW / 2)                                   # merkezden uzaklık
        gor = z["gorus_e"] & (z["mesafe"] < od.MENZIL_M)
        gorunur_t = gor & ~z["gizli_t"].all(-1)
        kalabalik = (gor & ~dec.all(-1)).sum(1)                        # sistemin çizdiği, ekrandaki nesne sayısı
        sec = {}
        # a) önden giden: en hızlı dönüş anı
        k = ind["onden"]
        yaw = np.abs(ko.sar(np.diff(v.Pt[:, 2], prepend=v.Pt[0, 2]))) * 15
        sec["a_onden"] = int(np.argmax([yaw[int(i)] if gor[qq, k] else -1 for qq, i in enumerate(self.kareler)]))
        # b) devriye: robota en yakın, görüşte
        k = ind["devriye"]
        sec["b_devriye"] = int(np.argmin(np.where(gor[:, k] & (ort[:, k] < 300), z["mesafe"][:, k], 99.0)))
        # c) kapıdan geçen: sistem dilimlerin bir kısmını gizliyor (2..5), 1,5-3,5 m, merkeze yakın
        k = ind["kapi"]
        adet = (dec[:, k] & sel[:, k]).sum(1)
        ok = gor[:, k] & (adet >= 2) & (adet <= 5) & (z["mesafe"][:, k] > 1.5) & (z["mesafe"][:, k] < 3.5)
        sec["c_kapidan"] = int(np.argmin(np.where(ok, ort[:, k] + 20 * kalabalik, 1e9)))
        # d) yörünge: en çok yanlış dilim; nesne görüşün orta bölümünde, 4,5 m içinde
        k = ind["yorunge"]
        ys = yanlis[:, k].sum(1) * (gor[:, k] & (z["mesafe"][:, k] < 4.5) & (ort[:, k] < 330))
        sec["d_yorunge"] = int(np.argmax(ys))
        # e) uzak: 5-6 m, merkeze yakın, kalabalık az
        k = ind["uzak"]
        ok = gor[:, k] & (z["mesafe"][:, k] > 4.8) & (z["mesafe"][:, k] < 6.2) & gorunur_t[:, k]
        ceza = ort[:, k] + 40.0 * kalabalik
        for j in range(self.K):                                        # başka nesnenin arkasında kalmasın (olabildiğince)
            if j != k:
                ceza = ceza + 300.0 * (gor[:, j] & (np.abs(px[:, j] - px[:, k]) < 70) & (z["mesafe"][:, j] < z["mesafe"][:, k]))
        sec["e_uzak"] = int(np.argmin(np.where(ok, ceza, 1e9)))
        # f) çok yakın: ekranda en büyük (en küçük ileri), merkezi ekranda
        k = ind["yakin"]
        sec["f_yakin"] = int(np.argmin(np.where(gor[:, k] & gorunur_t[:, k] & (il[:, k] > 0.2), il[:, k], 99)))
        # g) duvar dibi: 0,6-2,5 m, görüşün orta bölümünde; en çok yanlış dilim, yoksa 1,2 m'ye en yakın
        k = ind["duvar"]
        ok = gor[:, k] & (z["mesafe"][:, k] > 0.6) & (z["mesafe"][:, k] < 2.5) & (ort[:, k] < 330)
        ys = yanlis[:, k].sum(1) * ok
        sec["g_duvar"] = int(np.argmax(ys)) if ys.max() > 0 else int(np.argmin(np.where(ok, np.abs(z["mesafe"][:, k] - 1.2), 99)))
        # h) bilinmeyen bölge: gerçekte görünür, haritada bilinmeyen; görüşün ortasına en yakın an
        k = ind["bilinmeyen"]
        bil = np.zeros(self.nq, bool)
        for q, i in enumerate(self.kareler):
            M = v.harita_at(int(i))
            r, c = v.hucre(z["X"][q, k], z["Y"][q, k])
            bil[q] = (M is None) or (M[r, c] == 128)
        ok = gorunur_t[:, k] & bil
        sec["h_bilinmeyen"] = int(np.argmin(np.where(ok, ort[:, k], 1e9)))
        self.h_ilk_gorus = int(np.argmax(gorunur_t[:, k]))
        # en kötü: kayma
        kay = np.where(gor & ~dec.all(-1) & np.isfinite(z["kayma_yuzde"]), z["kayma_yuzde"], -1)
        sec["en_kotu_kayma"] = int(np.unravel_index(np.argmax(kay), kay.shape)[0])
        # en kötü: örtme (en çok yanlış dilim, 1 m ötesi, nesne ekranda)
        yp = (yanlis & (z["mesafe"][..., None] > 1.0) & (z["mesafe"][..., None] < 4.5) & gor[..., None]).sum(2)
        yp[:30] = 0                                                    # tur başı: harita henüz boş
        sec["en_kotu_orte"] = int(np.unravel_index(np.argmax(yp), yp.shape)[0])
        # V2: iki yöntemin en çok ayrıldığı (ve sağın daha doğru olduğu) kare; nesne 1-4 m, görüşün orta bölümünde
        fark = (sel & (z["harita"] != dec) & (z["mesafe"][..., None] < 4.0) & (z["mesafe"][..., None] > 0.8) & (ort[..., None] < 380))
        iyi = (sel & (z["harita"] != z["gizli_t"]) & (dec == z["gizli_t"]))        # sağ doğru, sol yanlış
        kotu = (sel & (z["harita"] == z["gizli_t"]) & (dec != z["gizli_t"]))       # sol doğru, sağ yanlış
        sk = (fark & iyi).sum((1, 2)) - 2 * (fark & kotu).sum((1, 2))
        sec["v2_sag_daha_iyi"] = int(np.argmax(sk))
        sk = (fark & kotu).sum((1, 2)) - 2 * (fark & iyi).sum((1, 2))
        sec["v2_sol_daha_iyi"] = int(np.argmax(sk))
        return sec


def calistir(onizleme, video, kare_sinir, crf):
    t0 = time.time()
    c = Hareketli("D")
    print("hazır: %d kare, %d nesne (%.0f sn)" % (c.nq, c.K, time.time() - t0), flush=True)
    if onizleme:
        sec = c.onizleme_secimi()
        print("önizleme kareleri:", {k: int(c.kareler[q]) for k, q in sec.items()}, flush=True)
        for ad, q in sec.items():
            if not ad.startswith("v2_"):
                cv2.imwrite(os.path.join(CIKTI, "onizleme_arh_%s.png" % ad), c.ciz_v1(q))
            if ad in ("c_kapidan", "d_yorunge", "en_kotu_orte") or ad.startswith("v2_"):
                cv2.imwrite(os.path.join(CIKTI, "onizleme_arh_v2_%s.png" % ad), c.ciz_v2(q))
    if video:
        y1 = ffmpeg_yazici(os.path.join(CIKTI, "ar_hareketli_D.mp4"), W, H, 15, crf)
        y2 = ffmpeg_yazici(os.path.join(CIKTI, "ar_ortme_karsilastirma.mp4"), W, H, 15, crf)
        aralik = range(c.nq) if kare_sinir is None else range(*kare_sinir)
        for q in aralik:
            i = int(c.kareler[q])
            gt = ga.perspektif(c.kay.resim(i), c.tabl)
            y1.stdin.write(c.ciz_v1(q, gt).tobytes())
            y2.stdin.write(c.ciz_v2(q, gt).tobytes())
            if q % 50 == 0:
                print("  video %d/%d (%.0f sn)" % (q, c.nq, time.time() - t0), flush=True)
        for y in (y1, y2):
            y.stdin.close()
            y.wait()
        for ad in ("ar_hareketli_D.mp4", "ar_ortme_karsilastirma.mp4"):
            yol = os.path.join(CIKTI, ad)
            vv = cv2.VideoCapture(yol)
            sayi = int(vv.get(cv2.CAP_PROP_FRAME_COUNT))
            ok, kk = vv.read()
            vv.release()
            print("%s  %.2f MiB  kare %d  açılıyor: %s  boyut %s" % (yol, os.path.getsize(yol) / 2 ** 20, sayi, ok, None if kk is None else kk.shape))
    print("bitti (%.0f sn)" % (time.time() - t0))


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--onizleme", action="store_true")
    ap.add_argument("--video", action="store_true")
    ap.add_argument("--kareler", nargs=2, type=int, default=None)
    ap.add_argument("--crf", type=int, default=23)
    a = ap.parse_args()
    calistir(a.onizleme, a.video, a.kareler, a.crf)
