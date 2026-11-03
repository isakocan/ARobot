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
# Değişiklik hemen uygulanır, yeniden başlatmak gerekmez.
set -e
KULLANICI=bs

case "$1" in
  terminal)
    loginctl enable-linger "$KULLANICI"
    systemctl set-default multi-user.target
    systemctl disable --now uzak-masaustu.service 2>/dev/null || true
    systemctl isolate multi-user.target
    # masaüstü oturumu kapanınca köprü de durmuş olabilir; linger'lı kullanıcı yöneticisi yeniden başlatır
    sleep 2
    systemctl start "user@$(id -u "$KULLANICI").service"
    echo "Terminal modu: arayüz kapalı. Boş bellek:"
    free -m | awk 'NR==2 {print "  " $7 " MB kullanılabilir"}'
    ;;
  masaustu)
    systemctl set-default graphical.target
    systemctl enable uzak-masaustu.service 2>/dev/null || true
    systemctl isolate graphical.target
    systemctl start uzak-masaustu.service 2>/dev/null || true
    echo "Masaüstü modu: arayüz açık."
    ;;
  *)
    echo "Şu an: $(systemctl get-default)  (multi-user = terminal, graphical = masaüstü)"
    free -m | awk 'NR==2 {print "Kullanılabilir bellek: " $7 " MB"}'
    ;;
esac
