#!/bin/bash
# stella_vslam yerel kurulumu (WSL Ubuntu-22.04, root). Resmi belge: stella-cv.readthedocs.io/en/latest/installation.html
# Fark: Eigen ve OpenCV Ubuntu deposundan (apt), belge kaynaktan derlemeyi öneriyor ama yalnız "Eigen>=3.3, OpenCV>=3.3.1" şartı var.
set -e
export DEBIAN_FRONTEND=noninteractive
apt-get update -y
apt-get install -y --no-install-recommends build-essential pkg-config cmake git wget curl unzip ca-certificates \
  libatlas-base-dev libsuitesparse-dev gfortran libeigen3-dev libopencv-dev libtbb-dev libyaml-cpp-dev libgflags-dev \
  sqlite3 libsqlite3-dev binutils-dev python3 python3-numpy libgl-dev libopengl-dev
mkdir -p /root/slam && cd /root/slam
[ -d FBoW ] || git clone https://github.com/stella-cv/FBoW.git
mkdir -p FBoW/build && cd FBoW/build && cmake -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=/usr/local .. && make -j3 && make install && cd /root/slam
[ -d g2o ] || git clone https://github.com/RainerKuemmerle/g2o.git
cd g2o && git checkout 20230223_git && mkdir -p build && cd build
cmake -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=/usr/local -DBUILD_SHARED_LIBS=ON -DBUILD_UNITTESTS=OFF -DG2O_USE_CHOLMOD=OFF -DG2O_USE_CSPARSE=ON -DG2O_USE_OPENGL=OFF -DG2O_USE_OPENMP=OFF -DG2O_BUILD_APPS=OFF -DG2O_BUILD_EXAMPLES=OFF -DG2O_BUILD_LINKED_APPS=OFF ..
make -j3 && make install && cd /root/slam
ldconfig
[ -d stella_vslam ] || git clone --recursive https://github.com/stella-cv/stella_vslam.git
mkdir -p stella_vslam/build && cd stella_vslam/build && cmake -DCMAKE_BUILD_TYPE=RelWithDebInfo .. && make -j3 && make install && cd /root/slam
ldconfig
[ -d stella_vslam_examples ] || git clone --recursive https://github.com/stella-cv/stella_vslam_examples.git
mkdir -p stella_vslam_examples/build && cd stella_vslam_examples/build && cmake -DCMAKE_BUILD_TYPE=RelWithDebInfo -DUSE_PANGOLIN_VIEWER=OFF -DUSE_IRIDESCENCE_VIEWER=OFF -DUSE_SOCKET_PUBLISHER=OFF .. && make -j3
[ -f /root/slam/orb_vocab.fbow ] || wget -q -O /root/slam/orb_vocab.fbow https://github.com/stella-cv/FBoW_orb_vocab/raw/main/orb_vocab.fbow
echo KURULUM_BITTI
