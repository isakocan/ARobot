"""stella haritası + kayıt videosundan tarayıcıda izlenecek örnek hazırlar (kat planı, 3B bulut, konum, AR).

Kullanım:
  python ornek_hazirla.py <cikti_klasoru> <ad> <harita_kaydi> [--kosu <kosu_kaydi>] [--baslik "..."]
    harita_kaydi : haritası çıkarılmış kayıt (harita/harita.msg, harita/izler/)
    --kosu       : bu haritada konum bulma modunda çalıştırılmış başka kayıt (harita/konum/ altı)
Yazar: <cikti>/veri/<ad>/{veri.json, video.mp4, plan.png} ve <cikti>/veri/katalog.js'e ekler.

Hizalama (yerçekimi ve metre): stella'nın birimi keyfi. Yukarı yön = kamera yolunun düzleminin normali
(kamera düz takılı, robot düz zeminde). Zemin = kameranın altındaki yoğun noktaların yükseklik dağılımının
tepesi (parlak zeminde yansıma noktaları zeminin altında kuyruk yapar, tepe zemindir). Ölçek: kamera
yerden KAMERA_Y metre. Dünya: y yukarı, metre, başlangıç = ilk karenin altındaki zemin, ilk bakış -z."""
import argparse
import base64
import json
import os
import shutil
import sqlite3
import subprocess
import sys

import cv2
import numpy as np
import imageio_ffmpeg

KAMERA_Y = 0.17   # mercek yerden yüksekliği (m), gerçek robotta ölçüldü
FPS = 15
PLAN_HUCRE = 0.025  # m/piksel
YAKIN_ESIK = 0.40   # m
ENGEL_UST = 0.35    # m: robot boyu kadar dilim; üstü (sarkan yapraklar, masa üstü, lambalar) plana girmez


