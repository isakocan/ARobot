# ARaki — Bitirme Projesi

Fiziksel robot araç + Android uygulaması ile oynanan AR oyun. Oyuncu robotu telefondan, robotun 360° kamerasını izleyerek sürer. Unity kamera görüntüsünün üstüne düşman/efekt/arayüz bindirir. Ev = keşif dünyası; oyuncu 3D baskı objeleri yerleştirip görev alanını kurar, kamera objeyi tanıyınca oyunda role dönüşür (kule, yakıt, düşman ini...). Ana üste dönüp robot geliştirilir; yükseltmeler gerçek davranışa yansır (ör. PWM limiti). Gerçek batarya = yakıt. Tema (uzay / savaş sonrası) henüz belirsiz → tema katmanı değiştirilebilir tutulur.

2 kişilik ekip. Unity'ye yeni. Kullanıcı Türkçe konuşuyor; Türkçe yanıtla.

## Donanım
Jetson Nano (P3450, JetPack 4.6 / Ubuntu 18.04, Docker + ROS 2 Humble kurulu), sumo robot platformu (encoder yok, 12V, ~1-2 km/h), Ricoh Theta S (360, telefonla çalıştığı doğrulandı; Jetson'da henüz denenmedi), servo, LED, aday: IMU / mesafe / batarya sensörü. Araç henüz monte edilmedi.

## Mimari kararlar
- Video ROS'tan geçmez: GStreamer + donanımsal H.264 → doğrudan telefona.
- Jetson: görüntü işleme, sensörler, motor kontrol. Telefon (Unity): oyun + arayüz.
- Global harita yok; AR için kısa süreli takip (IMU) yeterli.
- Obje tanıma: önce ArUco, sonra eğitilmiş model.
- Robot `IRobotLink` arayüzüyle soyutlanır; araba gelene kadar `MockRobot` kullanılır.
- Hedef platform önce Android, iOS sonra (ekipte Mac var).
- Son kullanıcı terminal kullanamaz: bağlantı akışı (Wi-Fi) tek dokunuş/QR olmalı.

## Kararlar / yönler (güncel)
- Telefon: Samsung S21+ (Android). Önce Unity Editor'de PC üzerinde geliştirilir, telefona sonra geçilir.
- Oyun modları aynı motorda "görev tipi": keşif/hayatta kalma + yarış (marker kapıları, tur süresi). Yarış elenmedi.
- Tema henüz seçilmedi; çekirdek tema-bağımsız, görsel yön "skin" olarak sonradan. Taslaklar: soğuk telemetri / paslı saha terminali / maket dünyası.
- Bağlantı: varsayılan Jetson AP (robotun kendi Wi-Fi'si); ortak ev Wi-Fi'si isteğe bağlı ve sonra (provisioning gerekir).
- Görev/hikaye üretimi: LLM (bulut, Jetson'da değil) yapılandırılmış JSON üretir, kural motoru doğrular; çevrimdışı şablon yedeği.
- Görüntü işleme: ArUco → hafif YOLO (TensorRT) ; ışık dayanıklılığı için ön işleme + augmentation.

## Kurallar
- UI/UX profesyonel ve özgün olmalı; "AI slop" jenerik görünüm istenmiyor.
- Detay: `docs/architecture.md`, yol haritası: `docs/roadmap.md`.

## Ana riskler
Düşük gecikmeli video · AR kayması · değişen ışıkta obje tanıma · Theta S Linux uyumu · Nano işlem yükü
