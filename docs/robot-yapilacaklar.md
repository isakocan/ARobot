# Robot: yapılacaklar

## Değişiklik günlüğü (5 Kasım 2026)

- **B2 (mesafe sensörü) ve B3 (IMU) iptal.** Robotta bu sensörler olmayacak; konum yalnızca 360° kameradan bulunacak. Hocadaki tek noktalı lidar çok büyük olduğu için takılmadı.
- **Yeni: 360° kamerayla oda haritalama denemesi.** Robot oda içinde dolaştırılır, 360° görüntülerden yapı çıkarılır; doğruluk telefon taramasıyla karşılaştırılır. Ölçüm için robotun yerden yüksekliği ve kamera çözünürlüğü not edilir.
- **Yeni: konum belirleme denemesi.** Kaydedilmiş oda haritası ile robotun 360° görüntüsü eşleştirilir (PICCOLO/CPO benzeri). Ar-Ge çekirdeği.
- **Hibrit oda taraması:** iPhone LiDAR ve LiDAR'sız telefonda çift kamera ile de harita çıkarılacak; hangi yöntemin hangi durumda daha iyi olduğu ölçülecek.
- **Fiyat ve malzeme:** Motor ve LiPo fiyatları ara raporda referans olarak yazıldı; diğer parçalar danışmandan temin edildi.

Öncelik sırasına yakın. Her maddede ne gerekiyor, nasıl yapılır, nereye dokunulur.

## 0. Robot bağlanınca (sırayla)
- [ ] Bekleyen yazılımı yükle: Arduino (havalı korna, gür foto sesi), köprü (görünen kısım, donanım JPEG çözücü), robot paneli (kurulum ağı 20 sn, sınırsız yeniden deneme, 2,4 GHz tercihi; `sudo bash ~/robot/kurulum.sh`), telefona son uygulama.
- [ ] Bağlantı kopup geri gelmeme sorunu: koptuğunda ne gördüğünü konuş, kayıtlara bak (`kopru.log`, `journalctl -u robot-paneli -u NetworkManager`).
- [ ] Wi-Fi: `bash ~/robot/araclar/wifi_kontrol.sh` (gönderme gücü, ülke kodu, bağlantı hızı); uzak odada `wifi_kontrol.sh izle`; antenleri farklı açılara getirip karşılaştır; telefon paylaşımı 2,4 GHz'te.
- [ ] Görünen kısım: Ayarlar › Görüntü › Görünen; veri düşüyor mu, dönüşte bulanıklık rahatsız ediyor mu (ediyorsa iptal).
- [ ] Telefonda MP4 kayıt, galeriye düşmesi, "Galeride aç"; büyük düğmeler.
- [ ] Görüntü bekçisi: zayıf sinyalden sonra görüntü kendiliğinden düzeliyor mu.
- [ ] Motor testleri (robot havadayken): ölü bölge, akım koruması, dönüş.

## A. Hemen denenecekler (parça gerekmez)

### A1. Yeni yazılımı robota yükle ve dene
Robot açıkken, bilgisayardan:

```bash
scp -r robot/arduino/robot_kontrol jetson:~/robot/arduino/
scp robot/jetson/surus_koprusu/*.py robot/jetson/surus_koprusu/surus.html jetson:~/robot/surus_koprusu/
ssh jetson 'systemctl --user stop surus-koprusu; ~/.local/bin/arduino-cli compile --upload -p /dev/ttyACM0 --fqbn arduino:avr:uno ~/robot/arduino/robot_kontrol; systemctl --user start surus-koprusu'
```