def q2r(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def r2q(R):
    t = np.trace(R)
    if t > 0:
        s = np.sqrt(t + 1) * 2
        return np.array([(R[2, 1] - R[1, 2]) / s, (R[0, 2] - R[2, 0]) / s, (R[1, 0] - R[0, 1]) / s, s / 4])
    i = np.argmax(np.diag(R))
    j, k = (i + 1) % 3, (i + 2) % 3
    s = np.sqrt(1 + R[i, i] - R[j, j] - R[k, k]) * 2
    q = np.zeros(4)
    q[i] = s / 4
    q[j] = (R[j, i] + R[i, j]) / s
    q[k] = (R[k, i] + R[i, k]) / s
    q[3] = (R[k, j] - R[j, k]) / s
    return q


def yol_oku(yol):
    """TUM: t x y z qx qy qz qw (kameradan dünyaya). Döner: kare indeksi, konum, dönme."""
    if not os.path.isfile(yol):  # hiç konum bulunamadıysa stella dosyayı yazmıyor
        return np.zeros(0, int), np.zeros((0, 3)), np.zeros((0, 3, 3))
    tr = np.loadtxt(yol, ndmin=2)
    idx = np.round(tr[:, 0] * FPS).astype(int)
    return idx, tr[:, 1:4], np.array([q2r(q) for q in tr[:, 4:8]])


def harita_oku(db_yol):
    db = sqlite3.connect(db_yol)
    kf = {}
    for i, b in db.execute("select id, pose_cw from keyframes"):
        M = np.frombuffer(b, np.float64).reshape(4, 4).T
        kf[i] = -M[:3, :3].T @ M[:3, 3]
    P, C, K = [], [], []
    for p, c, k in db.execute("select pos_w, color, ref_keyfrm from dense_points"):
        P.append(np.frombuffer(p, np.float64))
        C.append(np.frombuffer(c, np.uint8))
        K.append(k)
    L = np.array([np.frombuffer(b, np.float64) for (b,) in db.execute("select pos_w from landmarks")])
    return np.array(P), np.array(C), np.array(K), kf, L


def hizalama_bul(t, R, P, olcek_zorla=None):
    m = t.mean(0)
    up_kam = -R[:, :, 1].mean(0)
    up_kam /= np.linalg.norm(up_kam)
    _, s, vt = np.linalg.svd(t - m)
    n = vt[2] * np.sign(vt[2] @ up_kam)
    # yol düzlemsel değilse (olduğu yerde dönme) kameranın yukarısı kullanılır
    up = n if s[1] > 3 * s[2] and np.degrees(np.arccos(np.clip(n @ up_kam, -1, 1))) < 8 else up_kam
    h = (P - m) @ up
    alt = h[(h < -0.01) & (h > -0.6)]
    hist, e = np.histogram(alt, bins=120, range=(-0.6, 0))
    hist = np.convolve(hist, np.ones(3) / 3, mode="same")
    zemin = e[np.argmax(hist)] + (e[1] - e[0]) / 2
    if olcek_zorla:  # ölçek başka haritadan aktarıldıysa zemin kamera yüksekliğinden gelir
        zemin = -KAMERA_Y / olcek_zorla
    olcek = KAMERA_Y / -zemin
    ileri = R[0][:, 2] - (R[0][:, 2] @ up) * up
    ileri /= np.linalg.norm(ileri)
    z = -ileri
    x = np.cross(up, z)
    B = np.stack([x, up, z])                         # satırlar: dünya eksenleri (stella koordinatında)
    koken = t[0] - (((t[0] - m) @ up) - zemin) * up
    tavan = np.percentile(h, 99.5)
    print(f"  yol yükseklik sapması {((t - m) @ up).std() * KAMERA_Y / -zemin * 100:.1f} cm, tekil değerler {np.round(s, 2)}")
    return dict(B=B, koken=koken, olcek=olcek, zemin_birim=float(zemin),
                tavan_m=float((tavan - zemin) * olcek), yukari_kaynak="yol" if up is n else "kamera")


def olcek_aktar(kaynak, hedef_olcek, klasor="konum"):
    """kaynak kaydın kendi haritasındaki yolu ile bu haritadaki konum yolu arasındaki ölçek (Umeyama)."""
    i1, t1, _ = yol_oku(os.path.join(kaynak, "harita", "izler", "frame_trajectory.txt"))
    i2, t2, _ = yol_oku(os.path.join(kaynak, "harita", klasor, "izler", "frame_trajectory.txt"))
    ortak, a1, a2 = np.intersect1d(i1, i2, return_indices=True)
    A, Bp = t2[a2], t1[a1]               # bu harita -> kaynak harita
    ma, mb = A.mean(0), Bp.mean(0)
    U, S, Vt = np.linalg.svd((Bp - mb).T @ (A - ma) / len(A))
    d = np.sign(np.linalg.det(U @ Vt))
    D = np.diag([1, 1, d])
    s = np.trace(np.diag(S) @ D) / ((A - ma) ** 2).sum(1).mean()
    Rr = U @ D @ Vt
    hata = np.linalg.norm((s * (A - ma) @ Rr.T + mb) - Bp, axis=1)
    print(f"  ölçek aktarımı: {len(ortak)} ortak kare, oran {s:.4f}, hizalama hatası ort {hata.mean() * hedef_olcek * 100:.1f} cm")
    return s * hedef_olcek


def donustur(Hz, p):
    return (p - Hz["koken"]) @ Hz["B"].T * Hz["olcek"]


C3 = np.diag([1.0, -1.0, -1.0])   # three.js kamerası (x sağ, y yukarı, -z ileri) -> stella kamerası


def plan_ciz(Pw, renk, kf_w, Ktop, yolw):
    """Kuşbakışı doluluk planı: anahtar kareden noktaya ışın = boş, engel yüksekliğindeki nokta = dolu."""
    yuk = Pw[:, 1]
    xs = np.concatenate([Pw[:, 0], yolw[:, 0]])
    zs = np.concatenate([Pw[:, 2], yolw[:, 2]])
    lo = np.percentile(xs, 0.5), np.percentile(zs, 0.5)
    hi = np.percentile(xs, 99.5), np.percentile(zs, 99.5)
    pay = 0.6
    x0, z0 = lo[0] - pay, lo[1] - pay
    G = int((hi[0] - x0 + pay) / PLAN_HUCRE) + 1
    Y = int((hi[1] - z0 + pay) / PLAN_HUCRE) + 1
    bos = np.zeros((Y, G), np.float32)
    dolu = np.zeros((Y, G), np.float32)

    def pk(x, z):
        return int((x - x0) / PLAN_HUCRE), int((z - z0) / PLAN_HUCRE)
    engel = (yuk > 0.08) & (yuk < ENGEL_UST)
    # yalnızca engel yüksekliğindeki noktalara ışın: tavana giden ışınlar duvarın üstünden aşıp yıldız yapıyor
    aday = np.nonzero(engel)[0]
    sec = np.random.default_rng(0).permutation(aday)[:80000]
    for i in sec:
        if Ktop[i] not in kf_w:
            continue
        ax_, az_ = kf_w[Ktop[i]][[0, 2]]
        d = np.array([Pw[i, 0] - ax_, Pw[i, 2] - az_])
        L = np.linalg.norm(d)
        if L < 0.15:
            continue
        son = np.array([ax_, az_]) + d * (L - 0.08) / L       # engelin 8 cm önünde dur
        cv2.line(bos, pk(ax_, az_), pk(*son), 1.0, 1)
    for i in np.nonzero(engel)[0]:
        u, v = pk(Pw[i, 0], Pw[i, 2])
        if 0 <= u < G and 0 <= v < Y:
            dolu[v, u] += 1
    bos = cv2.GaussianBlur(bos, (0, 0), 1.5)
    dolu = cv2.GaussianBlur(dolu, (0, 0), 1.0)
    serbest = bos > 0.4
    duvar = dolu > 0.6
    # robotun geçtiği yer kesin boş
    yolm = np.zeros((Y, G), np.uint8)
    pts = np.array([pk(x, z) for x, z in yolw[:, [0, 2]]]).reshape(-1, 1, 2)
    cv2.polylines(yolm, [pts], False, 1, int(0.30 / PLAN_HUCRE))
    serbest |= yolm > 0
    serbest = cv2.morphologyEx(serbest.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8)) > 0
    duvar &= ~(yolm > 0)
    # renkler (BGRA): bilinmeyen saydam, boş = sıcak açık, duvar = koyu mürekkep
    img = np.zeros((Y, G, 4), np.uint8)
    img[serbest] = (214, 224, 230, 255)
    kenar = cv2.morphologyEx(serbest.astype(np.uint8), cv2.MORPH_GRADIENT, np.ones((3, 3), np.uint8)) > 0
    img[kenar & ~serbest] = (150, 160, 168, 255)
    # engeller kuşbakışı fotoğraf gibi: her hücrede en yüksek noktanın gerçek rengi (koltuk yeşil, saksı kahve)
    renkli = np.zeros((Y, G, 4), np.uint8)
    sira = np.nonzero(engel)[0]
    sira = sira[np.argsort(yuk[sira])]
    uu = ((Pw[sira, 0] - x0) / PLAN_HUCRE).astype(int)
    vv = ((Pw[sira, 2] - z0) / PLAN_HUCRE).astype(int)
    rgb = renk[sira][:, ::-1].astype(np.float32) * 0.8
    for du in (-1, 0, 1):
        for dv in (-1, 0, 1):
            u2, v2 = uu + du, vv + dv
            ok = (u2 >= 0) & (u2 < G) & (v2 >= 0) & (v2 < Y)
            renkli[v2[ok], u2[ok], :3] = rgb[ok]
            renkli[v2[ok], u2[ok], 3] = 255
    d = (cv2.dilate(duvar.astype(np.uint8), np.ones((2, 2), np.uint8)) > 0) & (renkli[..., 3] > 0) & ~(yolm > 0)
    img[d] = renkli[d]
    # z aşağı doğru artsın diye görüntü olduğu gibi: satır = z, sütun = x
    return img, dict(x0=float(x0), z0=float(z0), hucre=PLAN_HUCRE, g=G, y=Y)


