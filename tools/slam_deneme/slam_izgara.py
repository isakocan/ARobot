"""stella_vslam'in kendi verisinden 2B kuşbakışı doluluk ızgarası (segmentasyon YOK).
Girdi: köprünün 'state' çıktısı (noktalar [x,y,z,yerel,id], anahtar_kareler [{id,poz_wc,gozlem:[nokta_id]}]) ve ölçek (metre/harita birimi).
Dünya çerçevesi: x sağ, y AŞAĞI, z ileri (kamera ilk anahtar kare). Yer düzlemi y = y_zemin; yer üstü yükseklik = (y_zemin - y) * olcek.
  - Dolu kanıtı: yükseklik bandındaki (varsayılan 0,05-0,60 m) nokta sayısı. Zemin dokusu (bandın altı) ve tavan (üstü) engel olmaz.
  - Boş kanıtı: her anahtar karenin gözlediği her noktaya (yatay düzleme izdüşüm) kamera merkezinden ışın; ışın boyunca hücreler boş sayılır (noktadan bir hücre önce biter).
  - Güven: hücre başına (dolu_sayisi, bos_sayisi). Durum: dolu_sayisi >= esik_dolu ve dolu_sayisi >= bos_sayisi*oran -> dolu; bos_sayisi>0 -> boş; yoksa bilinmiyor.
izgara_hesapla(durum, olcek, ...) -> dict(origin_x_m, origin_z_m, hucre_m, dolu (int32 HxW), bos (int32 HxW), x_min..., zemin_y, ...)  satır = z (ileri) yukarı doğru artar, sütun = x.
png_yaz(izgara, yol, ...)"""
import numpy as np
import cv2


def izgara_hesapla(durum, olcek, hucre_m=0.20, yukseklik_bandi=(0.05, 0.60), kamera_yuksekligi_m=0.17, max_menzil_m=8.0, zemin_y=None, kenar_m=1.0):
    P = np.array(durum["noktalar"], dtype=np.float64).reshape(-1, 5) if len(durum["noktalar"]) else np.zeros((0, 5))
    KF = durum["anahtar_kareler"]
    if len(KF) == 0 or len(P) == 0:
        return None
    kc = np.array([np.array(k["poz_wc"]).reshape(4, 4)[:3, 3] for k in KF])
    if zemin_y is None:
        zemin_y = float(np.median(kc[:, 1]) + kamera_yuksekligi_m / olcek)
    # kamera yolundan max_menzil_m'den uzak noktalar (sınır dışı, güvenilmez derinlik) hiç alınmaz
    dmin = np.min(np.hypot(P[:, None, 0] - kc[None, :, 0], P[:, None, 2] - kc[None, :, 2]), axis=1) * olcek
    P = P[dmin <= max_menzil_m]
    X = P[:, :3]
    ids = P[:, 4].astype(np.int64)
    yuk = (zemin_y - X[:, 1]) * olcek
    xm, zm = X[:, 0] * olcek, X[:, 2] * olcek
    tum_x = np.concatenate([xm, kc[:, 0] * olcek]); tum_z = np.concatenate([zm, kc[:, 2] * olcek])
    x0, x1 = tum_x.min() - kenar_m, tum_x.max() + kenar_m
    z0, z1 = tum_z.min() - kenar_m, tum_z.max() + kenar_m
    W = int(np.ceil((x1 - x0) / hucre_m)); H = int(np.ceil((z1 - z0) / hucre_m))
    dolu = np.zeros((H, W), np.int32)
    bos = np.zeros((H, W), np.int32)
    band = (yuk >= yukseklik_bandi[0]) & (yuk <= yukseklik_bandi[1])
    cx = ((xm[band] - x0) / hucre_m).astype(int); cz = ((zm[band] - z0) / hucre_m).astype(int)
    np.add.at(dolu, (cz, cx), 1)
    # ışınlar
    idx_of = {int(i): j for j, i in enumerate(ids)}
    for k, kc_i in zip(KF, kc):
        ox, oz = kc_i[0] * olcek, kc_i[2] * olcek
        js = np.array([idx_of[g] for g in k.get("gozlem", []) if g in idx_of], dtype=int)
        if len(js) == 0:
            continue
        dx, dz = xm[js] - ox, zm[js] - oz
        d = np.hypot(dx, dz)
        ok = (d > 2 * hucre_m) & (d < max_menzil_m)
        for a, b, dd in zip(dx[ok], dz[ok], d[ok]):
            n = int((dd - hucre_m) / (hucre_m * 0.5))
            if n < 1:
                continue
            s = np.linspace(0, (dd - hucre_m) / dd, n)
            gx = ((ox + a * s - x0) / hucre_m).astype(int); gz = ((oz + b * s - z0) / hucre_m).astype(int)
            hh = np.unique(gz * W + gx)
            bos.ravel()[hh] += 1
    return dict(origin_x_m=float(x0), origin_z_m=float(z0), hucre_m=float(hucre_m), dolu=dolu, bos=bos, zemin_y=float(zemin_y), olcek=float(olcek),
                yukseklik_bandi=list(yukseklik_bandi), kf_xz_m=(kc[:, [0, 2]] * olcek).tolist())


def durum_izgarasi(g, esik_dolu=2, oran=0.5):
    """0 = bilinmiyor, 1 = boş, 2 = dolu"""
    d = np.zeros(g["dolu"].shape, np.uint8)
    d[g["bos"] > 0] = 1
    d[(g["dolu"] >= esik_dolu) & (g["dolu"] >= g["bos"] * oran)] = 2
    return d


def png_yaz(g, yol, yol_yol=None, esik_dolu=2, baslik=""):
    """Kuşbakışı PNG: beyaz = boş (koyuluk = güven), siyah = dolu, gri = bilinmiyor; kırmızı = robot yolu (anahtar kareler). z yukarı."""
    st = durum_izgarasi(g, esik_dolu)
    H, W = st.shape
    img = np.full((H, W, 3), 128, np.uint8)
    guven = np.clip(g["bos"] / 6.0, 0, 1)
    img[st == 1] = (150 + 105 * guven[st == 1])[:, None].astype(np.uint8)
    img[st == 2] = (0, 0, 0)
    img = img[::-1]
    s = max(1, int(900 / max(H, W)))
    img = cv2.resize(img, (W * s, H * s), interpolation=cv2.INTER_NEAREST)
    kf = np.array(g["kf_xz_m"])
    pts = np.stack([((kf[:, 0] - g["origin_x_m"]) / g["hucre_m"] + 0.5) * s, (H - ((kf[:, 1] - g["origin_z_m"]) / g["hucre_m"] + 0.5)) * s], 1).astype(np.int32)
    cv2.polylines(img, [pts.reshape(-1, 1, 2)], False, (0, 0, 255), 1, cv2.LINE_AA)
    # ölçek çubuğu 1 m
    px = int(s / g["hucre_m"])
    cv2.line(img, (20, img.shape[0] - 20), (20 + px, img.shape[0] - 20), (255, 0, 0), 3)
    cv2.putText(img, "1 m", (20, img.shape[0] - 28), 0, 0.5, (255, 0, 0), 1)
    cv2.putText(img, baslik, (10, 20), 0, 0.5, (0, 128, 255), 1)
    cv2.imwrite(yol, img)
    if yol_yol:
        np.savez_compressed(yol_yol, dolu=g["dolu"], bos=g["bos"], origin=[g["origin_x_m"], g["origin_z_m"]], hucre_m=g["hucre_m"])
