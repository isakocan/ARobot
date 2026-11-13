# AR ve konum: yol haritası

Durum: 12 Kasım 2026. Kapsam: lidarsız yol, yani robotun kendi 360° kamerası. iPhone LiDAR taraması ekip arkadaşının ayrı kolu; iki kol Aşama 5'te ortak harita biçiminde buluşur.

Bu belge bir plan taslağıdır. Hedef sayılar öneridir, ölçüm geldikçe güncellenir. Her aşama ayrı bir oturumda konuşulur.

## 1. Neden harita ve konum?

Sanal bir nesneyi bir yere koyup uzaklaştığında, döndüğünde aynı yerde durması gerekir. Bu doğru, ama "harita" kelimesi burada üç ayrı ihtiyacı birden taşıyor. Ayırmak işi kolaylaştırır:

| İhtiyaç | Ne gerektirir | Harita şart mı? |
|---|---|---|
| Nesne robot dönerken ekranda kaymasın | Robotun dönüş açısı, kare kare | Hayır |
| Nesne zemine otursun, doğru boyda görünsün | Kamera yüksekliği + düz zemin varsayımı | Hayır |
| Uzaklaşıp dönünce nesne aynı yerde olsun | Zamanla kaymayan konum; bunun için "burayı daha önce gördüm" diyebilmek | Evet, ama robotun kendi tuttuğu kayıt yeter |
| Nesne duvarın içinde çıkmasın, odaya göre çıksın | Duvarlar, gezilebilir zemin, oda bilgisi | Evet, kat planı |
| Nesne koltuğun arkasında kalınca gizlensin | Eşyaların yeri ve yüksekliği | Evet, ama isteğe bağlı |

Sonuç: AR'ın gözle görülen kalitesinin büyük kısmı (ilk iki satır) haritasız elde edilir. Harita, kalıcılık ve oyunun mekânı bilmesi için gerekir.

Dönüş açısının neden en önemli olduğu, sayıyla: 1°'lik dönüş hatası 2 m ötedeki nesneyi 3,5 cm, 5 m ötedekini 9 cm kaydırır ve ekrandaki bütün nesneler birlikte kayar. 20 cm'lik konum hatası ise 2 m ötedeki nesnede yaklaşık 6°'lik kayma demektir, uzak nesnede daha az.

## 2. Haritada ne kadar ayrıntı?

Oyunun bugünkü kurgusuna göre gereken en az harita:

- Duvarlar
- Eşyaların zeminde kapladığı alan (koltuk, masa, dolap...)
- Gezilebilir zemin (robotun girebildiği yerler)
- Oda etiketleri (salon, mutfak...) ve kapı geçişleri
- Üssün yeri (orijin)

Bunu taşıyan yapı bir **ızgara harita**: ev küçük karelere bölünür (ör. 5 cm), her kare üç durumdan birindedir: *boş zemin*, *dolu* (duvar ya da eşya), *bilinmiyor*. Sanal nesne yalnızca boş zemine konur; böylece duvarın ya da mobilyanın içinde çıkmaz. Robot için duvar ile eşya aynı şeydir (ikisi de zeminin bittiği yer), ayrı bir eşya tanıma gerekmez.

**Örtme gerekli.** Sanal nesneler sabit durmak zorunda değil; hareket edip duvarın ya da mobilyanın arkasına geçebilir. O zaman görünmemeli. İlk yaklaşım yükseklik ölçmeden çalışır: kamera yerden yaklaşık 25 cm'de olduğu için ondan yüksek her engel, arkasındaki zemin seviyesindeki nesneyi zaten tamamen kapatır. Dolu kareler Unity'de görünmez, yüksek bloklar olarak çizilir ve sanal nesneyi örter.

Robotun gözünden haritalamanın bir yan faydası: masa ve sandalyenin altı robot için boştur (yalnızca ayaklar doludur) ve robot gerçekten de altını görür. Zemin sınırına dayanan harita bunu kendiliğinden doğru işaretler; insan boyundan yapılan tarama masayı dolu blok sayardı.

İsteğe bağlı ek: dolu karelerin duvar mı eşya mı olduğu ve kaba yüksekliği (alçak engellerin üstünden görünme, duvarların haritada düz çizilmesi için).

