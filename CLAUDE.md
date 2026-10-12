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
- Oyun: serbest gezi. Çok odalı ev (HouseLayout: salon, koridor, mutfak, yatak odası, banyo, çalışma odası). Robotun mesafe sensörü evi haritalar (ExplorationMap); harita dolduğu yerlerde odaya göre "bulgu" çıkar (Pokémon GO gibi ama ev içinde, nesne koymadan). Beş bulgu türü, her biri ayrı bir fiil ister: ürkek (SHIFT ile sessiz yaklaş), ışığa gelen (LED), gizli (sonar X), hareketsiz (bekle), efsane (ışık + sessizlik). Yakalananlar robotun hafızasında taşınır, üste (şarj, kalibrasyon, laboratuvar) teslim edilir; pil biterse kaybolur. Teslim öz verir; öz robotu geliştirir (gerçek davranış) ve 6 bölümlük hikayeyi ilerletir. Mantık: unity/Assets/_Project/Scripts/Game/Expedition.cs.
- Dördüncü tema EV (varsayılan, düz duvar/tavan, odaya göre duvar rengi). Nadirlik: aynı anda en çok 3 bulgu, çıkışlar 22-48 sn arayla, nadir bulgular görülünce 40-75 sn sonra kaybolur. Gölge (Game/Ghost.cs): toplanmaz, kaçılır; gürültü/LED ile fark eder, robottan biraz yavaş, üs güvenli alan; dokunursa pil -%12 ve bir kayıt gider.
- Diğer üç tema (Safari / Gezegen / Gizem) aynı oynanışın görünüm+hikaye katmanı: ThemeCatalog, duvar/tavan gökyüzü gölgelendiricisi ARaki/Sky, AR nesneleri ARaki/Toon (hologram değil). Menüden seçilir, sahne yeniden yüklenir; ilerleme tema başına kayıtlı. Hangi tema ana yön olur henüz seçilmedi. Gerçek robotta "zemin/eşya dışı = gökyüzü" için bölütleme modeli gerekir (simde geometri bilindiği için hile).
- Yarış modu denendi ve sıkıcı bulunduğu için kaldırıldı (git geçmişinde: 7f1feaa).
- Performans: 360 yakalama 30 Hz ile sınırlı, telefonda 2K; Editor/Optimizer.cs mobil URP profili, ASTC doku ve Android ayarlarını sahne kurulurken uygular. Ek modeller: python tools/fetch_polyhaven.py (CC0).
- Simülasyon: 360 kamera "equirect → küre içinden bakış" hattıyla modelleniyor (Sim360Camera → PanoramaViewer); AR nesneleri robota göre konumla (ARRig) çiziliyor. Gerçek robotta yalnızca görüntü kaynağı değişecek.

## Henüz seçilmedi (kullanıcıyla konuşulacak)
- Görsel tema/yön (uzay / savaş sonrası / maket / başka).
- Wi-Fi: robotun kendi ağı kurulum için, günlük kullanım ev Wi-Fi'sinde (menzil için) fikri var; test edilecek.
- Görev/hikaye üretimi için LLM (NVIDIA API vb.) denenecek; derinlik/mesafe sensörü seçimi.
- Hikaye metinleri: Theming/ThemeCatalog.cs (3 tema x 6 bölüm); beğenilmezse değişir.

## Kurallar
- UI/UX profesyonel ve özgün olmalı; "AI slop" jenerik görünüm istenmiyor.
- Detay: `docs/architecture.md`, yol haritası: `docs/roadmap.md`.

## Ana riskler
Düşük gecikmeli video · AR kayması · değişen ışıkta obje tanıma · Theta S Linux uyumu · Nano işlem yükü

## Güncel durum (son tur)
- Tek tema: Ev. Safari/Gezegen/Gizem silindi (git geçmişinde: önceki commit). Oyun adı geçici: TENHA (Core/GameInfo.cs; değiştirmek için tek yer).
- Arayüz sade: yazı yalnızca sol üstte (hedef + ipucu); pil/sonar ince çubuk, taşınan kayıtlar küçük kareler, sağ altta ev haritası (gezdikçe açılır), ekran çevresinde yön üçgenleri (kırmızı tehdit, yeşil üs).
- 360° kamera özellikleri: Tab = tehdide dön, B = arkaya bak (tekrar: öne), Z = dürbün, Q/E ve sağ tık = serbest bakış.
- Gölge: LED'i üzerine tutarsan (40° koni, 3,4 m) donar, 2,5 sn sonra erir (+2 öz). Ürkek bulgu önce tedirgin olur (0,9 sn), 5 kaçışta gider.
- Üs (Ocak): katı lamba kulesi (hologram değil), kule platformun batı kenarında. Tasarım planı: docs/oyun-kurgusu.md (Faz B-E sırada: Pazar/Atölye, yeni gölge türleri, bina yerleri, Soğuk Dalga).

