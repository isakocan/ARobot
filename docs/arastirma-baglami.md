# Araştırma bağlamı: 360° kameralı ev robotu (Kasım 2026)

Bu dosya, araştırma istemlerine eklenmek içindir: önerilerin bizim sisteme uyması ve bildiğimiz / denediğimiz şeyleri tekrar önermemesi için.

## Proje
Telefondan sürülen küçük bir ev robotu ve AR oyunu. Oyuncu robotu telefondan, robotun 360° kamerasını izleyerek sürer. Telefondaki Unity uygulaması bu görüntünün üstüne AR nesneleri çizer. Asıl teknik hedefimiz: robotun odadaki **yerini ve yönünü** her yerde doğru bilmek (işaretten uzakta da), AR nesnelerini zemine kaymadan sabitlemek, ileride oda içinde güvenli otonom gezinti.
İki kişilik öğrenci ekibi, bütçe sınırlı.

## Donanım (değişmeyecek)
- **Jetson Nano 4 GB** (eski Maxwell, 128 CUDA çekirdeği; **Orin Nano DEĞİL**). JetPack 4.6 (L4T R32.7.6), Ubuntu 18.04, **Python 3.6**, TensorRT 8.2.1, CUDA 10.2. Şu an 5 W güç modunda. DeepStream kullanılmıyor.
- **Ricoh Theta S**, USB, UVC canlı yayın: **çift balık gözü MJPEG 1280x720, 15 fps** (eşdikdörtgen değil). Mercek yerden ~17 cm.
- Robot: 4 tekerli kızaklı (skid-steer) sumo platformu, olduğu yerde dönebiliyor, 1-2 km/s. **Tekerlek sayacı YOK, IMU YOK (kalıcı olarak eklenmeyecek), lidar / mesafe sensörü YOK.** Arduino Uno + motor sürücü seri porttan.
- Telefon: Samsung S21+ (Android). Robotu süren uygulama Unity 6.
- Geliştirme bilgisayarı: Windows, RTX 3050 Ti Laptop GPU, PyTorch 2.10 + CUDA. Ağır işler burada (kayıtlarla, çevrimdışı) denenebilir.

## Yazılım mimarisi (kendi yazdığımız)
- **ROS kullanmıyoruz** (Jetson'da Docker içinde kurulu ama devrede değil). Robottaki her şey tek bir Python 3.6 programı ("köprü").
- Görüntü: GStreamer, kameranın JPEG karelerini donanım JPEG çözücüyle açar, **donanım H.264 kodlayıcıyla (nvv4l2h264enc)** birkaç bit hızında kodlar. H.264 telefona **kendi WebSocket akışımızla, kendi paket biçimimizle** gider (her karenin başında kendi başlığımız var). Yani karelere istediğimiz bilgiyi kendi başlığımızda ekleyebiliriz, SEI'ye gerek yok. Telefonda MediaCodec ile çözülüp Unity'de küre içine çizilir.
- Sürüş: telefondan UDP, 20 Hz JSON paketi (sol/sağ motor PWM). Robot bayat komutları atar, paket kesilince durur.
- Telefonda ayrı bir tarama uygulaması (ARCore) odayı önceden tarar: 5 cm hücreli **açık zemin / engel haritası** ve **ArUco işaretlerinin oda içindeki yeri**. Bu harita robota yüklenir. Koordinat sistemi: ilk işaretin önündeki zemin, metre.

## Konum bulma: şu an çalışan (robotta)
- ArUco (odada 1-2 adet): yakındayken kesin yer ve yön (~1-2 derece).
- İşaret görünmezken: görüntü pusulası (360 görüntünün yatay kayması = dönüş), zemin izi (zemin dokusunun kayması = ilerleme) ve motor komutu, hepsi bir EKF'de.
- Ölçüm (telefon jiroskobu gerçek yön kabul edilerek): işaretten uzakta yön hatası ortanca 4,7, en kötü 22 derece.

## Konum bulma: PC'de denenen yeni yöntem (henüz robotta değil)
- 360 görüntüden eşdikdörtgen panorama (enlem -25..85), **XFeat** noktaları (1500 / kare), robotun gövdesi ve işaretin üstü maskeli.
- Robot dolaşırken 15 cm'de bir anahtar kare. İşaret yakınken yeri kesin. Uzaklaştıkça yer haritanın kendisinden bulunur ve harita zincirleme büyür. Yakın anahtar karelerde ortak noktalar üçgenlenir (3B nokta), sonra **demet ayarı** (işaret kareleri önselle sabit).
- Yeni karede: yakın anahtar karelerle karşılıklı en yakın komşu eşleştirme, 3B nokta ve yön (boylam / enlem) ilişkisinden RANSAC + Gauss-Newton ile düzlemsel yer + yön.
- Ölçüm (işaret hiç kullanılmadan, başka günün haritasıyla): işaretten uzakta yön hatası ortanca 2,5, en kötü 12-13 derece. İşaret yakınında yer hatası ~15-20 cm. **İşaretten uzakta yer hatası henüz ölçülmedi** (gerçek yer bilinmiyor).
- **XFeat, Jetson Nano'da TensorRT FP32 ile ölçüldü: 416x1440 girişte 80 ms / kare** (yalnız ağ; son işlem ölçülmedi). Saniyede 1 konum ölçümü yeterli (PC'de denendi, sonuç neredeyse aynı).

## Denenip bırakılanlar (tekrar önerilmesin, ya da neden farklı olduğunu açıklayarak önerilsin)
- **stella_vslam** (OpenVSLAM): denendi, bırakıldı.
- **ORB** anahtar kareler: aynı gün iyi, farklı günde (ışık değişince) tanıma çok düştü. XFeat aynı testte belirgin iyi.
- Siluet / ham görüntü benzerliği ile yer tanıma: günler arası tutmadı.
- Kalıcı IMU ya da yeni sensör eklemek: istemiyoruz (geçici ölçüm için telefon jiroskobu kullanıyoruz).

## Bilinen sorunlar
- AR: uygulama robotun konumunu ~0,3-0,4 sn geç alıyor. Robot hızlı dönerken AR nesneleri 20-30 derece sıçrıyor (konum hatası değil, gecikme).
- Wi-Fi: okul ağı (EDUROAM) cihazlar arası UDP'yi kesiyor. Sürüş telefon paylaşımı ya da robotun kendi ağı üzerinden.
- Işık her ortamda farklı. Sandalye, kapı gibi şeyler yer değiştiriyor.

## Araştırmadan beklentimiz
- **Jetson Nano (Maxwell) ölçümlerini Orin ölçümlerinden ayır.** Nano'da ölçülmemiş hızı "çalışır" diye yazma.
- Her öneri için: GitHub bağlantısı, son güncelleme tarihi, lisans, Python 3.6 / JetPack 4.6 ile çalışır mı.
- Bizim mimariye uyanı öne al: ROS'suz, kendi Python köprümüz, kendi video paket biçimimiz, kayıtlarla PC'de çevrimdışı denenebilen şeyler.
- Doğrulanmamış bilgiyi ayrıca belirt. Sonunda kısa bir "önce bunu dene" tablosu ver.
