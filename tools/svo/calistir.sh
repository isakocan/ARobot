#!/bin/bash
# Konteyner icinde calisir: SVO'yu lens1.bag uzerinde basliksiz kosar, poz/bilgi kaydeder, CPU olcer.
# Kullanim (WSL'den):
#   docker run --rm -v /mnt/c/.../kayitlar:/kayitlar -v /mnt/c/.../tools/svo:/araclar svo_pro \
#       /araclar/calistir.sh KAYIT_ADI [HIZ=1.0] [ETIKET=a] [runlc=true] [autoreinit=true]
KAYIT=$1; HIZ=${2:-1.0}; ETIKET=${3:-a}; LC=${4:-true}
source /opt/ros/noetic/setup.bash; source /svo_ws/devel/setup.bash
CIK=/kayitlar/$KAYIT/svo/$ETIKET; mkdir -p $CIK/iz
roscore > $CIK/roscore.log 2>&1 &
sleep 4
roslaunch /araclar/svo_lens1.launch trace_dir:=$CIK/iz runlc:=$LC autoreinit:=${5:-true} > $CIK/svo.log 2>&1 &
sleep 12
PID=$(pgrep -x svo_node | head -1)
echo "svo_node pid $PID"
(top -b -d 1 -p $PID | awk -v p=$PID '$1==p {print $9, $10; fflush()}' > $CIK/cpu.txt) &
TOPPID=$!
rosbag record -q -O $CIK/cikti.bag /svo/pose_cam/0 /svo/info /svo/loop_closures __name:=kayitci &
sleep 3
mkdir -p $CIK/iz
START=$(date +%s.%N)
rosbag play -r $HIZ /kayitlar/$KAYIT/svo/lens1.bag 2>&1 | tr "\r" "\n" | tail -n 4 > $CIK/play.log
sleep 5
END=$(date +%s.%N)
echo "oynatma suresi $(awk "BEGIN{print $END - $START}") sn" | tee $CIK/oynatma.txt
rosnode kill /kayitci; sleep 2
kill $TOPPID 2>/dev/null
rosnode kill /svo; sleep 2; pkill -f roslaunch; pkill roscore; pkill rosmaster
python3 /araclar/svo_analiz.py cikar $CIK