Kontrol listesi:
- [x] Yüklendi (3 Kasım). Ayarlar panelinde "Görüntü: H.264 (video)" yazıyor, veri ~4 Mbit/s, 15 fps, ping 3 ms.
- [ ] Ölü bölge: güç %20'de robot kımıldıyor mu? Kımıldamıyorsa `OLU_BOLGE`'yi büyüt, fırlıyorsa küçült.
- [ ] Korna kornaya benziyor mu? Değilse `KORNA_HZ_1/2` ve `KORNA_ADIMI` ile oyna (örn. 350/440 Hz, 3-8 ms).
- [ ] Motor sesi ve vites geçişi duyuluyor mu?
- [ ] Giderken dönüş yeterince keskin mi? `IC_TEKER` (-0.25) ve `DONUS_EN_AZ` (0.6) `surus.html` içinde.
- [ ] Kamera ayarındaki **Yön**'ü robotun önü ekranın ortasına gelecek şekilde ayarlayıp kaydet. Aynalar ve B tuşu buna göre çalışır.

### A2. Gecikme ölçümü
- Ekrana bir kronometre aç, robotun kamerasına göster. Sürüş sayfasıyla kronometreyi aynı fotoğrafa alıp iki saat farkına bak; bu uçtan uca görüntü gecikmesidir.
- JPEG ve H.264 için ayrı ölç (ayarlardan seçilir).

### A3. Wi-Fi: bant ve doğrudan bağlantı
- Anteni kontrol et:
  - Kartta iki kablo da takılı mı (MAIN/AUX)?
  - Anten 5 GHz destekliyor mu? Üstünde ya da belgesinde "2.4/5 GHz dual band" geçmeli.
  - Anten metal kasanın dışında mı?
