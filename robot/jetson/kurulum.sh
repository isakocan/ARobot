#!/bin/bash
# Jetson tek seferlik kurulum. Bilgisayardan şöyle çalıştırılır:
#   ssh -t jetson 'sudo bash ~/kurulum/kurulum.sh'
# Tekrar çalıştırmak güvenlidir; var olan ayarları bozmaz, gerekmedikçe yeniden başlatmaz.
set -euo pipefail

if [ "$(id -u)" != 0 ]; then echo "sudo ile çalıştır: sudo bash $0"; exit 1; fi
KULLANICI="${SUDO_USER:-bs}"
KUID="$(id -u "$KULLANICI")"
KAYNAK="$(cd "$(dirname "$0")" && pwd)"
AYAR=/etc/robot/kurulum_agi.json
YENIDEN_BASLAT=0

adim() { echo; echo "==> $*"; }

adim "Paketler (seri port, kamera araçları, uzak masaüstü)"
apt-get update -qq || echo "UYARI: apt update hatası (bazı depolar erişilemedi); devam ediliyor"
DEBIAN_FRONTEND=noninteractive apt-get install -y -qq python3-serial v4l-utils x11vnc iw >/dev/null

adim "Arduino seri portu için izin ($KULLANICI → dialout)"
if ! id -nG "$KULLANICI" | tr ' ' '\n' | grep -qx dialout; then
  usermod -aG dialout "$KULLANICI"
  YENIDEN_BASLAT=1
fi

adim "Önceki adlarla kalmış kurulumu kaldır"
if [ -f /etc/systemd/system/wifi-kurulum.service ]; then
  systemctl disable --now wifi-kurulum.service >/dev/null 2>&1 || true
  rm -f /etc/systemd/system/wifi-kurulum.service
fi
rm -rf /opt/robot/wifi_kurulum
if [ -f /etc/robot/wifi_kurulum.json ]; then
  [ -f "$AYAR" ] || mv /etc/robot/wifi_kurulum.json "$AYAR"
  rm -f /etc/robot/wifi_kurulum.json
fi

adim "Robot paneli (Wi-Fi kurulumu, güvenli kapatma)"
install -d /opt/robot/robot_paneli /etc/robot
install -m 755 "$KAYNAK/robot_paneli/robot_paneli.py" /opt/robot/robot_paneli/robot_paneli.py
install -m 644 "$KAYNAK/robot_paneli/ag_girisi.py" /opt/robot/robot_paneli/ag_girisi.py
install -m 644 "$KAYNAK/robot_paneli/panel.html" /opt/robot/robot_paneli/panel.html
if [ ! -f "$AYAR" ]; then
  MAC="$(tr -d ':' < /sys/class/net/wlan0/address | tail -c 5 | tr a-f A-F)"
  SIFRE="$(tr -dc 'abcdefghjkmnpqrstuvwxyz23456789' < /dev/urandom | head -c 10 || true)"
  printf '{"ssid": "Robot-%s", "sifre": "%s"}\n' "$MAC" "$SIFRE" > "$AYAR"
fi
chown root:"$KULLANICI" "$AYAR"
chmod 640 "$AYAR"
# Kurulum ağındayken her alan adı robota çıksın → telefon "ağda oturum aç" sayfasını açar
echo 'address=/#/10.42.0.1' > /etc/NetworkManager/dnsmasq-shared.d/kurulum-sayfasi.conf
# Wi-Fi güç tasarrufu kapalı: açıkken kart uyuklar, gelen paketler (sürüş komutu, görüntü
# isteği) yüzlerce ms bekleyebilir. Ayar sonraki bağlantılar için, iw de şimdiki için.
printf '[connection]\nwifi.powersave = 2\n' > /etc/NetworkManager/conf.d/wifi-guc-tasarrufu-kapali.conf
iw dev wlan0 set power_save off 2>/dev/null || true
systemctl reload NetworkManager 2>/dev/null || true   # ayarı okur, bağlantıyı koparmaz
cat > /etc/systemd/system/robot-paneli.service <<EOF
[Unit]
Description=Robot paneli (Wi-Fi kurulumu, guvenli kapatma)
After=NetworkManager.service
Wants=NetworkManager.service

[Service]
ExecStart=/usr/bin/python3 /opt/robot/robot_paneli/robot_paneli.py
Restart=always
RestartSec=3

[Install]
WantedBy=multi-user.target
EOF

adim "Monitörsüz masaüstü çözünürlüğü (1920x1080)"
if ! grep -q 'Virtual 1920 1080' /etc/X11/xorg.conf; then
  cp -n /etc/X11/xorg.conf /etc/X11/xorg.conf.yedek
  cat >> /etc/X11/xorg.conf <<'EOF'

# Monitör takılı değilken uzak masaüstü 640x480'de kalmasın
Section "Screen"
    Identifier  "Default Screen"
    Device      "Tegra0"
    DefaultDepth 24
    SubSection "Display"
        Depth   24
        Virtual 1920 1080
    EndSubSection
EndSection
EOF
  YENIDEN_BASLAT=1
fi

adim "Uzak masaüstü (VNC, yalnızca SSH tüneliyle erişilir)"
cat > /etc/systemd/system/uzak-masaustu.service <<EOF
[Unit]
Description=Uzak masaustu (x11vnc, yalnizca localhost)
After=display-manager.service

[Service]
User=$KULLANICI
ExecStart=/usr/bin/x11vnc -display :0 -auth /run/user/$KUID/gdm/Xauthority -localhost -nopw -forever -shared -noxdamage -repeat -rfbport 5900 -quiet
Restart=always
RestartSec=3

[Install]
WantedBy=graphical.target
EOF

systemctl daemon-reload
systemctl enable robot-paneli.service uzak-masaustu.service >/dev/null 2>&1
systemctl restart robot-paneli.service uzak-masaustu.service

adim "Bitti"
python3 - "$AYAR" <<'EOF'
import json, sys
a = json.load(open(sys.argv[1]))
print("  Robotun kurulum ağı : " + a["ssid"])
print("  Kurulum ağı şifresi : " + a["sifre"])
EOF
if [ "$YENIDEN_BASLAT" = 1 ]; then
  echo "  Ekran çözünürlüğü / seri port izni için yeniden başlatma gerekiyor."
  echo "  10 saniye içinde yeniden başlatılıyor (iptal: Ctrl+C)..."
  sleep 10
  reboot
fi
