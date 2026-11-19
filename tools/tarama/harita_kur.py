"""Telefon taramasından (ar_tarama kaydı) yüzey haritası ve kat planı kurar.

Kullanım: python harita_kur.py <kayit_klasoru> [--voksel 0.02] [--guven 128]
Çıktı (kayıt klasörüne): yuzey.ply (renkli üçgen ağ), plan.png (kuşbakışı), harita.json (ölçüler).

Eksenler: kayıttaki pozlar Unity oturum uzayında (sol el, y yukarı). Burada sağ el
dünyaya çevrilir: (x, y, -z). Görüntü sensör yönünde (manzara); dikey ekrandaki Unity
kamerasına geçiş: unity_x = -y_cv, unity_y = -x_cv, unity_z = z_cv. Bu eşleme ARCore'un
kendi özellik noktalarıyla sınandı (yüzeye medyan uzaklık 1,8 cm; diğer 7 seçenek 3,9-8,1 cm).
"""
import argparse
import json
import os

import cv2
import numpy as np
import open3d as o3d
import open3d.core as o3c
from scipy.spatial.transform import Rotation

M_CV_UNITY = np.array([[0, -1, 0], [-1, 0, 0], [0, 0, 1]], float)
F_SAG_EL = np.diag([1.0, 1.0, -1.0])


def oku(kayit):
    with open(os.path.join(kayit, "kareler.jsonl"), encoding="utf-8") as f:
        return [json.loads(s) for s in f if s.strip()]


def kamera_dunya(k):
    """Kamera (CV ekseni) -> sağ el dünya 4x4."""
    R = Rotation.from_quat([k["qx"], k["qy"], k["qz"], k["qw"]]).as_matrix()
    T = np.eye(4)
    T[:3, :3] = F_SAG_EL @ R @ M_CV_UNITY
    T[:3, 3] = F_SAG_EL @ np.array([k["px"], k["py"], k["pz"]])
    return T


def kare_yukle(kayit, k, gen, guven_esik):
    renk = cv2.imdecode(np.fromfile(os.path.join(kayit, k["jpg"]), np.uint8), cv2.IMREAD_COLOR)
    d = np.fromfile(os.path.join(kayit, k["depth_file"]), np.uint16).reshape(k["depth_h"], k["depth_w"])
    if k.get("conf_file") and os.path.exists(os.path.join(kayit, k["conf_file"])):
        g = np.fromfile(os.path.join(kayit, k["conf_file"]), np.uint8).reshape(k["conf_h"], k["conf_w"])
        d = np.where(g >= guven_esik, d, 0).astype(np.uint16)
    yuk = round(gen * k["intrinsics_h"] / k["intrinsics_w"])
    renk = cv2.cvtColor(cv2.resize(renk, (gen, yuk), interpolation=cv2.INTER_AREA), cv2.COLOR_BGR2RGB)
    d = cv2.resize(d, (gen, yuk), interpolation=cv2.INTER_NEAREST)
    s = gen / k["intrinsics_w"]
    ic = o3d.camera.PinholeCameraIntrinsic(gen, yuk, k["fx"] * s, k["fy"] * s, k["cx"] * s, k["cy"] * s)
    rgbd = o3d.geometry.RGBDImage.create_from_color_and_depth(
        o3d.geometry.Image(np.ascontiguousarray(renk)), o3d.geometry.Image(np.ascontiguousarray(d)),
        depth_scale=1000.0, depth_trunc=4.0, convert_rgb_to_intensity=False)
    return rgbd, ic


