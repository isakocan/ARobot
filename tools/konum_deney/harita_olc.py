"""Tahmin edilen doluluk haritasını gerçek haritayla karşılaştırır (ölçütler + yan yana görüntü).

Gözlenebilir bölge R (tekrarlanabilir tanım): gerçek haritada, yörüngedeki konumlardan 0,25 derece aralıklı ışınlar atılır;
ışın boş hücrelerde ilerler, ilk dolu (0) ya da harita dışı hücrede durur (o dolu hücre dahil); en çok `gorus_m` (varsayılan 4 m) uzağa.
Işın tarafından geçilen hücreler R'dir. Yani "robot yörüngesinden doğrudan görüş hattı olan, 4 m içindeki hücreler".
Ölçütler yalnızca R içinde hesaplanır (tahmindeki dolu/boş hücreler de R ile kesilir); en yakın-uzaklık ölçütlerinde gerçek/tahmin
dolu kümeleri bütün harita üstündedir."""
import os

import cv2
import numpy as np
from scipy import ndimage as ndi

import harita_kur as hk


def gozlenebilir_bolge(T, grid, konumlar, gorus_m=4.0):
    """T: gerçek harita (0/255/128), grid: Izgara (hücre bilgisi), konumlar: (N,3) x,y,yon. Dönüş R (bool)."""
    H, W = T.shape
    R = np.zeros(T.shape, bool)
    adim = grid.h / 2.0
    t = np.arange(0.0, gorus_m, adim)[None, :]
    aci = np.radians(np.arange(0, 360, 0.25))
    sx, sy = np.sin(aci)[:, None], np.cos(aci)[:, None]
    for x, y in konumlar[:, :2]:
        r, c = grid.hucre_idx(x + sx * t, y + sy * t)
        ic = (r >= 0) & (r < H) & (c >= 0) & (c < W)
        rr = np.clip(r, 0, H - 1)
        cc = np.clip(c, 0, W - 1)
        engel = (T[rr, cc] != 255) | ~ic                          # dolu ya da dışarı
        # ilk engelden SONRAKİ örnekleri at (engel hücresi dahil)
        gecti = np.cumsum(engel, axis=1)
        gor = (gecti == 0) | (engel & (gecti == 1))
        ok = gor & ic
        R[rr[ok], cc[ok]] = True
    return R & (T != 128)


def olc(T, E, R, h=0.05):
    """T, E: uint8 haritalar (0 dolu, 255 boş, 128 bilinmiyor/dışarı). R: gözlenebilir bölge."""
    trueOcc_all = T == 0
    estOcc_all = E == 0
    trueFree = (T == 255) & R
    trueOcc = trueOcc_all & R
    estFree = (E == 255) & R
    estOcc = estOcc_all & R
    d_t_cheb = ndi.distance_transform_cdt(~trueOcc_all, metric="chessboard") if trueOcc_all.any() else np.full(T.shape, 999)
    d_e_cheb = ndi.distance_transform_cdt(~estOcc_all, metric="chessboard") if estOcc_all.any() else np.full(T.shape, 999)
    d_t_euc = ndi.distance_transform_edt(~trueOcc_all) if trueOcc_all.any() else np.full(T.shape, np.inf)
    d_e_euc = ndi.distance_transform_edt(~estOcc_all) if estOcc_all.any() else np.full(T.shape, np.inf)
    s = {}
    inter, uni = (estFree & trueFree).sum(), (estFree | trueFree).sum()
    s["bos_iou"] = float(inter / uni) if uni else float("nan")
    s["bos_geri_cagirma"] = float(inter / trueFree.sum()) if trueFree.sum() else float("nan")
    s["bos_kesinlik"] = float(inter / estFree.sum()) if estFree.sum() else float("nan")
    for tol in (1, 2):
        s["dolu_kesinlik_%d" % tol] = float((d_t_cheb[estOcc] <= tol).mean()) if estOcc.any() else float("nan")
        s["dolu_geri_cagirma_%d" % tol] = float((d_e_cheb[trueOcc] <= tol).mean()) if trueOcc.any() else float("nan")
    s["tahmin_dolu_gercek_dolu_cm"] = float(d_t_euc[estOcc].mean() * h * 100) if estOcc.any() and np.isfinite(d_t_euc).any() else float("nan")
    s["gercek_dolu_tahmin_dolu_cm"] = float(d_e_euc[trueOcc].mean() * h * 100) if trueOcc.any() and estOcc_all.any() else float("nan")
    # AR için kritik iki hata
    s["bos_dolu_sanildi"] = float(((E == 0) & trueFree).sum() / trueFree.sum()) if trueFree.sum() else float("nan")
    s["bos_dolu_sanildi_tol1"] = float((((E == 0) & trueFree) & (d_t_cheb > 1)).sum() / trueFree.sum()) if trueFree.sum() else float("nan")
    s["dolu_bos_sanildi"] = float(((E == 255) & trueOcc).sum() / trueOcc.sum()) if trueOcc.sum() else float("nan")
    s["dolu_bos_sanildi_tol1"] = float((((E == 255) & trueOcc) & (d_e_cheb > 1)).sum() / trueOcc.sum()) if trueOcc.sum() else float("nan")
    s["bos_bilinmiyor"] = float(((E == 128) & trueFree).sum() / trueFree.sum()) if trueFree.sum() else float("nan")
    s["dolu_bilinmiyor"] = float(((E == 128) & trueOcc).sum() / trueOcc.sum()) if trueOcc.sum() else float("nan")
    s["n_bos_gercek"] = int(trueFree.sum())
    s["n_dolu_gercek"] = int(trueOcc.sum())
    s["n_dolu_tahmin"] = int(estOcc.sum())
    s["r_hucre"] = int(R.sum())
    return s