Yani 3B model değil, **2B kat planı**. Aşırı hassasiyet gerekmiyor; "iyiyse yeterli" ölçütüyle denenecek. Bu seviye robotun kendi kamerasıyla çıkarılabilir, çünkü:

- Gezilebilir zemin tanım gereği robotun gezdiği yerdir.
- Robot her yere yaklaştığı için yakın mesafe ölçümü yeter. Kamera alçakta olduğundan zemin çizgisiyle mesafe 1 m'de yaklaşık 2 cm, 3 m'de yaklaşık 21 cm hatalıdır (piksel başına, kamera 25 cm varsayımıyla). Uzak duvarlar için tavan çizgisi kullanılır (3 m'de yaklaşık 3,5 cm).
- Harita robotun kendi göz hizasından kurulduğu için "telefon yukarıdan taradı, robot aşağıdan bakıyor" sorunu ortadan kalkar.

Lidarsız telefonla tarama ikinci seçenek olarak kalır. Dezavantajları: kullanıcıya ayrı bir tarama işi çıkarır, telefonu yorar, beyaz duvarda zayıftır ve yükseklik farkı sorununu geri getirir. Raporda "çift kamera" olarak geçen yol bu yüzden karşılaştırma deneyi düzeyinde tutulur.

## 3. Üç tasarım kararı (12 Kasım 2026'da onaylandı)

**K1. Haritalama ayrı bir adım değil, oyunun ilk keşfidir.**
Oyunda harita zaten "gezdikçe açılıyor". İlk oturumda oyuncu evi gezer, robot haritayı o sırada kurar. Kullanıcı tarama yapmaz, ek cihaz gerekmez.

**K2. Görüntü işleme olabildiğince robotta (Jetson) çalışır, telefon sonucu alır.**
Şart değil, tercih: Jetson boş durmasın, telefon yorulmasın. Sığmayan iş telefona kayar; denenerek karar verilir. Telefona giden şey: konum (x, y, yön), harita güncellemesi. Geliştirme bilgisayarda Python + OpenCV ile yapılır; aynı kod Jetson'a taşınır. Jetson kameranın asıl karesini gördüğü için kalite de daha iyidir. Her sonuç kare numarasıyla gönderilir ki ekrandaki kareyle eşleşsin.
Bilinen: `robot-teknik-notlar.md` Bölüm 7'ye göre 5 W modunda (4 çekirdekten 2'si açık) görüntü aktarılırken işlemci %10-35, bellek 1,2 / 4 GB. H.264 sıkıştırması ayrı donanımda yapılıyor. Ekran kartı (CUDA) görüntü işleme için henüz kullanılmıyor, yani orada boş kapasite var.
Açık: 10 W moduna geçilirse (4 çekirdek) regülatörün yetip yetmediği.

**K3. Önce klasik görüntü işleme, model yalnızca gereken yerde.**
Gerçek zamanlı döngü (her kare) modelsiz kalır. Model gereken tek yer büyük ihtimalle zemin/duvar ayrımıdır; o da düşük hızda çalışabilir. Ağır yöntemler (tek kareden konum bulma) nadiren ya da araştırma karşılaştırması olarak çalışır.

**K4. Tek odaya göre ayar yok; her evde çalışmalı.** (12 Kasım 2026)
- Hiçbir eşik ya da renk belirli bir odaya göre elle ayarlanmaz; ayar gerekiyorsa sistem kendi ölçer (ör. zemin rengini o an robotun önünden öğrenir).
- Her yöntem en az üç farklı ortamda denenir: farklı zemin (parke, karo, halı), farklı ışık (gündüz, gece lambası), farklı doluluk (boş oda, eşyalı oda).
- Model eğitilirse kendi odalarımızla değil, hazır ve çeşitli iç mekân veri kümeleriyle eğitilir; kendi kayıtlarımız yalnızca sınama için kullanılır.
- Sim evi bu yüzden işe yarar: altı odası, dört ayrı zemin dokusu ve oda başına farklı duvar rengi var.

## 4. Yapı taşları

Her taş hem bir özellik hem de sonraki aşamanın girdisidir.

