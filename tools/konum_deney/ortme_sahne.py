"""Hareketli ve zor durumlu sanal nesne sahnesi (ev_turu kaydı, yalnızca sim). Her nesnenin her KARE için dünya konumu önceden hesaplanır.

Hareket, kayıt zamanına (t_sn) bağlıdır; robottan bağımsızdır. İstisna: ÖNDEN GİDEN, robotun gerçek rotasına bağlıdır.
Doğrulama (dogrula): konumlar gerçek haritada serbest hücrede mi, robota en yakın gerçek mesafe, durağan nesnelerin robota en yakın geçişi."""
import os
import sys

import numpy as np

KLASOR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, KLASOR)
import ortme_veri as ov  # noqa: E402
import konum_ortak as ko  # noqa: E402

KATEGORILER = ["onden", "devriye", "kapi", "yorunge", "uzak", "yakin", "duvar", "bilinmeyen"]
KAT_AD = {"onden": "a ÖNDEN GİDEN", "devriye": "b DEVRİYE", "kapi": "c KAPIDAN GEÇEN", "yorunge": "d YÖRÜNGE",
          "uzak": "e UZAK", "yakin": "f ÇOK YAKIN", "duvar": "g DUVAR DİBİ", "bilinmeyen": "h BİLİNMEYEN BÖLGE"}


class Nesne:
    def __init__(self, kat, etiket, sekil, renk, yaw, X, Y, aciklama):
        self.kat, self.etiket, self.sekil, self.renk, self.yaw = kat, etiket, sekil, renk, yaw
        self.X, self.Y = np.asarray(X, float), np.asarray(Y, float)
        self.aciklama = aciklama

    def sozluk(self, i):
        return dict(ad=self.etiket, sekil=self.sekil, x=float(self.X[i]), y=float(self.Y[i]), renk=self.renk, yaw=self.yaw)


def _yol_ornek(wp, s):
    """Noktalar arası gidiş-dönüş (ping-pong) yol üzerinde s (m) konumu."""
    wp = np.asarray(wp, float)
    seg = np.hypot(*np.diff(wp, axis=0).T)
    cs = np.r_[0.0, np.cumsum(seg)]
    L = cs[-1]
    s = np.mod(s, 2 * L)
    s = np.where(s > L, 2 * L - s, s)
    return np.interp(s, cs, wp[:, 0]), np.interp(s, cs, wp[:, 1])