def yan_yana(T, E, R, konumlar, grid, yol, olcek=4, baslik=None, kirp=True):
    """gerçek | tahmin | hata görüntüsü."""
    H, W = T.shape

    def renk(M):
        im = np.full((H, W, 3), 200, np.uint8)                    # bilinmiyor/dışarı
        im[M == 255] = (255, 255, 255)
        im[M == 0] = (30, 30, 30)
        im[(T == 128) & (M == 128)] = (150, 150, 150)
        return im

    a = renk(T)
    b = renk(E)
    c = np.full((H, W, 3), 160, np.uint8)
    d_t = ndi.distance_transform_cdt(~(T == 0), metric="chessboard") if (T == 0).any() else np.full(T.shape, 999)
    d_e = ndi.distance_transform_cdt(~(E == 0), metric="chessboard") if (E == 0).any() else np.full(T.shape, 999)
    dogru_bos = R & (T == 255) & (E == 255)
    dogru_dolu = R & (T == 0) & (d_e <= 1)
    yanlis_dolu = R & (E == 0) & (d_t > 1)                          # kırmızı: boş yer dolu sanıldı (AR nesnesi engellenir)
    kayip_dolu = R & (T == 0) & (E == 255)                          # mavi: dolu yer boş sanıldı (AR nesnesi duvarda çıkar)
    bilinm = R & (E == 128) & (T != 128)
    c[R] = (235, 235, 235)
    c[dogru_bos] = (90, 200, 90)
    c[dogru_dolu] = (30, 120, 30)
    c[bilinm] = (60, 200, 235)                                      # sarı (BGR): bilinmiyor
    c[R & (T == 0) & ~dogru_dolu & ~kayip_dolu & (E == 0)] = (200, 160, 0)   # dolu yakın ama uzak ~ toleransın dışı
    c[yanlis_dolu] = (40, 40, 230)
    c[kayip_dolu] = (230, 80, 40)
    imgs = []
    for im in (a, b, c):
        im = cv2.resize(im, (W * olcek, H * olcek), interpolation=cv2.INTER_NEAREST)
        if konumlar is not None and len(konumlar):
            c_, r_ = grid.dunya_pikseli(konumlar[:, 0], konumlar[:, 1]) if hasattr(grid, "dunya_pikseli") else _px(grid, konumlar)
            pts = np.stack([(c_ + 0.5) * olcek, (r_ + 0.5) * olcek], 1).astype(np.int32)
            cv2.polylines(im, [pts], False, (0, 140, 255), 1, cv2.LINE_AA)
        imgs.append(im)
    if kirp:
        ys, xs = np.where(T != 128)
        if len(ys):
            y0, y1, x0, x1 = max(ys.min() - 4, 0) * olcek, (ys.max() + 5) * olcek, max(xs.min() - 4, 0) * olcek, (xs.max() + 5) * olcek
            imgs = [i[y0:y1, x0:x1] for i in imgs]
    for im, ad in zip(imgs, ("gercek", "tahmin", "hata: kirmizi=bos dolu sanildi, mavi=dolu bos sanildi, sari=bilinmiyor")):
        cv2.putText(im, ad, (6, 16), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 0, 0), 1, cv2.LINE_AA)
    out = np.hstack([imgs[0], np.full((imgs[0].shape[0], 4, 3), 255, np.uint8), imgs[1], np.full((imgs[0].shape[0], 4, 3), 255, np.uint8), imgs[2]])
    if baslik:
        cv2.putText(out, baslik, (6, out.shape[0] - 8), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 0, 160), 1, cv2.LINE_AA)
    os.makedirs(os.path.dirname(yol), exist_ok=True)
    cv2.imwrite(yol, out)
    return out


def _px(grid, konumlar):
    c = (konumlar[:, 0] - grid.x0) / grid.h - 0.5
    r = grid.Hh - 1 - ((konumlar[:, 1] - grid.y0) / grid.h - 0.5)
    return c, r
