#!/bin/bash
# kurulum.sh'in devamı: g2o cmake yapılandırması OpenGL başlıklarını istedi (libgl-dev, libopengl-dev eklendi).
set -e
export DEBIAN_FRONTEND=noninteractive

cd /root/slam
ldconfig
[ -d stella_vslam_examples ] || git clone --recursive https://github.com/stella-cv/stella_vslam_examples.git
mkdir -p stella_vslam/build && cd stella_vslam/build && cmake -DCMAKE_BUILD_TYPE=RelWithDebInfo .. && make -j3 && make install && cd /root/slam
ldconfig
mkdir -p stella_vslam_examples/build && cd stella_vslam_examples/build && cmake -DCMAKE_BUILD_TYPE=RelWithDebInfo -DUSE_PANGOLIN_VIEWER=OFF -DUSE_IRIDESCENCE_VIEWER=OFF -DUSE_SOCKET_PUBLISHER=OFF .. && make -j3
[ -f /root/slam/orb_vocab.fbow ] || wget -q -O /root/slam/orb_vocab.fbow https://github.com/stella-cv/FBoW_orb_vocab/raw/main/orb_vocab.fbow
echo KURULUM_BITTI