| # | Yapı taşı | Kullanıcının gördüğü | Yöntem | Yük |
|---|---|---|---|---|
| Y1 | Kamera geometrisi (piksel → yön, yükseklik) | Yok, her şeyin temeli | Kalibrasyon | Bir kez |
| Y2 | Ortam parlaklığı | Otomatik far (ertelendi) | Klasik | Çok hafif |
| Y3 | Dönüş açısı (görsel pusula) | Nesneler dönerken sabit durur | Klasik | Hafif |
| Y4 | Zemin sınırı → serbest mesafe | Haritada duvar ve eşya | Klasik ya da küçük model | Orta |
| Y5 | İlerleme ölçümü (zemin akışı) | Nesneler kısa sürede yerinde kalır | Klasik | Hafif-orta |
| Y6 | Sabit işaret (üsteki ArUco) | Üs hep doğru yerde, kayma sıfırlanır | Klasik | Hafif |
| Y7 | Harita kurma (Y3-Y6'nın birikimi) | Ev haritası gezdikçe açılır | Klasik | Orta |
| Y8 | Yeri yeniden tanıma | Ertesi gün nesneler yerinde | Klasik + belki model | Nadiren |
| Y9 | Panoramadan ışık | Sanal nesneler odanın ışığına uyar | Unity | Hafif |

## 5. Aşamalar

### Aşama 0. Hedefi sabitle (konuşma)
- Bölüm 2'deki harita tanımı ve Bölüm 3'teki üç karar onaylanır ya da değiştirilir.
- Hedef sayılar: konum 20 cm, yön 5° (öneri).
- Ortak harita biçimi ekip arkadaşıyla kararlaştırılır (LiDAR kolu aynı biçime yazsın).
- Robot gerekmez.

### Aşama 1. Veri ve ölçüm altyapısı
Amaç: her yöntemi kayıtlı veri üstünde, robot olmadan, tekrar tekrar deneyebilmek.
- **Sim veri kümesi:** Unity evinden robot yüksekliğinde panoramalar + gerçek konumlar; ardından görüntüyü Theta S kalitesine düşürme (1280×720, iki balık gözü, bulanıklık).
- **Gerçek kayıt aracı:** ham kare + gönderilen motor komutu + zaman damgası birlikte kaydedilir.

Eldeki kayıtların durumu (12 Kasım 2026'da incelendi):
- `tools/robotsuz_deneme/kayit/goruntu.h264`: 89 kare, yaklaşık 6 sn, ham çift balık gözü, kamera sabit, komut kaydı yok. Hareket deneyine yaramaz; ham görüntünün neye benzediğini gösterir.
- `kayitlar/video_2026-11-08_20-43-18.avi` (ana klasörde): 13 dk sürüş, ama uygulamanın ekran kaydı (1600×720, kokpit bindirmeli, tek yöne bakan kırpılmış görüntü, bakış yönü değişiyor). 360° yöntemleri için kullanılamaz; ortamın zorluklarını gösterir.
- Bu videodan görülen zorluklar: parlak karo zemin (tavan ve eşya yansıyor), karanlık ortamda robotun kendi ışığı zeminde robotla birlikte gezen parlak bir leke yapıyor, loş ışıkta hareket bulanıklığı ve 10 kare/sn'ye düşüş, ince sandalye ayakları, kafesli paravan, cam kapı, yürüyen insanlar.

Hazır olanlar:
- `SceneBuilder.Screenshot()` verilen konumdan eşdikdörtgen PNG yazabiliyor; `MockRobotLink.Teleport` konumu ayarlıyor; `ExplorationMap` ışın atarak gerçek serbest mesafeyi zaten hesaplıyor. Eksik: çok konumlu toplu çekim ve konum dosyası.
- `tools/surus_kaydi.py` bilgisayardan H.264 akışını (ham çift balık gözü) ve 10 Hz motor durumunu kaydediyor. Eksik: sıkıştırılmamış JPEG kare, karede zaman damgası, gönderilen komutun aynı saatle kaydı.

İş paketleri:
- **P1. Sim veri üretici (Unity, toplu):** gerçekçi sürüş yolları boyunca 15 kare/sn panorama + konum dosyası + gerçek ızgara harita + her yön için gerçek serbest mesafe.
- **P2. Theta S benzetimi ve ölçüm düzeneği (Python):** sim panoramasını gerçek mercek ölçüleriyle 1280×720 çift balık gözüne çevirir, gövde maskesi, JPEG bozulması, bulanıklık ekler; bir yöntemin çıktısını gerçek konumla karşılaştırıp hata verir.
- **P3. Robotta ham kayıt (Jetson köprüsü):** kameranın JPEG karelerini, kare numarasını, Jetson saatini ve gelen komutları aynı dosyaya yazar. Robot açılınca yüklenir.

Sonuç (12 Kasım 2026, üçü de yazıldı ve bilgisayarda sınandı):
- **P1:** `unity/Assets/Editor/KonumVeriUretici.cs`. 9 kayıt, 3738 kare, `kayitlar/sim/` (1 GB, git dışında). Komutlar bilerek kusurlu: gerçek hareket komuttan ±%15 sapıyor, düz gidişte 2°/m yana kayıyor. Yön sözleşmesi sayıyla doğrulandı (robot saat yönünde dönünce görüntü sola kayar, hata en çok 1,4°).
- **P2:** `tools/konum_deney/` (kullanım: `BENIOKU.md`). 10 test geçiyor. Gerçek karenin panoramaya çevrilmesi gözle kontrol edildi: ufuk düz, dikiş yok.
- **P3:** `robot/jetson/surus_koprusu/ham_kayit.py` + `tools/ham_kayit.py`. 9 test geçiyor. Robotta denenmedi.
- **Taban çizgisi (yalnızca motor komutundan konum):** ev turunda 21 m yolda 5,3 m hata (%25), yön sapması 42°. Kameranın düzeltmesi gereken budur.
- **Gövde ne kadar kapatıyor:** ön yönde ufkun yaklaşık 39° altından itibaren (32-45°). Kamera 25 cm'deyse robotun önündeki ilk yaklaşık 30 cm zemin görünmez; ötesi görünür. Maske masadaki kareden elle çizildi, robot yerdeyken yeniden ölçülmeli.

Bilinen eksikler:
- Simde mobilyaların çarpışma kutusu modelin tamamını sarıyor. Bu yüzden gerçek harita ve gerçek zemin mesafesi masanın altını dolu sayıyor, oysa görüntüde masanın altı görünüyor. Aşama 3'ten önce düzeltilmeli (mobilyaya ağ çarpıştırıcı).
- Sim aydınlık ve dokulu; gerçek kayıttaki karanlık, parlak zemin ve robot ışığı durumları simde yok. Işık çeşitlemesi eklenecek.
- Ön mercek: ham karede soldaki daire, pilin göründüğü taraf (12 Kasım 2026'da doğrulandı). Düzenekteki varsayımla aynı. Robotta saklı "Yön" ayarının sayısal değeri hâlâ depoda yok.
- Robotta: SD kartın yazma hızı ve Python 3.6'da çalışma doğrulanmadı.
- **Gerçek konum ölçümü:** robotun üstüne ArUco, yukarıdan bakan sabit telefon; ya da zeminde bantla işaretli noktalar.
- **Kalibrasyon:** mercek modeli, kamera yüksekliği, ön yön.
- **Jetson bütçesi:** video akarken boşta kalan işlemci ve bellek.
- Çıktı: "bir yöntemi ver, hatasını cm ve derece olarak al" düzeneği.
- Robot: sim kısmı için gerekmez; kayıt, kalibrasyon ve bütçe için gerekir.

### Aşama 2. Dönüş açısı (modelsiz)
- **Görsel pusula (Y3):** ardışık panoramalar arasındaki yatay kayma = dönüş açısı.
- Neden 360° kamerada kolay: robot ilerlerken soldaki nesneler bir yöne, sağdakiler ters yöne kayar ve tüm çember üstünden bakınca birbirini götürür. Geriye kalan ortak kayma dönüştür.
- **Anahtar kare:** her kare bir öncekiyle değil, yakında alınmış sabit bir kareyle karşılaştırılır. Böylece hata kare kare birikmez; robot yerinde on tur dönse de aynı yere döner. Hata yalnızca anahtar kare değişirken eklenir.
- Görüntünün üst kısmı (tavan, duvarların üstü) kullanılır: uzakta olduğu için ilerlemeden az etkilenir, gövde ve yürüyen insanlar orada yoktur.
- İki aday karşılaştırılır: (M1) sütun parlaklık profillerinin dairesel eşleştirmesi, çok ucuz; (M2) nokta eşleştirip dönüşü çözme, daha pahalı ama eğimi de verir.
- Komut sıfırken ve görüntü değişmiyorken açı sabit tutulur.
- Sınama: altı odada yerinde dönüş, düz gidiş (2°/m gerçek sapmayı ölçebilmeli), salon turu, ev turu (kapı geçişleri); hem temiz sim hem Theta benzetimi üstünde.
- Bu anahtar kareler Aşama 6'da "burayı daha önce gördüm" veritabanının da temelidir.

Sonuç (13 Kasım 2026, yalnızca sim; kod `tools/konum_deney/yontem_pusula_*.py`, tablo `kayitlar/sonuclar/pusula/tablo.md`, 6 test geçiyor):

| Kayıt (gerçek kamera benzetimi, en çok hata) | M1 sütun profili | M2D nokta + düzlemsel hareket |
|---|---|---|
| Yerinde dönüş, 6 oda | 1,2° - 3,9° | 0,4° - 1,1° |
| Düz gidiş | 46° | 0,8° |
| Salon turu | 37° | 1,0° |
| Ev turu | 34° | 2,3° |

- Hedef tuttu: altı odada yerinde tur sonunda sapma 2°'nin altında (iki yöntemde de).
- **Yukarıdaki iki varsayım yanlış çıktı.** "İlerlerken sol ve sağ kaymalar birbirini götürür" ancak iki yan eşit uzaklıkta ve eşit dokuluysa doğru; koridorda bir yan kapılı, öbür yan düz duvar olunca M1 düz giderken dönüyor sanıyor. "Üst kısım ilerlemeden az etkilenir" de tutmadı: kaymayı belirleyen yükseklik değil duvarın yatay uzaklığı. Üst bant yine de kullanılıyor, çünkü gövde, zemin yansıması ve insan orada yok.
- **Seçilen: M2D.** Noktaları eşleştirir (ORB), sonra robotun düz zeminde hareket ettiğini bilerek dönüşü ve ilerleme yönünü birlikte çözer. Düz gidişte hata 19°'den 0,7°'ye indi. Simdeki 2°/m gerçek sapmayı 0,1°/m içinde izliyor.
- Motor komutu şart değil: komutsuz kipte en çok hata 2,6°.
- Kamera bozulmaları (bulanıklık, gürültü, sıkıştırma) hatayı 0,1-0,5° artırıyor. En etkilisi uzun pozlamadaki hareket bulanıklığı.
- Yan ürün: M2D ilerleme yönünü de veriyor (mesafeyi değil). Aşama 4'ün yarısı.
- M1 yalnızca "robot dönüyor mu, duruyor mu" gibi ucuz bir ön ölçüm olarak işe yarar (7,7 ms).
- **Hız:** M2D bu bilgisayarda kare başına yaklaşık 73 ms (15 kare/sn için sınır 66 ms). Jetson'da ölçülmedi. Her karede çalışmak zorunda değil; küçültülmüş görüntüde 32-41 ms ama hata 1,6-2,2°.
- **Denenmeyenler:** gerçek kamera görüntüsü, karanlık ve parlak zemin, yürüyen insan, robotun sarsılması ve eğilmesi (yöntem kameranın dik durduğunu varsayıyor).
- Unity'de sanal nesneler bu açıyla sabitlenir.
- Ölçüt (öneri): yerinde tam tur sonunda sapma 2°'den az.
- Karar noktası: dönüş Jetson'da mı hesaplanacak, telefonda mı (gecikmeye bakılarak).

### Aşama 3. Zemin ve serbest mesafe
- Zemin sınırını bulmanın üç adayı karşılaştırılır: (a) robotun hemen önündeki zemin rengini örnek alan klasik yöntem, (b) küçük bölütleme ağı, (c) yerleşim ağı (HorizonNet, yalnızca bilgisayarda).
- Çıktı: her yön için "zemin şu kadar uzakta bitiyor" ölçümü. Haritadaki duvar ve eşya buradan gelir.
- Ölçüt (öneri): 1,5 m içinde mesafe hatası %10'dan az (şerit metreyle).
- Karar noktası: ilk yapay zekâ kararı burada verilir; model gerekli mi, hangisi, Jetson'da kaç kare/sn. Hazır model yetmezse kendi evlerimizden etiketli karelerle küçük bir model eğitilir (Kaggle, T4, haftada 30 saat).

Erken deneme (13 Kasım 2026, yalnızca sim, robotun gerçek konumu hazır verilerek; ayrıntı `konum-harita-nasil-calisir.md`, tablolar `kayitlar/sonuclar/harita/`):
- **Modelsiz yöntem yetmiyor:** yönlerin %40'ında büyük hata (duvar rengi zemine benzeyince, kapı eşiğinde zemin değişince, duvara yakınken).
- **Hazır model (SegFormer-B0, eğitilmeden):** büyük hata %15-29. Kapı eşiğini ve halıyı doğru çözüyor.
- **Menzil:** bir piksellik hata 3 m'de yaklaşık 20 cm. 3,2 m'den uzak ölçüm haritaya engel olarak yazılmıyor. Kameranın fiziksel sınırı.
- **Harita (ev turu, gerçek kamera benzetimi):** kusursuz zemin sınırıyla boş alan IoU 0,91; modelle 0,83, duvarlar ortalama 8 cm içinde. Ev planı tanınıyor. Dolu karelerin yakalanma oranı düşük (%52; kusursuz sınırla bile %70, sebebi ayrıştırılmadı).
- **Gerçek kareler (göz kararı, 16 kare):** iyi pozlanmış karanlık lobide zemin düzgün; robot ışığıyla patlamış beyaz zeminde bulunamadı. Sınama adil değil (ekran kaydı, menüler). Belirsiz.
- **Hız:** bu bilgisayarın ekran kartında 300 ms/kare, 1,8 GB. Jetson'da ölçülmedi.
- Açık işler: dolu kare kaybının sebebi, modelin lisansı, Jetson'da çalışıp çalışmadığı, daha hafif bir zemin modeli.

### Aşama 4. Göreli konum
- **İlerleme (Y5):** zemin görüntüsü kuş bakışına çevrilir, kayması metreye dönüşür.
- Motor komutu zayıf ipucu olarak eklenir. Özellikle "komut sıfırsa robot duruyor" bilgisi kaymayı durdurur.
- **Üs işareti (Y6):** ArUco görülünce konum sıfırlanır; üs = haritanın orijini.
- Ölçüt (öneri): 5 m'lik tur sonunda başlangıca 30 cm içinde dönüş; üs yakınında 10 cm.
- Bu aşamanın sonunda gösterilebilir bir AR vardır: nesne koy, dolaş, dön, nesne yaklaşık yerinde.

Sonuç (13 Kasım 2026, yalnızca sim; kod `tools/konum_deney/konum_*.py`, tablo `kayitlar/sonuclar/konum/tablo.md`):
- İlerleme: zemin kuş bakışına çevrilir (2 cm/piksel), desenin kayması metre verir. Düzeltme: zemin sınırı taraması robotun kendi haritasına oturtulur.
- Ev turu (22 m, gerçek kamera benzetimi), ortalama konum hatası: komut + kamera yönü 25 cm; yalnızca kamera 6 cm; yalnızca kamera + harita düzeltmesi 5 cm.
- Robotun kendi konumuyla kurduğu harita, hazır konumla kurulanla aynı kalitede (boş alan örtüşmesi 0,83, duvar 8 cm).
- Sanal nesne denemesi (`gosterim_ar.py`, 4 sabit nesne): ekrandaki kayma ortanca %0,8, onda biri %2,2'den fazla, en kötü %13 (26 cm, tur sonundaki yerinde dönüşte). Motor komutlu varyantta ortanca %2,8.
- Haritaya göre gizleme zayıf: kararların %16'sı yanlış, çoğu gereksiz gizleme (dar kalan kapı aralıkları, henüz görülmemiş bölgeler).
- Kayıtlar: ayarlar aynı üç kayıt üstünde seçildi, görülmemiş rotada sınanmadı; yöntem desenli zemine bağlı; kamera yüksekliği %5 yanlışsa hata 27 cm'ye çıkıyor; "başka yere konunca yerini bulma" yapılmadı.

### Aşama 5. Harita
- Robot gezerken serbest mesafe ölçümleri konumla birleşip ızgara haritaya yazılır; belirli aralıklarla panorama da saklanır.
- Oda ayrımı ve etiketleme (otomatik mi, oyuncu mu adlandırır: konuşulacak).
- Kaydetme / yükleme.
- **Karşılaştırma:** aynı oda iPhone LiDAR ile ve robotla haritalanır; duvar konumu farkı ölçülür. İsteğe bağlı üçüncü kol: lidarsız telefon.
- Çıktı: oyunun kullandığı gerçek ev haritası.

### Aşama 6. Kalıcı konum
- Robot kapatılıp açıldığında ya da elle başka yere konduğunda yerini bulur: saklanan panoramalarla benzerlik + parçacık filtresi.
- Zor durumlar: gece/gündüz, yeri değişen eşya, birbirine benzeyen odalar.
- **Araştırma karşılaştırması:** PICCOLO / CPO / FGPL, LiDAR haritası üstünde, bilgisayarda. Raporda söz verilen kısım.
- Ölçüt (öneri): odaya dönüşte denemelerin %80'inde 20 cm / 5° içinde.

### Aşama 7. AR kalitesi
- Zemin gölgesi ve doğru ölçek.
- Panoramanın kendisi ortam ışığı ve yansıma kaynağı olarak kullanılır (Y9).
- Haritadan örtme (duvar ve eşya arkasında gizlenme). Gerekli; kenarlarda birkaç cm'lik hata görüneceği için nesne örtülürken yumuşak solma kullanılır.
- Görüntü karesi ile konumun eşleşmesi (gecikme telafisi).
- Hata saklayan yerleşim: nesneler yakın ve gezilmiş alanda çıkar; konum belirsizken uzak nesneler gizlenir.

### Aşama 8. Oynanış, hikaye, deneyim
- İlk bölüm = evi keşfetme = haritalama (K1).
- Odaya göre bulgular gerçek haritaya bağlanır.
- Katılımcı denemeleri (raporda 10 üzerinden puan).
- Ayrı oturumlarda konuşulacak.

### Aşama 9. Son kullanıcı akışı ve ölçümler
- Kurulum: üssü koy, robotu aç, sür. Başka adım olmamalı.
- Yük dağılımı kesinleşir (Jetson / telefon), pil ve ısı ölçülür.
- Rapor ölçümleri: konum hatası, gecikme, kopma sayısı, oyun süresi.

### Ek özellikler (ilk etapta yok)
Aynı yapı taşlarından sonradan ucuza çıkar; öncelik değil.
- **Otomatik far (Y2):** ortalama parlaklık, iki eşik + bekleme. Dikkat: kamera karanlıkta pozlamayı kendisi artırır, eşik gerçek görüntüyle ayarlanmalı.
- **Engelde yavaşlama (Y4):** ön yönde serbest mesafe azaldıkça izin verilen hız düşer. Yavaşlatır, durdurmaz.

## 6. Takvim ve kesme çizgisi

Raporun planında oda taraması ve konum 7-11. haftalar, oyun ve AR 9-12, testler 10-12, final raporu 12-13.

| Hafta | Tarih | Aşama |
|---|---|---|
| 7 | 9-15 Kasım | 0, 1 |
| 8 | 16-22 Kasım | 2, 3 |
| 9 | 23-29 Kasım | 4 |
| 10 | 30 Kasım-6 Aralık | 5, 7 başlar |
| 11 | 7-13 Aralık | 6, 8 |
| 12-13 | 14-28 Aralık | 9, rapor |

Kesme çizgisi: Aşama 4'ün sonu. Oraya kadar gelindiyse elde çalışan, gösterilebilir bir AR vardır. Aşama 5 ve 6 gecikirse araştırma sonucu olarak (ölçüm + karşılaştırma) sunulur, oyun Aşama 4 seviyesiyle oynanır.

## 7. Açık sorular

- Kamera merceğinin yerden yüksekliği (ölçülecek).
- Jetson'da boş işlemci payı (ölçülecek).
- Robotun yerinde dönüş hızı ve loş ışıkta hareket bulanıklığı.
- Odaları kim adlandırır.
- Raporda H2 "LiDAR ya da çift kamera" diyor; bu plan "robotun kendi kamerası"nı öne alıyor. Ara Rapor 2'de nasıl anlatılacağı konuşulacak.
- PICCOLO deposu eski bir ortam istiyor (torch 1.7, CUDA 10.1); RTX 3050 Ti için daha yeni bir CUDA derlemesine uyarlamak gerekebilir (denenmedi).

## 8. Çalışma biçimi

- Her oturumda bir aşama konuşulur.
- Düşünme ve karar ana oturumda; kod, sim ve test işleri alt ajanlara verilir.
- Her aşamanın sonunda bu belge güncellenir.
