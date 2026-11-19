# SVO Pro (uzh-rpg/rpg_svo_pro_open) -- ROS Noetic / Ubuntu 20.04
# README kurulum bolumunun birebir uygulamasi; GTSAM global haritasi YOK (README 1. secenek:
# on uc + kayan pencere arka ucu + dongu kapatma/pose graph).
# Derleme:  docker build -t svo_pro tools/svo   (WSL icinde, bkz. README notlari calistir.sh)
FROM ros:noetic

ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
      git wget ca-certificates build-essential cmake python3-pip \
      python3-catkin-tools python3-vcstool python3-osrf-pycommon python3-rosdep \
      libglew-dev libopencv-dev libyaml-cpp-dev \
      libblas-dev liblapack-dev libsuitesparse-dev \
      ffmpeg python3-opencv python3-numpy python3-scipy python3-matplotlib procps sysstat \
      ros-noetic-cv-bridge ros-noetic-image-transport ros-noetic-nodelet ros-noetic-tf \
      ros-noetic-pcl-ros ros-noetic-rosbag ros-noetic-nav-msgs ros-noetic-sensor-msgs \
      ros-noetic-visualization-msgs ros-noetic-cmake-modules ros-noetic-rostest \
      ros-noetic-roslaunch ros-noetic-rosbash \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /svo_ws
# README: catkin config (Eigen yolu ozellikle belirtiliyor)
RUN catkin config --init --mkdirs --extend /opt/ros/noetic \
      --cmake-args -DCMAKE_BUILD_TYPE=Release -DEIGEN3_INCLUDE_DIR=/usr/include/eigen3

WORKDIR /svo_ws/src
# Son commit'e sabitlendi (4 Agu 2022)
RUN git clone https://github.com/uzh-rpg/rpg_svo_pro_open.git \
 && git -C rpg_svo_pro_open checkout ca371f304637e7fb355cf4624d0a02da4e3da220

# dependencies.yaml SSH (git@github.com:) adresleri kullaniyor; anahtarsiz https'e cevir
RUN sed -i 's#git@github.com:#https://github.com/#' rpg_svo_pro_open/dependencies.yaml \
 && vcs-import < ./rpg_svo_pro_open/dependencies.yaml \
 && touch minkindr/minkindr_python/CATKIN_IGNORE \
 && for d in */; do printf '%s %s\n' "$d" "$(git -C $d rev-parse HEAD 2>/dev/null)"; done > /svo_ws/bagimlilik_commitleri.txt \
 && cat /svo_ws/bagimlilik_commitleri.txt

# dongu kapatma sozlugu
RUN cd rpg_svo_pro_open/svo_online_loopclosing/vocabularies && ./download_voc.sh

# arayuz paketi (rqt) gerekmiyor, headless
RUN touch rpg_svo_pro_open/rqt_svo/CATKIN_IGNORE

WORKDIR /svo_ws
# glog_catkin autoreconf kullaniyor (libtoolize eksikti); ceres/gflags icin de yararli
RUN apt-get update && apt-get install -y --no-install-recommends libtool libtool-bin autoconf automake pkg-config unzip curl gfortran && rm -rf /var/lib/apt/lists/*
# eksik ROS paketleri (eigen_conversions vb.) rosdep ile; svo_backend/svo_global_map yerel degil, atlanir
RUN (rosdep init || true) && rosdep update --rosdistro noetic  && apt-get update && rosdep install --from-paths /svo_ws/src --ignore-src -y -r --rosdistro noetic       --skip-keys="svo_backend svo_global_map gtsam" ; rm -rf /var/lib/apt/lists/*
# dbow2_catkin / opengv gibi paketler CMake icinde git@github.com: (SSH) ile klonluyor -> https'e yonlendir
RUN git config --global url."https://github.com/".insteadOf "git@github.com:"  && git config --global --add url."https://github.com/".insteadOf "ssh://git@github.com/"
# RAM sinirli (WSL ~7 GB): paralellik dusuk. Hata gizlenmesin diye tail'e borulamiyor.
RUN catkin build -j4 -p2 > /svo_ws/build.log 2>&1 || (tail -n 120 /svo_ws/build.log; exit 1)
RUN tail -n 15 /svo_ws/build.log
RUN echo "source /svo_ws/devel/setup.bash" >> /root/.bashrc