def b64(a):
    return base64.b64encode(np.ascontiguousarray(a).tobytes()).decode()


def video_yaz(kaynak, hedef):
    ff = imageio_ffmpeg.get_ffmpeg_exe()
    subprocess.run([ff, "-y", "-v", "error", "-i", kaynak, "-c:v", "libx264", "-crf", "21", "-preset", "medium",
                    "-g", "15", "-pix_fmt", "yuv420p", "-movflags", "+faststart", hedef], check=True)


AC_CMD = [  # cmd dosyası ASCII: Türkçe harfler cmd'de bozuluyor
    "@echo off",
    "rem Ornek goruntuleyiciyi acar: bu klasorde yerel sunucu baslatir, tarayicida gosterir.",
    "rem Bitince kucultulmus 'Harita sunucusu' penceresini kapat.",
    'cd /d "%~dp0"',
    'start "Harita sunucusu" /min python sunucu.py 8765',
    "timeout /t 2 >nul",
    'start "" http://127.0.0.1:8765/',
]


def sayfa_kur(cikti):
    bura = os.path.join(os.path.dirname(os.path.abspath(__file__)), "gosterici")
    for f in ("index.html", "app.js", "sunucu.py"):
        shutil.copy(os.path.join(bura, f), os.path.join(cikti, f))
    with open(os.path.join(cikti, "Aç.cmd"), "wb") as f:
        f.write(("\r\n".join(AC_CMD) + "\r\n").encode("ascii"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("cikti")
    ap.add_argument("ad")
    ap.add_argument("harita_kaydi")
    ap.add_argument("--kosu", default=None)
    ap.add_argument("--baslik", default=None)
    ap.add_argument("--aciklama", default="")
    ap.add_argument("--video-yok", action="store_true")
    ap.add_argument("--konum-klasor", default="konum", help="koşu kaydında harita/<bu>/izler okunur")
    ap.add_argument("--olcek-kaynak", default=None, help="kendi haritası + bu haritada konum koşusu olan kayıt")
    a = ap.parse_args()

    hk = os.path.join(a.harita_kaydi, "harita")
    P, Crgb, K, kf, L = harita_oku(os.path.join(hk, "harita.msg"))
    idx_h, t_h, R_h = yol_oku(os.path.join(hk, "izler", "frame_trajectory.txt"))
    # yoğun derinlikte düz duvar/kapı kenarlarında yanlış yakın noktalar çıkıyor (referans kareye çok yakın):
    # gerçek ortamda kameranın YAKIN_ESIK metre dibinde neredeyse hiçbir şey yok, bunlar atılır
    ref_uzak = np.array([np.linalg.norm(P[i] - kf[k]) if k in kf else -1 for i, k in enumerate(K)])
    oz = None
    if a.olcek_kaynak:
        kh = os.path.join(a.olcek_kaynak, "harita")
        P2, _, _, _, _ = harita_oku(os.path.join(kh, "harita.msg"))
        _, t2, R2 = yol_oku(os.path.join(kh, "izler", "frame_trajectory.txt"))
        oz = olcek_aktar(a.olcek_kaynak, hizalama_bul(t2, R2, P2)["olcek"])
    Hz = hizalama_bul(t_h, R_h, P, oz)
    iyi = ref_uzak * Hz["olcek"] > YAKIN_ESIK
    print(f"  yakın/başıboş yoğun nokta atıldı: {(~iyi).sum()} / {len(P)}")
    P, Crgb, K = P[iyi], Crgb[iyi], K[iyi]
    print(f"ölçek {Hz['olcek']:.3f} m/birim, tavan ~{Hz['tavan_m']:.2f} m, yukarı: {Hz['yukari_kaynak']}")

    if a.kosu:
        kd = os.path.join(a.kosu, "harita")
        idx, t, R = yol_oku(os.path.join(kd, a.konum_klasor, "izler", "frame_trajectory.txt"))
        video_kaynak = os.path.join(kd, "esdikdortgen.mp4")
    else:
        idx, t, R = idx_h, t_h, R_h
        video_kaynak = os.path.join(hk, "esdikdortgen.mp4")
    cap = cv2.VideoCapture(video_kaynak)
    n_kare = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    cap.release()

    Pw = donustur(Hz, P).astype(np.float32)
    kf_w = {k: donustur(Hz, v[None])[0] for k, v in kf.items()}
    tw = donustur(Hz, t)
    Rw = np.einsum("ij,njk,kl->nil", Hz["B"], R, C3)
    yol_h_w = donustur(Hz, t_h)

    # kare başına poz (kayıp kareler null); ilk poz sıfır karedir (stella başlatma referansı)
    poz = [None] * n_kare
    for i, p, r in zip(idx, tw, Rw):
        if 0 <= i < n_kare:
            poz[i] = [round(float(v), 4) for v in (*p, *r2q(r))]
    takip = sum(p is not None for p in poz)

    plan, pb = plan_ciz(Pw, Crgb, kf_w, K, yol_h_w)
    hedef = os.path.join(a.cikti, "veri", a.ad)
    os.makedirs(hedef, exist_ok=True)
    cv2.imencode(".png", plan)[1].tofile(os.path.join(hedef, "plan.png"))  # cv2.imwrite Türkçe yolda sessizce başarısız

    # 3B için en çok 250 bin nokta
    sec = np.random.default_rng(1).permutation(len(Pw))[:250000]
    lm = donustur(Hz, L).astype(np.float32)
    veri = dict(
        ad=a.ad, baslik=a.baslik or a.ad, aciklama=a.aciklama,
        tur="konum" if a.kosu else "harita", fps=FPS, kare=n_kare, takip=takip,
        olcek=Hz["olcek"], tavan_m=Hz["tavan_m"], kamera_y=KAMERA_Y,
        poz=poz, plan=pb, harita_yolu=[[round(float(x), 3), round(float(z), 3)] for x, _, z in yol_h_w[::3]],
        nokta=b64(Pw[sec]), renk=b64(Crgb[sec]), seyrek=b64(lm),
    )
    with open(os.path.join(hedef, "veri.json"), "w") as f:
        json.dump(veri, f, separators=(",", ":"))
    if not a.video_yok:
        video_yaz(video_kaynak, os.path.join(hedef, "video.mp4"))

    sayfa_kur(a.cikti)
    kat = os.path.join(a.cikti, "veri", "katalog.json")
    liste = json.load(open(kat, encoding="utf-8")) if os.path.isfile(kat) else []
    liste = [x for x in liste if x["ad"] != a.ad]
    liste.append(dict(ad=a.ad, baslik=veri["baslik"], tur=veri["tur"], aciklama=a.aciklama,
                      takip=takip, kare=n_kare))
    json.dump(liste, open(kat, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(f"{a.ad}: {takip}/{n_kare} kare konumlu, {len(Pw)} yoğun nokta, plan {pb['g']}x{pb['y']}")


if __name__ == "__main__":
    main()
