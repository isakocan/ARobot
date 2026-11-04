# ARaki (TENHA) — Proje Durumu, Yapılanlar ve Planlar

> Bu belge ekip arkadaşı için hazırlandı. Projenin **şu anki hali**, neler yapıldığı, neler kaldığı, donanımın ve yazılımın nasıl bağlandığı ve açık kararlar burada. Ara raporun temel kaynağı olarak da kullanılabilir.
> Son güncelleme: 4 Kasım 2026. Kaynak: `CLAUDE.md`, `docs/` klasörü, git geçmişi (35 commit) ve kod.
>
> **Durum etiketleri:** Donanım ve yazılım mimarisi büyük ölçüde **kesin**. Oyun kurgusu (bulgu isimleri, bölümler, yükseltme değerleri, hikaye) **taslak**tır ve büyük ihtimalle değişecek. Oyunun temel fikri Pokémon GO benzeri: gerçek dünyada keşfet, yakala, geliştir döngüsü; burada ev içinde ve dokunmadan, taktikle.

---

## 1. Özet (tek paragraf)

ARaki, fiziksel bir sumo robotunu telefondan ve 360° kamerasının görüntüsünden süren, üstüne AR katmanı bindirilmiş bir oyundur. Oyunun iki ayağı var:

1. **Gerçek robot:** 4 tekerlekli, kızaklı sürüşlü robot; Jetson Nano + Arduino Uno + Ricoh Theta S 360° kamera. Sürüş, görüntü ve ses/ışık çalışıyor; Unity uygulaması robota bağlanıp sürebiliyor.
2. **Oyun (Unity):** Ev içinde "keşif" oyunu. Robotun mesafe sensörü evi haritalar, harita dolduğunda bulgular (Evcikler) ve düşmanlar (Gölgeler) çıkar. Yakalananlar üsse taşınır, öz harcanarak robot geliştirilir ve 6 bölümlük hikaye ilerler.

Oyunun adı şimdilik **TENHA** (geçici; `unity/Assets/_Project/Scripts/Core/GameInfo.cs` içinde tek yerden değişir). Görsel tema olarak **Ev** seçili; uzay/savaş sonrası gibi alternatifler kaldırıldı, "hangi tema ana yön olacak" kararı açık.

---

## 2. Ekip, araçlar ve ortam

| Konu | Durum |
|---|---|
| Ekip | 2 kişi (proje sahibi: İsa Koçan). Unity'ye yeni başlanmış. |
| Oyun motoru | Unity **6000.3.26f1 (6.3 LTS)**, URP, Input System |
| Hedef telefon | Samsung S21+ (Android). iOS sonra (ekipte Mac var) |
| Geliştirme | Önce Unity Editor'de PC (Windows), sonra telefona derleme |
| Robot bilgisayarı | Jetson Nano (P3450), JetPack 4.6 / Ubuntu 18.04, Docker + ROS 2 Humble kurulu |
| Depo | Git, `main` dalı. Büyük çıktılar (`build/`, `kayitlar/`, `Library/`) `.gitignore`'da |
| Ara rapor | Yazılacak. Bu belge girdi olarak kullanılabilir |

**Çalışma dili ve kurallar:** Türkçe. UI/UX profesyonel ve özgün olmalı ("AI slop" jenerik görünüm istenmiyor). Son kullanıcı terminal kullanamaz; bağlantı akışı tek dokunuş/QR olmalı.

---

## 3. Donanım

### 3.1 Sumo robot platformu
- 4 tekerlek, kızaklı sürüş (tank gibi), yerinde 360° dönebilir. **Encoder yok.**
- Motorlar: Hsiang Neng HN-GH12-2217Y, 12 V, 120 dev/dk, 50:1 redüktörlü.
- Tekerlek çapı **henüz ölçülmedi** (hız/mesafe hesabı için gerekli).
- Hız: ~1-2 km/h.

### 3.2 Elektronik
| Parça | Açıklama |
|---|---|
| Jetson Nano | Görüntü alma, sıkıştırma, sürüş köprüsü, robot paneli, Wi-Fi. USB ile Theta S ve Arduino'ya bağlı |
| Arduino Uno | Motor, röle, buzzer gibi zamanlaması kesin işler. Jetson'dan USB seri (115200) |
| Pololu Dual VNH5019 shield | Motor sürücü. Sol = M1 (yön 2/4, PWM 9, arıza 6, akım A0), Sağ = M2 (yön 7/8, PWM 10, arıza 12, akım A1). Test edildi |
| Ricoh Theta S | 360° kamera. Telefonla çalıştığı doğrulandı; Jetson'da USB görüntüsü çalışıyor |
| Röle | Pin 3 → LED (ters mantık: LOW = açık, test edildi). Pin 5 = boş ikinci röle |
| Buzzer (pasif piezo) | Pin 11, `tone()` ile çalışıyor |
| Servo, LED | LED röle üzerinden çalışıyor. Servo "aday" listesinde; takılı olup olmadığı doğrulanmalı |
| Pil | 4S LiPo 14,8 V, 5200 mAh. **15 V altına inilmez** |
| Mesafe / yakınlık sensörü | Hocada tek noktalı lidar ve yakınlık sensörü var. Model henüz belirlenmedi (TF-Luna/TFmini mi, VL53L0X mi?) |

Shield'in 3, 5, 11 pinleri boş (3/5 röle, 11 buzzer için kullanılıyor).

