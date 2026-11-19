#!/usr/bin/env bash
# stella_vslam_dense'i bir kaydın eşdikdörtgen videosuyla çalıştırır (WSL Ubuntu 22.04 içinde).
# Önce Windows'ta: python tools/haritalama/esdikdortgen.py <kayit_klasoru>
# Sonra:  wsl -d Ubuntu-22.04 -- bash tools/haritalama/calistir.sh /mnt/c/.../kayitlar/<kayit> [--izle] [ek run_video_slam seçenekleri]
#   --izle : canlı 3B görüntüleyici (tarayıcıda http://localhost:8080), gerçek hızda; bitince sayfadaki "Terminate SLAM" kaydeder ve kapatır
# Çıktılar <kayit>/harita/ altına: yogun.ply (yoğun nokta bulutu), harita.msg (harita), izler/ (yörünge), log.txt
set -euo pipefail
KAYIT=${1:?kayit klasoru}; shift
IZLE=0; EK=()
for a in "$@"; do [[ $a == --izle ]] && IZLE=1 || EK+=("$a"); done

KOK=${STELLA_KOK:-/root/slam_dense}          # derlenmiş stella_vslam_dense (resmi depo, değiştirilmemiş)
BURA=$(cd "$(dirname "$0")" && pwd)
H=$KAYIT/harita
[[ -f $H/esdikdortgen.mp4 ]] || { echo "önce esdikdortgen.py çalıştır: $H/esdikdortgen.mp4 yok"; exit 1; }

export LD_LIBRARY_PATH=$KOK/kur/lib:$KOK/stella_vslam_dense/build/lib:$KOK/stella_vslam_dense/build/3rd/patch_match_dense:/usr/local/cuda/lib64:/usr/lib/wsl/lib
export PYTHONPATH=$KOK/stella_vslam_dense/build/lib:$KOK/stella_vslam_dense/tools
# izlerken: kayıt gerçek hızında oynar, video bitince harita ekranda kalır; kaydetmek ve kapatmak için "Terminate SLAM"
if [[ $IZLE == 1 ]]; then SECENEK=(--wait); PY=$BURA/izle.py
else SECENEK=(--auto-term --disable-viewer); PY=$KOK/stella_vslam_dense/tools/run_video_slam.py; fi
mkdir -p "$H/izler"
cd "$H"
set -x
"$KOK/venv/bin/python" "$PY" "${SECENEK[@]}" \
  -v /root/slam/orb_vocab.fbow -c "$BURA/${AYAR:-theta_s_1280.yaml}" \
  -m "$H/esdikdortgen.mp4" --mask "$H/maske.png" \
  -p "$H/yogun.ply" -o "$H/harita.msg" --eval-log-dir "$H/izler" "${EK[@]}" 2>&1 | tee "$H/log.txt"
