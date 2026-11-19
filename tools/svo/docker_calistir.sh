#!/bin/bash
# WSL (root) icinde: konteynerde komut calistirir. Ornek:
#   docker_calistir.sh python3 /araclar/kayit_bag.py /kayitlar/2026-11-14_19-36-00_5_saksi_turu
R=/mnt/c/Users/PC/Desktop/ARobot
exec docker run --rm -v $R/kayitlar:/kayitlar -v $R/.claude/worktrees/stella-vslam-dense-mapping-5eff80/tools/svo:/araclar svo_pro bash -c "source /opt/ros/noetic/setup.bash; source /svo_ws/devel/setup.bash; $*"