def plan_ciz(mesh, zemin_y, hucre=0.025):
    v = np.asarray(mesh.vertices)
    c = np.asarray(mesh.vertex_colors)
    h = v[:, 1] - zemin_y
    x0, z0 = v[:, 0].min() - 0.2, v[:, 2].min() - 0.2
    W = int((v[:, 0].max() + 0.2 - x0) / hucre) + 1
    H = int((v[:, 2].max() + 0.2 - z0) / hucre) + 1
    ix = ((v[:, 0] - x0) / hucre).astype(int)
    iz = ((v[:, 2] - z0) / hucre).astype(int)
    img = np.zeros((H, W, 4), np.uint8)
    zemin = (h > -0.05) & (h < 0.05)
    engel = (h >= 0.10) & (h < 1.9)
    # zemin: açık renk; engel: en yüksek noktanın rengi koyulaştırılmış
    img[iz[zemin], ix[zemin]] = (226, 230, 232, 255)
    sira = np.argsort(h[engel])
    e_ix, e_iz, e_c = ix[engel][sira], iz[engel][sira], c[engel][sira]
    img[e_iz, e_ix, :3] = (e_c[:, ::-1] * 255 * 0.55).astype(np.uint8)
    img[e_iz, e_ix, 3] = 255
    return img, (x0, z0, hucre)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("kayit")
    ap.add_argument("--voksel", type=float, default=0.02)
    ap.add_argument("--guven", type=int, default=0)
    ap.add_argument("--genislik", type=int, default=480)
    a = ap.parse_args()

    kareler = [k for k in oku(a.kayit) if k.get("has_intrinsics") and k.get("depth_file")]
    # Not: bu Open3D sürümünde eski ScalableTSDFVolume boş ağ veriyor; tensör VoxelBlockGrid kullanılıyor.
    cihaz = o3c.Device("CPU:0")
    hacim = o3d.t.geometry.VoxelBlockGrid(
        attr_names=("tsdf", "weight", "color"), attr_dtypes=(o3c.float32, o3c.float32, o3c.float32),
        attr_channels=((1), (1), (3)), voxel_size=a.voksel, block_resolution=16,
        block_count=200000, device=cihaz)
    for k in kareler:
        rgbd, ic = kare_yukle(a.kayit, k, a.genislik, a.guven)
        d_np = np.asarray(rgbd.depth)
        if (d_np > 0).mean() < 0.05:
            continue
        derinlik = o3d.t.geometry.Image(o3c.Tensor(d_np))
        renk = np.asarray(rgbd.color)
        renk = o3d.t.geometry.Image(o3c.Tensor((renk * 255 if renk.max() <= 1 else renk).astype(np.float32)))
        ic_t = o3c.Tensor(ic.intrinsic_matrix, o3c.float64)
        dis_t = o3c.Tensor(np.linalg.inv(kamera_dunya(k)), o3c.float64)
        bloklar = hacim.compute_unique_block_coordinates(derinlik, ic_t, dis_t, 1.0, 4.0)
        hacim.integrate(bloklar, derinlik, renk, ic_t, dis_t, 1.0, 4.0)
    mesh = hacim.extract_triangle_mesh().to_legacy()
    renkler = np.asarray(mesh.vertex_colors)
    if renkler.size and renkler.max() > 1.0:
        mesh.vertex_colors = o3d.utility.Vector3dVector(np.clip(renkler / 255.0, 0, 1))
    mesh.remove_degenerate_triangles()
    mesh.remove_unreferenced_vertices()
    # küçük kopuk parçaları at
    kume, say, _ = mesh.cluster_connected_triangles()
    kume, say = np.asarray(kume), np.asarray(say)
    mesh.remove_triangles_by_mask(say[kume] < 200)
    mesh.remove_unreferenced_vertices()
    mesh.compute_vertex_normals()
    o3d.io.write_triangle_mesh(os.path.join(a.kayit, "yuzey.ply"), mesh)

    zeminler = [k["floor_y"] for k in kareler if k.get("has_floor")]
    zemin_y = float(np.median(zeminler)) if zeminler else float(np.percentile(np.asarray(mesh.vertices)[:, 1], 2))
    plan, (x0, z0, hucre) = plan_ciz(mesh, zemin_y)
    cv2.imencode(".png", plan)[1].tofile(os.path.join(a.kayit, "plan.png"))
    yol = np.array([kamera_dunya(k)[:3, 3] for k in kareler])
    with open(os.path.join(a.kayit, "harita.json"), "w", encoding="utf-8") as f:
        json.dump({"kare": len(kareler), "ucgen": len(mesh.triangles), "zemin_y": zemin_y,
                   "plan": {"x0": x0, "z0": z0, "hucre": hucre, "gen": plan.shape[1], "yuk": plan.shape[0]},
                   "yol": yol.round(3).tolist()}, f)
    print(f"{len(kareler)} kare, {len(mesh.triangles)} üçgen, zemin y={zemin_y:.3f}, plan {plan.shape[1]}x{plan.shape[0]}")


if __name__ == "__main__":
    main()
