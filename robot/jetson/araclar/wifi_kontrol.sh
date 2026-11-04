#!/bin/bash
# Robotun Wi-Fi'ını yazılım tarafında kontrol eder; hiçbir ayarı değiştirmez (sudo gerekmez).
#   bash ~/robot/araclar/wifi_kontrol.sh            # bir kez
#   bash ~/robot/araclar/wifi_kontrol.sh izle       # 1 sn'de bir sinyal / hız / yeniden deneme (Ctrl+C ile çık)
# Bakılanlar: güç tasarrufu, gönderme gücü ve ülke kodu, sürücü ve kart, bağlantı hızı (MCS), kanal ve
# kanal doluluğu, yeniden gönderme / başarısız paket sayıları. Sonunda kısa bir değerlendirme yazar.
ARAYUZ=${ARAYUZ:-wlan0}

if [ "$1" = "izle" ]; then
  echo "zaman     sinyal  gönderme hızı         alma hızı             yeniden-dene  başarısız"
  onceki_r=0; onceki_f=0
  while true; do
    d=$(iw dev "$ARAYUZ" station dump 2>/dev/null)
    s=$(echo "$d" | awk '/signal:/ {print $2; exit}')
    tx=$(echo "$d" | awk -F'\t' '/tx bitrate:/ {print $3; exit}' | cut -c1-20)
    rx=$(echo "$d" | awk -F'\t' '/rx bitrate:/ {print $3; exit}' | cut -c1-20)
    r=$(echo "$d" | awk '/tx retries:/ {print $3; exit}'); f=$(echo "$d" | awk '/tx failed:/ {print $3; exit}')
    printf "%s  %4s dBm  %-20s  %-20s  %6s/sn  %6s/sn\n" "$(date +%T)" "$s" "$tx" "$rx" \
      "$(( ${r:-0} - onceki_r ))" "$(( ${f:-0} - onceki_f ))"
    onceki_r=${r:-0}; onceki_f=${f:-0}
    sleep 1
  done
fi

baslik() { echo; echo "== $*"; }

baslik "Kart ve sürücü"
surucu=$(basename "$(readlink -f /sys/class/net/$ARAYUZ/device/driver 2>/dev/null)" 2>/dev/null)
echo "arayüz $ARAYUZ, sürücü: ${surucu:-bilinmiyor}"
lspci 2>/dev/null | grep -i -E "network|wireless" || lsusb 2>/dev/null | grep -i -E "wireless|wlan|802.11|realtek|intel"
[ "$surucu" = "iwlwifi" ] && echo "iwlmvm power_scheme: $(cat /sys/module/iwlmvm/parameters/power_scheme 2>/dev/null || echo '?') (1 = hep uyanık, 2 = dengeli)"

baslik "Güç tasarrufu (açık olmamalı)"
iw dev "$ARAYUZ" get power_save 2>/dev/null || echo "okunamadı"
ls /etc/NetworkManager/conf.d/ 2>/dev/null | grep -i power && cat /etc/NetworkManager/conf.d/*power* 2>/dev/null

baslik "Gönderme gücü ve ülke kodu"
iw dev "$ARAYUZ" info 2>/dev/null | grep -E "txpower|channel|type"
iw reg get 2>/dev/null | grep -E "country|\(2400|\(5[0-9]{3}" | head -8

baslik "Bağlantı"
iw dev "$ARAYUZ" link 2>/dev/null
iw dev "$ARAYUZ" station dump 2>/dev/null | grep -E "signal|tx bitrate|rx bitrate|tx retries|tx failed|beacon loss|connected time"

baslik "Kanal doluluğu (busy / active: yüksekse kanal kalabalık)"
iw dev "$ARAYUZ" survey dump 2>/dev/null | awk '/in use/ {u=1} u && /frequency|noise|active time|busy time/ {print} /Survey data/ && u {exit}' | head -6

baslik "Değerlendirme"
ps_=$(iw dev "$ARAYUZ" get power_save 2>/dev/null | awk '{print $NF}')
sinyal=$(iw dev "$ARAYUZ" link 2>/dev/null | awk '/signal:/ {print $2}')
frek=$(iw dev "$ARAYUZ" link 2>/dev/null | awk '/freq:/ {print int($2)}')
txh=$(iw dev "$ARAYUZ" link 2>/dev/null | awk '/tx bitrate:/ {print int($3)}')
ulke=$(iw reg get 2>/dev/null | awk '/country/ {print $2; exit}' | tr -d ':')
[ "$ps_" = "on" ] && echo "- Güç tasarrufu AÇIK: kurulum.sh'yi sudo ile yeniden çalıştır (ya da: sudo iw dev $ARAYUZ set power_save off)."
[ "$ps_" = "off" ] && echo "- Güç tasarrufu kapalı (iyi)."
[ "$ulke" = "00" ] && echo "- Ülke kodu 00 (dünya): bazı kanallar ve gönderme gücü kısık olabilir. TR ayarı açılışta uygulanıyor mu bak."
[ -n "$ulke" ] && [ "$ulke" != "00" ] && echo "- Ülke kodu $ulke."
if [ -n "$sinyal" ]; then
  if [ "$sinyal" -ge -60 ]; then echo "- Sinyal $sinyal dBm: iyi.";
  elif [ "$sinyal" -ge -70 ]; then echo "- Sinyal $sinyal dBm: orta; 4 Mbit görüntü sorunsuz olmalı.";
  elif [ "$sinyal" -ge -78 ]; then echo "- Sinyal $sinyal dBm: zayıf; görüntü kalitesi düşebilir (uyarlanabilir görüntü devreye girer).";
  else echo "- Sinyal $sinyal dBm: çok zayıf; anten bağlantılarını ve konumunu kontrol et."; fi
fi
[ -n "$frek" ] && { [ "$frek" -lt 3000 ] && echo "- 2,4 GHz'te ($frek MHz)." || echo "- 5 GHz'te ($frek MHz)."; }
[ -n "$txh" ] && [ "$txh" -lt 20 ] && echo "- Gönderme hızı $txh Mbit/s: düşük; 4 Mbit görüntü sınırda kalır."
exit 0
