# Robot: nasıl çalışıyor, neden böyle yapıldı

Gerçek robotun yazılımı. Kodlar: `robot/arduino/robot_kontrol/robot_kontrol.ino` (Arduino), `robot/jetson/surus_koprusu/` (sürüş köprüsü ve sayfası), `robot/jetson/robot_paneli/` (Wi-Fi ve güç paneli).

## 1. Genel yapı

```
Telefon / PC tarayıcısı ──Wi-Fi──► Jetson Nano ──USB seri, 115200──► Arduino Uno ──► motor sürücü (VNH5019), röle (LED), buzzer
                                     ▲
                       Ricoh Theta S (USB, canlı 360° görüntü)
```

- **Arduino:** pinlere dokunan ve zamanlaması kesin olması gereken her şey (motor, röle, buzzer). Tek iş yapar ama hiç takılmaz.
- **Jetson:** ağır ve ağla ilgili işler: görüntüyü alıp sıkıştırmak, tarayıcıyla konuşmak, Wi-Fi'ı yönetmek. Linux çalıştırdığı için zamanlaması kesin değildir (bazen birkaç ms gecikir). Bu yüzden motor PWM'i ona değil Arduino'ya verildi.
- **Tarayıcı:** sürüş hesabı (vites, dönüş), 360° görüntünün düzeltilmesi (ekran kartında), arayüz.

## 2. Arduino

### Dil ve yapı
- **Dil:** C++. "Arduino dili" aslında C++ ile Arduino'nun hazır fonksiyonlarıdır (`digitalWrite`, `analogRead`, `tone`, `millis`...).
- Sınıf kullanılmadı. Global değişkenler ve küçük fonksiyonlarla, C tarzı yazıldı:
  - `motorYaz`: bir motora yön ve güç yazar.
  - `yaklas`: rampa (gücü adım adım değiştirir).
  - `komutuIsle`: seriden gelen komutu yorumlar.
  - `sesGuncelle`: buzzer'da hangi sesin çalacağına karar verir.
  - `melodiIlerlet`, `selektorIlerlet`, `dortluIlerlet`: zamanı gelince sıradaki adımı yapar.
  - `durumGonder`: durum satırını yollar.
- **`setup()`:** açılışta bir kez çalışır: pinleri ayarlar, röleleri kapalı başlatır, PWM zamanlayıcısını kurar.
- **`loop()`:** sonsuza dek döner.

### "Tek çekirdek" sorunu nasıl çözüldü
- **Sorun `delay()`'di.** `delay(500)` yarım saniye her şeyi durdurur; o sırada motor komutu okunmaz, ses değişmez.
- **Çözüm: hiç `delay` yok.**
  - `loop()` saniyede on binlerce kez döner. Her turda `millis()` ile saate bakar: "rampanın sıradaki adımı geldi mi? notanın süresi doldu mu? dörtlünün yanıp sönme zamanı geldi mi?"
  - Sırası gelen işi birkaç mikrosaniyede yapıp geçer. Her iş kendi "en son ne zaman yaptım" değişkenini tutar.
  - Lokanta garsonu gibi: tek kişi ama hiçbir masada beklemiyor, sırayla hepsine uğruyor.
- **Asıl hızlı işleri donanım yapar.** Uno'nun içinde üç zamanlayıcı (timer) var, işlemciden bağımsız sayarlar:
  - **Timer0:** `millis()` saati.
  - **Timer1:** iki motorun PWM'i, saniyede 20.000 kez.
  - **Timer2:** buzzer'ın sesi (`tone`).

  İşlemci yalnızca "şu oranı kullan", "şu frekansta çal" diye bir sayı yazar; aç/kapat işini zamanlayıcı kendisi yapar. Bu yüzden motor sürerken müzik çalmak ikisini de yavaşlatmaz.

### Motor: PWM
- **Ne yapar:** motor sürücü motorun gerilimini saniyede 20.000 kez açıp kapatır. Açık kaldığı süre oranı (doluluk) motora giden ortalama gerilimi belirler: %50 açık ≈ yarım gerilim. Motorun bobini (indüktans) ve ataleti bu hızlı aç-kapa'yı yumuşatır; motor bunu düz bir gerilim gibi hisseder.
- **Neden 20 kHz:**
  - İnsan kulağı ~20 kHz'e kadar duyar. Arduino'nun varsayılan PWM'i 490 Hz'dir; motor o frekansta cızırtıyla öter.
  - VNH5019'un desteklediği en yüksek frekans da 20 kHz.