def _onden_gelen(v, uzak=1.5, yarim_aci=35.0):
    """Robotun gerçek DÖNME MERKEZİ rotasında 'uzak' kadar ilerideki noktanın robota göre yönü, ±35 derece ile sınırlanır (dönüşlerde
    nesne görüş kenarında kalır); nesne her karede kameradan (yön, yarıçap) ile konumlanır, yarıçap serbest hücre için küçülebilir.
    Yön ve yarıçap 5 karelik (0,33 sn) ortalamayla yumuşatılır."""
    Pt = v.Pt
    y = np.radians(Pt[:, 2])
    cx = Pt[:, 0] + ko.KAM_GERI * np.sin(y)
    cy = Pt[:, 1] + ko.KAM_GERI * np.cos(y)
    # 0,1 mm altındaki adımlar sayılmaz: yerinde dönmede kamera çevresel dolanır, merkez ise sabit kalır
    ds = np.hypot(np.diff(cx, prepend=cx[0]), np.diff(cy, prepend=cy[0]))
    s = np.cumsum(np.where(ds > 1e-4, ds, 0.0))
    n = len(s)
    ang = np.zeros(n)
    for i in range(n):
        j = int(np.searchsorted(s, s[i] + uzak))
        if j < n:
            f = np.array([np.sin(y[i]), np.cos(y[i])])
            d = np.array([cx[j] - cx[i], cy[j] - cy[i]])
            ang[i] = np.degrees(np.arctan2(d[0] * f[1] - d[1] * f[0], d[0] * f[0] + d[1] * f[1]))     # saat yönünde +
    for i in range(1, n):                          # rota noktası robotun arkasına geçince işaret sıçramasın: önceki yönde kal
        if abs(ang[i]) > 100.0:
            ang[i] = np.sign(ang[i - 1] if ang[i - 1] != 0 else 1.0) * yarim_aci
    ang = np.clip(ang, -yarim_aci, yarim_aci)
    k = np.ones(5) / 5.0
    ang = np.convolve(np.r_[np.full(2, ang[0]), ang, np.full(2, ang[-1])], k, "valid")
    for i in range(1, n):                          # yön değişimi en çok 3 derece/kare (45 derece/sn)
        ang[i] = ang[i - 1] + np.clip(ang[i] - ang[i - 1], -3.0, 3.0)
    X = np.zeros(n)
    Y = np.zeros(n)
    rr = np.zeros(n)
    izgara = np.arange(uzak, 0.45, -0.05)
    for i in range(n):
        th = y[i] + np.radians(ang[i])
        qx, qy = Pt[i, 0] + izgara * np.sin(th), Pt[i, 1] + izgara * np.cos(th)
        ok = v.serbest_mi(qx, qy, 0.15)
        rr[i] = izgara[int(np.argmax(ok))] if ok.any() else izgara[-1]
    # yarıçap: 9 karelik en küçük süzgeç, sonra 9 karelik ortalama (duvar yanında ani sıçrama olmasın)
    from numpy.lib.stride_tricks import sliding_window_view as swv
    rr = swv(np.r_[np.full(4, rr[0]), rr, np.full(4, rr[-1])], 9).min(1)
    rr = np.convolve(np.r_[np.full(4, rr[0]), rr, np.full(4, rr[-1])], np.ones(9) / 9.0, "valid")
    for i in range(n - 2, -1, -1):                 # yarıçap değişimi en çok 0,03 m/kare: iki yönlü, engelden ÖNCE küçülmeye başlar
        rr[i] = min(rr[i], rr[i + 1] + 0.03)
    for i in range(1, n):
        rr[i] = min(rr[i], rr[i - 1] + 0.03)
    for i in range(n):
        th = y[i] + np.radians(ang[i])
        r = rr[i]
        for _ in range(12):
            qx, qy = Pt[i, 0] + r * np.sin(th), Pt[i, 1] + r * np.cos(th)
            if v.serbest_mi(qx, qy, 0.1):
                break
            r -= 0.05
        X[i], Y[i] = qx, qy
    return X, Y


