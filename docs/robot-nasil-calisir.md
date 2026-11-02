# Robot nasıl çalışıyor: adım adım, örneklerle

Bu belge robotun yazılımını baştan sona, örneklerle anlatır. Kısa özet ve sayı tablosu için: `robot-teknik-notlar.md`. Sıradaki işler: `robot-yapilacaklar.md`.

İçindekiler:
1. Büyük resim: W'ye bastığında ne oluyor?
2. Kim ne yapıyor: tarayıcı, Jetson, Arduino
3. Sayfa tarafı (tarayıcıda çalışan kısım)
4. Ağ: TCP, UDP, kuyruk ve "masaya koyunca düşer mi?"
5. Jetson tarafı: sürüş köprüsü
6. Arduino tarafı: tek çekirdekte "aynı anda" iş yapmak
7. Motor: PWM, rampa, ölü bölge, akım
8. Buzzer: ses nasıl çıkıyor, öncelik, şarkılar
9. 360° görüntü: kameradan ekrana
10. Wi-Fi, anten ve gecikme
11. Güvenli kapatma nasıl çalışıyor
12. Pil ne kadar gider?
13. ROS: ne işe yarar, kullanıyor muyuz?
14. Sürüş hissi: oyunlardaki "araba hissi" nereden gelir?
15. Telefon uygulaması
16. AR için haritalama
17. Hız göstergesi, sürüş kaydı, parti modu fikirleri

---

## 1. Büyük resim: W'ye bastığında ne oluyor?

Bir tuşa basmanın robota kadar olan yolculuğu. Zamanlar yaklaşık; yol böyle işler.

```
 0 ms   Klavyede W'ye bastın.
        Tarayıcı "keydown" olayını sayfadaki koda verir: tus.ileri = true.

 0-30   Sayfanın 30 ms'de bir çalışan sürüş fonksiyonu (surusTik) uyanır:
        vites 1, hız %0'dan başlıyor. Güç %60 → sol=0, sag=0'dan küçük adımlarla artacak.
        İlk turda hız ≈ 0,8 × 0,03 = 0,024 → sol = sag = round(0,024 × 0,6 × 255) = 4.

  ~30   Sayfa WebSocket'ten şu mesajı yollar:
        {"t":"surus","sol":4,"sag":4,"z":123456.7}     (z = sayfanın saati, ms)

 ~35    Mesaj Wi-Fi'dan Jetson'a ulaşır (iyi sinyalde 2-10 ms).

 ~35    Jetson'daki köprü (Python) mesajı okur:
        - "Bu komut bayat mı?" (z'ye bakar; bölüm 5)  → hayır
        - "Başka biri mi sürüyor?"                     → hayır
        - Arduino'ya USB seri porttan yazar:  "M 4 4\n"   (6 bayt)

 ~36    6 bayt 115200 bit/sn hızla 0,5 ms'de Arduino'ya geçer.
        Arduino'nun seri donanımı baytları kendi küçük tamponuna (64 bayt) alır.

 ~36    Arduino'nun loop()'u bir sonraki turda (en geç birkaç mikrosaniye sonra) tamponu okur,
        satır tamamlanınca: hedefSol = 4, hedefSag = 4.

 ~40    Rampa adımı (5 ms'de bir): simdiSol 0 → 4 (hızlanma adımı 4).
        Ölü bölge: 4 → 50 + 4×205/255 ≈ 53 → PWM doluluğu 53/255 ≈ %21.
        Timer1 bu oranı motor sürücüye saniyede 20.000 kez uygular.

 ~40+   Motor dönmeye başlar. W basılı kaldıkça sayfa 55-60 ms'de bir yeni değer yollar,
        hız ve vites artar, Arduino rampayla takip eder.

 her 100 ms  Arduino durum satırı yollar: "D 153 153 412 398 0 0 0 0 0 1"
             → köprü okur → sayfaya {"t":"durum", ...} → sayfa göstergeleri günceller.
```

Tuşu bıraktığında:

```
 0 ms   keyup → tus.ileri = false
 0-30   surusTik: hız saniyede 1,8 birim düşmeye başlar (tam hızdan ~0,55 sn'de sıfır).
        Değerler azalarak gider: 140, 125, 108, ... 0.
        Sıfıra varınca "sol":0,"sag":0 bir kez gönderilir, sonra gönderim durur.
        Arduino yavaşlama rampası (5 ms'de 9 adım) bunu daha da yumuşatır.
```

---

## 2. Kim ne yapıyor: tarayıcı, Jetson, Arduino