- **Hesap:** `ICR1 = 400`, faz doğru PWM → 16 MHz / (2 × 400) = 20 kHz.
- **Yön:** her motor için iki yön pini (INA/INB). INA=1, INB=0 bir yön; tersi öbür yön; ikisi 0 = fren (motor uçları kısa devre, çabuk durur).

### Komutlar: `M 150 150` ne demek
- Sol ve sağ motor için -255…+255 arası bir sayı. Artı ileri, eksi geri; 255 = tam güç (motora giden gerilimin tamamı).
- Sayıyı tarayıcı hesaplar: güç sınırı (sayfadaki kaydırıcı, örn. %60) × vitesin o anki hız oranı × dönüş karışımı × 255.
  - Örnek: güç %60, 4. viteste tam hız, düz gidiş → 0,6 × 1 × 255 = 153 → `M 153 153`.
- **Arduino bu değere hemen atlamaz:**
  - Rampayla adım adım çıkar.
  - Ölü bölgeyi atlar: 153 komutu motora yaklaşık 173/255 (%68) olarak gider.

### Rampa (yumuşak kalkış, çabuk duruş)
- Her 5 ms'de bir motor değeri hedefe doğru bir adım ilerler.
- **Hızlanırken adım 4:** 0'dan 255'e 255/4 × 5 ms ≈ 320 ms.
  - **Neden yavaş:** duran motor ilk anda çok akım çeker (dönmeyen motorun direnci düşüktür, karşı-EMK henüz yok). Ani kalkış hem dişlileri zorlar hem pilden ani akım çeker.
  - **Ölçüm:** kalkışta en fazla 0,9 A, sürerken 0,5 A, boşta 0,1-0,2 A gördün. Rampa olmasa kalkış tepesi daha yüksek olurdu.
  - **Neden 0,32 sn:** 0,5 sn'nin altı insana "hemen tepki verdi" gibi gelir, 0,3 sn civarı dişliye ve pile darbe vurmayacak kadar yumuşaktır. Kesin bir formülden değil, bu iki ölçütten seçilmiş bir başlangıç değeri. Kalkış akımı ölçülerek ince ayarlanabilir (`HIZLANMA_ADIMI`).
- **Yavaşlarken adım 9:** 255'ten 0'a ≈ 140 ms. Durmak güvenlik işidir, çabuk olmalı. Yavaşlarken motor zaten akım istemez.
- **Yön değiştirirken:** önce sıfıra iner, sonra öbür yöne kalkar. Tam ileriden tam geriye ani geçiş, motorun ve sürücünün en çok zorlandığı durumdur (ters akım).
- **Tuşu bırakınca:**
  - Sayfa hızı saniyede 1,8 birim düşürür: tam hızdan ~0,55 sn'de sıfır.
  - Arduino'nun yavaşlama rampası bunun üstüne yumuşatır.

### Ölü bölge
- Motorlar çok düşük PWM'de dönmez, sadece vınlar: sürtünmeyi yenecek kadar tork yoktur. Bu tür redüktörlü motorlarda eşik kabaca %15-25 arasıdır.
- Kod sıfır olmayan her komutu 50/255'ten (%20) başlatır: 1…255 → 50…255. Böylece yavaş sürüş de gerçekten hareket ettirir.
- 50 tahmini bir değer. Yerde "robotun zar zor kımıldadığı" değere göre ayarlanmalı (`OLU_BOLGE`).

### Emniyet
- **Emniyet süresi:** sayfa basılı tuş varken 60 ms'de bir komut gönderir. Arduino 300 ms komut alamazsa motorları kendisi durdurur. Wi-Fi koparsa, sayfa kapanırsa ya da Jetson takılırsa robot kaçmaz. 300 ms, art arda ~5 komutun kaybolmasına izin verir; tek tük kayıpta robot sarsılmaz.
- **Arıza pinleri:** VNH5019 aşırı ısınma ya da kısa devrede arıza pinini düşürür. Durum satırında `ariza=1` olur, sayfada "sürücü arızası" çıkar.
- **Akım ölçümü:** sürücü motor akımıyla orantılı gerilim verir (~140 mV/A). `analogRead` 0-1023 → mA'e çevrilir. Duruşta görülen 0,1-0,2 A ölçüm gürültüsü ve sürücünün kendi tüketimidir.

