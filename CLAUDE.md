# ARaki — Bitirme Projesi

Fiziksel robot araç + Android uygulaması ile oynanan AR oyun. Oyuncu robotu telefondan, robotun 360° kamerasını izleyerek sürer. Unity kamera görüntüsünün üstüne düşman/efekt/arayüz bindirir. Ev = keşif dünyası; oyuncu 3D baskı objeleri yerleştirip görev alanını kurar, kamera objeyi tanıyınca oyunda role dönüşür (kule, yakıt, düşman ini...). Ana üste dönüp robot geliştirilir; yükseltmeler gerçek davranışa yansır (ör. PWM limiti). Gerçek batarya = yakıt. Tema (uzay / savaş sonrası) henüz belirsiz → tema katmanı değiştirilebilir tutulur.

2 kişilik ekip. Unity'ye yeni. Kullanıcı Türkçe konuşuyor; Türkçe yanıtla.

## Donanım
Jetson Nano (P3450, JetPack 4.6 / Ubuntu 18.04, Docker + ROS 2 Humble kurulu), sumo robot platformu (4 tekerlek, kızaklı sürüş, olduğu yerde 360° dönebilir; encoder yok, 12V, ~1-2 km/h), Ricoh Theta S (360, telefonla çalıştığı doğrulandı; Jetson'da henüz denenmedi), servo, LED, aday: IMU / mesafe / batarya sensörü. Araç henüz monte edilmedi.

Not: motor sürücüler Arduino uyumlu; hocada tek noktalı lidar ve yakınlık sensörü var.

## Mimari kararlar
- Video ROS'tan geçmez: GStreamer + donanımsal H.264 → doğrudan telefona.
- Jetson: görüntü işleme, sensörler, motor kontrol. Telefon (Unity): oyun + arayüz.
- Global harita yok; AR için kısa süreli takip (IMU) yeterli.
- Obje tanıma: önce ArUco, sonra eğitilmiş model.
- Robot `IRobotLink` arayüzüyle soyutlanır; araba gelene kadar `MockRobot` kullanılır.
- Hedef platform önce Android, iOS sonra (ekipte Mac var).
- Son kullanıcı terminal kullanamaz: bağlantı akışı (Wi-Fi) tek dokunuş/QR olmalı.

## Kesin kararlar
- Unity 6.3 LTS (6000.3.26f1), proje: `unity/`, URP + Input System. Telefon: Samsung S21+ (Android). Önce Unity Editor'de PC'de geliştirilir, telefona sonra geçilir.
- Yarış modu eklendi (menüde R): oda zemininde AR pist, kapılardan sırayla geç, 3 tur, süre/madalya. Gerçekte kapılar ArUco işaretli 3D baskı olacak, pist onların arasına oturur; simde RaceController üretir (unity/Assets/_Project/Scripts/Game/RaceController.cs). Keşif modu (uzay/ORBİT-9) da duruyor; hangi tema ana yön olur henüz seçilmedi.
- Performans: 360 yakalama 30 Hz ile sınırlı, telefonda 2K; Editor/Optimizer.cs mobil URP profili, ASTC doku ve Android ayarlarını sahne kurulurken uygular.
- Simülasyon: 360 kamera "equirect → küre içinden bakış" hattıyla modelleniyor (Sim360Camera → PanoramaViewer); AR nesneleri robota göre konumla (ARRig) çiziliyor. Gerçek robotta yalnızca görüntü kaynağı değişecek.

## Henüz seçilmedi (kullanıcıyla konuşulacak)
- Görsel tema/yön (uzay / savaş sonrası / maket / başka).
- Wi-Fi: robotun kendi ağı kurulum için, günlük kullanım ev Wi-Fi'sinde (menzil için) fikri var; test edilecek.
- Görev/hikaye üretimi için LLM (NVIDIA API vb.) denenecek; derinlik/mesafe sensörü seçimi.
- Demo hikayesi: ORBİT-9 istasyonu salona düştü (unity/Assets/_Project/Scripts/Game/Story.cs); beğenilmezse değişir.

## Kurallar
- UI/UX profesyonel ve özgün olmalı; "AI slop" jenerik görünüm istenmiyor.
- Detay: `docs/architecture.md`, yol haritası: `docs/roadmap.md`.

## Ana riskler
Düşük gecikmeli video · AR kayması · değişen ışıkta obje tanıma · Theta S Linux uyumu · Nano işlem yükü