- 2,4 GHz deneme (duvardan daha iyi geçer): panelde "Wi-Fi bandı" bölümünden 2,4 GHz seç. Kart (Intel 8265) iki bandı da destekliyor; okulda EDUROAM'ın 2,4 GHz yayını da görünüyor. O bantta bağlanamazsa robot eski ayara kendisi döner.
- Doğrudan bağlantı: panelden "Robotu kurulum moduna al", bilgisayarı `Robot-A78C` ağına bağla, `http://10.42.0.1:8080`. Aynı gecikme ve menzil ölçümünü yap.
- **Telefonun internet paylaşımı:** telefonun hotspot'unu aç, PC'yi ve robotu ona bağla (robotu panelden). Ev dışı / okul ağı olmadan kullanım böyle olacak.
- **Robotun kendi ağına doğrudan:** PC'yi Robot-A78C'ye bağla (o sırada PC'nin interneti gider), Unity'den sür.
- Sonuçları karşılaştır: sinyal (dBm, ayarlar panelinde), gidip gelme (ms), kaç metrede/hangi duvarda koptuğu. Ölçümü Claude yapar (UDP gidip gelme, kayıp, sinyal); kullanıcı sürerek his testi yapar.

### A4. Jetson'u tam güç moduna al (isteğe bağlı)
- Şu an 5 W modunda (2 çekirdek). `sudo nvpmodel -m 0 && sudo jetson_clocks` ile 10 W, 4 çekirdek.
- **Dikkat:** 5 V regülatör 4 A verebilmeli. Yetmezse Jetson ani yükte kendini kapatır.

### A5. Theta S'in kendiliğinden kapanmasını engelle
- [ ] **Arkadaşa söyle:** kameranın uygulaması (RICOH THETA) onda. Kamera ayarlarından "Otomatik kapanma" ve "Uyku"yu kapatsın; ayar kamerada kalıcı. (Kamera bir kez 13 dk sonra USB'den düştü.)
- Olmazsa: Jetson'dan USB ile kapanma süresi sıfırlanabiliyor (ptpcam, 0xD802 = 0); canlı yayın modunda çalışıp çalışmadığı denenecek.

### A6. Kayıt ve tekrar (yazılım fikirleri)
- **Sürüşü kaydet ve tekrar oynat:** sayfa gönderdiği komutları zamanlarıyla saklar, "tekrar" deyince aynı yolu sürer. Encoder olmadığı için kayma birikir; kısa yollarda eğlenceli. Senaryolar:
  - **Üsse otomatik dönüş:** pil azalınca robot geldiği yolu tersten sürüp üsse döner. Kayma birikeceği için son metrelerde kamera üssü (ArUco işareti) görüp yerleşmeli.
  - **Devriye:** kaydedilen bir yolu robot tekrar tekrar sürer, sen kameradan izlersin (ev bekçisi).
  - **Hayalet yarış:** önceki turun AR'da yarı saydam bir robot olarak görünür, onunla yarışırsın.
- **Parti modu:** müzik + ışık ritimle (dörtlü melodinin vuruşuna göre). Röle yıpranmasına dikkat.
- **Telefonla bakma:** telefonun jiroskopu ile telefonu çevirince 360° görüntüde etrafa bakılır; Cardboard ile VR gözlüğü.
- **El hareketiyle kontrol:** kameraya el sallayınca robot gelir, avuç gösterince durur, işaret parmağıyla yön. Jetson'da el iskeleti modeli (ör. MediaPipe Hands / trt_pose_hand) 360° karenin ön parçasında çalışır.
- **360° fotoğraf:** düğmeyle o anki tam kareyi indir.

## B. Sensör gerektirenler

### B2. (İPTAL) Mesafe sensörü: robotta sensör olmayacak
- **Elde olan:** hocadaki tek noktalı lidar (modeli öğrenilmeli: TF-Luna / TFmini ise UART ya da I2C; VL53L0X / VL53L1X ise I2C) ve yakınlık sensörü.
- **Bağlantı:**
  - I2C ise Arduino A4 (SDA) / A5 (SCL).
  - UART ise Arduino'nun seri portu Jetson'a gittiği için ya SoftwareSerial (boş dijital pin, ör. 13) ya da sensörü doğrudan Jetson'a USB-UART ile bağla.
- **Engelde fren (önde):** ileri giderken izin verilen hız mesafeyle sınırlanır. Örneğin 60 cm'den uzakta serbest, 60-15 cm arasında orantılı azalır, 15 cm'de ileri hareket durur (geri serbest). Bu Arduino'da yapılmalı; ağ kopsa da çalışsın.
- **Park sensörü bipi (arkada):** geri giderken bip aralığı mesafeyle kısalır, 10 cm'de sürekli bip. Ses altyapısı hazır (geri vites bipinin aralığını mesafeye bağlamak yeter).
- **Oyun:** CLAUDE.md'deki "mesafe sensörü evi haritalar" (ExplorationMap) fikri bu sensörle gerçek olur. Robot yerinde dönerken ölçüm alıp çevresinin kabaca haritasını çıkarır.

### B3. (İPTAL) IMU: robotta sensör olmayacak
- **Parça:** MPU-6050 (ucuz) ya da BNO055 (yönü kendisi hesaplar, daha kolay). I2C, A4/A5.
- **Düz gitme:** encoder yok; iki taraf aynı PWM'de bile biraz farklı döner, robot kayar. Jiroskopla yön tutulur: düz giderken sapma varsa bir tarafa küçük düzeltme (PID'in P'si yeter).
- **Çarpma algılama:** ani ivme → motorları durdur, dörtlü yak, kısa bip.
- **Sabit dönüş:** "tam 90° dön" komutu jiroskopla.
- **AR:** CLAUDE.md'deki "AR için kısa süreli takip (IMU)" bu. Kameranın baktığı yön değişimini IMU'dan alıp görüntüdeki sanal nesneleri sabit tutmak.

### B4. Motor akımı sınırı (sensör var, ölçüm gerekli)
- VNH5019'un akım ölçümü zaten okunuyor. Eksik olan, sıkışma akımının ne olduğu.
- **Ölçüm:**
  - Robotu bir duvara dayayıp kısa süre (1 sn) güç %40'ta sür, sayfadaki akımı not et.
  - Sonra %60'ta tekrarla.
- **Kod:** akım sıkışma akımının ~%70'ini 800 ms aşarsa o motoru durdur, sayfada "teker sıkıştı" uyarısı. Kısa kalkış tepeleri (0,9 A) tetiklemesin diye süre şartı var.

### B5. (YERİNE GÖRÜNTÜDEN) Işık sensörü (LDR) → otomatik far
- **Parça:** LDR + 10 kΩ direnç, bir analog pine (A3).
- **Davranış:** karanlık eşiğin altına inince ışık kendiliğinden yanar. Sayfada "otomatik far" seçeneği.

### B6. Ses: gerçek korna ve tok motor sesi
- **Parça:** DFPlayer Mini (MP3 çalar modülü, microSD kart) + küçük hoparlör (3 W, 4-8 Ω).
- **Bağlantı:**
  - DFPlayer'ın RX'i 1 kΩ dirençle boş bir dijital pine (SoftwareSerial).
  - Beslemesi 5 V; hoparlör doğrudan modüle.
- **Ne kazandırır:**
  - Gerçek korna, motor ve vites sesi kayıtları.
  - Uzun ve çok sesli müzik.
  - **Ses seviyesi komutla ayarlanır** (0-30).
- **Şimdiki piezo için basit ses kısma:** buzzer'a seri 100 Ω-1 kΩ direnç ya da trimpot. Yazılımla gerçek ses ayarı yok.

### B7. Renkli LED (RGB) → parti modu, durum ışığı
- **Parça:** WS2812B ("NeoPixel") şerit ya da halka. Tek veri kablosuyla her LED'in rengi ayrı ayarlanır, 5 V.
- **Bağlantı:** veri pini boş bir dijital pine (ör. 13), 330 Ω seri direnç. Çok LED varsa beslemeyi Arduino'dan değil 5 V regülatörden al.
- **Kullanım:**
  - Parti modu: renkler ve yanıp sönme müziğin temasına göre (Doom kırmızı ve hızlı, Zelda yeşil ve yavaş).
  - Durum: pil azalınca turuncu, bağlantı kopunca kırmızı yanıp sönme.
  - Oyunda bulgu yakalanınca efekt.
- Röle gibi yıpranmaz; istediğin kadar hızlı yakıp söndürebilirsin.

## C. Görüntü

### C1. Theta S HDMI çıkışı
- **Fikir:** Theta S'in yanında mikro HDMI çıkışı var. Canlı modda bu çıkıştan USB'den daha yüksek çözünürlük (1920×1080) ve daha yüksek kare hızı (30) alınabilir. Kameranın bu modda ne verdiği denenmeli.
- **Parça:**
  - USB 3.0 HDMI yakalama kartı (HDMI'yi bilgisayara "web kamerası" gibi tanıtan küçük çubuk; ~300-600 TL; "USB 3.0 HDMI capture" diye aranır).
  - Mikro HDMI → HDMI kablo.
  - Elde olan "USB HDMI" bir ekran adaptörüyse (görüntü verir, almaz) işe yaramaz. Yakalama kartı görüntü alır.
- **Kod:**
  - `KAMERA` ve boyut (1920×1080) değişir.
  - Gölgelendiricideki lens ölçüleri yeni çözünürlüğe göre `lens_ayarla.py` ile yeniden hesaplanır.
- **Dikkat:** HDMI modunda kamera USB'den şarj olmayabilir; pil durumuna bakılmalı.

### C2. HTTPS → WebCodecs (daha düşük gecikme)
- Robotun sayfası `http` olduğu için tarayıcı H.264'ü `<video>` üzerinden (~0,2 sn arabellek) gösteriyor. `https` olsa WebCodecs ile ~1 kare.
- **Yol:** köprüye kendi imzalı sertifika ver. Tarayıcı bir kez "güvenli değil, yine de devam et" der.
- **Daha kolayı:** bilgisayarda SSH tüneliyle `http://localhost:8080` (localhost güvenli sayılır).

### C3. Kamera yönüne göre sürüş
B ile arkaya bakınca W yine robotun önüne gider. İstenirse "kameraya göre sür" seçeneği: arkaya bakarken tuşlar ters çevrilir.

## D. Uygulama (Unity) tarafı

- [ ] **Görüntüden otomatik far:** ışık sensörü yerine kameranın kendi görüntüsünün parlaklığı: ön lensin ortalama parlaklığı birkaç saniye eşiğin altında kalınca ışık yanar, aydınlanınca söner (iki eşik + bekleme: ışık yanınca görüntü de aydınlanacağı için gidip gelmesin). Jetson'da ucuz (küçük kare üstünden ortalama) ya da uygulamada.
- [ ] **(Fikir) Nesne algılamayla otomatik fren:** ileride önündeki engeli / insanı görüntüden algılayıp robotu yavaşlatmak ya da durdurmak (Jetson'da küçük bir algılama modeli, ön lens).
- [x] Sürüş Unity'de (3 Kasım): `Scenes/RealRobot.unity`, `Scripts/Drive`. UDP sürüş kanalı (köprü :8081), robotu otomatik bulma, kokpit (direksiyon sol/orta/sağ, iç ve yan aynalar, devir ve hız saatleri), vites, kamera hareketi, şarkı menüsü, foto/video, robotsuz demo (kayıtlı görüntü + buzzer kopyası). Tarayıcı sürüş sayfası artık yalnızca yedek.
- [ ] **Titreşim:** telefonda titreşimle his: korna ve düğmelerde kısa tık, vites geçişinde hafif vuruş, çarpma / sert frende güçlü titreşim, sinyal zayıflayınca uyarı titreşimi.
- [ ] **EDUROAM'a bağlanamama:** robot 11 Kasım'da EDUROAM'a bağlanmadı (sebep bakılacak: profil, giriş sayfası, kayıtlı giriş bilgisi). Not: okul ağı cihazlar arası UDP'yi kestiği için sürüşe zaten uygun değil; robota internet ya da panel erişimi için gerekli.
- [ ] **Jiroskopla direksiyon:** telefonu direksiyon gibi sağa sola yatırarak dönme (ayarlardan seçilir); ortada ölü bölge, yatırma açısı direksiyon açısına yumuşak eşlenir, titreşimle orta noktası hissedilir.
- [ ] **Düğmelerin soluklaşması:** 5 sn dokunulmayınca düğmeler ve bilgiler soluklaşsın, ekran görüntüye kalsın; dokununca ya da tuşa basınca geri gelsin.
- **Görüntü:** H.264 Android'in donanım çözücüsü (MediaCodec) ile çözülür, 360° düzeltme aynı gölgelendirici mantığıyla Unity'de.
- **Son kullanıcı arayüzü:** teşhis bilgileri (ms, dBm, Mbit/s, akım) gizli bir geliştirici ekranına taşınır; kullanıcı yalnızca basit durum görür.

## E. Korumalar ve iyileştirmeler (bütün sistem)
Rampa, 300 ms bekçi ve "önce sıfırdan geç" Arduino'nun çekirdeğinde (`loop` → `yaklas`): hangi yoldan gelirse gelsin (sayfa, köprünün "dur"u, bekçi) her motor komutu bunlardan geçer. Aşağıdakiler de mümkün olduğunca en alt katmana konmalı.

### Arduino (ağ kopsa da çalışır)
- **Donanım bekçisi (AVR watchdog):** `loop` bir hata yüzünden takılırsa Arduino 0,25 sn içinde kendini yeniden başlatır, motorlar durur. Parça gerekmez, birkaç satır.
- **Sürücü arıza pini:** VNH5019 aşırı ısı / kısa devre bildirirse (pin 6 / 12) o tarafı durdur, sayfada "motor sürücü arızası". Şu an yalnızca bildiriliyor.
- **Akım sınırı ve sıkışma:** B4.
- **Düşük pil:** B1 ile; 15,2 V altında güç sınırı %50, 15 V'ta motorlar durur.
- **Sıcaklık payı:** yüksek akım uzun sürerse gücü kademeli düşür (sürücü ve motor ısınmasın).

### Jetson
- **Düşük pilde güvenli kapanma:** B1 ile; kritik voltajda Jetson kendini kapatır, SD kart bozulmaz.
- **Kamera donarsa:** birkaç saniye kare gelmezse görüntü boru hattı kendiliğinden yeniden başlar.
- **Isı ve gerilim takibi:** `tegrastats` ile; Jetson ısınırsa ya da gerilim düşerse sayfada uyarı.
- **SD kart ömrü:** günlükleri RAM'de tut (sürekli yazma kartı yıpratır).

### Ağ
- **Sinyale göre görüntü kalitesi:** gidip gelme süresi ya da kayıp artınca H.264 bit hızı 4 → 1,5 Mbit/s'ye iner, düzelince geri çıkar. Görüntü biraz bulanıklaşır ama donmaz ve gecikme birikmez.
- **Sinyale göre en yüksek hız:** ağ kötüleşince güç sınırı kendiliğinden düşer; komutlar geç ulaşırken robot hızlı gitmesin.

## F. Konum bulma fikirleri (beyin fırtınası, sonra bakılacak)
Ev haritası telefonla taranırsa harita bilinir; robot yalnızca haritadaki yerini bulur (SLAM gerekmez).
- **Kameradan sahte lidar:** kamera yüksekliği h biliniyor. Zeminin bittiği yer ufkun α altındaysa mesafe = h / tan α (h = 25 cm, α = 14° → ~1 m). 360°'de her yön için yapılınca lidar taraması gibi. Gerekli: zemin bölütleme modeli (aynı model duvar / eşya / "gökyüzü" temasını da verir).
- **Tavan:** lambalar, köşeler, kirişler sabit işaret; eşya yer değiştirse de tavan değişmez.
- **Panoramadan yer tanıma:** haritalarken her ~50 cm'de 360° kare kaydet; sonra en çok benzeyeni bul. Panoramayı yatayda kaydırıp eşleştirmek yönü de verir. Robot "kaybolursa" kendini bununla bulur.
- **Optik akış (görsel kilometre sayacı):** zemin deseninin kaymasından gerçekten ne kadar gidildiği; encoder yerine, teker kaymasını da yakalar.
- **Wi-Fi parmak izi:** her odada görünen ağların sinyal gücü kaydedilir, oda düzeyinde (2-4 m) konum. Wi-Fi RTT (FTM) 1-2 m verir ama çoğu modem desteklemez.
- **Motor + IMU:** düzeltmeler arasında tahmin. Motor: HN-GH12-2217Y, 12 V'ta 120 dev/dk, 50:1.
- **Birleştirme: parçacık filtresi.** 500 "burada olabilir" tahmini; hareketle kaydır (motor + IMU), kameranın sahte lidarına uymayanları ele. Başlangıç / kaybolma için Wi-Fi ve panorama tanıma.
- **Tek kameradan derinlik:** MiDaS / Depth Anything (küçük), göreli derinlik; ölçek zemin düzleminden.

## G. İleride: ROS
- [ ] **ROS 2'yi yeniden değerlendir:** hoca önerdi. Bugünkü sürüş (köprü + Arduino) için gerekmiyor; haritalama, konum bulma ve otonom gezinme gelince hazır paketleri (SLAM, Nav2, tf, rosbag) için ele alınır. Jetson'da Docker içinde Humble kurulu. Yol: köprünün yanına durum yayınlayan / hız komutu dinleyen ince bir ROS düğümü; görüntü ROS'tan geçmez.
