#!/bin/bash
# Jetson açılışta masaüstünü (grafik arayüz) başlatsın mı?
#
#   sudo bash ~/robot/araclar/ekran_modu.sh terminal    arayüz yok: ~0,5-0,8 GB RAM ve biraz işlemci boşa çıkar
#   sudo bash ~/robot/araclar/ekran_modu.sh masaustu    arayüz geri gelir (Jetson Ekranı / uzak masaüstü çalışır)
#   sudo bash ~/robot/araclar/ekran_modu.sh             şu anki mod
#
# Terminal modunda:
#   - Robot paneli, Wi-Fi, SSH zaten arayüzden bağımsız çalışır.
#   - Sürüş köprüsü bs kullanıcısının servisidir ve bugüne kadar masaüstüne otomatik girişle başlıyordu;
#     "linger" açılır, böylece kimse giriş yapmadan açılışta başlar.
#   - Uzak masaüstü (x11vnc) kapatılır; ekran olmadığı için işe yaramaz.
#   - Wi-Fi profilleri sisteme ait yapılır: masaüstünden kurulan profil yalnız o kullanıcının oturumu açıkken
#     bağlanabiliyordu; arayüz kapanınca robot ağdan düşüyordu (3 Kasım 2026'da böyle oldu).
# Değişiklik hemen uygulanır. "systemctl isolate" kullanılmaz: Wi-Fi'ın kullandığı wpa_supplicant gibi
# hizmetleri de durduruyordu; yalnız ekran yöneticisi durdurulur.
set -e
KULLANICI=bs

case "$1" in
  terminal)
    loginctl enable-linger "$KULLANICI"
    # Wi-Fi profilleri herkese açık olsun ve şifreleri sistemde saklansın (oturum olmadan da bağlansın)
    nmcli -t -f NAME,TYPE con show | while IFS=: read -r ad tur; do
      [ "$tur" = "802-11-wireless" ] || continue
      nmcli con modify "$ad" connection.permissions "" 2>/dev/null || true
      nmcli -s -g 802-11-wireless-security.psk con show "$ad" >/dev/null 2>&1 &&         nmcli con modify "$ad" 802-11-wireless-security.psk-flags 0 2>/dev/null || true
    done
    systemctl set-default multi-user.target
    systemctl disable --now uzak-masaustu.service 2>/dev/null || true
    systemctl stop display-manager.service
    # masaüstü oturumu kapanınca köprü de durmuş olabilir; linger'lı kullanıcı yöneticisi yeniden başlatır
    sleep 2
    systemctl start "user@$(id -u "$KULLANICI").service"
    echo "Terminal modu: arayüz kapalı. Boş bellek:"
    free -m | awk 'NR==2 {print "  " $7 " MB kullanılabilir"}'
    ;;
  masaustu)
    systemctl set-default graphical.target
    systemctl enable uzak-masaustu.service 2>/dev/null || true
    systemctl start display-manager.service
    systemctl start uzak-masaustu.service 2>/dev/null || true
    echo "Masaüstü modu: arayüz açık."
    ;;
  *)
    echo "Şu an: $(systemctl get-default)  (multi-user = terminal, graphical = masaüstü)"
    free -m | awk 'NR==2 {print "Kullanılabilir bellek: " $7 " MB"}'
    ;;
esac
