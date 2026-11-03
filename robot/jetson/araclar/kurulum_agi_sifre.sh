#!/bin/bash
# Robotun kendi kurulum ağının (Robot-XXXX) şifresini değiştirir.
#   sudo bash ~/robot/araclar/kurulum_agi_sifre.sh yenisifre
# Şifre 8-63 karakter olmalı (WPA2 kuralı). Panel yeniden başlatılır; robot o an okul / ev ağındaysa
# bağlantı kopmaz, yeni şifre kurulum ağı bir sonraki açılışında geçerli olur.
set -e
AYAR=/etc/robot/kurulum_agi.json
SIFRE="$1"
if [ ${#SIFRE} -lt 8 ] || [ ${#SIFRE} -gt 63 ]; then
  echo "Şifre 8-63 karakter olmalı." >&2
  exit 1
fi
python3 - "$AYAR" "$SIFRE" <<'EOF'
import json, sys
yol, sifre = sys.argv[1], sys.argv[2]
with open(yol) as f:
    d = json.load(f)
d["sifre"] = sifre
with open(yol, "w") as f:
    json.dump(d, f)
print("Kurulum ağı:", d["ssid"], "· yeni şifre kaydedildi")
EOF
systemctl restart robot-paneli.service