def sahne(v):
    t = v.t_sn
    nes = []
    # a) ÖNDEN GİDEN
    X, Y = _onden_gelen(v)
    nes.append(Nesne("onden", "Önden giden", "kure", (250, 205, 40), 0.0, X, Y,
                     "robotun gerçek rotasında 1,5 m önde (dönüşlerde ±35 derece sınırında), her an görüşte"))
    # b) DEVRİYE: koridorun güney şeridi, 0,3 m/sn, x -0,6 ... 3,0
    yd = _yol_ornek([(0.8, -0.38), (-1.2, -0.38)], 0.30 * (t + 0.5))
    nes.append(Nesne("devriye", "Devriye", "koni", (165, 110, 255), 0.0, yd[0], yd[1],
                     "koridor güney şeridinde y=-0,38, x -1,2 ile 0,8 arası gidiş-dönüş, 0,3 m/sn (robotun şeridi y~0,1; yanından ~0,47 m geçer)"))
    # c) KAPIDAN GEÇEN: kuzey odası -> kapı -> koridor -> güney açıklığı -> güneybatı odası, 0,4 m/sn gidiş-dönüş
    wp = [(-0.4, 1.9), (-0.35, 0.95), (-0.35, 0.05), (-1.0, -0.3), (-1.4, -1.0), (-1.5, -1.7)]
    xc, yc = _yol_ornek(wp, 0.40 * (t + 15.5))
    nes.append(Nesne("kapi", "Kapıdan geçen", "kup", (255, 112, 70), 25.0, xc, yc,
                     "kuzey oda (-0,4;1,9) -> kapı (-0,35;0,7) -> koridor (-0,35;0,05) -> güney açıklığı (-1,2;-0,5) -> güney oda (-1,5;-1,7), 0,4 m/sn, gidiş-dönüş"))
    # d) YÖRÜNGE: kuzeydoğu odasında, ayaklı mobilyanın (orta kaide + 8 ayak) çevresinde
    cx, cy, R, V, ph = 3.1, 2.15, 1.25, 0.35, 3.93
    th = ph + V / R * t
    nes.append(Nesne("yorunge", "Yörünge", "silindir", (40, 205, 190), 0.0, cx + R * np.cos(th), cy + R * np.sin(th),
                     "merkez (3,1;2,15), yarıçap 1,25 m (0,8 m ayaklar arası 0,3 m boşluğa sığmadı), 0,35 m/sn; ayaklı mobilyanın ayakları ve kaidesi arkasından geçer"))
    # e) UZAK
    n = len(t)
    nes.append(Nesne("uzak", "Uzak", "kure", (255, 90, 170), 0.0, np.full(n, 4.3), np.full(n, 0.10),
                     "durağan, koridorun doğu ucu (4,3;0,1): robot x=-1,9...-1,0 iken 5,2-6,2 m"))
    # f) ÇOK YAKIN: tur başında rotanın 0,36 m sağında
    i0 = 45
    yw = np.radians(v.Pt[i0, 2])
    fx = v.Pt[i0, 0] + 0.36 * np.cos(yw)
    fy = v.Pt[i0, 1] - 0.36 * np.sin(yw)
    nes.append(Nesne("yakin", "Çok yakın", "kup", (90, 170, 255), 25.0, np.full(n, fx), np.full(n, fy),
                     "durağan (%.2f;%.2f): tur başında robot 0,36 m yanından geçer" % (fx, fy)))
    # g) DUVAR DİBİ: koridor kuzey duvarının tabanında, duvara ~0,17 m
    gx = 1.6
    ys = np.arange(-0.2, -1.0, -0.01)
    dolu = ~v.serbest_mi(np.full(len(ys), gx), ys, 0.0)
    ydv = ys[np.argmax(dolu)] + 0.025                 # güney duvarın üst kenarı (ilk dolu hücrenin üst sınırı)
    gy = ydv + 0.17
    nes.append(Nesne("duvar", "Duvar dibi", "silindir", (120, 230, 90), 0.0, np.full(n, gx), np.full(n, gy),
                     "durağan (%.2f;%.2f): koridor güney duvarına 0,17 m (nesne yarıçapı 0,15 m); robot 0,5 m yanından geçer" % (gx, gy)))
    # h) BİLİNMEYEN BÖLGE: robot ilk gördüğünde (kare ~568) haritada bilinmeyen güneydoğu odası
    nes.append(Nesne("bilinmeyen", "Bilinmeyen", "koni", (235, 60, 60), 0.0, np.full(n, 4.3), np.full(n, -3.6),
                     "durağan (4,3;-3,6): güneydoğu odası, ilk kez kuzey kapısından iki kapı boyunca ~4,7 m'den görünür; o anda robotun haritasında bilinmeyen"))
    return nes


def dogrula(v, nes):
    cik = []
    for o in nes:
        ok = v.serbest_mi(o.X, o.Y, 0.0)
        pay = float(v.uzak[v.hucre(o.X, o.Y)].min())
        d = np.hypot(o.X - v.Pt[:, 0], o.Y - v.Pt[:, 1])
        cik.append(dict(kat=o.kat, hepsi_serbest=bool(ok.all()), min_pay_m=pay, robota_en_yakin_m=float(d.min()),
                        en_yakin_kare=int(d.argmin()), yol_m=float(np.sum(np.hypot(np.diff(o.X), np.diff(o.Y))))))
    return cik


if __name__ == "__main__":
    v = ov.Veri("D")
    nes = sahne(v)
    for o, d in zip(nes, dogrula(v, nes)):
        print(o.kat, d)