### Buzzer ve sesler
- **Buzzer nasıl ses çıkarır:**
  - Buzzer pasif piezo: içinde gerilim gelince bükülen bir seramik disk var.
  - `tone(440)` pini saniyede 880 kez değiştirir (440 tam dalga). Disk saniyede 440 kez titrer ve 440 Hz'lik ses (La notası) çıkar. Frekans perdeyi belirler.
  - PWM'le aynı fikir (pin aç-kapa), ama burada aç-kapa hızı sesin kendisidir.
- **Ses seviyesi:** kare dalganın genliği hep 5 V. Yazılımla gerçek bir ses ayarı yok. Disk en yüksek ~2-4 kHz'de çıkar; kalın sesler kısık duyulur, o yüzden bazı ezgiler bir oktav yukarı taşındı. Ses ayarı için donanım gerekir (yapılacaklar listesi).
- **Korna:** gerçek arabalarda iki ayrı korna aynı anda çalar (~420 ve ~500 Hz). Buzzer aynı anda tek ses çalabildiği için iki ton arasında 4 ms'de bir gidip geliyoruz; kulak bunu kalın ve pürüzlü tek ses gibi duyar.
- **Motor sesi:**
  - Perde motor gücüne bağlı: rölantide 38 Hz, tam güçte 140 Hz. Düz vızıltı gibi durmasın diye her 30 ms'de %6 oynar.
  - Vites değişirken güç kısa bir an düştüğü için motor sesinde de vites geçişi duyulur.
  - Piezo bu kalın sesleri kısık ve "vızz" gibi çıkarır. Gerçek tok ses için hoparlör gerekir (yapılacaklar).
- **Geri vites bipi:** iki motor da geri giderken 1100 Hz, 450 ms çal / 450 ms sus.
- **Melodiler:** {frekans, süre} çiftleri Arduino'nun program belleğinde (`PROGMEM`) durur. RAM 2 KB, program belleği 32 KB olduğu için listeler oraya konur. Her notanın sonunda 20 ms sessizlik var; böylece art arda aynı nota ayrı duyulur.
- **Öncelik:** tek buzzer var, aynı anda istenen seslerden hangisinin çalacağı şöyle seçilir: bip > korna > melodi > geri vites bipi > motor sesi. Kornaya basınca melodi susmaz, korna bitince kaldığı yerden duyulur.

### Işık (röle)
- Röle mekanik bir anahtardır; içindeki mıknatıs bir kontağı çeker. Bu modülde LOW = açık (ters mantık; test edildi).
- **Selektör:** 110 ms aralıkla iki kez yakıp söndürür.
- **Dörtlü:** 500 ms yanık / 500 ms sönük, dakikada 60. Arabalarda yasal aralık dakikada 60-120'dir; bu ağır ucu.
- Mekanik röle her çekişte biraz aşınır (tipik ömür ~100.000 çekiş). Dörtlü saatte 3.600 çekiş yapar, bu yüzden uzun süre açık bırakmamak iyi. Röle "tık tık" sesini de gerçek arabadaki flaşör rölesi gibi çıkarır.

### Durum satırı
Arduino 100 ms'de bir şunu yollar:

```
D <sol> <sag> <solmA> <sagmA> <ariza> <led> <melodi> <dortlu> <motorSesi> <geriBip>
```

Sayfadaki göstergeler buradan gelir. Böylece iki kişi bağlıysa da (telefon + PC) ikisi de aynı durumu görür.

### Komut listesi

| Komut | Ne yapar |
|---|---|
| `M <sol> <sag>` | motor gücü, -255…255; 300 ms yenilenmezse durur |
| `S` | hemen dur |
| `L 0/1` | ışık |
| `F` | selektör |
| `H 0/1` | dörtlü |
| `K <ms>` | korna |
| `B <hz> <ms>` | tek ton bip |
| `C <n>` | n. melodi (`C 0` sus) |
| `E 0/1` | motor sesi |
| `V 0/1` | geri vites bipi |
| `R 0/1` | ikinci röle (boşta) |
| `?` | durumu hemen gönder |

## 3. Sürüş hesabı (sayfada)

