#!/bin/bash
# Wi-Fi profillerini kimsenin oturumuna bağlı olmayan (sisteme ait) hâle getirir.
#   sudo bash ~/robot/araclar/ag_onar.sh
# Masaüstünden kurulan profil yalnızca o kullanıcı masaüstüne girmişken kullanılabiliyordu; arayüz kapalıyken
# robot ağa girip hemen düşüyor, sonra kendi kurulum ağını açıyordu (3 Kasım 2026). Şifre de kullanıcının
# anahtarlığında değil sistemde saklanır. Ardından NetworkManager yeniden yüklenir.
set -e
nmcli -t -f NAME,TYPE con show | while IFS=: read -r ad tur; do
  [ "$tur" = "802-11-wireless" ] || continue
  [ "$ad" = "Kurulum-Agi" ] && continue
  nmcli con modify "$ad" connection.permissions "" connection.autoconnect yes
  if nmcli -g 802-11-wireless-security.key-mgmt con show "$ad" | grep -q .; then
    nmcli con modify "$ad" 802-11-wireless-security.psk-flags 0 2>/dev/null || true
  fi
  echo "düzeltildi: $ad"
done
systemctl reload NetworkManager
echo "Tamam. Kayıtlı ağlar artık arayüz kapalıyken de bağlanır."