### 3.3 Ağ
- Jetson kendi Wi-Fi kartıyla (Intel 8265, iki anten kablosu) okul/ev ağına bağlanır.
- 45 sn ağ bulamazsa robot kendi kurulum ağını açar: **`Robot-XXXX`** (XXXX = MAC'in son 4 hanesi). Şifre `/etc/robot/kurulum_agi.json` içinde.
- **Bilinen sorun:** robotun sinyali zayıf (aynı yerde bilgisayar %92, robot %35 / −83 dBm). Anten kablolarının takılı olup olmadığına ve antenin metal kasa dışında kalıp kalmadığına bakılmalı.

---

## 4. Yazılım mimarisi

### 4.1 Genel bağlantı

```
Telefon / PC (Unity) ──UDP 8081 (istenen durum, 50 ms)──► Jetson (surus_koprusu)
                    ◄──WebSocket/HTTP 8080 (tarayıcı yedeği)──┘        │
                                                                  seri 115200
Ricoh Theta S ──USB (JPEG + donanım H.264)──────────────────────► Jetson
                                                                       │
                                                                       ▼
                                                  Arduino Uno → VNH5019 (motor), röle (LED), buzzer
```

Ana kural: **Video ROS'tan geçmez.** Görüntü GStreamer ile doğrudan telefona/tarayıcıya gider. ROS 2 Humble kurulu ama şu an sürüşte kullanılmıyor.

### 4.2 Sorumluluk dağılımı
- **Arduino:** motor PWM (20 kHz), rampa, ölü bölge, emniyet süresi (300 ms komut gelmezse durur), röle, buzzer, melodiler. Hiç `delay()` yok; `millis()` ile zamanlanır.
- **Jetson (Python):** seri köprü, kamera (GStreamer: JPEG kolu + NVENC H.264 kolu), UDP/WebSocket sunucusu, Wi-Fi ve güç paneli, sıcaklık alarmı (80 °C).
- **Unity (telefon/PC):** oyun mantığı, sürüş (kokpit, vites, dönüş karışımı), 360° görüntü (equirect → küre), AR nesneleri, arayüz, ses sentezi.
- **Tarayıcı sayfası** (`surus.html`): yalnızca **yedek**, artık geliştirilmiyor.

### 4.3 Unity projesi (`unity/`)
Ana sahneler: `Scenes/RealRobot.unity` (gerçek robot sürüşü), oyun sahnesi (ev), menü.

Önemli scriptler (`unity/Assets/_Project/Scripts/`):

| Klasör | Ne yapıyor |
|---|---|
| `Game/` | `Expedition.cs` (keşif döngüsü, bölümler, bulgular, gölge, teslim), `Find.cs` (bulgu türleri: Toz Topu, Işık Kurdu, Gizli Cep, Unutulmuş Tohum, Evin Kalbi), `Ghost.cs` (gölge), `Upgrades.cs` (6 yükseltme, en fazla seviye 3), `SaveData.cs`, `GameSession.cs` |
| `Sensing/` | `ExplorationMap.cs`: mesafe sensörü simülasyonu, ev haritası |
| `World/` | `HouseLayout.cs`: çok odalı ev (salon, koridor, mutfak, yatak odası, banyo, çalışma odası) |
| `Theming/` | `ThemeCatalog.cs` (Ev teması, 6 bölüm metni), `ThemeSkin`, `ThemeSurface` |
| `Robot/` | `IRobotLink` (soyutlama), `MockRobotLink` (sahte robot), `RealRobotLink` (gerçek), `DemoRobotConnection` (robotsuz demo), `BuzzerSynth`, `RobotLed`, `WheelSpinner`, `FirmwareData` |
| `Drive/` | `DriveController`, `DriveModel`, `DriveCockpit` (kokpit, direksiyon, aynalar, saatler), `DriveSettings`, `ScreenRecorder` (foto/video), `VectorShape` |
| `Net/` | `RobotConnection` (robotu otomatik bulma), `PhoneWifi` |
| `Video/` | `H264Decoder` (OpenH264, Windows + Android), `RobotVideo`, `EquirectSphere`, `PanoramaViewer`, `Sim360Camera` (simülasyon 360), `RobotCameraMount` |
| `AR/` | `ARRig` (robota göre AR konumu), `FindVisual`, `GhostVisual`, `LidarOverlay`, `ProceduralMeshes` |
| `Audio/` | `AudioSynth`, `SfxPlayer` |
| `UI/` | `ExpeditionHud`, `ScreenUi`, `UiKit` |
| `Dev/` | Geliştirici araçları: klavye sürüşü, debug overlay, kamera geçişi |

Testler: `Tests/PlayMode/` altında 4 dosya (ExpeditionTests, HouseTests, RealRobotDemoTests, RealRobotLiveTests).

Editör araçları: `Editor/Optimizer.cs` (mobil URP profili, ASTC doku, Android ayarları), `Editor/AndroidPermissions.cs` (yeni, commit edilmedi).

---

## 5. Oyun tasarımı (TASLAK — büyük ihtimalle değişecek)

Bu bölümdeki isimler, sayılar ve bölümler kodda ve taslakta yazılı hâliyle verilmiştir; kesin karar değildir.

### 5.1 Fikir
Pokémon GO mantığı: oyuncu gerçek dünyada dolaşır, bulgular çıkar, yakalar, üsse taşır, üssünü ve robotunu geliştirir. Fark: mekan bir ev, oyuncu robot, yakalama dokunmayla değil taktikle (sessizlik, ışık, sonar, bekleme).

Ev gerçek, robot gerçek, her şey AR; ama **hologram değil: katı, gölgeli, maket gibi**. Robot evi gezer, sensörüyle haritayı açar. Harita dolduğunda bulunan şeyler (bulgu) çıkar. Her bulgu türü **farklı bir fiil** ister (yaklaş, ışık yak, sonar at, bekle, ışık + sessizlik).

### 5.2 Bulgular (Evcikler)
| Tür (kod adı) | Oyuncu adı | Nasıl yakalanır | Öz |
|---|---|---|---|
| Shy | Toz Topu | SHIFT ile sessiz yaklaş; gürültüde kaçar | 2 |
| Lure | Işık Kurdu | LED (L) ile çağır; pil harcar | 2 |
| Hidden | Gizli Cep | Sonar (X) ile gör | 3 |
| Relic | Unutulmuş Tohum | Yanında bekle (sabır) | 1 |
| Legend | Evin Kalbi | Işık + tam sessizlik | 10 |

- Aynı anda en çok **4** bulgu (`MaxAliveFinds`). Nadirlik: yaygın ~%82, nadir ~%15, çok nadir ~%3.
- Yakalananlar robotun hafızasında taşınır; **pil bitince kaybolur**. Üsse teslim edilince öz verir.

### 5.3 Gölgeler (düşmanlar)
- Toplanmaz; **kaçılır**. LED'i üzerlerine tutarsan (40° koni, 3,4 m) donar, 2,5 sn sonra erir (+2 öz).
- Dokunursa pil −%12 ve bir kayıt gider.
- Robottan biraz yavaş. Gürültü ve LED ile fark edilir.
- Aynı anda en fazla **1** gölge; 5. bölümden sonra **2**.

### 5.4 Üs (Ocak) ve yükseltmeler
- Üs: katı bir lamba kulesi (hologram değil), platformun batı kenarında. Haritada büyük simge, ekran kenarında yön üçgeni (yeşil üs, kırmızı tehdit).
- **Yükseltmeler** (6 adet, en fazla seviye 3, maliyet 3 + 2×seviye):
  - Verimli LED (LED %40 daha az pil)
  - Turbo motorlar (güç limiti +%15)
  - Hızlı kayıt (bulgu kaydetme %25 kısa)
  - Geniş batarya (pil %20 yavaş biter)
  - Geniş sensör (menzil +0,5 m)
  - Güçlü sonar (menzil +0,8 m)
- Yükseltmeler **gerçek davranışa** yansır (ör. Turbo = Unity'de PWM limiti).

### 5.5 Hikaye: 6 bölüm (kodda)
| Bölüm | Başlık | Hedef | Öğretilen |
|---|---|---|---|
| 1 | Evin Sesi | 3 kayıt teslim (herhangi) | Bulgular var, yanında bekle |
| 2 | Toz Topları | 2 Toz Topu | Sessiz yaklaşma (SHIFT), gölgeler |
| 3 | Işık Kurtları | 2 Işık Kurdu | LED ile çağırma |
| 4 | Gizli Cepler | 2 Gizli Cep | Sonar |
| 5 | Mutfak | 3 kayıt, mutfakta | Oda ayrımı, gölgelere dikkat |
| 6 | Evin Kalbi | 1 Evin Kalbi | Işık + sessizlik (final) |

Final sonrası serbest gezi ve dalgalar devam eder. Gölgeler yok edilmez, sakinleşir.

> **Tutarsızlık:** `docs/oyun-kurgusu.md` taslağında 7 bölüm, Soğuk Dalga olayı ve daha fazla bina/düşman türü var. Kodda 6 bölüm var. Taslak henüz uygulanmadı.

### 5.6 Görsel ve oynanış kararları
- Tek tema: **Ev** (duvar/tavan düz, odaya göre duvar rengi). Poly Haven CC0 modeller ve PBR dokular kullanılıyor.
- Ekran düzeni sade: sol üstte hedef + ipucu yazısı; sağ altta ev haritası; pil/sonar ince çubuk; taşınan kayıtlar küçük kareler.
- 360° kamera kısayolları: **Tab** tehdide dön, **B** arkaya bak, **Z** dürbün, **Q/E** ve sağ tık serbest bakış.
- Daha önce denenip kaldırılanlar: yarış modu (`7f1feaa`), üç tema (Safari/Gezegen/Gizem; `c81983f` ile silindi), uzay demosu, ORBİT-9 hologramı.

---

## 6. Gerçek robot sürüşü: ne çalışıyor

### 6.1 Çalışanlar
- **Sürüş:** Unity'den UDP ile komut. Robot kendiliğinden bulunur (yayın paketi, `{"t":"bul"}`).
- **Kokpit:** direksiyon, iç ve yan aynalar, devir ve hız saatleri, dinamik yerleşim.
- **Vites:** 5 vites (sıralı manuel). Geri vites tek vites, en fazla %50. Vites geçişinde 0,2 sn kavrama düşüşü.
- **Dönüş:** yerinde dönüş %80; giderken dönüş: dış teker ≥%60, iç teker dış tekerin %25'i kadar ters.
- **Işık, selektör, dörtlü, korna, motor sesi, melodiler (`sarkilar.h`, `sarkilar_ek.h`), geri vites bipi.**
- **Kopma güvenliği (çok katmanlı):**
  - Unity: 0,9 sn cevap gelmezse sıfır gönderir.
  - Köprü: 250 ms'den bayat komutu uygulamaz.
  - Arduino: 300 ms yeni komut gelmezse durur. Ses ve korna 1,5 sn nabız yoksa susar.
- **Motor koruması:** yön değişiminde sıfıra inip 80 ms bekleme; rampa (kalkış ~320 ms, duruş ~140 ms).
- **Görüntü:** Theta S USB'den. JPEG kolu (20-50 Mbit/s) ve donanım H.264 kolu (~4 Mbit/s, NVENC). Kamera yalnızca izleyen varken açılır, 10 sn kimse izlemezse kapanır. Kamera USB'den düşerse köprü her 2 sn'de yeniden arar.
- **Robot paneli (port 80):** Wi-Fi kurulumu, ağ girişi (EDUROAM/FortiGate için otomatik), internet durumu geçmişi, "Kapat" (güvenli kapatma).
- **Robotsuz demo:** kayıtlı görüntü + buzzer kopyası ile Unity'de sürüş denemesi (`tools/robotsuz_deneme`).
- **Foto ve video kaydı:** Unity'den; video kopma anını da kaydeder.

### 6.2 Yazılım sürümü notu
Son commit `48a65c0`: "H.264 kesilince JPEG'e geçiş". Yani H.264 kararlılığı hâlâ sorunlu olabilir; Unity'de H.264 çözücüsü (`b2a384b`) var ama yapılacaklar listesinde "Unity'de H.264" hâlâ işaretsiz. Bu, test edilmesi gereken bir konu.

---

## 7. Yapılanlar (kronoloji)

Git geçmişi: 35 commit, 8 Ekim 2026'dan itibaren. Tarihler commit mesajları ve belgelerden yaklaşık çıkarıldı. Özet:

| Dönem (yaklaşık) | Commit | Ne yapıldı |
|---|---|---|
| Ekim 2026 başı | `aa1c672`, `3ca0721`, `050a26e`, `0164c70` | Proje bağlamı, mimari taslak, yol haritası, CLAUDE.md kararları |
| Ekim 2026 | `97f6693` | Unity 6.3 LTS projesi, URP, Input System, klasör iskeleti |
| Ekim 2026 | `a89529e`, `11a3cb3`, `7b0c320` | Sürüş simülasyonu (IRobotLink, MockRobotLink), robot kamerası, uzay demosu, 360 panorama |
| Ekim 2026 | `2d1fad5`, `6920faf`, `d1dde72` | Keşif robotu dengesi, gerçekçi oda (Poly Haven), görev akışı, AR hologram denemesi |
| Ekim 2026 | `7f1feaa` | Yarış modu + performans optimizasyonu (sonra kaldırıldı) |
| Ekim 2026 | `971169f`, `0dd8820` | Gezegen keşif görevi, mesafe sensörü simülasyonu, çok odalı ev, bulgu sistemi, 6 bölüm hikaye |
| Ekim 2026 | `e6471d4`, `90f0ab5`, `52d243c`, `c81983f`, `a126728` | Ev teması, nadirlik, gölgeler, tek tema, sade arayüz, 360 kamera özellikleri, üs |
| 2-3 Kasım 2026 | `cf4bd1a`, `ede9323`, `4b29605`, `a73268b`, `19068ab` | Gerçek robot: Jetson paneli, sürüş köprüsü, Arduino kodu, H.264, teknik notlar, şarkılar, robotsuz demo |
| 3 Kasım 2026 | `5279284`, `5847f09`, `6f4175a`, `bc0e029` | Sürüş Unity'ye taşındı (UDP, kokpit, foto/video), kokpit düzeni |
| 3 Kasım 2026 | `b2a384b`, `08b38a7`, `0733639`, `48a65c0` | Unity'de H.264, tek parça ön panel, korna, sesler, dinamik kokpit, 5 vites, JPEG'e geri dönüş |
| 3 Kasım 2026 | `7816e6e`, `edaa810`, `089ebad` | Kopma güvenliği, Unity'nin robotu kendiliğinden bulması, arayüz cilası, Wi-Fi profili koruması |

### 7.1 Donanım tarafında tamamlananlar
- Jetson Ubuntu + Docker + ROS 2 Humble kurulumu.
- Theta S telefonla çalıştı; Jetson'a USB ile bağlandı, `/dev/video0` göründü.
- Araç monte edildi (Kasım 2026). Motor sürücü, röle ve buzzer test edildi.
- Jetson paneli, Wi-Fi kurulum akışı (`Robot-XXXX`), ağ girişi ve güvenli kapatma çalışıyor.
- Ses ve ışık (röle) çalışıyor.
- `arduino-cli` Jetson'da; Arduino kodunu Jetson üzerinden yükleyebiliyoruz.

### 7.2 Yazılım tarafında tamamlananlar
- Oyun: çok odalı ev, bulgu sistemi, 6 bölüm, gölgeler, üs, yükseltmeler, kayıt/yükleme (`SaveData`), testler.
- Sürüş: UDP kanal, robotu otomatik bulma, kokpit, vites, ses, foto/video.
- Görüntü: JPEG + H.264 (Jetson tarafı), Unity çözücü (Windows + Android derlemesi).
- Android derlemesi (`build/RobotSurus.apk`) üretildi; telefonda deneme yapılması gerekiyor.

---

## 8. Yapılacaklar (öncelik sırasıyla)

Kaynak: `docs/robot-yapilacaklar.md`, `docs/olasi-sorunlar.md`, `docs/oyun-kurgusu.md`, `CLAUDE.md`.

### 8.1 Hemen (donanım, parça gerektirmez)
1. **Yazılımı robota yükle ve dene** (son Arduino ve köprü sürümü, A1). Kontrol listesi:
   - Ölü bölge: %20 güçte robot kımıldıyor mu? (`OLU_BOLGE`)
   - Korna gerçek korna gibi mi? (`KORNA_HZ_1/2`, `KORNA_ADIMI`)
   - Motor sesi ve vites geçişi duyuluyor mu?
   - Dönüş yeterince keskin mi? (`IC_TEKER`, `DONUS_EN_AZ`)
   - Kamera "Yön" ayarı: robotun önü ekranın ortasına gelecek şekilde ayarla.
2. **Gecikme ölçümü** (A2): kronometreyle uçtan uca görüntü gecikmesi; JPEG ve H.264 ayrı.
3. **Wi-Fi bandı ve anten** (A3): antenleri kontrol et, 2,4 GHz dene, doğrudan `Robot-XXXX` bağlantısı ölç, telefon hotspot'u ile test et. Sinyal (dBm), gidip gelme (ms) ve kopma mesafesi kaydedilmeli.
4. **Jetson tam güç modu** (A4, isteğe bağlı): `nvpmodel -m 0`, 10 W. Dikkat: 5 V regülatör 4 A verebilmeli.
5. **Theta S kendiliğinden kapanmasın** (A5): telefondaki RICOH THETA uygulamasında uyku ve otomatik kapanmayı kapat.

### 8.2 Sensör gerektirenler
6. **Pil voltajı ölçümü** (B1): gerilim bölücü (30 kΩ + 7,5 kΩ) → Arduino A2. 15,3 V altı uyarı, 15,0 V altı motor gücü %40, daha düşükte Jetson güvenli kapatma. **Oyuna bağlanacak** (gerçek pil = oyundaki yakıt).
7. **Mesafe sensörü** (B2): hocadaki lidar/yakınlık sensörünün modeli öğrenilecek. Engelde fren ve geri park bipi. Oyunda ExplorationMap'in gerçek karşılığı.
8. **IMU** (B3): MPU-6050 veya BNO055. Düz gitme (kayma düzeltme), çarpma algılama, AR için yön takibi.
9. **Akım sınırı** (B4): sıkışma akımı ölçümü, 800 ms aşarsa motoru durdur.
10. **LDR far** (B5), **DFPlayer ses modülü** (B6), **RGB LED şerit** (B7): isteğe bağlı donanım iyileştirmeleri.

### 8.3 Görüntü
11. **Theta S HDMI yakalama** (C1): 1920×1080, 30 fps olasılığı. USB 3.0 HDMI yakalama kartı gerekir (~300-600 TL).
12. **HTTPS → WebCodecs** (C2): düşük gecikme için. Yedek tarayıcı sayfası için düşünülür, Unity öncelikli olduğu için düşük öncelik.

### 8.4 Unity ve oyun
13. **H.264 kararlılığı** (yapılacaklar D): çalışıyor mu, kesilirse JPEG'e geçiş doğru mu, telefonda test.
14. **Titreşim geri bildirimi:** korna/düğme tık, vites vuruşu, çarpma, sinyal uyarısı.
15. **Düğmelerin soluklaşması:** 5 sn dokunulmayınca UI soluklaşsın.
16. **Telefona kurulum ve Build & Run:** Unity Hub → "Android Build Support" modülü, telefon kablo ile.
17. **Sürücü arızası ve akım göstergesi:** Arduino bildiriyor, Unity henüz göstermiyor.
18. **Oyun Faz B-E** (`docs/oyun-kurgusu.md`):
    - Faz A (temizlik): tema silme (yapıldı), ürkek dengesi (yapıldı), üs netliği (kısmen).
    - Faz B (ekonomi): Pazar, Atölye, satma/işleme ayrımı, Kafes.
    - Faz C (düşman): Fener ile dondurma, Fısıltı, Bekçi, Yem tuzağı.
    - Faz D (üs ve savunma): bina yerleri, Fener Direği, Gözlem Kulesi, **Soğuk Dalga** olayı, Ocak ısısı.
    - Faz E (hikaye ve cila): bölüm metinleri (7 bölüm taslağı), defter ekranı, ses.
19. **Hikaye metinleri** (`ThemeCatalog.cs`): beğenilmezse değişecek.

### 8.5 Yazılım fikirleri (isteğe bağlı, sonra)
- Hız sabitleyici (C tuşu), sürüşü kaydet ve tekrar oynat (devriye, üsse otomatik dönüş, hayalet yarış).
- Parti modu (müzik + ışık ritmi), telefonla etrafa bakma (jiroskop), el hareketiyle kontrol.
- Konum bulma (ev haritası üzerinde robotun yerini bulma): kameradan sahte lidar, tavan işaretleri, panoramadan yer tanıma, parçacık filtresi.

---

## 9. Açık kararlar (birlikte konuşulacak)

| Konu | Seçenekler / not |
|---|---|
| **Görsel tema / ana yön** | Şimdilik Ev. Uzay / savaş sonrası / maket / başka. Gerçek robotta "zemin/eşya dışı = gökyüzü" için bölütleme modeli gerekir |
| **Oyun adı** | TENHA (önerilen) mı, HOLLOWLIGHT gibi uluslararası bir isim mi? Mağaza çakışması kontrol edilmedi |
| **Wi-Fi modeli** | Robotun kendi ağı yalnızca kurulum için; günlük kullanım ev/okul Wi-Fi'ı mı, yoksa telefonun hotspot'u mu? Test edilecek |
| **Soğuk Dalga (üs savunması)** | Zorunlu mu, yoksa "Sakin mod" ile kapatılabilir mi? |
| **Fener ile gölge** | Gölgeyi eritmek (öldürmek) de olsun mu, yoksa sadece donsun mu? |
| **Derinlik/mesafe sensörü** | Hocadaki lidar mı, yoksa tek kameradan derinlik (MiDaS / Depth Anything) mi? |
| **Görev/hikaye üretimi için LLM** | NVIDIA API vb. denenecek. Şu an hikaye metinleri elle yazılı |
| **Jetson güç modu** | 5 W (kararlı, şu an) mı, 10 W (hızlı ama güç kaynağı 4 A verebilmeli) mı? |
| **Kamera** | Theta S USB (1280×720, 15 fps) mi, HDMI yakalama (1920×1080, 30 fps) mi? |

---

## 10. Riskler

| Risk | Durum | Önlem |
|---|---|---|
| **Düşük gecikmeli video** | En büyük risk. JPEG 20-50 Mbit/s zayıf Wi-Fi'da tıkanıyor; H.264 yolu kesilirse JPEG'e dönülüyor | H.264 kararlılığı, ağ ölçümü, menzil testi |
| **Wi-Fi zayıflığı** | Robot −83 dBm'de (bilgisayar %92). Kopmaların ana sebebi | Anten kontrolü, 2,4 GHz denemesi, hotspot yolu |
| **Okul ağı** | DFS (5 GHz kanal 60, ~1 dk düşme), AP dolu (code 17), bazı ağlar cihazlar arası erişimi engeller | Ev/hotspot ile test |
| **Android internetsiz ağdan kaçabilir** | Robot kendi ağında iken Samsung mobil veriye geçebilir; paketler robota gitmez | Telefonda "Uyarlanabilir Wi-Fi" kapatma, uygulamada Wi-Fi zorlama (henüz yok) |
| **Pil** | 15 V altına inerse LiPo zarar görür. Voltaj ölçülmüyor | B1: bölücü ile ölçüm |
| **Motor sıkışması** | Akım ölçülüyor ama sınır yok | B4: akım sınırı |
| **Theta S kendini kapatır** | 13 dk sonra USB'den düştü bir kez | A5: kamera ayarı, köprü yeniden arama |
| **Jetson ısınması / kapanması** | 5 V regülatör ani yükte yetmeyebilir; Wi-Fi kartı 67 °C | Güç modu kararı, havalandırma, sıcaklık alarmı |
| **SD kart bozulması** | Düğmeden kapatma riski | Panelden "Kapat" kullanılmalı |
| **Oyun-robot bağlantısı** | Oyunun gerçek robot verisiyle (pil, mesafe sensörü) beslenmesi henüz yok; simülasyon | Faz çalışmaları ile paralel |
| **Tekerlek çapı ölçülmedi** | Mesafe/hız hesabı yapılamıyor | Ölç ve kaydet |

---

## 10A. Literatür taraması konuları

Bu bölüm literatür taraması için **konu başlıklarını** listeler. Ağırlık projenin **teknik ve tasarım problemlerinde** (A-E) ama bunlarla sınırlı değil. Oyun tasarımı (F) bölümünde de Pokémon GO benzeri konum tabanlı oyunlar, fiziksel-dijital hibrit oyunlar gibi konular taranabilir. Her konu için: projede nerede ortaya çıktığı, durumu ve İngilizce anahtar kelimeler verilir. Aramalar IEEE Xplore, ACM Digital Library, Google Scholar ve arXiv üzerinde yapılabilir. **Henüz hiçbir makale okunmadı; aşağıdakiler yalnızca arama başlangıç noktalarıdır.**

Ek konu önerileri (listede yok, eklenebilir): konum tabanlı AR oyunlarında oyuncu motivasyonu, uzaktan kontrollü robotlarda oyunlaştırma (gamification), ev robotlarında gizlilik (kamera verisi), gömülü sistemlerde güç ve ısı yönetimi.

### A. Ağ ve gecikme

**A1. Zayıf Wi-Fi üzerinde düşük gecikmeli video**
- **Nerede:** Robot Wi-Fi'ı −83 dBm'de; JPEG 20-50 Mbit/s tıkanıyor, H.264 ~4 Mbit/s ama kesilince JPEG'e dönülüyor.
- **Durum:** Ölçülmedi (uçtan uca gecikme, kronometre yöntemi, A2). Adaptif bit hızı fikri (1,5-4 Mbit/s) var, uygulanmadı.
- **Anahtar kelimeler:** `low-latency video streaming wireless robot`, `adaptive bitrate teleoperation video`, `video teleoperation latency measurement`, `H.264 low latency encoder NVENC`, `WebRTC vs RTP latency teleoperation`

**A2. Kopma ve gecikme altında güvenli durdurma (fail-safe)**
- **Nerede:** Unity 0,9 sn, köprü 250 ms, Arduino 300 ms emniyet süresi; bayat komut reddi. Eşikler sezgisel seçildi.
- **Durum:** Çalışıyor, ama eşiklerin neden bu değerler olduğunun ölçümle gerekçelendirilmesi gerekiyor.
- **Anahtar kelimeler:** `watchdog timeout teleoperated mobile robot`, `stale command rejection networked control`, `communication loss fail-safe ground robot`, `heartbeat safety remote control robot`

**A3. Telefonun Wi-Fi davranışı (internetsiz ağ, mobil veriye kaçış)**
- **Nerede:** Robotun kendi ağında Samsung paketleri mobil veriye yönlendirebiliyor.
- **Durum:** Çözüm yok; telefonda ayar ya da uygulama tarafında Wi-Fi zorlama gerekiyor.
- **Anahtar kelimeler:** `Android Wi-Fi no internet connectivity fallback`, `Android network binding local UDP`, `Wi-Fi offline network mobile data switch`

**A4. Yerel ağ keşfi ve tek dokunuşla bağlanma**
- **Nerede:** Son kullanıcı terminal kullanamaz; robot kendi ağını açıyor (`Robot-XXXX`), uygulama robotu otomatik buluyor.
- **Durum:** Çalışıyor (panel + UDP yayın). Kullanıcı deneyimi ölçülmedi.
- **Anahtar kelimeler:** `Wi-Fi provisioning IoT captive portal`, `Wi-Fi Easy Connect DPP device onboarding`, `zero-configuration device discovery mDNS UDP broadcast`

### B. Robot kontrolü ve sürüş

**B1. Encoder'siz kızaklı sürüşte konum ve hız tahmini**
- **Nerede:** 4 tekerlek, tank gibi dönüş; tekerlekler kayıyor, encoder yok. Dönüş ve hız mesafe hesabı için bilinmiyor; tekerlek çapı ölçülmedi.
- **Durum:** Açık. IMU (B3 yapılacak) ve görsel yöntemler aday.
- **Anahtar kelimeler:** `skid-steer robot odometry slip estimation`, `dead reckoning without wheel encoders`, `skid steer kinematics wheel slip compensation`, `IMU gyroscope heading correction mobile robot`

**B2. Motor sürme: ölü bölge, yumuşak kalkış, ters yönde frenleme**
- **Nerede:** Arduino rampası (kalkış ~320 ms, duruş ~140 ms), ölü bölge %20, ters yönde sıfırdan geçiş.
- **Durum:** Sezgisel değerler; ölçüm yok.
- **Anahtar kelimeler:** `DC motor dead zone compensation PWM`, `soft start motor current inrush`, `H-bridge motor driver reversal braking`, `VNH5019 motor driver protection`

**B3. Teker sıkışması (stall) algılama**
- **Nerede:** Duvara dayanınca motor akım çekip ısınıyor. Akım ölçülüyor, sınır yok.
- **Durum:** Planlandı (B4), uygulanmadı.
- **Anahtar kelimeler:** `motor stall detection current sensing`, `wheel slip detection current mobile robot`, `thermal protection DC motor current limit`

**B4. Motor ve sürücü ısınması, Jetson güç yönetimi**
- **Nerede:** Jetson 5 W modunda; 10 W için 4 A'lık regülatör gerekir. Wi-Fi kartı 67 °C.
- **Anahtar kelimeler:** `Jetson Nano power mode thermal throttling`, `embedded GPU power budget robot`

### C. Konum, harita ve algılama

**C1. Tek noktalı mesafe sensörüyle ev haritalama**
- **Nerede:** Oyunun temel fikri (ExplorationMap): robot dönerek tek nokta mesafe ölçer, harita dolar. Gerçek sensör modeli henüz belli değil (hocadaki lidar).
- **Durum:** Oyunda simüle edildi; gerçek robotta yok.
- **Anahtar kelimeler:** `occupancy grid mapping single-beam range sensor`, `rotating range sensor indoor mapping`, `sparse range measurements mapping home environment`, `robot exploration frontier-based indoor`

**C2. Robotun evdeki yerini bulma (global konum, kısa süreli takip)**
- **Nerede:** CLAUDE.md "global harita yok, AR için kısa süreli takip yeterli" diyor. Ama oyun ev haritasını tutuyor; kaybolma ve başlangıç konumu problemi var.
- **Durum:** Fikirler var (panorama tanıma, tavan işaretleri, parçacık filtresi), uygulanmadı.
- **Anahtar kelimeler:** `particle filter localization sparse sensors`, `Monte Carlo localization range sensor`, `kidnapped robot problem relocalization`, `panoramic image place recognition`, `ceiling-based visual localization indoor`

**C3. Tek kameradan zemin ve mesafe tahmini (sahte lidar)**
- **Nerede:** Kamera yüksekliği h biliniyor; zeminin bittiği ufuk açısından mesafe hesaplanabilir. Zemin bölütleme modeli gerekiyor.
- **Durum:** Fikir (`docs/robot-nasil-calisir.md` F bölümü), deneme yok.
- **Anahtar kelimeler:** `monocular ground plane estimation mobile robot`, `visual lidar from monocular camera`, `monocular depth estimation Depth Anything MiDaS`, `free space segmentation robot camera`

**C4. Robotun ürettiği kamera görüntüsünde dikiş ve paralaks**
- **Nerede:** İki balık gözü lens (Theta S); dikiş 90° çizgisinde yumuşatıldı; yakın nesnelerde (robot gövdesi) paralaks kalıyor.
- **Durum:** Kısmen çözüldü (lens ölçümü, `tools/lens_ayarla.py`).
- **Anahtar kelimeler:** `dual fisheye stitching calibration 360 camera`, `equirectangular projection fisheye lens model`, `parallax correction omnidirectional camera`

**C5. Engel algılama ve yakınlık sensörü ile fren**
- **Nerede:** Hocadaki yakınlık sensörü ve mesafe sensörü; 60 cm'den itibaren hız sınırlama planı.
- **Durum:** Planlandı (B2).
- **Anahtar kelimeler:** `obstacle detection proximity sensor speed limiting mobile robot`, `reactive collision avoidance small robot`

### D. Görüntü, AR ve 360°

**D1. AR nesnelerinin kayması (drift) ve IMU ile kısa süreli takip**
- **Nerede:** AR nesneleri robota göre konumlanıyor (ARRig). Robot dönünce ya da kamera kayınca sanal nesneler yerinden oynuyor.
- **Durum:** Kısmen; IMU yok (B3).
- **Anahtar kelimeler:** `AR object drift mitigation inertial sensor`, `visual-inertial odometry mobile AR`, `camera pose tracking without global map`, `rotation-only tracking 360 video AR`

**D2. Sanal nesnelerin gerçek sahneye oturması (hologram değil, katı maket)**
- **Nerede:** Oyunda AR nesneleri gölge ve duvar/zemin ile katı görünmeli; "AI slop" görünüm istenmiyor.
- **Durum:** Toon gölgelendirici ve gölgeler var; gerçek sahne ışığıyla uyum (lighting estimation) yok.
- **Anahtar kelimeler:** `AR object lighting estimation mobile`, `AR occlusion real environment shadows`, `non-photorealistic rendering AR toon shading`

**D3. 360° görüntüde hareket hastalığı (simulator sickness)**
- **Nerede:** Robotun kendi dönüşü ve 360° kamera bakışı kullanıcıda mide bulantısına yol açabilir; kamera hareketi ve dikiz aynaları bu riski etkiler.
- **Durum:** Ölçülmedi.
- **Anahtar kelimeler:** `simulator sickness 360 video teleoperation`, `cybersickness head-mounted display locomotion`, `telepresence robot user study motion sickness`

**D4. Görüntü gecikmesinin kullanıcı tarafından algılanması**
- **Nerede:** Hangi gecikme sürüşü bozar sorusu; ölçüm ve oyuncu deneyimi bağlanmalı.
- **Anahtar kelimeler:** `perceived latency teleoperation threshold`, `latency effect on driving performance remote vehicle`, `video delay tolerance remote driving`

### E. Tanıma

**E1. Değişen ışıkta nesne tanıma**
- **Nerede:** Önce ArUco, sonra eğitilmiş model (CLAUDE.md). Ev içinde ışık değişiyor.
- **Durum:** ArUco planlandı; model yok.
- **Anahtar kelimeler:** `ArUco marker detection robustness illumination`, `object recognition varying lighting indoor`, `TensorRT object detection Jetson Nano`, `fiducial marker tracking AR`

**E2. Jetson Nano'da model çalıştırma**
- **Nerede:** 4 GB RAM, 5 W mod, Docker ve ROS 2 Humble kurulu. Görüntü işleme yükü ölçülmedi.
- **Anahtar kelimeler:** `edge inference Jetson Nano benchmark`, `real-time object detection embedded GPU`

### F. Oyun tasarımı

**F1. Gerçek donanım ile oyunun bağlanması (pil = yakıt, robot = oyuncu)**
- **Nerede:** Gerçek pil oyundaki öz/yakıt; pil ölçülmüyor (B1).
- **Anahtar kelimeler:** `LiPo battery state of charge estimation voltage`, `energy-aware game mechanics`, `physical digital hybrid games`

**F2. Konum tabanlı ve fiziksel AR oyunlarında yakalama döngüsü**
- **Nerede:** Pokémon GO benzeri "keşfet-yakala-geliştir" döngüsü, ama dokunma yerine taktik (sessizlik, ışık, sonar). Oyun dengesi henüz test edilmedi.
- **Anahtar kelimeler:** `location-based AR game engagement`, `pervasive games design`, `hybrid physical digital game design evaluation`, `stealth game AI enemy behavior`

**F3. Yordamsal düşman ve bulgu üretimi (nadirlik, tekrar değeri)**
- **Nerede:** Nadirlik (%82/%15/%3), çıkış aralıkları (22-48 sn), kaybolma süreleri.
- **Anahtar kelimeler:** `procedural content generation rarity systems games`, `game balancing spawn rate`, `reward schedule engagement games`

**F4. Hikaye ve metin üretimi (LLM)**
- **Nerede:** Hikaye elle yazıldı; LLM ile üretim düşünülüyor (NVIDIA API vb.).
- **Anahtar kelimeler:** `LLM procedural narrative generation games`, `LLM game dialogue evaluation`

### G. Kullanıcı deneyimi ve değerlendirme

**G1. Sistemin değerlendirme yöntemi (ara rapor için)**
- **Nerede:** Gecikme, menzil, kopma sayısı, batarya ömrü ölçülmeli; kullanıcı çalışması yapılmalı.
- **Anahtar kelimeler:** `user study teleoperation robot evaluation`, `System Usability Scale mobile app`, `teleoperation performance metrics mean time to complete`

**G2. Titreşim ve geri bildirim (haptic / vibration) ile sürüş hissi**
- **Nerede:** Telefonda titreşimle korna, vites, çarpma geri bildirimi planlandı.
- **Anahtar kelimeler:** `vibrotactile feedback teleoperation`, `haptic feedback remote driving mobile`

### Literatür taraması için öneri sırası
1. **En kritik:** A1, A2, D3, B1, C2 (projenin çalışıp çalışmadığını doğrudan belirler).
2. **Sonra:** C1, C3, D1, E1, F2.
3. **Ara rapor yazımı için:** G1, D4, F3.

Her makale için kayıt: başlık, yıl, problem kodu (A1 vb.), ne ölçtüğü, bizim projeye uygunluğu. Bu tablo ayrı bir dosyada tutulabilir (`docs/literatur.md`).

---

## 11. Nasıl çalıştırılır (kısa)

### 11.1 Unity (PC)
1. Unity Hub → Unity **6000.3.26f1** ile `unity/` klasörünü aç.
2. Oyun: ev sahnesi (oyun) veya `Scenes/RealRobot.unity` (gerçek robot).
3. Gerçek robot için robot açık ve aynı Wi-Fi'da olmalı; Unity robotu kendisi bulur. Bulamazsa robot kurulum ağına (`Robot-XXXX`) bağlanılır.
4. Robotsuz deneme: `RealRobot` sahnesinde demo modu (kayıtlı görüntü + buzzer).

### 11.2 Jetson
```bash
# Bağlantı (PC'den)
ssh jetson            # kullanıcı bs, jetson.local, anahtar ~/.ssh/id_ed25519

# İlk kurulum (tek sefer)
scp -r robot/jetson/kurulum.sh robot/jetson/robot_paneli jetson:~/kurulum/
ssh -t jetson 'sudo bash ~/kurulum/kurulum.sh'

# Sürüş köprüsü (kullanıcı servisi)
scp -r robot/jetson/surus_koprusu jetson:~/robot/
ssh jetson 'mkdir -p ~/.config/systemd/user && cp ~/robot/surus_koprusu/surus-koprusu.service ~/.config/systemd/user/ && systemctl --user daemon-reload && systemctl --user enable --now surus-koprusu'

# Arduino yükleme
ssh jetson 'systemctl --user stop surus-koprusu; ~/.local/bin/arduino-cli compile --upload -p /dev/ttyACM0 --fqbn arduino:avr:uno ~/robot/arduino/robot_kontrol; systemctl --user start surus-koprusu'
```

Günlük: `ssh jetson 'journalctl --user -u surus-koprusu -n 50'` (köprü) ve `ssh jetson 'journalctl -u robot-paneli -n 50'` (panel).

Robot paneli: `http://jetson.local` ya da robotun IP'si. Tarayıcı sürüş sayfası: `http://<robot>:8080` (yedek).

### 11.3 Jetson ekranı (PC'den)
Masaüstündeki **Jetson Ekranı** kısayolu (`tools/jetson_ekrani.ps1`): SSH tüneli + TigerVNC.

### 11.4 Android
Unity Hub → "Android Build Support" (OpenJDK + SDK/NDK). Telefon kablo ile bağlanır, Build and Run. Hazır APK: `build/RobotSurus.apk` (git'te yok, yerel).

---

## 12. Dosya haritası

| Yol | İçerik |
|---|---|
| `CLAUDE.md` | Proje bağlamı ve kesin kararlar (ana kaynak) |
| `docs/roadmap.md` | **Eski, güncel değil.** Yalnızca başlangıç maddeleri |
| `docs/architecture.md` | **Taslak, kısmen güncel.** Video ve kontrol akışı fikri |
| `docs/oyun-kurgusu.md` | Oyun tasarımı taslağı v2 (7 bölüm, bina yerleri, düşman türleri) |
| `docs/robot-nasil-calisir.md` | Robot yazılımının ayrıntılı anlatımı (Arduino, sürüş, görüntü, ağ) |
| `docs/robot-teknik-notlar.md` | Robotun teknik notları ve sayılar |
| `docs/robot-yapilacaklar.md` | Robot tarafı yapılacaklar (A-F) |
| `docs/olasi-sorunlar.md` | Bağlantı kopması, güç, motor, yazılım sorunları ve önlemler |
| `robot/arduino/robot_kontrol/` | Arduino kodu (`.ino`, `sarkilar.h`, `sarkilar_ek.h`) |
| `robot/arduino/pin_testi/` | Pin testi |
| `robot/jetson/surus_koprusu/` | Sürüş köprüsü (`surus_koprusu.py`, `websoket.py`, `surus.html`, servis dosyası) |
| `robot/jetson/robot_paneli/` | Robot paneli (Wi-Fi, güç, ağ girişi) |
| `robot/jetson/araclar/` | Jetson yardımcı betikleri (Wi-Fi onarım, ekran modu, seri komut) |
| `unity/` | Unity projesi |
| `tools/` | PC araçları: `lens_ayarla.py` (360 lens ölçümü), `surus_kaydi.py`, `melodi_cevir.py`, `efekt_tasarim.py` (yeni), `fetch_polyhaven.py`, `robotu_bul.ps1`, `jetson_ekrani.ps1`, `robotsuz_deneme/` |
| `kayitlar/` | Yerel kayıtlar (git'te yok) |
| `build/` | Yerel derlemeler (git'te yok) |

---

## 13. Şu anki git durumu

- Dal: `main`. Son commit: `48a65c0` (3 Kasım 2026).
- **Commit edilmemiş 15 dosya var** (1042 ekleme, 143 silme). Başlıcaları:
  - Robot: `robot_kontrol.ino`, `robot_paneli.py`, `surus_koprusu.py`
  - Unity sürüş: `DriveCockpit.cs`, `DriveController.cs`, `DriveModel.cs`, `DriveSettings.cs`
  - Unity ayarlar ve malzeme: URP ayarları, `FindShadow.mat`, `Robot360.mat`, `RobotFirmware.txt`, `ProjectSettings.asset`
  - Yeni dosyalar: `tools/efekt_tasarim.py`, `unity/Assets/Editor/AndroidPermissions.cs` (+ `.meta`), `unity/.utmp/`
- **Yorum:** Arkadaşın çalışmadan önce bu değişiklikler ya commit edilmeli ya da konuşulmalı. Satır sonu uyarıları (CRLF/LF) var; bu kozmetik.
- Satır sonları (`.gitattributes`) tutarlı hâle getirilmeli; aksi hâlde her commit'te gereksiz diff çıkar.

---

## 14. Ara rapor için öneri

Ara rapora şu başlıklar konabilir (bu belge zaten hepsini içerir):
1. Problem ve motivasyon (ev içi AR oyun + gerçek robot)
2. Sistem mimarisi (Bölüm 4)
3. Donanım (Bölüm 3)
4. Oyun tasarımı (Bölüm 5)
5. Gerçek robot ve sürüş (Bölüm 6)
6. Yapılanlar ve zaman çizelgesi (Bölüm 7)
7. Ölçümler ve bulgular (ağ ölçümleri, H.264 vs JPEG, akım, kopma güvenliği)
8. Riskler (Bölüm 10)
9. Yapılacaklar ve takvim (Bölüm 8)
10. Açık kararlar (Bölüm 9)
11. Literatür taraması konuları (Bölüm 10A): her konu için anahtar kelimeler hazır

Ölçüm eksikleri: tekerlek çapı, Theta S uçtan uca gecikme, pil voltajı, sürüş hızı, Wi-Fi menzili. Bunlar ölçülüp rapora eklenmeli.
