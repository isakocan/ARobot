# Mimari (taslak, değişebilir)

```
Theta S ──UVC──► Jetson ──GStreamer H.264/RTP (UDP)──► Telefon (Unity)
                  │  ▲                                    │  ▲
         ROS 2    │  └── kontrol: sürüş/tare/LED/ayar ────┘  │
   (motor, sensör,│                                          │
    ArUco)        └──── telemetri + algılamalar (JSON/UDP) ──┘
```

- **Jetson**: video (GStreamer, ROS dışı) + ROS 2 düğümleri: `motor_driver`, `sensors`, `vision` (ArUco), `gateway` (telefonla tek kapı).
- **Telefon**: video çözme, 360 görünüm, AR bindirme, oyun mantığı, UI.
- **Telefona giden**: video, telemetri (batarya, IMU), algılamalar (id, poz, zaman damgası).
- **Jetson'a giden**: sürüş (sol/sağ -100..100), tare açısı, LED, oyun kaynaklı ayarlar (PWM limiti).
- **Bağlantı**: Jetson kendi Wi-Fi ağını (AP) açar; app QR ile bağlanır ve cihazı otomatik bulur.
- **Güvenlik**: heartbeat; kopunca motorlar durur.
