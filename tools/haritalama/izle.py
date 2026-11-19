"""Resmi run_video_slam.py'yi canlı görüntüleyiciyle çalıştırır, yalnızca çizim boyutlarını küçültür.
Neden: tek kamerada harita birimi keyfi; bizim kayıtlarda robotun yolu ~1-2 birim. Viser'in varsayılan
nokta boyu (0.1) ve kamera çerçevesi (0.3) bu ölçekte sahneyi kaplıyor. SLAM'e hiçbir şekilde dokunulmaz.
Seçenekler run_video_slam.py ile aynı; calistir.sh --izle bunu kullanır."""
import os
import sys

from viser import _scene_api

NOKTA = float(os.environ.get("NOKTA_BOYU", "0.008"))
CERCEVE = float(os.environ.get("CERCEVE_BOYU", "0.04"))

_nokta = _scene_api.SceneApi.add_point_cloud
_cerceve = _scene_api.SceneApi.add_camera_frustum


def add_point_cloud(self, name, points, colors, **kw):
    kw.setdefault("point_size", NOKTA if name == "dense_points" else NOKTA * 1.5)
    return _nokta(self, name, points, colors, **kw)


def add_camera_frustum(self, name, fov, aspect, **kw):
    kw.setdefault("scale", CERCEVE * (1.5 if name == "camera" else 1.0))
    return _cerceve(self, name, fov, aspect, **kw)


_scene_api.SceneApi.add_point_cloud = add_point_cloud
_scene_api.SceneApi.add_camera_frustum = add_camera_frustum

import run_video_slam  # noqa: E402  (PYTHONPATH'te stella_vslam_dense/tools)

sys.argv[0] = "run_video_slam.py"
run_video_slam.main()
