"""Derinlik betiklerinin ortak işlevleri (harita okuma, eşdikdörtgen geometri, kare eşleme). Kendi başına çalıştırılmaz.
Geometri stella_vslam/src/stella_vslam/camera/equirectangular.cc:39-52 (convert_point_to_bearing / convert_bearing_to_point) ile birebir:
  lon = (u/cols - 0.5)*2pi ; lat = -(v/rows - 0.5)*pi ; yön = (cos(lat) sin(lon), -sin(lat), cos(lat) cos(lon)) ; kamera: x sağ, y AŞAĞI, z ileri."""
import json
import numpy as np

KOLON, SATIR = 1280, 640


def yon_izgarasi(h, w):
    """h x w eşdikdörtgen görüntünün her pikselinin birim yönü (h,w,3), piksel merkezleri (u+0.5)."""
    u = (np.arange(w) + 0.5) / w
    v = (np.arange(h) + 0.5) / h
    lon = (u - 0.5) * 2 * np.pi
    lat = -(v - 0.5) * np.pi
    LON, LAT = np.meshgrid(lon, lat)
    return np.stack([np.cos(LAT) * np.sin(LON), -np.sin(LAT), np.cos(LAT) * np.cos(LON)], -1), LAT


def yon_to_piksel(b, kolon=KOLON, satir=SATIR):
    """birim yön (...,3) -> (u,v) piksel (kolon x satir görüntüsünde), convert_bearing_to_point."""
    lat = -np.arcsin(np.clip(b[..., 1], -1, 1))
    lon = np.arctan2(b[..., 0], b[..., 2])
    return kolon * (0.5 + lon / (2 * np.pi)), satir * (0.5 - lat / np.pi)


def harita_yukle(yol):
    d = json.load(open(yol))
    P = np.array(d["noktalar"], dtype=np.float64)          # [x,y,z,yerel_mi,id]
    KF = []
    for k in d["anahtar_kareler"]:
        KF.append({"id": int(k["id"]), "t": float(k["t"]), "T_wc": np.array(k["poz_wc"], dtype=np.float64).reshape(4, 4),
                   "gozlem": np.array(k["gozlem"], dtype=np.int64)})
    return P, KF


def kare_numarasi(KF, zamanlar_csv):
    """anahtar kare t (sn) -> kare no (zamanlar.csv: sira,kare,t_ms; en yakın t_ms). Döner: [(kare, fark_ms)]"""
    z = np.loadtxt(zamanlar_csv, delimiter=",", skiprows=1)
    kare, t_ms = z[:, 1].astype(int), z[:, 2]
    out = []
    for k in KF:
        i = int(np.argmin(np.abs(t_ms - k["t"] * 1000.0)))
        out.append((int(kare[i]), float(abs(t_ms[i] - k["t"] * 1000.0))))
    return out


def kameraya(T_wc, X):
    """dünya noktaları (N,3) -> kamera çerçevesi (N,3): p_c = R_cw (p_w - c)."""
    R = T_wc[:3, :3]
    return (X - T_wc[:3, 3]) @ R          # (R^T (x-c))^T = (x-c) R
