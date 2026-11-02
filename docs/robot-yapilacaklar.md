# Robot: yapılacaklar

Öncelik sırasına yakın. Her maddede ne gerekiyor, nasıl yapılır, nereye dokunulur.

## A. Hemen denenecekler (parça gerekmez)

### A1. Yeni yazılımı robota yükle ve dene
Robot açıkken, bilgisayardan:

```bash
scp -r robot/arduino/robot_kontrol jetson:~/robot/arduino/
scp robot/jetson/surus_koprusu/*.py robot/jetson/surus_koprusu/surus.html jetson:~/robot/surus_koprusu/
ssh jetson 'systemctl --user stop surus-koprusu; ~/.local/bin/arduino-cli compile --upload -p /dev/ttyACM0 --fqbn arduino:avr:uno ~/robot/arduino/robot_kontrol; systemctl --user start surus-koprusu'
```

Kontrol listesi:
- [ ] Ayarlar panelinde "Görüntü: H.264 (video)" yazıyor, veri ~4 Mbit/s.
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
- 2,4 GHz deneme (duvardan daha iyi geçer):

  ```bash
  ssh -t jetson "sudo nmcli con modify EDUROAM 802-11-wireless.band bg && sudo nmcli con up EDUROAM"
  ```

  Geri almak için: `802-11-wireless.band ""`.
- Doğrudan bağlantı: panelden "Robotu kurulum moduna al", bilgisayarı `Robot-A78C` ağına bağla, `http://10.42.0.1:8080`. Aynı gecikme ve menzil ölçümünü yap.
- Sonuçları karşılaştır: sinyal (dBm, ayarlar panelinde), gidip gelme (ms), kaç metrede/hangi duvarda koptuğu.

### A4. Jetson'u tam güç moduna al (isteğe bağlı)
- Şu an 5 W modunda (2 çekirdek). `sudo nvpmodel -m 0 && sudo jetson_clocks` ile 10 W, 4 çekirdek.
- **Dikkat:** 5 V regülatör 4 A verebilmeli. Yetmezse Jetson ani yükte kendini kapatır.

### A5. Theta S'in kendiliğinden kapanmasını engelle
Kamera bir kez 13 dk sonra USB'den düştü. Telefondaki RICOH THETA uygulamasında "Uyku modu" ve "Otomatik kapanma"yı kapat (kamera ayarları).

### A6. Hız sabitleyici, kayıt ve tekrar (yazılım fikirleri)
- **Hız sabitleyici:** C tuşu o anki hızı tutar, tuşa basmadan gider.
- **Sürüşü kaydet ve tekrar oynat:** sayfa gönderdiği komutları zamanlarıyla saklar, "tekrar" deyince aynı yolu sürer. Encoder olmadığı için kayma birikir; kısa yollarda eğlenceli.
- **Parti modu:** müzik + ışık ritimle (dörtlü melodinin vuruşuna göre). Röle yıpranmasına dikkat.
- **Telefonla bakma:** telefonun jiroskopu ile telefonu çevirince 360° görüntüde etrafa bakılır; Cardboard ile VR gözlüğü.
- **Telefonu direksiyon yapmak:** telefonu yatay tutup sağa sola eğerek dönme.
- **360° fotoğraf:** düğmeyle o anki tam kareyi indir.

## B. Sensör gerektirenler

### B1. Pil voltajı → sayfada pil yüzdesi, düşük pil uyarısı
- **Neden:** 4S LiPo 15 V altına inmemeli. Şu an regülatör ekranından bakılıyor.
- **Gerekenler:**
  - Gerilim bölücü: iki direnç, örneğin 30 kΩ + 7,5 kΩ (5'e böler) ya da hazır "0-25 V voltaj sensörü modülü".
- **Bağlantı:**
  - Pil + → bölücü → Arduino **A2** (boş pin).
  - Bölücünün eksisi Arduino GND'ye; ortak toprak şart.
  - 16,8 V / 5 = 3,36 V, Arduino'nun 5 V sınırının altında. 30k+7,5k bölücüde 17 V'ta bile güvenli.
- **Kod:**
  - Arduino: `analogRead(A2) * 5.0 / 1023 * 5` = pil voltajı. Ortalamayı al; motor çekerken gerilim düşer.
  - Durum satırına ekle.
  - Sayfa: voltajdan yüzde. 4S için kabaca 16,8 V = %100, 15,0 V = %0 (bizim sınırımız).
- **Davranış:**
  - 15,3 V altında uyarı ve "dönüş" melodisi.
  - 15,0 V altında motor gücü %40'a iner, dörtlüler yanar.
  - Daha da düşerse panel Jetson'u güvenli kapatır.
- **Oyuna bağlantı:** gerçek pil = oyundaki yakıt (CLAUDE.md'deki fikir).

### B2. Mesafe sensörü → engelde fren, park sensörü
- **Elde olan:** hocadaki tek noktalı lidar (modeli öğrenilmeli: TF-Luna / TFmini ise UART ya da I2C; VL53L0X / VL53L1X ise I2C) ve yakınlık sensörü.
- **Bağlantı:**
  - I2C ise Arduino A4 (SDA) / A5 (SCL).
  - UART ise Arduino'nun seri portu Jetson'a gittiği için ya SoftwareSerial (boş dijital pin, ör. 13) ya da sensörü doğrudan Jetson'a USB-UART ile bağla.
- **Engelde fren (önde):** ileri giderken izin verilen hız mesafeyle sınırlanır. Örneğin 60 cm'den uzakta serbest, 60-15 cm arasında orantılı azalır, 15 cm'de ileri hareket durur (geri serbest). Bu Arduino'da yapılmalı; ağ kopsa da çalışsın.
- **Park sensörü bipi (arkada):** geri giderken bip aralığı mesafeyle kısalır, 10 cm'de sürekli bip. Ses altyapısı hazır (geri vites bipinin aralığını mesafeye bağlamak yeter).
- **Oyun:** CLAUDE.md'deki "mesafe sensörü evi haritalar" (ExplorationMap) fikri bu sensörle gerçek olur. Robot yerinde dönerken ölçüm alıp çevresinin kabaca haritasını çıkarır.

### B3. IMU (ivme + jiroskop) → düz gitme, çarpma algılama, AR
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

### B5. Işık sensörü (LDR) → otomatik far
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
- `RealRobotLink`: köprünün WebSocket'ine bağlanır (`/kontrol`, `/goruntu-h264`), ya da daha düşük gecikme için Jetson'a UDP komut kanalı eklenir.
- **Görüntü:** H.264 Android'in donanım çözücüsü (MediaCodec) ile çözülür, 360° düzeltme aynı gölgelendirici mantığıyla Unity'de.
- **Son kullanıcı arayüzü:** teşhis bilgileri (ms, dBm, Mbit/s, akım) gizli bir geliştirici ekranına taşınır; kullanıcı yalnızca basit durum görür.