| İş | Kim | Neden orada |
|---|---|---|
| Tuşları okumak, ekrandaki düğmeler | Tarayıcı (sayfa) | Sen onunla etkileşiyorsun |
| Vites, dönüş karışımı, güç sınırı | Tarayıcı | Ayar/oyun mantığı; değiştirmesi kolay, robotu yeniden programlamak gerekmez |
| 360° görüntüyü düzeltmek (balık gözü → normal görüntü), aynalar | Tarayıcı (ekran kartı) | Her karede milyonlarca piksel hesabı; telefonun/PC'nin ekran kartı bunu çok hızlı yapar |
| Ses ve ışık düğmeleri, kokpit, göstergeler | Tarayıcı | Arayüz |
| Wi-Fi'ı yönetmek, robot paneli, okul ağı girişi | Jetson (robot_paneli.py) | Linux ve ağ işleri |
| Kameradan görüntü almak, sıkıştırmak (H.264) | Jetson (GStreamer + donanım kodlayıcı) | Arduino'nun bu işe gücü yetmez; Jetson'da buna özel donanım var |
| Komutları ağdan alıp Arduino'ya iletmek, bayat komutları atmak | Jetson (surus_koprusu.py) | Ağ ile seri port arasındaki köprü |
| Motorların PWM'i, rampa, emniyet süresi | Arduino | Zamanlaması kesin olmalı. Linux bazen birkaç ms takılır, Arduino asla takılmaz |
| Röle (ışık), buzzer, motor akımını okumak | Arduino | Pinler Arduino'da |
| Güvenli kapatma | Jetson (Linux'un kendisi) | İşletim sistemi işi |

Kural şu: **"Gerçek dünyaya dokunan ve zamanlaması kesin olması gereken" her şey Arduino'da; "ağır, akıllı, ağla ilgili" her şey Jetson'da; "sana gösterilen ve senin dokunduğun" her şey tarayıcıda.**

İleride görüntü işleme (ArUco işaretlerini tanımak, nesne tanıma) Jetson'da olur. Jetson şu an çok rahat (bölüm 10 sonu): işlemci %10-35, ekran kartı (GPU) %0. Yapay zeka modelleri için GPU tamamen boşta.

---

## 3. Sayfa tarafı (tarayıcıda çalışan kısım)

**"Sayfa tarafı"** = `surus.html` dosyası. Bu dosya Jetson'da durur, ama tarayıcıyla `http://<robot>:8080` adresini açtığında bilgisayarına ya da telefonuna iner ve **orada, senin cihazında çalışır**. İçinde üç dil var:
- **HTML:** ekrandaki parçalar (düğmeler, göstergeler).
- **CSS:** görünüş (renkler, boyutlar).
- **JavaScript:** davranış (tuşları dinlemek, hesap yapmak, robota mesaj yollamak).

Sayfa açıldığında şunlar olur:
1. Robota iki WebSocket bağlantısı açar: `/kontrol` (komut ve durum için) ve `/goruntu-h264` (görüntü için).
2. Birkaç zamanlayıcı kurar:

| Ne | Ne sıklıkla | Ne yapar |
|---|---|---|
| `surusTik` | 30 ms | tuşlara bakar, vites/hız hesaplar, gerekirse komut yollar |
| ping | 250 ms | robota "ping" yollar; cevap ("pong") gelene kadar geçen süre = gidip gelme (ms) |
| sinyal rozeti | 250 ms | ping cevabı 0,9 sn'den eski ise "kopuk" der, uyarıyı gösterir |
| çizim (`requestAnimationFrame`) | ekranın yenilenme hızı (60 Hz) | yeni kamera karesini ekran kartına verir, 360° görüntüyü ve aynaları çizer |

3. Robottan durum mesajı geldikçe (10 Hz) göstergeleri günceller: ışık açık mı, hangi şarkı çalıyor, akım.

### 3.1 Komut gönderme kuralları (surusTik içinde, sırasıyla)

```
1. Vites hesabı → f (ileri/geri hız, -1…1)
2. Dönüş karışımı → sol, sag (-1…1)
3. Güçle çarp, 255 ölçeğine çevir → örn. sol=153, sag=-38
4. Bağlantı zayıfsa (son pong 0,9 sn'den eski): sol = sag = 0
5. Hareket varsa ve tarayıcının giden kutusunda hâlâ gönderilmemiş mesaj varsa: bu turu atla
6. Değer değiştiyse ya da son gönderimden 55 ms geçtiyse: gönder
```

**5. kuralın açıklaması.** "Komut kuyruğunu sayfa tarafında da kapatıyorum" dediğim buydu. Tarayıcı WebSocket'e yollanan mesajları önce kendi **giden kutusuna** koyar, ağ uygun olunca gönderir. Sinyal zayıflayınca mesajlar ağdan çıkamaz, kutuda birikir. Sinyal geri gelince hepsi peş peşe gider.

Şimdi sayfa, kutuda bekleyen bir şey varsa (`ws.bufferedAmount > 0`) üstüne yeni hareket mesajı koymuyor. Örnek:

```
t=0      sinyal kötüleşti; "sol=153" mesajı kutuda, gidemiyor
t=60     surusTik: kutu dolu → yeni mesaj koyma (atla)
t=120    surusTik: kutu dolu → atla
...      (kutuda en fazla 1 mesaj kalır, birikme olmaz)
t=900    ping cevabı 0,9 sn'dir yok → "zayıf": artık sol=sag=0 hesaplanır
         Durma mesajı kutu dolu olsa da eklenir (durmak her zaman önceliklidir)
```

Bu kural tek başına yetmez; köprü de bayat komutu atar (bölüm 5.2). Arduino da 300 ms'de durur. Üçü birlikte çalışır.

### 3.2 Vites hesabı (örnek: W'yi 3 sn basılı tut, güç %60)

| Zaman | Vites | Hız (güç sınırına oran) | Giden komut |
|---|---|---|---|
| 0,15 sn | 1 | 0,12 | 21 21 |
| 0,45 sn | 1 → 2 | 0,35'e vardı → vites 2. "Kavrama": 0,2 sn güç ×0,55 | 76 → 47 |
| 0,9 sn | 2 → 3 | 0,55'e vardı | 113 → 65 |
| 1,7 sn | 3 → 4 | 0,75'e vardı | 4. vites |
| 2,9 sn | 4 | 1,0 (tam) | 153 153 |

Bu değerler bilgisayardaki deneyde ölçüldü. Her vitesin hızlanması bir öncekinden yavaş: 0,8 / 0,45 / 0,3 / 0,2 birim/sn. Gerçek arabada yüksek viteste tork azaldığı için böyle olur.

### 3.3 Dönüş karışımı (örnekler, güç %60, tam hızda)

| Tuşlar | sol | sağ | Ne olur |
|---|---|---|---|
| W | 153 | 153 | düz ileri |
| W + D | 153 | -38 | dış teker (sol) tam ileri, iç teker (sağ) hafif geri → keskin sağ kavis |
| D (yerinde) | 122 | -122 | olduğu yerde sağa döner |
| S + D | -92 | 23 | geri giderken arka sağa döner (araba gibi) |
| S + A | 23 | -92 | geri giderken arka sola döner |

**Neden iç teker ters dönüyor?** Robot kızaklı şasi: tekerler yön değiştirmiyor, dönmek için iki taraf farklı hızda döner, tekerler yerde **kayar**. İki taraf arasındaki fark küçükse sürtünme dönmeyi yener, robot neredeyse düz gider. Eski sürümde iç teker %15 ileri dönüyordu; "giderken dönüş çok yavaş" sorunu buydu. Şimdi iç teker ters dönüyor, fark büyük.

**Eski "arka sol → arka sağ" hatası neydi?** Eski formül `sol = f + t·0,6` idi. f geri giderken eksi olunca t'nin etkisi ters taraftaki tekere biniyordu. Yeni kural yönden bağımsız: "dönülen taraf iç teker". İleri de geri de aynı davranıyor.

---

## 4. Ağ: TCP, UDP, kuyruk ve "masaya koyunca düşer mi?"

### 4.1 TCP ve UDP ne?
İnternette veri küçük paketler hâlinde gider. İki ana gönderme biçimi var:

- **TCP (taahhütlü mektup):** her paket karşıya ulaşmak ve sırayla teslim edilmek zorunda. Bir paket kaybolursa tekrar gönderilir; arkadaki paketler o gelene kadar **bekler**. Doğruluk garantili, ama ağ takılınca her şey kuyrukta birikir ve sonra hep birden gelir.
- **UDP (bağırarak söylemek):** paket gönderilir, gitmezse gitmez; tekrar yok, sıra yok. Kaybolan komutun yerine zaten yenisi gelir. Uzaktan kumandalı araçlar, oyunlar, canlı görüntü genelde UDP kullanır.

### 4.2 Eskiden ne oluyordu?
İlk sürümde sayfa her 100 ms'de bir HTTP isteği yolluyordu. HTTP TCP üstünde çalışır. Robotu eline alıp uzaklaştığında:

```
t=0      sinyal zayıfladı. Giden "M 153 153" paketleri kayboluyor, TCP tekrar deniyor.
t=0-300  Arduino 300 ms komut alamadı → motorları durdurdu (emniyet çalıştı).
t=2000   Sinyal döndü. TCP kuyruktaki paketleri teslim etti:
         "M 153 153" Arduino'ya ulaştı → motor 300 ms daha döndü.
```

Senin gördüğün tam olarak buydu: "elimdeyken motor birazcık döndü, son giden komutu yaptı". Tarayıcılar UDP gönderemez (yalnızca WebRTC ile; çok karmaşık ve Jetson'daki eski yazılımlarda zor). Bu yüzden TCP'yi **UDP gibi davrandıran** üç katman kurduk.

### 4.3 Şimdi: üç katmanlı koruma

| Katman | Nerede | Kural |
|---|---|---|
| 1 | Sayfa | Ping cevabı 0,9 sn gelmezse hareket komutu göndermez (yalnız sıfır). Gönderilmemiş komut varken yenisini yığmaz |
| 2 | Jetson köprüsü | Her komutta sayfanın saati var. Normalden 250 ms geç gelen hareket komutu uygulanmaz, robot durdurulur |
| 3 | Arduino | 300 ms komut gelmezse motorlar durur |

**Masaya koyma senaryosu (şimdi):**

```
t=0      Robotu elinle uzağa taşıdın, sinyal koptu. Sayfa "W" basılı sanıyor.
t=0.3    Arduino: 300 ms komut yok → durdu.            (katman 3)
t=0.9    Sayfa: pong yok → "kopuk"; artık sıfır hesaplıyor. (katman 1)
t=5      Robotu masaya koydun.
t=7      Sinyal döndü. Kuyruktan eski bir "sol=153" (gönderim saati t=0) köprüye geldi.
         Köprü: "bu komut normalden 7000 ms geç" → uygulamaz, "M 0 0" yollar. (katman 2)
         Robot kımıldamaz.
```

Sinyal kopukken W'den elini çekmezsen ne olur? Sinyal döndüğü anda sayfa yeni ve taze komutları yollar, robot gider; çünkü sen hâlâ "git" diyorsun. Bu doğru davranış. Robotu kaldırırken tuşlara basmamak yeterli.

---

## 5. Jetson tarafı: sürüş köprüsü

`surus_koprusu.py` bir Python programı. Jetson açılınca `systemd` onu kendiliğinden başlatır (kullanıcı servisi; çökerse 2 sn sonra yeniden başlar). İçinde aynı anda çalışan birkaç iş parçacığı (thread) var:

| İş parçacığı | Ne yapar |
|---|---|
| Arduino okuyucu | Seri portu açar, "HAZIR" bekler, gelen "D ..." satırlarını okuyup durumu saklar. Port koparsa 2 sn'de bir yeniden dener |
| Kamera | İzleyen varken GStreamer'ı başlatır, kareleri okur. 10 sn kimse izlemezse kapatır |
| H.264 okuyucu | Sıkıştırılmış akışı karelere böler, izleyicilerin kuyruklarına dağıtır |
| Hız ölçer | Saniyede bir, gönderilen veri miktarından Mbit/s hesaplar |
| Web sunucusu | Her bağlanan tarayıcı için ayrı iş parçacığı açar (sayfa, `/kontrol`, `/goruntu-h264`) |
| Durum yollayıcı | Her `/kontrol` bağlantısı için 100 ms'de bir durum mesajı |

### 5.1 WebSocket
HTTP'de her komut ayrı bir "istek + cevap"tı: ~500 bayt başlık ve her seferinde cevap beklemek. WebSocket bir kez açılan ve iki yöne de mesaj taşıyan bir hat; mesaj başına birkaç bayt ek yük var.
- Sürüş komutu ~60 bayt; saniyede ~17 tane ≈ 1 KB/sn.
- Durum ~300 bayt; saniyede 10 tane ≈ 3 KB/sn.

WebSocket sunucusunu hazır kütüphane olmadan kendimiz yazdık (`websoket.py`, ~120 satır). Jetson'daki Python 3.6 eski; hazır kütüphaneler yeni Python istiyor.

### 5.2 Bayat komut nasıl anlaşılıyor (örnek)
Sayfanın saati ile Jetson'un saati farklı, birbirinin aynısı değil. Bu yüzden "komut kaç ms gecikti" doğrudan hesaplanamaz. Köprü şu hileyi kullanır:

```
fark = (komut köprüye geldiğinde köprünün saati) − (komuttaki sayfa saati)
```

Saatler farklı olduğu için bu sayı tek başına anlamsız; örneğin 51.234.000 ms. Ama ağın **en iyi** anındaki fark, şimdiye kadar görülen **en küçük** farktır. Gecikme ondan ne kadar büyükse o kadar artmıştır:

```
komut 1: fark = 51.234.010   → en küçük = 51.234.010, gecikme = 0
komut 2: fark = 51.234.006   → en küçük = 51.234.006, gecikme = 0
komut 3: fark = 51.234.015   → gecikme = 9 ms     → uygula
komut 4: fark = 51.241.006   → gecikme = 7000 ms  → BAYAT, uygulama, "M 0 0" yolla
```

Eşik 250 ms. Normal ağ dalgalanması (10-50 ms) sorun çıkarmaz; birikmiş eski komutlar çıkarır. Ayarlar panelinde "Atılan eski komut" sayısı görünür.

### 5.3 Sürücü kilidi (örnek)
Telefondan ve PC'den aynı anda açtın. Telefondan W'ye bastın. PC sayfası her açılışta ya da tuş bırakıldığında "0 0" yollayabilir. Köprü: son 0,6 sn'de hareket ettiren telefon olduğu için PC'nin komutlarını yok sayar. Telefon bırakınca 0,6 sn sonra PC sürebilir.

### 5.4 Görüntü hattı
```
Theta S ──USB──► v4l2src (JPEG kareler, 1280×720, 15/sn)
                    │
                   tee (ikiye ayır)
          ┌─────────┴──────────┐
     JPEG kolu             H.264 kolu
  (olduğu gibi)        jpegdec (JPEG'i çöz, CPU)
          │            nvvidconv (GPU belleğine al)
          │            nvv4l2h264enc (donanım kodlayıcı, 4 Mbit/s)
          ▼                    ▼
  /goruntu.mjpg          /goruntu-h264 (WebSocket)
  /kare.jpg
```

**JPEG ve H.264 farkı (örnek):**
- **JPEG:** her kare bağımsız bir fotoğraf (~150-400 KB). 15 fotoğraf/sn = 2,2-6 MB/sn = 18-48 Mbit/s.
- **H.264:** ilk kare tam fotoğraf (anahtar kare, IDR). Sonrakiler yalnızca **farkı** gönderir ("şu bölge 3 piksel sağa kaydı, şurası biraz koyulaştı"). Robot dururken fark neredeyse sıfırdır. Ortalama 4 Mbit/s, yani 5-12 kat az.
- **Bedeli:** fark kareleri öncekine dayanır, ortadan biri kaybolursa sonrakiler bozulur. Bu yüzden saniyede bir anahtar kare gönderiyoruz ve kuyruk dolarsa "sonraki anahtar kareye kadar atla" kuralı var:

```
İzleyicinin kuyruğu: [kare 41] [kare 42] [kare 43]  ← 3 kare birikti, ağ yetişmiyor
→ kuyruğu boşalt, "anahtar kare bekle" moduna geç
kare 44 (fark) → atla
kare 45 (anahtar, IDR) → kuyruğa koy, devam
```

Ekranda kısa bir donma olur ama gecikme birikmez. Eskiden JPEG'de böyle bir sınır yoktu, gecikme saniyelerce büyüyebiliyordu.

---

## 6. Arduino tarafı: tek çekirdekte "aynı anda" iş yapmak

### 6.1 Sorun: `delay()`
Arduino kodunda en sık görülen yazım:

```cpp
void loop() {
  tone(11, 440); delay(500);   // yarım saniye nota
  noTone(11);    delay(100);
}
```

`delay(500)` = "yarım saniye hiçbir şey yapma". O sırada:
- Jetson'dan "dur" komutu gelse okunmaz.
- Motor rampası ilerlemez.
- Işık yanıp sönmez.

Tek çekirdek "aynı anda" iki iş yapamaz; ama işleri **çok küçük parçalara bölüp hızla sırayla** yaparsa insana aynı anda gibi gelir.

### 6.2 Çözüm: saate bak, sırası geleni yap, beklemeden geç
`millis()`, Arduino açıldığından beri geçen milisaniyeyi verir; Timer0 adlı donanım sayacı bunu kendiliğinden sayar. Her işin "en son ne zaman yaptım" değişkeni var:

```cpp
void loop() {
  seriOku();                         // gelen bayt var mı? varsa al, satır bittiyse komutu işle
  unsigned long simdi = millis();

  if (simdi - sonRampa >= 5) {       // 5 ms geçti mi?
    sonRampa = simdi;
    // motorları hedefe bir adım yaklaştır
  }
  melodiIlerlet(simdi);              // notanın süresi doldu mu? dolduysa sıradakine geç
  sesGuncelle(simdi);                // buzzer'da şu an ne çalmalı?
  selektorIlerlet(simdi);            // selektörün sıradaki yak/söndür zamanı geldi mi?
  dortluIlerlet(simdi);              // dörtlünün 500 ms'si doldu mu?
  if (simdi - sonDurum >= 100) {     // 100 ms geçti mi?
    sonDurum = simdi;
    durumGonder();
  }
}
```

Bu `loop()` bir turu ~50-100 mikrosaniyede bitirir, yani **saniyede ~10.000-20.000 kez döner**. Her turda çoğu iş "daha zamanım gelmedi" deyip hemen geçer.

### 6.3 Örnek 1: sürerken korna (zaman çizelgesi)
```
t=0,000 ms  seriOku: "M 153 153" → hedefSol = hedefSag = 153
t=0,08      tur bitti, yenisi başladı. Rampa? 5 ms dolmadı → geç. Ses? yok → geç.
...         (60 tur boyunca hiçbir şey yapmadan geçiyor)
t=5,0       rampa: simdiSol 0 → 4, OCR1A'ya yaz (Timer1 PWM'i kendisi üretiyor)
t=10,0      rampa: 4 → 8
...
t=50,0      seriOku: "K 260" → kornaBitis = 310
            sesGuncelle: korna aktif → tone(11, 420)
            (Timer2 artık pin 11'i saniyede 840 kez kendisi çeviriyor)
t=54,0      sesGuncelle: (54/4)%2 = 1 → tone(11, 500)
t=55,0      rampa: 40 → 44   ← motor rampası korna yüzünden hiç beklemedi
t=58,0      sesGuncelle: tone(11, 420)
...
t=310       korna süresi bitti → sesGuncelle: çalacak bir şey yok → noTone
```

### 6.4 Örnek 2: müzik + dörtlü + sürüş aynı anda
Her işin kendi saati var:

| İş | Kendi değişkeni | Ne zaman bir şey yapar |
|---|---|---|
| Melodi | `notaBitis`, `sesBitis` | nota süresi dolunca sıradaki notaya geçer |
| Dörtlü | `dortluZamani` | 500 ms'de bir röleyi çevirir |
| Rampa | `sonRampa` | 5 ms'de bir |
| Durum | `sonDurum` | 100 ms'de bir |

Bunlar birbirini hiç beklemez. Melodi 250 ms'lik bir notadayken o 250 ms boyunca on binlerce tur döner; rampa 50 kez ilerler, dörtlü belki bir kez çevrilir.

### 6.5 "Bu çok mu basit, hazır fonksiyon mu?"
Kısmen hazır, kısmen tasarım:
- **Hazır gelenler (Arduino kütüphanesi ve donanım):**
  - `millis()`: saat.
  - `tone()`: Timer2 ile kare dalga.
  - `Serial`: seri donanım, gelen baytları **kesmeyle** (interrupt) kendi tamponuna alır. `loop()` meşgulken bile bayt kaybolmaz.
  - `analogRead`, `digitalWrite`.
- **Tasarlanan:**
  - Her iş için "son zaman + durum" değişkenleri, yani küçük durum makineleri. Melodi: hangi notada, ne zaman bitecek. Selektör: kaç adım kaldı.
  - Timer1'i 20 kHz PWM için elle ayarlamak. Arduino'nun hazır `analogWrite`'ı 490 Hz verir ve motoru cızırdatır.
  - Rampa kuralları, ölü bölge, emniyet süresi, tek buzzer için ses önceliği.
  - Her işin süresini işine göre seçmek:

| İş | Aralık | Neden bu kadar |
|---|---|---|
| Rampa adımı | 5 ms | 255 adımı ~0,3 sn'ye yaymak için yeterince sık; daha sık gereksiz |
| Durum satırı | 100 ms | göstergeler için yeterince taze; seri hattı yormaz |
| Emniyet süresi | 300 ms | sayfa 60 ms'de bir yolluyor; 4-5 komut kaybı tolere edilir, ama robot 300 ms'den fazla kör gitmez |
| Korna ton değişimi | 4 ms | 125 Hz'lik "pürüz" verir, kulak iki tonu tek kalın ses gibi duyar |
| Selektör | 110 ms | röle mekanik; daha hızlısı rölenin tepkime süresine yaklaşır |
| Dörtlü | 500 ms | dakikada 60, gerçek arabaların alt sınırı |

Bu yazım biçiminin adı **"kooperatif çoklu görev"** (cooperative multitasking). Arduino'nun resmi "Blink Without Delay" örneğinin büyütülmüş hâlidir.

### 6.6 Arduino'ya yük biniyor mu?
Hayır, çok rahat:
- **İşlemci:** zamanının %99'undan fazlasında "henüz zamanı gelmedi" deyip geçiyor.
- **Seri hat:** 115.200 bit/sn ≈ 11.500 bayt/sn kapasite. Kullanımımız ~1.000 bayt/sn ≈ %9.
- **Bellek:** RAM 2 KB'nin %17'si, program belleği 32 KB'nin %48'i (şarkılar dahil).

Gecikmenin Arduino ile ilgisi yok; ağdan kaynaklanıyor.

### 6.7 Dil ve yapı
C++ (Arduino dili = C++ + Arduino kütüphanesi). Sınıf kullanmadık; global değişkenler ve kısa fonksiyonlar, C tarzı. Dosyalar:
- `robot_kontrol.ino`: her şey.
- `sarkilar.h`: araçla üretilen şarkı dizileri.

Fonksiyonlar:
- `motorYaz`: bir motora yön ve güç yazar.
- `yaklas`: rampa adımı.
- `motorlariDurdur`: iki motoru da hemen durdurur.
- `akimMa`: akım ölçümünü mA'e çevirir.
- `komutuIsle`: seriden gelen satırı yorumlar.
- `seriOku`: gelen baytları satıra toplar.
- `durumGonder`: "D ..." satırını yollar.
- `notaBaslat`, `melodiCal`, `melodiIlerlet`: melodi çalar.
- `sesYaz`, `sesGuncelle`: buzzer'ı yönetir.
- `ledYaz`, `dortluAyarla`, `dortluIlerlet`, `selektorIlerlet`: ışık.

---

## 7. Motor: PWM, rampa, ölü bölge, akım

### 7.1 PWM (20 kHz) adım adım
Motor sürücü (VNH5019) motora giden gerilimi bir anahtar gibi açıp kapatır. Timer1 bir sayaç: 0'dan 400'e sayar, 400'den 0'a geri iner; bir tam tur 50 mikrosaniye, saniyede 20.000 tur. Pin 9'un açık mı kapalı mı olduğunu karşılaştırma değeri belirler:

```
OCR1A = 240 (60% doluluk) için bir tur (50 µs):
sayaç:  0 → → → 240 → → → 400 → → → 240 → → → 0
pin 9:  AÇIK─────────┐ KAPALI ─────────┌ AÇIK
                     └─────────────────┘
→ zamanın %60'ında açık → motor ortalama %60 gerilim görür
```

Motorun bobini akımın ani değişmesine direnir, rotor da ağır. Saniyede 20.000 kez açılıp kapanma motora düz bir gerilim gibi gelir. 490 Hz'te (Arduino varsayılanı) bobin bu açma-kapamayı duyulur bir ötmeye çevirir. 20 kHz insan kulağının sınırında, duyulmaz.

`M 150 150` → ölü bölge → 50 + 150×205/255 ≈ 171 → OCR1A = 171×400/255 ≈ 268 → %67 doluluk. Motorlara 12 V gidiyorsa ortalama ~8 V.

### 7.2 Rampa (örnek: 0'dan 153'e, sonra bırak)
```
Hızlanma (5 ms'de +4):   0, 4, 8, 12, ... 152, 153   → 39 adım × 5 ms ≈ 195 ms
Yavaşlama (5 ms'de −9):  153, 144, 135, ... 9, 0      → 17 adım × 5 ms ≈ 85 ms
İleriden geriye (153 → −153):
                         153 … 0 (yavaşlama hızıyla)  → sonra 0 … −153 (hızlanma hızıyla)
```

**Neden hızlanma yavaş, yavaşlama hızlı?**
- **Duran motor ilk anda çok akım çeker.** Dönerken motor kendi içinde ters bir gerilim üretir (karşı-EMK) ve bu akımı sınırlar. Dururken bu yoktur, akımı yalnızca bobinin küçük direnci sınırlar. Ani tam güç → akım tepesi → pil gerilimi anlık düşer (Jetson bundan etkilenebilir), dişliler darbe alır.
  - Senin ölçümün: kalkışta en fazla 0,9 A, sürerken 0,1-0,5 A. Rampa bu tepeyi yumuşatıyor.
- **Durmak güvenlik işidir, hızlı olmalı.** Yavaşlarken motor zaten akım istemez.

**0,32 sn nereden geldi:** ölçümden değil, iki ölçütten seçilmiş bir başlangıç değeri:
- 0,5 sn'nin altı insana "tuşa basınca hemen tepki verdi" gibi gelir.
- 0,3 sn civarı, kalkış akımını ve dişliye vuran darbeyi belirgin azaltacak kadar yumuşaktır.

Daha iyisi ölçerek ayarlamak: rampayı 0,1 / 0,3 / 0,5 sn yapıp kalkıştaki en yüksek akıma bakmak. Kalkış akımı 0,3 sn'de zaten 0,9 A ile düşük; değiştirmeye gerek görünmüyor.

### 7.3 Ölü bölge
Motor, dişli kutusu ve tekerlek sürtünmesi yüzünden düşük PWM'de motor dönmez, sadece vınlar. Bu tür motorlarda eşik kabaca %15-25. Kod sıfır olmayan her komutu %20'den (50/255) başlatıyor. Gerçek eşik yerde denenip ayarlanmalı:
- Güç %20, W'ye kısa bas.
- Robot kımıldamıyorsa `OLU_BOLGE`'yi büyüt.
- Fırlıyorsa küçült.

### 7.4 Akım: kaç amper normal?
VNH5019 her motor kanalı için akımla orantılı gerilim verir (~140 mV/A). Arduino bunu `analogRead` ile 0-1023 arası okur; bir adım ≈ 34 mA. Yani 0,03 A'nın altını göremez, düşük akımlarda ölçüm de kabadır. Bir kanalda iki motor paralel bağlıysa (4 tekerli şaside sık olur) okunan değer ikisinin toplamıdır.

12 V, 120 devir/dk'lık redüktörlü DC motorlar için tipik değerler. Motor modeline göre değişir; kesin değer motorun etiketinde ya da satıcı sayfasında olur:

| Durum | Tipik akım (motor başına) |
|---|---|
| Boşta (tekerlek havada) | 0,1-0,3 A |
| Düz zeminde sürüş | 0,2-0,6 A |
| Kalkış tepesi | 0,5-1,5 A (rampalı) |
| Zorlanma (halı, rampa) | 1-2 A |
| Sıkışma (stall: teker döndürülemiyor) | 2-6 A |

**Senin ölçümün:** düz sürüşte 0,1-0,2 A, kalkış tepesi 0,9 A, dururken 0. Bu **düşük ve sağlıklı**: robot hafif, zemin düz, motorlar zorlanmıyor. VNH5019 kanal başına sürekli ~12 A, tepe 30 A taşır; çok uzağız.

Asıl risk sıkışma: duvara dayanıp teker dönemezken uzun süre güç vermek motoru ısıtır. Önlemi yapılacaklarda (B4: akım sınırı).

---

## 8. Buzzer: ses nasıl çıkıyor, öncelik, şarkılar

### 8.1 Ses nasıl çıkıyor
Buzzer pasif bir piezo. İçinde gerilim gelince bükülen ince bir seramik disk var.
- **`tone(11, 440)`:** Timer2 pin 11'i saniyede 880 kez çevirir (440 açık + 440 kapalı = 440 tam dalga). Disk saniyede 440 kez titrer ve havayı titreştirir: 440 Hz, La notası.
- **Frekans = perde:** 880 Hz bir oktav tiz.
- **Ses seviyesi:** dalga hep tam 5 V; kod sesi kısamaz. Disk 2-4 kHz civarında en gür; kalın sesler kısık çıkar. Bu yüzden şarkı çevirici aracı kalın şarkıları otomatik oktav yukarı taşıyor (Doom 3 oktav).

### 8.2 Motor PWM'i ile farkı
İkisi de "pin aç-kapa" ama amaçları farklı:

| | Aç-kapa hızı | Önemli olan |
|---|---|---|
| Motor | 20.000/sn, sabit | açık kalma **oranı**, gücü belirler |
| Buzzer | notanın frekansı | aç-kapa **hızı**, sesin kendisi |

İkisi de ayrı donanım sayaçlarında (Timer1 / Timer2) çalıştığı için birbirini etkilemez.

### 8.3 Öncelik (tek buzzer)
Her turda `sesGuncelle` sırayla bakar ve ilk uyanı çalar:
1. Bip (`B` komutu)
2. Korna (`K`)
3. Melodi
4. Geri vites bipi (iki motor da geri komutu alıyorsa)
5. Motor sesi (açıksa)

Örnek: Megalovania çalarken geri gidiyorsun → melodi geri bipinden önce olduğu için melodi duyulur. Kornaya basarsan korna duyulur, melodi arkada zamanını saymaya devam eder, korna bitince kaldığı yerden duyulur.

### 8.4 Şarkılar nasıl eklendi
- Hazır nota dökümleri (`github.com/robsoncouto/arduino-songs`, Megalovania için o deponun 23 numaralı konusu) şu biçimde: `NOTE_E5, 8` (nota adı, bölen).
- `tools/melodi_cevir.py` bunları {Hz, ms} çiftlerine çevirir: tempo 140'ta bir dörtlük 428 ms, sekizlik 214 ms; eksi işaret noktalı (1,5 kat). Sonuç `sarkilar.h` dosyasına yazılır.
- Diziler program belleğinde (`PROGMEM`). 2 KB'lık RAM'e sığmazlardı; program belleği 32 KB.
- **Witcher 3:** güvenilir bir nota dökümü bulunamadı. Google'ın yapay zeka cevabı olmayan bir dosyayı gösterdi. Bir **MIDI dosyası** bulunursa (MuseScore'da ücretsiz hesapla indirilebiliyor) şu komutla çevrilir:

  ```bash
  python tools/melodi_cevir.py midi kaer_morhen.mid M_KAER_MORHEN "Kaer Morhen"
  ```

  Çıktı `sarkilar.h`'ye eklenir, `MELODILER` listesine ve sayfadaki listeye adı yazılır. MIDI'de birden çok ses varsa en tiz olanı (genelde melodi) alınır.

---

## 9. 360° görüntü: kameradan ekrana

### 9.1 Kamera ne veriyor
Theta S'in iki balık gözü lensi var. Her biri ~203° görür, yani yarım küreden biraz fazla. USB'den yan yana iki daire gelir: 1280×720 piksel, saniyede 15 kare. 15 fps ve bu çözünürlük Theta S'in USB üzerinden verebildiğinin en fazlası; yazılımla artırılamaz.

### 9.2 Ekrandaki her piksel nasıl boyanıyor (örnek)
Ekranın ortasındaki piksel için:
1. Bu piksel bakış yönünde düz ileriyi gösterir.
2. Senin sürükleyerek çevirdiğin açı ve kameranın robota takılış açısıyla döndürülür: "kameraya göre şu yön".
3. Bu yön ön lense mi arka lense mi düşüyor? Optik eksenle arasındaki açıya bakılır, diyelim 40°.
4. Balık gözü kuralı: merkezden uzaklık = açı × ölçek. Ölçülen ölçek 180,87 piksel/radyan. 40° = 0,698 rad → merkezden 126 piksel uzakta.
5. Lens merkezi (322,42; 319,04), yön de belli → kaynak karedeki piksel bulunur, rengi alınır.

Bunu ekran kartı **her piksel için, her karede** yapar: 1920×1080 ekran = 2 milyon piksel × 60 kare/sn. Bu küçük programın adı "gölgelendirici" (shader).

### 9.3 Birleştirme
- **Eski sorun:** lens ölçüleri tahminiydi ve iki lens 90° çizgisinde kesin bir çizgiyle değişiyordu; dikiş belliydi.
- **Ölçüm:** robottan iki kare alıp bir optimizasyon programı (`tools/lens_ayarla.py`) çalıştırdık. Program iki lensin **ikisinin de gördüğü** 90° ± 6° bandında, iki lensten alınan görüntüler en çok benzeyene kadar merkezleri, ölçeği ve ikinci lensin dönmesini değiştirdi. Uyumsuzluk 0,29'dan 0,14'e indi; ikinci lens 1° kadar yamuk takılıymış.
- **Şimdi:** bantta iki lens yumuşakça karışıyor. Lens kenarındaki karanlık halka kullanılmıyor.
- **Kalan iz:** robotun gövdesi gibi çok yakın şeylerde iz kalıyor. İki lens birkaç cm aralıklı, yakın nesneyi iki farklı açıdan görüyorlar (gözünü sırayla kapatınca parmağının kayması gibi).

### 9.4 Aynalar, öne bak, arkaya bak
- **Aynalar:** aynı 360° kareden, baktığın yönün 125° solu ve sağı, dar bir görüşle çizilir ve soldan sağa ters çevrilir. Gerçek ayna gibi; ayrı kamera yok.
- **R:** bakışı yumuşakça (0,3 sn) öne döndürür.
- **B:** arkaya döndürür, tekrar basınca öne.

---

## 10. Wi-Fi, anten ve gecikme

### 10.1 Ölçülen
- **Sinyal:** robot ve bilgisayar yan yanayken bilgisayar %92 (≈ -45/-50 dBm), robot -70 dBm.
  - dBm logaritmik bir ölçek: her -3 dBm gücün yarıya inmesi, -10 dBm onda birine inmesi demek. -70 dBm, -50 dBm'den **100 kat zayıf** sinyal.
- **Ağ:** EDUROAM, 5 GHz, kanal 60.
- **Eski görüntü:** 20-50 Mbit/s JPEG.

### 10.2 Neden kopuyordu
Zayıf sinyalde Wi-Fi kartı daha yavaş ama daha sağlam bir hıza iner. -70 dBm'de gerçek kullanılabilir hız kabaca 20-60 Mbit/s. Duvar 5 GHz sinyali ~10-15 dBm daha zayıflatır, kullanılabilir hız 5-10 Mbit/s'ye düşer. Bizim JPEG akışımız 20-50 Mbit/s isterken:

```
ağın taşıyabildiği < görüntünün istediği
 → kareler kuyrukta birikir (gecikme büyür)
 → komutlar o kuyruğun arkasında bekler (sürüş gecikir)
 → kuyruk taşar, bağlantı zaman aşımına uğrar (kopma)
```

H.264 (4 Mbit/s) ile aynı sinyalde 5-10 kat pay kalır. En büyük iyileşme bundan beklenir.

### 10.3 Modem mi, doğrudan bağlantı mı?
**Modeme bağlanmak:** robot → modem → bilgisayar. **İki ayrı kablosuz atlama** var ve ikisi de iyi olmalı. Senin fikrin doğruydu: modem evin ortasındaysa, robot hangi odada olursa olsun modeme yakın kalır. Ama:
- Robotun anteni zayıfsa robot → modem atlaması her yerde zayıf kalır.
- Okulda EDUROAM'un erişim noktası nerede, bilmiyoruz; tavandaki cihaz başka odada olabilir.
- Okul ağı kalabalık, aynı kanalı onlarca kişi paylaşıyor.
- Okul ağları genelde cihazdan cihaza trafiği merkezi bir denetleyiciden geçirir; bu biraz gecikme ekler.

**Doğrudan bağlanmak** (bilgisayar robotun `Robot-XXXX` ağına): tek atlama, kalabalık yok, 2,4 GHz (duvardan daha iyi geçer). Ama menzil robotla bilgisayarın arasındaki mesafeyle sınırlı. Bilgisayar sabit, robot gezerse uzaklaşınca zayıflar.

**Hangisi daha iyi:** önceden kesin söylenemez, ölçmek gerek. Planın doğru: pil dolunca bilgisayarı ve robotu **kendi ev ağına** bağla (okul ağından kurtulmuş olursun), sonra doğrudan bağlantıyı dene. Her birinde:
- Ayarlar panelindeki Wi-Fi sinyali (dBm).
- Gidip gelme süresi (ms).
- Veri hızı (Mbit/s).
- Hangi duvarda koptuğu.

### 10.4 Anten
- **İkisinin de takılı olması iyi.** Kart 2×2: iki anten aynı anda kullanılır.
- **Jetson'a 2-3 parmak yakınlık:** soğutucu büyük bir metal parçası, yakın metal anteni biraz zayıflatır ve sinyali bir yönde gölgeler. 5 GHz'te dalga boyu 6 cm, 2,4 GHz'te 12,5 cm. Dalga boyunun yarısından fazla uzaklık (3-6 cm) genelde yeterli; 2-3 parmak sınırda ama kabul edilebilir.
- **İyileştirmek için:**
  - Antenleri dik konumla, robotun üst kenarından biraz yukarı taşı.
  - Motor ve pil kablolarından uzak tut.
  - İkisini birbirine dik açıyla koy (biri dik, biri 45°). Farklı yönlerden gelen sinyali daha iyi yakalar.
- **Dikkat:** drone'larda 2,4 GHz kumanda ve 5,8 GHz görüntü antenleri kullanılır. Antenin yalnız 2,4 GHz için yapılmış olma ihtimali var. Öyleyse 5 GHz'te (okul ağı, kanal 60) zayıf çeker, 2,4 GHz'te iyi çeker. Ev modeminin 2,4 GHz ağına bağlanınca sinyal belirgin artıyorsa sebep budur.
- **Asıl sorun anten mi:** robot ile bilgisayar arasındaki 100 kat sinyal farkının büyük kısmı anten tarafında. Ama kopmayı tetikleyen, buna ağır görüntü akışının eklenmesiydi. İkisi birlikte.

### 10.5 Saniyede ne kadar veri
| Akış | Yön | Miktar |
|---|---|---|
| Görüntü (JPEG, eski) | robot → sen | 20-50 Mbit/s |
| Görüntü (H.264, yeni) | robot → sen | ~4 Mbit/s |
| Sürüş komutları | sen → robot | ~1 KB/sn (0,008 Mbit/s) |
| Durum | robot → sen | ~3 KB/sn |
| Ping | iki yön | ~0,2 KB/sn |

### 10.6 Jetson ne kadar yükte
`tegrastats` ölçümü (görüntü akarken):
- **İşlemci:** 2 çekirdek açık (5 W modu, diğer 2 kapalı), %10-35, 921 MHz.
- **GPU:** %0.
- **RAM:** 1,2 / 4 GB.
- **Sıcaklık:** 42 °C; Wi-Fi kartı 67 °C.

H.264'e geçince JPEG çözme işlemciye ~%20 ekler; sıkıştırma ayrı donanımda yapılır. Görüntü işleme (ArUco, nesne tanıma) için bol yer var. Gerekirse 10 W moduna geçilir (4 çekirdek, 1,43 GHz).

---

## 11. Güvenli kapatma nasıl çalışıyor

```
Paneldeki "Kapat" → onay → sayfa robota POST /api/guc {"islem":"kapat"} yollar
→ robot_paneli.py (Jetson'da root olarak çalışan servis) isteği alır
→ "tamam" cevabını verir, 1 sn bekler (cevap sayfaya ulaşsın diye)
→ subprocess ile `systemctl poweroff` çalıştırır
→ systemd (Linux'un servis yöneticisi) sırayla:
     1. Bütün servislere "kapan" sinyali yollar (sürüş köprüsü, panel, uzak masaüstü, Docker...)
     2. Kapanmayanları birkaç saniye sonra zorla kapatır
     3. Bellekte bekleyen dosya yazmalarını SD karta yazar (sync)
     4. Dosya sistemlerini ayırır (unmount): SD kart "temiz kapatıldı" olarak işaretlenir
     5. Jetson'un güç yönetim yongasına "kapat" der → kart güç çekmeyi bırakır
→ sayfa robota ulaşamayınca 15 sn sayar ve "Robot kapandı" der
→ şimdi pil şalterini indirebilirsin
```

**sudo gerekmiyor mu?** `sudo`, sıradan kullanıcının (bs) bir komutu yönetici (root) olarak çalıştırmasını sağlar ve şifre ister. Panel servisi zaten root olarak çalışıyor (`kurulum.sh` onu öyle kurdu). Root'un sudo'ya ihtiyacı yok; `systemctl poweroff` doğrudan çalışır. Ubuntu'da menüden "Kapat" da aynı komutu çağırır.

**Neden önemli:** güç aniden kesilirse yazılmakta olan dosyalar yarım kalır. SD kartın dosya sistemi bozulabilir, Jetson bir dahaki açılışta açılmayabilir.

**Dikkat:** robotun ağındaki herkes paneli açıp robotu kapatabilir. Okul ağında bu bir risk; ileride panele basit bir şifre eklenebilir.

---

## 12. Pil ne kadar gider?

**Pil:** 4S LiPo, 5200 mAh, 14,8 V nominal (16,8 V dolu). Toplam enerji ≈ 14,8 V × 5,2 Ah ≈ 77 Wh.

**Hocanın 15,0 V sınırı:** hücre başına 3,75 V. LiPo dinlenirken 3,75 V'ta kabaca **%40-50 dolu** demek. Yani bu sınırla pilin yaklaşık yarısını (~35-40 Wh) kullanıyoruz. Bu temkinli ve pile iyi gelir; LiPo için asıl tehlike sınırı yükteyken hücre başına 3,3-3,5 V (13,2-14 V).

**Tüketim tahmini:**

| Ne | Güç |
|---|---|
| Jetson (5 W modu, görüntü akarken) | 4-6 W |
| Theta S (USB'den beslenip şarj oluyor) | ~2,5 W |
| Wi-Fi, Arduino, röle, regülatör kayıpları | ~2 W |
| Motorlar, sürerken (2 kanal × 0,2-0,5 A × 12 V) | 5-12 W |
| **Toplam, sürüş yokken** | **~9 W** |
| **Toplam, sürekli sürerken** | **~15-20 W** |

**Hesap:** ~37 Wh ÷ 9 W ≈ **4 saat** boşta açık. Sürekli sürüşte 37 ÷ 17 ≈ **2-2,5 saat**. Karışık kullanımda (ara ara sürüş) **~3-3,5 saat**.

**Gözlem:** bugün ~19:00'dan ~22:30'a kadar açık kaldı (arada yemek molası, kapalıyken tüketim yok) ve 14,9 V'a indi. Tahminle uyumlu. Kesin ölçüm için yapılacaklardaki pil voltajı sensörü (B1) gerekir; o gelince süre de sayfada tahmin edilebilir.

---

## 13. ROS: ne işe yarar, kullanıyor muyuz?

**Şu an kullanmıyoruz.** Robotun bütün yazılımı ROS'suz çalışıyor.

**ROS (Robot Operating System) nedir:** işletim sistemi değil, robot programlarını birbirine bağlayan bir altyapı. Programlar "düğüm" (node) olur; birbirlerine "konu" (topic) üzerinden mesaj yollarlar. Örnek:
- kamera düğümü → `/kamera/goruntu` konusu → nesne tanıma düğümü
- IMU düğümü → `/imu` → konum tahmini düğümü → `/konum` → harita düğümü

**Kazandırdıkları:**
- **Hazır paketler:** SLAM (harita çıkarma), Nav2 (otonom gezinme), robot_localization (IMU + tekerlek verisini birleştirip konum tahmini), lidar/kamera sürücüleri.
- **Araçlar:** `rviz` (robotu, haritayı, sensörleri 3B görme), `rosbag` (bütün sensör verisini kaydedip sonra aynen tekrar oynatma; AR geliştirirken robotu sürmeden test etmeyi sağlar).
- **Standart:** hocalar ve akademik çalışmalar ROS bekler; tezde "ROS 2 ile entegre" yazmak artıdır.

**Neden şimdi gerek yok:**
- Bizde iki parça var (Jetson ↔ Arduino) ve basit bir köprü yetiyor.
- ROS her mesaja gecikme ve karmaşıklık ekler. Mimari kararlarında "video ROS'tan geçmez" diye yazdık.
- Jetson Nano'nun Ubuntu 18.04'ü ROS 2 Humble'ı doğrudan çalıştıramıyor; bu yüzden Docker içinde kurulu. Kurulumu ve bakımı zor.

**Ne zaman işe yarar:** haritalama ve konum tahmini (bölüm 16) başlayınca. IMU, mesafe sensörü, ArUco tespiti ve sürüş komutlarını birleştirip "robot evin neresinde" sorusunu çözmek için ROS'un hazır paketleri (robot_localization, rtabmap_ros) çok iş görür.

**Önerim:**
1. Sürüş ve görüntü ROS'suz kalsın; hızlı ve basit.
2. Sensörler gelince, köprünün yanına küçük bir ROS 2 düğümü ekleyelim. Durum, IMU ve ArUco tespitlerini ROS konularına yayınlasın.
3. Hocaya `rviz`'de robotun konumunu gösterebiliriz.

Hocaya neden istediğini sormak iyi olur: belli bir paketi mi (SLAM, Nav2) düşünüyor, yoksa genel olarak "ROS kullanın" mı diyor?

---

## 14. Sürüş hissi: oyunlardaki "araba hissi" nereden gelir?

Sürüşü ben hissedemiyorum; ETS2'ye benzeyip benzemediğini sen söyleyebilirsin. Simülasyon oyunlarında "araba sürüyormuşum" hissini veren şeyler ve bizdeki karşılıkları:

| His veren şey | ETS2 / simülasyonlarda | Bizde |
|---|---|---|
| Kütle / atalet | araç yavaş hızlanır, bırakınca kayarak durur | ✔ vites ve hız eğrisi, bırakınca 0,55 sn'de durma |
| Motor sesi devirle değişir | gaza basınca ses yükselir | ✔ (E), piezo olduğu için ince |
| Vites geçişi | geçişte kısa çekiş kaybı ve ses düşüşü | ✔ kavrama (0,2 sn güç ×0,55) |
| Direksiyon | ekranda döner; direksiyon setinde kuvvet geri bildirimi | ✔ ekranda döner (kuvvet geri bildirimi yok) |
| Göstergeler | hız, devir, vites | ✔ vites ve güç çubuğu (hız göstergesi fikri bölüm 17'de) |
| Aynalar | sağ/sol ayna | ✔ |
| Kamera hareketi | hızlanınca baş hafif geri gider, frende öne eğilir, yolda titreşim | ✗ yok (fikir: hızlanma/frende görüntüyü 1-2° eğmek, ucuz ama etkili) |
| Ortam sesi | rüzgâr, lastik sesi | ✗ |
| Gerçek dünya | — | robot gerçekten gidiyor; simülasyonun veremeyeceği şey |

Kamera hareketi ve ortam sesi gerçekçiliği en çok artıran iki eksik. İstersen kamera eğilmesini ekleyebilirim.

---

## 15. Telefon uygulaması

Tarayıcı sayfası hızlı geliştirme ve deneme için iyi, ama sınırları var:
- Sayfa `http` olduğu için Chrome bazı özellikleri **kapatıyor**: telefonun jiroskopu (telefonu çevirerek bakmak), WebCodecs (en düşük gecikmeli görüntü çözme). Bunlar yalnız `https` ya da `localhost`'ta açılıyor.
- Tam ekran, ekranın kapanmaması, titreşim, arka planda çalışma gibi şeyler sınırlı.

**Önerim: Unity'de Android uygulaması.** Oyun zaten Unity'de; `IRobotLink` arayüzü ve `MockRobot` hazır. Adımlar:
1. **RealRobotLink:** robota WebSocket ile bağlanır (Unity için hazır paket: NativeWebSocket). Sayfanın yolladığı mesajların aynısını yollar: `surus`, `led`, `korna`...
2. **Görüntü:**
   - Önce kolay yol: JPEG kareleri al, `Texture2D.LoadImage` ile çöz. 720p/15 fps'i Galaxy S21+ kaldırır; ağ yükü yüksek kalır.
   - Sonra asıl yol: H.264'ü Android'in donanım çözücüsüyle (MediaCodec) çözen küçük bir eklenti, ya da hazır bir eklenti.
3. **360° düzeltme:** sayfadaki gölgelendiricinin aynısı Unity gölgelendiricisi olarak (`PanoramaViewer` zaten benzer bir iş yapıyor).
4. **Telefon sensörleri:** `Input.gyro` ile telefonu çevirince bakmak, ivmeölçerle telefonu direksiyon gibi eğerek sürmek. Native uygulamada https sorunu yok.
5. **İleride:** gerçek UDP komut kanalı (native uygulama UDP açabilir).

---

## 16. AR için haritalama

**Problem:** AR nesnelerini (bulgu, kule, düşman) evin belli yerlerine "koymak" için iki şey gerekir:
1. Evin haritası (nesneler nereye konacak).
2. Robotun o harita içinde **şu an nerede ve hangi yöne baktığı** (konumlama). Bu olmadan nesneler robot döndükçe kayar.

**Hocanın önerisi, iki aşama:**

### Aşama A: Evi telefonla taramak
| Cihaz | Araç | Ne verir |
|---|---|---|
| iPhone Pro (LiDAR'lı) | Apple RoomPlan (Polycam, "3D Scanner App" gibi uygulamalar içinde) | birkaç dakikada ölçülü oda modeli; duvar, kapı, mobilya ayrı ayrı tanınır. En iyisi bu |
| Android (S21+) | Polycam (fotoğraf modu), Scaniverse, KIRI Engine | fotoğraflardan 3B model (fotogrametri); yavaş ama iyi |
| Android | RTAB-Map uygulaması | ARCore ile hareket ederken nokta bulutu ve kaba model |

Çıktı bir 3B model dosyası (OBJ/GLB/USDZ). Unity'ye aktarılır; simdeki `HouseLayout` yerine **gerçek ev** gelir. AR nesneleri Unity'de bu modelin içine yerleştirilir: "kule salonun köşesinde".

Ekipte LiDAR'lı bir iPhone (Pro modeli) varsa Aşama A çok kolaylaşır. Yoksa S21+ ile fotogrametri.

### Aşama B: Robotu haritada konumlamak
Robotun kamerası düşük çözünürlüklü 360°. Telefonun yaptığı gibi doğrudan "ben neredeyim" demek zor. Pratik çözüm:
1. **ArUco işaretleri** (mimari kararlarındaki "önce ArUco"). Evin birkaç yerine (her odaya 1-2) A4 boyunda siyah-beyaz kare işaretler yapıştırılır. Her işaretin taranmış modeldeki yeri bir kez ölçülüp kaydedilir.
2. Robot kamerasında bir işaret görünce açısından ve büyüklüğünden kendi konumunu hesaplar ("2. işaretin 1,4 m önündeyim, 30° sağdan bakıyorum"). Jetson'da OpenCV ile; bölüm 10'daki boş işlemci gücü bunun için.
3. İki işaret arasında konum IMU (dönüş) ve sürüş komutları (yaklaşık ilerleme) ile tahmin edilir. Biraz kayar; sonraki işaret görülünce düzelir.
4. Unity, robotun konumunu bilince AR nesnelerini doğru yerde çizer.

Bu yöntem bitirme projesi için gerçekçi ve hocaya anlatması kolay. Daha ileri yöntemler (görsel SLAM, taranmış modele göre görüntüden konum bulma) araştırma düzeyinde; zaman kalırsa denenir. ROS'un `rtabmap` paketi bu noktada işe yarayabilir (bölüm 13).

**Not:** robotun 360° kamerasında küçük işaretler uzaktan seçilmez. 15 cm'lik bir işaret kabaca 1-1,5 m'ye kadar, A4 boyu ~2 m'ye kadar okunur. Bu tahmini deneyerek doğrularız.

---

## 17. Hız göstergesi, sürüş kaydı, parti modu fikirleri

### 17.1 Hız göstergesi
- **Güç kaydırıcısı hız sabitleyici değil.** Kaydırıcı "en fazla ne kadar güç" sınırı. Hız sabitleyici, tuşa basmadan o hızda gitmeye devam etmek demek; ayrı bir özellik.
- **Hızı güçten tahmin etmek mümkün:**
  - tekerlek devri ≈ (PWM doluluğu) × 120 devir/dk (motorun 12 V'taki boşta devri; yükte biraz düşer)
  - hız = devir × π × tekerlek çapı
- **Örnek:** tekerlek çapı 6,5 cm, doluluk %67 → 0,67 × 120 = 80 devir/dk → 80 × π × 0,065 m = 16,4 m/dk ≈ **1 km/sa**.
- **Gereken:** tekerlek çapı. Bir de kalibrasyon: robotu 2 m düz sürüp süreyi ölçmek. Bunlarla kokpite "≈ km/sa" eklenir.
- Kızaklı şasi dönerken kaydığı için dönüşte tahmin bozulur; düz gidişte iyi çalışır.

### 17.2 Sürüş kaydı: bir amaç bulmak
- **Otomatik üsse dönüş:** oyunda pil azalınca "üsse dön" görevi. Robot gelirken kaydettiği yolu tersten sürer. Encoder olmadığı için kayar; ArUco ile düzeltilirse işe yarar.
- **Devriye:** bir yol kaydedilir, robot onu tekrar tekrar sürer, sen sadece kameradan izlersin (gölge avı).
- **Hayalet tur:** bir turunu kaydedersin; sonraki turda önceki hâlin AR'da yarı saydam bir "hayalet robot" olarak gösterilir, onunla yarışırsın. Konumlama (bölüm 16) gerekir.

### 17.3 Parti modu
Müzik çalarken ışık müziğin ritmine göre yanıp söner. Röle mekanik olduğu için hızlı yanıp sönmeye uygun değil; parti modu asıl renkli LED gelince güzel olur (yapılacaklar B7):
- Doom: kırmızı, hızlı yanıp sönme.
- Zelda's Lullaby: yeşil-mavi, yavaş geçiş.
- Megalovania: mavi ve beyaz çakmalar.

Şimdilik rölelide notaların bazılarında yanıp sönen sade bir sürüm yapılabilir.

### 17.4 Telefonu çevirerek bakma, direksiyon gibi eğerek sürme
İkisi de telefonun jiroskopu ve ivmeölçeriyle olur. Tarayıcı sayfası `http` olduğu için Chrome izin vermiyor. Unity uygulamasında doğrudan yapılır (bölüm 15).