### Vites
- Basılı tuttukça hız o vitesin üst sınırına çıkar, sınıra varınca vites büyür. Vites sınırları güç sınırının %35 / %55 / %75 / %100'ü.
- Hızlanma yüksek viteste yavaşlar (saniyede 0,8 → 0,2), gerçek arabadaki gibi. 1'den 4'e ~1,7 sn, tam hıza ~2,9 sn.
- **Vites geçişi:** 0,2 sn güç %55'e düşer ("kavrama"). Göstergede ve motor sesinde hissedilir.
- **Geri vites (R):** tek vites, en fazla %50.
- **Ters yöne basmak:** önce saniyede 3 birimle fren yapar, sonra öbür yöne geçer.
- **Kapatmak için:** ayarlardan "Vites" kutusu ya da G tuşu. Kapalıyken tuş = tam güç (Arduino rampasıyla).

### Dönüş
- Robot kızaklı şasi (tank gibi): direksiyon yok, iki taraftaki tekerlerin hız farkıyla döner. Dönerken tekerlekler yere sürtünerek kayar; bu yüzden küçük hız farkı neredeyse hiç döndürmez.
- **Yerinde dönüş:** bir taraf ileri, öbürü geri, %80. En keskin dönüş bu.
- **Giderken dönüş:**
  - Dış teker en az %60 güçle döner.
  - İç teker dış tekerin %25'i kadar **ters** yönde döner.
  - Önceki sürümde iç teker %15 ileri dönüyordu, fark küçük olduğu için dönüş yavaştı.
- **Geri giderken:** aynı kural geçerli, yani araba gibi davranır: S+D'de arka sağa döner. İlk sürümde formül geri giderken farkı tersine çeviriyordu; "arka sol"a basınca arka sağa gidiyordu.
- Sayfa bağlantının zayıfladığını anlarsa (ping cevabı 0,9 sn gecikirse) hareket komutu göndermez, sıfır gönderir.

## 4. 360° görüntü

### Görüntünün dönüştürülmesi
- **Kamera ne verir:** Theta S iki balık gözü lensli. Her lens ~203° görür; USB'den yan yana iki daire olarak gelir (1280×720, 15 kare/sn, JPEG).
- **Ekrana nasıl çizilir:** her piksel için "bu piksel hangi yöne bakıyor" hesaplanır. O yön hangi lense düşüyorsa, lensin dairesinde merkezden `açı × ölçek` uzaklıktaki noktadan renk alınır. Bunu ekran kartı (WebGL, gölgelendirici) her karede milyonlarca piksel için yapar.
- **Birleştirme ve lens ölçümü:**
  - İlk sürümde lens ölçüleri tahminiydi (merkez 320,320; 200°) ve iki lens 90° çizgisinde sert kesiliyordu. Bu yüzden dikiş belirgindi.
  - Kameradan iki kare alındı. Bir bilgisayar programı, iki lensin örtüştüğü bantta görüntüler en iyi eşleşene kadar lens merkezlerini, ölçeği ve ikinci lensin küçük dönmesini (~1°) aradı. Bu bir optimizasyon: hata 0,29'dan 0,14'e indi.
  - Şimdi iki lens 90° çizgisinin ±6° çevresinde yumuşakça karışıyor. Lens kenarındaki karanlık halka kullanılmıyor.
- **Kalan iz (paralaks):** iki lens birkaç cm aralıklı. Kameraya çok yakın nesneleri (robotun kendi gövdesi) iki lens farklı açıdan görür. Bu yalnızca derinlik bilinirse düzeltilebilir.

### Aynalar ve kalite
- **Aynalar:** ayrı kamera yok. Aynı 360° kareden sürücünün baktığı yönün 125° sol-arkası ve sağ-arkası çizilir, soldan sağa ters çevrilir (gerçek ayna gibi). Sürücü kamerayı çevirince aynalar da onunla döner.
- **Kalite sınırı:**
  - Theta S USB'den en fazla 1280×720, 15 kare/sn verir; tüm küre bu kadar pikselde. Ekranda 90°'lik bir bölüme bakınca o bölüm büyütülür, o yüzden yumuşak görünür.
  - Kameranın HDMI çıkışı daha yüksek çözünürlük ve 30 kare/sn verebilir. Bunun için HDMI yakalama kartı gerekir (yapılacaklar).

## 5. Jetson: sürüş köprüsü

### Bağlantı ve komutlar
- **HTTP'den WebSocket'e geçildi.** HTTP'de her komut ayrı bir istekti (~500 bayt başlık, cevap beklenir). WebSocket'te bağlantı bir kez açılır, iki yöne de küçük mesajlar beklemeden gider. Robot da durumu 100 ms'de bir kendisi yollar.
- **Neden gerçek UDP değil:** tarayıcılar UDP açamaz (yalnız WebRTC ile, o da Jetson'daki eski yazılımlarla zor). Onun yerine **UDP gibi davranış** kuruldu:
  - Her komutta sayfanın saati var. Köprü, komutun "normalden ne kadar geç" geldiğini hesaplar: sayfa saati ile köprü saati arasındaki farkın şimdiye kadarki en küçük değerine göre.
  - 250 ms'den geç gelen hareket komutu uygulanmaz, robot durdurulur.
  - Sinyal kopup düzelince TCP'nin sırayla teslim ettiği eski komutlar artık robotu hareket ettirmez. Robotu elinde tutarken motorun kısa dönmesinin nedeni buydu: biriken komutlar sonradan geldi.
  - Atılan komut sayısı ayarlar panelinde görünür.
- **Telefonun yazılacak kendi uygulaması (Unity)** gerçek UDP kullanabilir.
- **Sürücü kilidi:** iki cihaz aynı anda sürerse son hareket ettiren sürücü olur, öteki 0,6 sn yok sayılır.

### Görüntü
- GStreamer kameranın JPEG karelerini ikiye ayırır:
  - **JPEG kolu:** kareler hiç işlenmeden aktarılır. Kare başına 150-400 KB (aydınlık, ayrıntılı sahne daha büyük) × 15 = 20-50 Mbit/s.
  - **H.264 kolu:** JPEG çözülür, Jetson'un donanım kodlayıcısı (NVENC) sıkıştırır: ~4 Mbit/s, 5-12 kat az. Ölçüm: 6 sn = 2,3 MB.
- **H.264 neden bu kadar az yer tutar:** her kareyi baştan göndermez, yalnızca önceki kareden farkı gönderir.
- **Tam kare (IDR):** saniyede bir gönderilir. Yeni bağlanan izleyici en geç 1 sn bekler; ağ takılıp kare atlamak gerekirse sonraki tam kareden devam edilir. Ara kareler öncekilere dayandığı için tek tek atılamaz.
  - Jetson'un kodlayıcısına bunun için hem `iframeinterval` hem `idrinterval` verilmeli. Yalnız birincisi verilince ara tam kareler IDR olmuyor; bu, kayıtta yakalandı.
- **Tarayıcıda çözme:**
  - Sayfa `https` ya da `localhost` ise tarayıcının WebCodecs'i ile (en düşük gecikme).
  - Değilse (robotun adresi `http`) kareler sayfada MP4 parçalarına sarılıp `<video>`'ya verilir (MSE; ~0,2 sn arabellek).
  - İkisi de olmazsa JPEG yoluna dönülür.
- Kamera yalnızca izleyen varken açılır; 10 sn kimse izlemezse kapanır.

## 6. Ağ ve gecikme

### Ölçümler

| | Değer |
|---|---|
| Robot Wi-Fi sinyali (EDUROAM, 5 GHz, kanal 60) | -70 dBm (zayıf) |
| Bilgisayar sinyali, aynı modem | %92 |
| Görüntü (JPEG) | 20-50 Mbit/s |
| Görüntü (H.264) | ~4 Mbit/s |
| Komutlar | ~16 mesaj/sn × ~60 bayt ≈ 1 KB/sn |
| Durum | 10 mesaj/sn × ~300 bayt ≈ 3 KB/sn |

### Gecikmenin kaynakları
1. **Kamera:** Theta S'in kendi içindeki gecikme, henüz ölçülmedi.
2. **Wi-Fi'ın tıkanması:** JPEG 20-50 Mbit/s'ye zayıf sinyal yetişmeyince kareler kuyrukta birikiyordu, komutlar da bu kuyruğun arkasında bekliyordu. H.264 ile veri ~10 kat azalır; asıl çözüm bu.
3. **Wi-Fi güç tasarrufu:** açıkken kart uyur, gelen paketler yüzlerce ms bekleyebilir. Kurulum betiği kapatıyor.
4. **Tarayıcı arabelleği:** WebCodecs'te ~1 kare, MSE'de ~0,2 sn.

### Duvar ve mesafe
- 5 GHz duvardan 2,4 GHz'e göre çok daha zor geçer. Zaten zayıf olan -70 dBm, bir duvarla kullanılamaz hâle gelir.
- Anten iyi olsa bile şunlar kontrol edilmeli:
  - Antenin hangi banda yapıldığı (drone antenleri çoğu zaman yalnız 2,4 ya da 5,8 GHz).
  - İki anten kablosunun da karta takılı olup olmadığı.
  - Antenlerin metal kasanın içinde kalıp kalmadığı.

### Doğrudan bağlantı
Bilgisayar robotun kendi ağına (`Robot-XXXX`) bağlanırsa:
- Okul ağının erişim noktası ve kalabalığı aradan çıkar.
- Bağlantı 2,4 GHz olur; duvardan daha iyi geçer, ama hızı daha düşüktür. H.264 ile hız yeter.

Denenmeli (yapılacaklar).

## 7. Jetson yükü
- **İşlemci:** Jetson şu an 5 W modunda (4 çekirdekten 2'si açık, 921 MHz). Görüntü aktarılırken işlemci %10-35. H.264'te JPEG'i çözmek bir miktar daha yük ekler, sıkıştırma ayrı donanımda yapılır.
- **RAM:** 1,2 / 4 GB.
- **Sıcaklık:** işlemci 42 °C. Wi-Fi kartı 67 °C (ılık; kasada hava alması iyi olur).

## 8. Robot paneli (port 80)
- **Wi-Fi:**
  - Robot bilinen bir ağ bulamazsa 45 sn sonra kendi ağını açar. Telefon bağlanınca kurulum sayfası kendiliğinden açılır.
  - Yeni ağa geçilir ve o ağda 5 dk kimse panele ulaşamazsa robot önceki ağa döner.
- **Okul ağı (FortiGate):** giriş sayfası çıkınca kayıtlı bilgilerle robot kendisi giriş yapar, oturumu canlı tutar.
- **Güvenli kapatma:** "Kapat" düğmesi `systemctl poweroff` çalıştırır, yani Ubuntu'da menüden kapatmakla aynı. Programlar kapanır, SD karta yazılacaklar yazılır. Güç ondan sonra şalterden kesilir.

## 9. Önceden düşünülen sorunlar ve önlemler

| Olası sorun | Önlem |
|---|---|
| Wi-Fi koparsa robot son komutla gider | Arduino 300 ms emniyet süresi |
| Sinyal dönünce biriken eski komutlar robotu oynatır | Köprü 250 ms'den bayat komutu atar |
| Bağlantı zayıfken sürücü fark etmez | Sinyal rozeti kırmızı, uyarı çıkar, sayfa hareket göndermez |
| Pencere arka plana geçerken tuş basılı kalır | Odak kaybında bütün tuşlar bırakılır |
| İki cihaz aynı anda sürer | Sürücü kilidi |
| Geri giderken dönüş ters | Karışım ileri ve geri için aynı kural (arabadaki gibi) |
| İleriden geriye ani geçiş | Rampa önce sıfıra iner |
| Arduino portu açılınca kendini sıfırlar | Köprü "HAZIR" satırını bekler |
| Kamera USB'den düşer (uyku, pil) | Köprü kamerayı her 2 sn arar, gelince yeniden açar |
| Boşluk tuşu son tıklanan düğmeye de basar | Tıklamadan sonra odak bırakılır |
| Jetson'un H.264 kodlayıcısı açılmazsa görüntü gider | Hat kare vermezse yalnızca JPEG ile yeniden açılır |
| Tarayıcı H.264 çözemezse | Kendiliğinden JPEG'e döner |
| Röle dörtlüde yıpranır | 500 ms aralık; uzun süre açık bırakmamak |
| Pil 15 V altına iner | Şimdilik regülatör ekranından bakılıyor; sensör yapılacaklarda |

Henüz önlemi olmayanlar:
- Teker sıkışınca (duvara dayanınca) motorun uzun süre yüksek akım çekmesi (yapılacaklar: akım sınırı).
- B ile arkaya bakarken W'nin yine robotun önüne gitmesi (kafa karıştırabilir).
